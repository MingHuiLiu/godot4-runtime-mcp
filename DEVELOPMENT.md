# C# v3 开发与测试

## 项目

- `addons/godot_mcp/GodotMcpServer.cs`：Godot 4 .NET 主线程工具实现。
- `addons/godot_mcp/GodotRuntimeTransport.cs`：每游戏进程独立的本机 HTTP 服务、PID/令牌登记和有界任务队列。
- `RuntimeHost/`：完全由 C#/.NET 8 实现的 MCP stdio/HTTP Server、CLI、GitHub Releases 安装器、ToolService 和多项目实例发现。
- `RuntimeHost/tool_manifest.json`：编译嵌入工具清单（46 个运行时工具）；另外提供 5 个管理工具。添加/修改操作须同时同步清单。
- `skills/godot-runtime/SKILL.md`：Agent Skill 指令。
- `Tests/`：无需 xUnit/NuGet 依赖的 .NET Console 回归测试。
- `ExampleProject/`：Godot 4.7 C# 实际集成项目。
- `Legacy/`：只供参考，不参与新解决方案的历史实现。

## 本机校验

```bash
dotnet build Godot-Mcp.sln
dotnet run --project Tests/Tests.csproj
dotnet run --project RuntimeHost -- status
dotnet run --project RuntimeHost -- pack --path . --out /tmp/godot-mcp-addon.zip
dotnet build ExampleProject/ExampleProject.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path ExampleProject
```

可以启动多个游戏进程，使用 `dotnet run --project RuntimeHost -- instances` 并用 `call ... --project ... --instance ...` 验证路由隔离。**不要**用 Docker、GitHub Actions 或 Python 实现任何客户端或服务。
