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
├── Program.cs             命令入口
├── McpProtocol.cs         MCP JSON-RPC 与 ToolService
├── RuntimeRegistry.cs     项目/实例发现、健康检查、唯一目标选择
└── BackendInstaller.cs    Release ZIP / 嵌入资源 / 本地 ZIP 安装
         │  localhost HTTP + Bearer Token
         ▼
Godot 4 .NET 游戏进程（每进程一个 Backend）
├── GodotRuntimeTransport.cs  HTTP /health, /tools, /call，任务队列
└── GodotRuntimeBackend.cs    Godot _Process 主线程执行 46 项工具
```

## 离线管理与运行时执行分离

宿主程序集在构建时嵌入 `addons/godot_mcp/tool_manifest.json` 和插件源码。`initialize`、`tools/list`、`godot_status`、`godot_list_projects`、`godot_list_instances`、`godot_setup_guide`、`godot_install_backend` 无须运行中的 Godot 游戏。其余操作必须查找实时实例并发起本机 HTTP 调用。

`tool_manifest.json` 是 46 项工具 Schema 的唯一源文件；`McpProtocol.cs` 再补充 5 项宿主管理工具。CLI、MCP stdio、MCP HTTP 共用 `ToolService.RunAsync`。

## 实例发现与路由

每个 Godot 游戏的 Autoload Backend 生成随机端口、256-bit 令牌、进程 PID、实例 ID，并在系统临时目录的 `godot-runtime-mcp/runtime-PID-ID.json` 写入用户私有记录，约每 2 秒更新心跳。`RuntimeRegistry` 检查记录格式、文件权限、实际 PID 和 HTTP `/health` 身份匹配，再按 `--project` 和 `--instance` 过滤。匹配 0 个或多于 1 个时均拒绝运行时操作。

已安装项目列表单独保存在 `~/.config/godot-runtime-mcp/projects.json`（可通过 `GODOT_MCP_CONFIG_DIR` 指定）。运行时登记目录可通过 `GODOT_MCP_REGISTRY_DIR` 覆盖。

## 线程和请求处理

Godot 的 `GodotRuntimeTransport` 在线程池接收 JSON，认证后把 `tool`、`arguments` 放入有界队列；`GodotRuntimeBackend._Process` 每帧最多执行 16 个 WorkItem，所有节点/场景等操作进入 Godot 主线程。最大队列 128、HTTP 消息体约 1 MiB、单任务等待 15 秒。它们是防止积压的界限，**不是任务执行时长配额**；高耗时工具仍可阻塞单帧。

## 安装与兼容

`BackendInstaller` 先尝试 GitHub Release，默认 `auto` 在网络失败/不存在最新 Release 时回退到 AOT 宿主所嵌入的本版 C# 插件。源文件和本地 ZIP 也可直接安装。安装器校验 ZIP 条目和必需文件，拒绝危险路径及未经 `--force` 许可的差异覆盖。旧 Autoload `GodotMcpServer` 可迁移到新的 `GodotRuntimeBackend`；迁移仍须完成插件升级并编译 Godot C# 游戏。

## 安全与限制

后端只绑定 `127.0.0.1`，私有 Bearer Token 不向普通 CLI 列表公开；HTTP MCP 另有独立认证令牌。威胁模型是可信同一操作系统用户使用本机开发工具，而不是多租户或互联网服务。MCP HTTP 的实现为无状态 POST JSON-RPC，未提供流式 GET/SSE。当前没有完整的插件/宿主版本兼容协商或长任务调度。 `execute_csharp` 不实现动态 C# 执行。
