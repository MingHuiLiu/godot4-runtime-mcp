# 架构：独立 C# MCP/CLI + Godot C# Runtime Backend

## 分层

```text
Agent / Terminal
├── MCP stdio: godot-mcp-server
├── optional MCP HTTP: godot-runtime-cli serve --token ...
├── CLI: godot-mcp ...
└── Skills: 使用 CLI 的操作指南
         │
         ▼
RuntimeHost (C#/.NET 8, 发布为 Native AOT)
├── Program.cs / CommandLineApp.cs    System.CommandLine 入口
├── McpHost.cs                    官方 MCP SDK stdio/HTTP + ASP.NET Core
├── ToolService.cs                强类型工具目录 + Godot Variant 适配
├── Contracts.cs / RuntimeJsonContext.cs  records、JSON 源生成
├── RuntimeRegistry.cs            项目/实例发现、健康检查、唯一目标选择
└── BackendInstaller.cs           Release ZIP / 嵌入资源 / 本地 ZIP 安装
         │  localhost HTTP + Bearer Token
         ▼
Godot 4 .NET 游戏进程（每进程一个 Backend）
├── GodotRuntimeTransport.cs  HTTP /health, /tools, /call，任务队列
└── GodotRuntimeBackend.cs    Godot _Process 主线程执行 51 项工具
```

## 离线管理与运行时执行分离

宿主程序集在构建时嵌入 `addons/godot_mcp/tool_manifest.json` 和插件源码。`initialize`、`tools/list`、`godot_status`、`godot_list_projects`、`godot_list_instances`、`godot_setup_guide`、`godot_install_backend` 无须运行中的 Godot 游戏。离线快照查询、场景导出和状态检查直接访问本机持久归档，无须运行中的游戏；其余运行时操作必须查找实时实例并发起本机 HTTP 调用。

`tool_manifest.json` 是 51 项运行时工具 Schema 的唯一源文件；`ToolService.cs` 再补充 13 项宿主管理与离线快照/时间线查询工具。CLI 由 System.CommandLine 2.0.11 提供参数解析，MCP stdio/HTTP 由官方 ModelContextProtocol.AspNetCore 2.2.0 提供协议和传输，HTTP 使用 ASP.NET Core/Kestrel。它们共用 `ToolService.RunAsync`。内部 `ToolDefinition`/`ToolInputSchema` 是强类型模型；只有向 Godot 发任意 Variant 参数时需要 JSON。

## 实例发现与路由

每个 Godot 游戏的 Autoload Backend 生成随机端口、256-bit 令牌、进程 PID、实例 ID，并在系统临时目录的 `godot-runtime-mcp/runtime-PID-ID.json` 写入用户私有记录，约每 2 秒更新心跳。`RuntimeRegistry` 检查记录格式、文件权限、实际 PID 和 HTTP `/health` 身份匹配，再按 `--project` 和 `--instance` 过滤。匹配 0 个或多于 1 个时均拒绝运行时操作。

已安装项目列表单独保存在 `~/.config/godot-runtime-mcp/projects.json`（可通过 `GODOT_MCP_CONFIG_DIR` 指定）。运行时登记目录可通过 `GODOT_MCP_REGISTRY_DIR` 覆盖。

## 线程和请求处理

Godot 的 `GodotRuntimeTransport` 在线程池接收 JSON，认证后把 `tool`、`arguments` 放入有界队列；`GodotRuntimeBackend._Process` 每帧最多执行 16 个 WorkItem，所有节点/场景等操作进入 Godot 主线程。最大队列 128、HTTP 消息体约 1 MiB、单任务等待 15 秒。它们是防止积压的界限，**不是任务执行时长配额**；高耗时工具仍可阻塞单帧。

## 安装与兼容

`BackendInstaller` 先尝试 GitHub Release，默认 `auto` 在网络失败/不存在最新 Release 时回退到 AOT 宿主所嵌入的本版 C# 插件。源文件和本地 ZIP 也可直接安装。安装器校验 ZIP 条目和必需文件，拒绝危险路径及未经 `--force` 许可的差异覆盖。旧 Autoload `GodotMcpServer` 可迁移到新的 `GodotRuntimeBackend`；迁移仍须完成插件升级并编译 Godot C# 游戏。

## 安全与限制

后端只绑定 `127.0.0.1`，私有 Bearer Token 不向普通 CLI 列表公开；HTTP MCP 另有独立认证令牌。威胁模型是可信同一操作系统用户使用本机开发工具，而不是多租户或互联网服务。MCP HTTP 由官方 SDK 实现 Streamable HTTP；会按照协商版本处理标准 MCP 请求/事件流（不再是自写的 POST JSON-RPC 子集）。当前没有完整的插件/宿主版本兼容协商。快照捕获与离线导出另有有限的异步 Job 状态机，但其他高耗时工具尚未统一支持长任务调度。 `execute_csharp` 不实现动态 C# 执行。

## Runtime Black Box Checkpoint 与 Headless 导出

新增 `addons/godot_mcp/Recording/` 子系统：`SceneCheckpointCapture` 在游戏主线程只读遍历节点；`VariantSnapshotCodec` 记录支持的持久化属性与 Resource 引用；`CaptureJobController` 接收短时控制命令并将归档写盘安排在线程池。`CheckpointArchive` 是宿主和 Godot 双端共享的纯 C# / Native AOT 兼容文件格式。写入时先提交文件及 SHA-256，最后发布 `manifest.json`，重复快照 ID 禁止覆盖。

`RuntimeHost/Recording/CheckpointStore.cs` 在游戏停止后提供私有目录校验、分页节点查询。`ExportJobManager` 在同一 Godot .NET 项目下启动新的 Headless 引擎进程，`SnapshotSceneBuilder` 将快照重建成隔离节点树，`ExportWorker` 调用原生 `PackedScene.Pack` / `ResourceSaver.Save`，`SnapshotSceneVerifier` 对重新加载后的树与资源图逐属性验证。严格失败时不得标为 `checkpoint_exact`。工作进程运行可信项目代码，不是安全沙箱；其他项目 Autoload 仍可能被启动。

当前属于按需 Checkpoint MVP，**不具备连续时间轴/Delta 录制、视频录制或确定性 replay**。输出 `.tscn` 是可验证的原生场景资源，不是完整的进程内存快照。

### Snapshot codec 与依赖一致性（第二轮加固）

Checkpoint 的 v1 编码器支持 Godot 标量、StringName/NodePath、二维/三维/四维浮点与整数向量、Rect2/Rect2i、Color、Quaternion、Plane、Basis、Projection、Transform2D/3D、AABB、普通 Array/Dictionary 和 Packed 数组。RID、Callable、Signal 和非有限浮点等无法安全保真的值返回结构化 Issue，严格模式拒绝标记完整。递归深度、单字段长度、资源数量和累计编码字节均有限制。

对于指向 `res://` 或 `user://` 的外部 Resource，采集器记录原始磁盘文件 SHA-256，并将可检查的持久化属性与绕过 ResourceLoader 缓存的独立磁盘实例进行比较；内存差异会产生 `external_resource_mutated`，不能验证嵌套引用时会产生 `external_resource_unverifiable`。恢复前再次检查源文件的 SHA-256，变化产生 `resource_dependency_changed`。此类磁盘读取会运行在游戏主线程，因此极大依赖应按资源预算或项目排除规则处理。隔离进程重建和原生重新加载再次比对节点/组/脚本/资源。外部文件即使已保存快照，也可能因项目代码或资源删除而无法还原，`project_relative` 不等于独立便携包。

## 时间线采样（独立功能分支）

`RuntimeTimelineController` 在 Godot 主线程执行**选择性、有界、按 Process 帧间隔**的场景快照采集，自动关联同一 `sessionId` 和 Runtime Transport `instanceId`。录制必须主动开启，`sampleEveryFrames` 为 1..600，`maxSamples` 为 1..240。Godot 对象只在主线程使用；后台串行写入 Checkpoint 和 `TimelineArchive`，不允许同时写多份采样。I/O 期间遗漏的采样间隔通过 `droppedSamples` 暴露。

`TimelineDeltaBuilder` 用节点身份比较相邻两个观察点，记录节点增删、名称/所有权/组/脚本变化、可持久化属性和诊断属性，以及已捕获 Resource 属性的变化。当前 Resource ID 是每份快照的局部标识；资源重排可能导致额外差异，不能将这些事件解释成严格的 Resource 对象生命线。

每段录制在 `recordings/<project-id>/<session-id>/timeline/` 存储状态和带 SHA-256 的有序 JSON 事件批次，并沿用同一会话里的原生 SceneCheckpoint。状态文件最后原子发布；离线 `TimelineStore` 通过项目路径和会话 ID 限制访问，提供分页及完整性校验。退出游戏后可以浏览既有样本及事件，但若进程异常终止，则状态可能仍为 `running`，不能把它解释为活跃进程。

**录制不是确定性 replay：** 不包括输入原始序列、任意信号每次发射、GPU 视频帧、线程/物理引擎私有瞬时状态；采样间隔内产生又消失的节点不会被发现。后续功能若引入这些数据必须使用独立且可核验的录制通道，不得混淆观测差异和事件日志。
