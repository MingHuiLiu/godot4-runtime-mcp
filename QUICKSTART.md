# Godot Runtime MCP v3 快速开始（纯 C#）

依赖已有 .NET 8 SDK；运行时要求 Godot 4 .NET。Godot 未运行、插件未安装时仍可以使用以下命令：

```bash
dotnet build RuntimeHost/RuntimeHost.csproj
./bin/godot-mcp status
./bin/godot-mcp tools
./bin/godot-mcp install --project /absolute/path/to/game
```

安装器先检查 [GitHub Releases](https://github.com/MingHuiLiu/godot4-runtime-mcp/releases)；当前没有最新 Release 时使用程序集内嵌的 C# 后端。随后 `dotnet build /absolute/path/to/game/Game.csproj`，运行游戏，再用 `./bin/godot-mcp instances` 查看进程，通过 `--project`、`--instance` 精确调用。

标准 stdio MCP：`./start-mcp-bridge.sh`。安装后端的 MCP 工具名为 `godot_install_backend`。完整文档参见 [README](README.md) 和 [Agent Skill](skills/godot-runtime/SKILL.md)。
