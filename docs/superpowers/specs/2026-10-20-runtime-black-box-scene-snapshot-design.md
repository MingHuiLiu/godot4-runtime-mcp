# Runtime Black Box：完整运行时场景快照与 `.tscn` 重建设计

> 状态：待用户审查的架构规格。记录于 2026-10-20。代码实施需要独立的 implementation plan 和评审。
> 范围：第一可交付子系统「完整场景 Checkpoint → 查询 → 离线重建 → Godot 原生 .tscn 导出」，并预留后续连续录制接口。
> 基线：仓库 `MingHuiLiu/godot4-runtime-mcp` 本地提交 `2442177`，Godot 4 .NET / C#、RuntimeHost .NET 8 Native AOT。

## 1. 用户意图与验收目标

用户希望让 AI 对运行中的 Godot 游戏自主排查，并额外要求：**能够完整录制场景节点树，在指定时刻恢复，并按需输出 `.tscn`**。本次先交付能独立运行的完整结构化快照与导出闭环；后续连续时间轴、输入重放、截图与自动诊断另立规格。核心成功条件：

1. 即使存在 `Owner == null` 的运行时动态节点，也能枚举并持久化目标根节点以下的全部可观察节点，并准确标记无法恢复的节点。
2. 保存节点身份、父子关系、子节点顺序、原 Owner、组、可序列化属性、脚本和资源关系，保证重建可追踪。
3. 从已完成快照导出一个可被 Godot `ResourceLoader` 加载的原生 `.tscn`，并生成运行时附属状态、依赖及验证报告。
4. 导出过程不临时改写**正在运行的游戏**的节点树、Owner 或子节点关系。
5. 游戏意外退出后，宿主仍可离线列出和检查**已经持久化**的 Checkpoint；若能启动兼容的 Godot .NET 工作进程，则仍可按需导出 `.tscn`。
6. 遇到缺失脚本、不可序列化对象或资源时，不静默忽略：每次导出带 `quality`、`issues` 与可验证的节点统计。

本规格的“完整”指 **完整可观察拓扑 + 所支持的可持久化属性与资源 + 可选显式运行时状态**。不承诺保存调用栈、协程执行位置、全部物理服务器状态、网络连接、任意 C# 私有字段等；`.tscn` 不等于字节级进程内存快照。

## 2. 仓库现状与设计约束

- `addons/godot_mcp/GodotRuntimeBackend.cs` 现有 `save_node_as_scene` 会暂时修改 Owner，或移除/重新添加子节点；不能用于无副作用黑匣子采集，也存在异常路径下恢复不完整的风险。
- 示例 `ExampleProject/scripts/TestHarness.cs` 动态创建许多未设置 Owner 的节点，是必须覆盖的集成用例。
- `GodotRuntimeTransport` 每帧最多执行 16 个 WorkItem；请求正文约 1 MiB、等待约 15 秒，适合小型控制命令，不适合直接传送大量 Checkpoint。
- `RuntimeHost/ToolService.cs` 与 `McpHost.cs` 复用统一工具清单，`RuntimeJsonContext` 必须支持 .NET Native AOT 源生成。
- `scripts/sync-example-addon.sh` 仅复制 `addons/godot_mcp` 顶层文件，增加子目录时须变为安全的递归同步；测试应由固定工具数量转为从 Manifest 动态验证。
- 客户端和后端以 `--project`、`--instance` 定位实例，避免误操作；离线 Checkpoint 要以项目身份 + 录制会话 + 快照 ID 唯一定位。
- 维持 Godot 4 .NET 依赖；不新增常驻服务，不重新实现 MCP 协议，不要求用户安装 Python、数据库或 FFmpeg。

## 3. 方案对比与选择

| 方案 | 优点 | 缺点 | 结论 |
| --- | --- | --- | --- |
| 直接 `PackedScene.Pack(liveRoot)` | 开发快、输出原生文件 | 依赖 Owner，可能漏掉动态节点；若修改 live Owner 会有副作用 | 不采用 |
| `liveRoot.Duplicate` 然后 Pack | 可复用部分 Godot 对象 | 内部节点、脚本、共享资源和动态状态复制不能保证完整；实例化可能有副作用 | 不作为事实来源 |
| **显式 SceneCheckpoint DTO → 独立 Godot 进程重建 → Pack** | 可诊断、可版本化、可独立查询；不会为打包修改 live 树 | 需要 Variant/Resource 编码、工作进程、严格验证 | **采用** |

`PackedScene.Pack()` 作为**最终输出器**，而不是持久化格式或捕获器。第一期 `flatten` 导出为主要模式；`preserve_instances` 只保留在扩展路线中，不承诺第一期准确支持嵌套场景覆盖关系。

## 4. 系统边界与组件

```text
AI / CLI / MCP
  ├── RuntimeHost ToolService
  │     ├── 运行实例查询/触发 —— RuntimeRegistry —— GodotRuntimeBackend
  │     │                                      └── SceneCheckpointCapture (主线程)
  │     │                                              └── SnapshotWriter (后台、纯 DTO)
  │     └── 离线查询/导出 —— CheckpointStore + SnapshotQuery
  │                                     └── ExportJobManager
  │                                             └── Godot --headless --scene export_worker.tscn
  │                                                     └── SnapshotSceneBuilder → PackedScene → .tscn
  └── 录制存储根目录（同用户、私有权限、稳定索引）
```

**Capture** 负责读取 live Godot 节点；**Store/Query** 不链接 Godot；**Exporter** 运行在独立 Godot .NET 进程。它们只通过固定版本的 Checkpoint DTO 和磁盘文件通信。

建议代码位置（具体粒度可以在实施计划中拆分）：

- `addons/godot_mcp/Recording/SceneCheckpointCapture.cs`：目标场景遍历与采集。
- `addons/godot_mcp/Recording/VariantSnapshotCodec.cs`：Godot Variant → 可持久化 DTO。
- `addons/godot_mcp/Recording/SnapshotModels.cs`：节点/资源/关系定义。
- `addons/godot_mcp/Recording/SnapshotWriter.cs`：写盘队列，禁触 Godot API。
- `addons/godot_mcp/Recording/ExportWorker.cs` + `export_worker.tscn`：隔离重建与 Godot 原生序列化。
- `RuntimeHost/Recording/CheckpointStore.cs`：离线枚举、读取及校验。
- `RuntimeHost/Recording/ExportJobManager.cs`：受控执行单次 Headless 工作进程，异步任务状态。
- `Tests/Recording/`：DTO、隔离、恢复、安全及集成用例。

## 5. 场景捕获语义

### 5.1 范围及根节点

默认以 `SceneTree.CurrentScene` 为快照根；未设置时返回 `scene_unavailable` 并要求指定 `rootPath`。允许显式指定当前游戏内有效的节点路径；默认不把整个 `/root` 与所有 Autoload 打包成一个场景。Autoload 仅在未来专门的状态提供器中采集，避免污染场景结构。

遍历时使用 `GetChildren(includeInternal: true)` 枚举所有可观察子节点，保存 `isInternal`、原始父节点/Owner、原始节点路径和兄弟顺序；无法导出的内部引擎节点仍进入拓扑记录并标注 `non_exportable`，不得伪称一比一恢复。捕获器不调用 `AddChild`、`RemoveChild`、`Owner =` 或 `Duplicate()` 作用于 live 树。

### 5.2 节点身份

`nodeId` 为 Snapshot 内唯一的字符串 ID；同一连续录制会话可由后续 Recorder 复用会话级身份映射。Godot ObjectID / `GetInstanceId()` 只用于会话内映射，不跨进程、不跨导出复用。必须支持同路径销毁重生、重命名、重设父节点和相同名称出现在不同父节点下。

Node DTO：`nodeId,parentId,siblingIndex,name,nativeClass,scriptPath,sceneFilePath,originalPath,originalOwnerId,isInternal,uniqueNameInOwner,groups,properties,diagnosticProperties,issues`。属性值使用专用 `SnapshotValue` 标记类型，不依赖不受控的 `object` JSON 自动序列化。

### 5.3 属性规则

默认只采集 PropertyList 中带 `PROPERTY_USAGE_STORAGE` 的持久化属性及诊断所需的白名单状态（局部 Transform、全局 Transform、Velocity、当前动画、Timer 等）。避免遍历 `list_properties` 返回的一切编辑器/内部属性。非导出私有字段**不自动收集**；未来 `IRuntimeSnapshotProvider` 显式提供 `CaptureState`，写入附属状态 JSON，默认不自动执行恢复钩子。

第一期 Codec 支持 null、bool、int64、有限浮点、字符串/StringName、NodePath、Vector2/3/4、Rect2/Rect2i、Color、Quaternion、Basis、Transform2D/3D、AABB、常见 Godot 数值数组、有限深度的 Array/Dictionary（允许受限键类型）、Node 引用（转 nodeId）、Resource 引用（转 resourceId）。RID、Callable、Signal、线程/进程句柄及其他不支持值写成明确 Issue，而不调用任意对象 ToString 冒充可恢复值。

默认限制嵌套深度 32、单字段编码 256 KiB、单快照节点数 2,000、资源数 4,096、原始 DTO 最大 64 MiB；超额时拒绝严格 Checkpoint，并返回统计及溢出原因。这些是产品初始安全限制，不是性能已验证结论。

### 5.4 Resource 图

为每个唯一 Resource 分配 resourceId，记录类型、ResourcePath、引用关系、local-to-scene 标志、可持久化属性及路径/内容校验信息；对共享资源保持对象共享关系，检测并阻断循环序列化。外部资源默认以 `res://` 引用并记录哈希，内联资源则在导出时转换为 PackedScene 子资源。对运行中被修改的外部 Resource，严格模式要求捕获足够的覆盖属性或报 `external_resource_mutated`；不允许悄悄引用磁盘上的旧值。

### 5.5 一致性与性能

工作命令立即返回 `jobId`；主线程在单次采样阶段执行读操作并拷贝为不可变 DTO，后台线程只做压缩、校验及持久化。同步采样阶段不能依赖 HTTP 的 15 秒等待处理大量数据；超过安全上限返回失败记录，不能返回伪完整快照。

第一期以**单主线程采样窗口**作为原子性边界：捕获前记录 `startedPhysicsFrame` / `startedProcessFrame`，完成后记录 `ended...`；单次阶段执行且无主动 yield 的快照标为 `single_main_thread_phase`。外部线程对游戏状态的并发修改无法天然冻结，因此文档只承诺“主线程遍历范围内的一致性”；按需追加 `quality=partial`。捕获预算采用实际采样耗时与预检测控制，不能假称可抢占任意耗时 Godot getter。

## 6. 持久化与恢复

### 6.1 目录布局

```text
<recordings-root>/<project-id>/<session-id>/<snapshot-id>/
  manifest.json
  nodes.jsonl.gz
  resources.jsonl.gz
  state.json
  checksums.json
  issues.json
  export/
    scene.tscn
    runtime_state.json
    dependencies.json
    reconstruction_report.json
```

默认 `recordings-root` 在同用户私有配置目录（与 `GODOT_MCP_CONFIG_DIR` 同一根目录下的 `recordings`），可配置但必须通过规范化路径及权限检查。写入临时分段后 fsync/flush（按平台能力）并用原子重命名提交 Manifest；仅 `completed` 且校验通过的快照可严格导出。进程崩溃导致的中断快照标记 `incomplete`，可诊断但不得伪装完整。

Schema `version=1`，包含 Godot 主次版本、项目目录身份、会话 ID、实例 ID、快照采样帧、毫秒/微秒时钟、根节点身份、节点数、资源数、每类丢失统计。重大协议升级使用版本迁移器，未知版本拒绝载入并返回 `unsupported_schema_version`。

### 6.2 导出动作及安全

由 RuntimeHost 选择原项目的 Godot .NET 可执行文件，在**新**进程执行：

```text
godot --headless --path <project> --scene res://addons/godot_mcp/Recording/export_worker.tscn -- --snapshot-job <job-file>
```

`--scene` 是 Godot 官方 CLI 提供的运行指定场景方式；工作场景不加载用户的主游戏场景。工作进程通过受控环境标记关闭本插件的常规 HTTP Runtime 注册；但项目其他 Autoload 仍可能启动，因此这属于**进程隔离而非强安全沙箱**，仅对可信本机项目自动运行。命令只能使用参数数组调用 ProcessStartInfo（不得 shell 拼接），超时/退出码/标准错误全部写入 JobResult。用户没有兼容的 Godot .NET 可执行程序，或项目资源无法编译时返回 `export_engine_unavailable` / `project_build_invalid`，快照仍可离线读取。

导出器先构建与 live 树**不共享节点实例**的临时树：创建节点、设置脚本与持久化属性、设置局部父子关系/顺序和可导出的节点组；根据 Source Owner 映射或 flatten 策略，为临时根重新赋合法 Owner，再 `PackedScene.Pack()` 并 `ResourceSaver.Save()` 到限定的诊断目录。节点附加脚本或属性 setter 可能产生副作用，因此只在新的受控进程内进行；默认不自动执行项目状态恢复 Hook。

完成后重新 `ResourceLoader.Load<PackedScene>()` 并实例化到未挂载的隔离对象树中，比对节点树、类型、支持属性与资源依赖；输出 JSON 验证报告。严格模式 `strict=true` 如有不可导出节点、关键属性不匹配或依赖丢失则拒绝报告成功。宽松模式可生成可打开的部分场景，但必须标记 `quality=partial` 且逐项列明缺口。

第一期唯一正式导出方式 `flatten`，默认保留 `res://` 路径外部依赖（`project_relative`）；便携打包和保留嵌套场景实例是后续独立功能。

### 6.3 重建语义

第一期仅导出实际采到的完整 Checkpoint：`checkpoint_exact` 指“在捕获边界内完整恢复已支持的节点/属性集合”，**不**代表恢复任意引擎内部状态。未来历史任意时间点重建使用 `Checkpoint + Delta`，并根据是否丢失事件/采样返回 `observed_reconstruction` / `partial`。不把低频采样伪装成任意帧精确恢复。

## 7. MCP / CLI 契约

工具命名遵循现有 Canonical Manifest；管理类离线工具放 RuntimeHost，运行时工具放 Godot 工具 Manifest。

| 工具 | 作用 | 是否需要运行中的实例 |
| --- | --- | --- |
| `godot_snapshot_capture` | 创建 Checkpoint Job，默认 current scene，支持 rootPath、strict 和采集上限 | 是 |
| `godot_snapshot_job_status` | 返回采样/写盘/导出 Job 状态、结果和问题 | 采样 Job 是；导出 Job 否 |
| `godot_snapshot_list` | 按项目和时间列出本地持久化 Checkpoint | 否 |
| `godot_snapshot_info` | 返回 manifest、节点统计、资源统计及 Issues | 否 |
| `godot_snapshot_nodes` | 按 nodeId、pathPrefix、class、分页查看节点与属性 | 否 |
| `godot_snapshot_export_tscn` | 从指定 Checkpoint 创建异步导出 Job，返回 jobId | 否；需要可启动的 Godot 工作进程 |
| `godot_snapshot_export_status` | 检查导出进度/输出路径/验证报告 | 否 |

`godot_snapshot_job_status` 第一阶段只查询在线采样；离线持续任务查询由 `godot_snapshot_export_status` 承担，避免用户必须提供 instance。成功请求结构均含 `schemaVersion,requestId,projectId,snapshotId`（不存在时不返回），完整数据留在磁盘，工具只返回受限分页数据。CLI 自动由 Schema 派生命令：例如 `snapshot-capture`、`snapshot-export-tscn`，遵循现有 kebab-case 转换规则。异步工作任务结束后返回可查询的绝对输出路径，但不自动覆盖 `res://`；显式导入项目文件必须另立授权接口。

错误结构统一含 `code,message,details`，典型状态：`scene_unavailable`、`capture_limit_exceeded`、`snapshot_incomplete`、`snapshot_corrupt`、`unsupported_variant`、`resource_missing`、`export_engine_unavailable`、`export_validation_failed`、`unsafe_output_path`。RuntimeHost 保持旧工具行为，不改变已有 46 个运行时工具接口。

## 8. 异常、权限及生命周期

- 单实例采样只允许一个活跃 Capture Job；同时发起返回 `snapshot_busy`。队列有界，Job 有最大存活时间和持久状态；宿主查询不到已退出运行实例时仍可读完成快照。
- Project ID 由归一化绝对路径派生，快照目录检查真实路径/符号链接逃逸；只在允许的持久化根中读写。绝不在 `res://` 内静默覆盖文件。
- 默认不采集任意 TextEdit/LineEdit 输入、聊天和隐私文本；支持节点/属性排除列表。显式调试白名单才能保存敏感属性。
- 离线查询与导出仍复用当前本机可信用户威胁模型；导出的文件不会自动上传到 AI 服务。MCP HTTP 身份验证与运行时实例路由维持现状。
- 资源解码拒绝未知类型/超深嵌套，防止超大资源与递归对象图造成无界消耗。导出进程有内存、CPU/超时和输出大小限制，失败时保留可解释的诊断结果。
- 引擎运行中的场景绝不直接用于 `Pack`；原方法 `save_node_as_scene` 保持兼容但应独立加上安全保护/废弃说明，不作为新功能调用链。

## 9. 必须验证的测试及验收

1. **动态 Owner 测试**：在 ExampleProject/TestHarness，`GameWorld/Player/CollisionShape`、Enemies、Items 等 Owner 为空的动态节点，被完整枚举；导出 `.tscn` 再加载后节点数与层级可核对。
2. **脚本及资源**：C# `[Export]` 属性、内联 `RectangleShape2D`、共享资源、NodePath、子组和信号描述分别被编码；不可持久化值产生 Issue，不导致错误的“完整”结果。
3. **重命名/同路径实例**：同路径先后不同对象在两个 Snapshot 中有不同 nodeId，不通过 `NodePath` 判定恒等身份。
4. **无副作用**：采样前后 live 树 Owner、父节点、子节点顺序及业务状态一致；采样过程中无意外退出/重新进入树信号。
5. **崩溃恢复**：已提交快照在 Godot 退出后仍可由 RuntimeHost 查询；半写记录可检测并拒绝严格导出；校验失败不生成“有效完成”状态。
6. **导出/验证**：Headless 工作进程不启动主游戏场景；`.tscn` 可被 Godot 加载并实例化；与采集拓扑和受支持属性的 diff 为空才返回严格成功。
7. **多实例与恶意路径**：两个项目、相同项目多个游戏实例、危险 `rootPath`、输出路径穿越、符号链接重定向、并行任务与大 payload 都有明确失败语义。
8. **预算测试**：对 2,000 节点的上限场景及资源大对象采样，报告 CPU/内存/磁盘开销；超过限额拒绝，不在主线程写压缩文件。
9. **发布兼容**：`dotnet build Godot-Mcp.sln -c Release`、`dotnet run --project Tests/Tests.csproj -c Release`、`dotnet build ExampleProject/ExampleProject.csproj`、Godot headless 集成及 macOS Native AOT 实际发布验证；规范插件与示例副本递归一致。
10. **离线输出报告**：成功时 `scene.tscn`、`runtime_state.json`、`dependencies.json`、`reconstruction_report.json` 均存在；每项无法还原的节点/资源能追踪到 ID。

## 10. 后续迭代（不包含在本规格实施范围）

- 持续 Recorder：固定时刻全量 Checkpoint + 生命周期事件 + 属性 Delta + 信号与输入 + 环形缓存、异常触发前后窗口与持久写盘。
- 历史重建：在任意 `tUs` / `physicsFrame` 上对最近 Checkpoint 应用已观察 Delta；展示证据精度与缺口。
- 视觉记录：低频渲染后截图，支持 MCP ImageContentBlock、时间轴关联、过载时降频。
- 自主诊断：场景差异、异常摘要、输入复现和回归断言；精确确定性回放为单独研究方向。
- 导出增强：保留 PackedScene 实例、独立便携资源包、显式运行时状态提供器、可视化 Ghost Scene。
- 超大场景：分帧采样与可证明的一致性协议，只有完成跨帧变更校核后才能标注高可信快照。

## 11. 审查重点及外部依据

请优先审查：(a) 首期只保证「可观察拓扑和支持的可持久化属性」是否符合“完整”的预期；(b) 默认 `flatten + project_relative` 是否合适；(c) 使用另一个 Headless Godot 进程时，项目其他 Autoload 仍有副作用的边界是否可以接受；(d) Checkpoint 的 2,000 节点/64 MiB 初始上限是否需要配置或提高。

Godot 官方参考：
- [PackedScene](https://docs.godotengine.org/en/stable/classes/class_packedscene.html) — Pack 与 Owner 规则。
- [Node](https://docs.godotengine.org/en/stable/classes/class_node.html) — 所有权与内部子节点。
- [SceneState](https://docs.godotengine.org/en/stable/classes/class_scenestate.html) — 序列化场景节点/属性元数据。
- [Godot CLI](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html) — `--headless` 与 `--scene`。
- [ResourceLoader](https://docs.godotengine.org/en/stable/classes/class_resourceloader.html) — 加载/依赖。

**实施门槛**：本设计文档须经用户审查批准。批准后才使用 Superpowers `writing-plans` 编写分任务实施计划；实施计划再次经用户确认后，才进入 TDD 产品代码修改。
