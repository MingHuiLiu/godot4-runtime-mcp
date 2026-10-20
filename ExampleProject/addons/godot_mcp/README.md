# Godot C# Runtime Backend

`addons/godot_mcp/` 是规范的 Godot 4 .NET 插件目录：

- `GodotRuntimeBackend.cs`：46 项运行时工具分发、节点、资源、信号、日志等 Godot 操作。
- `GodotRuntimeTransport.cs`：每个游戏进程独立监听的本机 HTTP 接口、随机认证 Token、运行时注册和主线程队列。
- `runtime_backend.tscn`：Godot 自动加载场景。
- `tool_manifest.json`：46 个工具的唯一 Schema 定义（也会嵌入 C# 宿主）。

通过 `godot-mcp install --project /path/to/game` 自动部署插件并写入：

```ini
[autoload]
GodotRuntimeBackend="*res://addons/godot_mcp/runtime_backend.tscn"
```

安装器可以识别并迁移旧的 `GodotMcpServer` Autoload；不要在一个项目中同时注册两个后端。安装完毕后编译并启动目标 **Godot 4 .NET C#** 游戏。CLI/MCP Server 是另一独立进程，**不在**插件里运行 MCP 协议服务或固定端口 7777。

程序启动和退出时自动登记/注销实例；多个项目和同项目多个运行进程互相隔离。 `execute_csharp` 为明确不支持的旧接口，并未捆绑 Roslyn。
