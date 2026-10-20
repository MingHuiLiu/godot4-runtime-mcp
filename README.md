# Godot Runtime MCP v3 — C# / .NET 8

**同一 Godot C# 运行时后端、独立 C# MCP Server、C# CLI 和 Agent Skills。** 不使用 Python/Node.js 作为实现，不需要 Docker、GitHub Actions 或安装额外软件。运行时需要 Godot 4 .NET；**Release 的 Native AOT CLI/MCP 二进制无需 .NET SDK/运行时**（仓库内源码开发仍使用 .NET SDK）。

## 架构

```text
AI Agent ─ MCP stdio ─┐
AI Agent ─ MCP HTTP ──┼── RuntimeHost (.NET 8, standalone)
Agent Skills ─ CLI ───┘       │ ToolService (one tool implementation)
                              │
                     RuntimeRegistry (discovery and routing)
                              │
                      ┌───────┴──────────┐
                   Project A           Project B
                 ┌────┴────┐              │
                PID 1     PID 2          PID 3
                port1     port2          port3
                Godot C# backend (authenticated local HTTP)
                Godot main thread: 46 runtime operations
```

**核心保证：**

- Godot 每个进程独立申请回环端口、生成 256-bit 令牌与 instanceId，注册 PID + 项目绝对路径；无固定 7777 端口，无进程冲突。
- 独立的 `RuntimeHost/` 同时提供 MCP stdio、MCP HTTP（可选）以及 CLI；**Godot 不启动或插件未安装时也能初始化 MCP、列出 51 个工具和安装后端**。
- 多进程时自动拒绝模糊目标；运行时工具的 `project` 和 `instance` 用于路由，不会盲目控制其他游戏。
- CLI **先尝试 GitHub Releases**；最新 Release 404/网络不可达时使用 **C# 可执行程序集内嵌的相同后端版本**，无需已有插件。指定 release 标签的安装不会回退。用户可以选择本地仓库或插件 ZIP。
- 安装后自动写 `project.godot` 的 `[autoload]`，必要时补齐已有 C# 项目的程序集名称；不再依赖导致游戏程序集加载问题的 EditorPlugin 类。全部调用由 Godot 主线程队列执行。
- 强制覆盖会备份原插件到用户配置目录（不放在会被 Godot C# 编译的 `addons` 下）；ZIP 路径检查，服务监听 loopback，Bearer 认证。

## 直接下载 Native AOT 可执行文件（无需 .NET）

在 [v3.0.0 Release](https://github.com/MingHuiLiu/godot4-runtime-mcp/releases/tag/v3.0.0) 下载与你的 Mac 相符的 `godot-mcp-v3.0.0-osx-arm64.tar.gz`（Apple Silicon）或 `godot-mcp-v3.0.0-osx-x64.tar.gz`（Intel），解压后获得两个**各自独立的单文件可执行程序**：

- `godot-mcp`：CLI，使用 `./godot-mcp status`、`./godot-mcp install --project /path/to/game`、`./godot-mcp instances`。
- `godot-mcp-server`：MCP stdio，**无需参数**，将完整路径设置为 Agent 的 MCP command。也可以用 `godot-mcp mcp`。
- `godot-mcp-addon-v3.0.0.zip`：Godot C# 后端源码插件；建议使用 CLI install 自动下载和配置 project.godot。手动复制 ZIP 中的 `addons/godot_mcp` 还需配置 Autoload。
- `SHA256SUMS.txt`：下载后使用 `shasum -a 256 -c SHA256SUMS.txt` 核对包完整性。

Native AOT **自身即生成单文件原生可执行文件**，不与 `PublishSingleFile` 同时使用。复现构建：`scripts/publish-release.sh v3.0.0 osx-arm64`（或 `osx-x64`）。本机编译脚本只支持 macOS 双架构，未宣称支持未经构建验证的 Linux/Windows AOT。macOS 首次下载的程序可能触发系统的未公证应用警告；请只从官方 Release 下载并核对 SHA256。

## 使用 CLI

在仓库根目录（已有 .NET SDK）：

```bash
dotnet build RuntimeHost/RuntimeHost.csproj -c Release
./bin/godot-mcp status
./bin/godot-mcp tools
./bin/godot-mcp install --project /absolute/path/to/game
dotnet build /absolute/path/to/game/Game.csproj
./bin/godot-mcp instances
./bin/godot-mcp call get_scene_tree_simple --project /absolute/path/to/game --instance INSTANCE_ID --args '{"maxDepth":2}'
```

不联网，从当前仓库直接安装：

```bash
./bin/godot-mcp install --project /path/to/game --source local --path .
```

强制安装新插件（旧插件将备份，非必要不使用）：

```bash
./bin/godot-mcp install --project /path/to/game --force
```

安装过程不启动 Godot，不安装 .NET SDK 或其他软件。**若目标游戏没有 C# csproj，必须先在 Godot 4 .NET 编辑器内创建 C# 项目**；纯 GDScript 运行时无法执行 C# 后端。构建后运行游戏进程才可以使用场景树、属性、信号和性能等实时工具。

## MCP stdio / HTTP

Claude Desktop 配置：

```json
{
  "mcpServers": {
    "godot-runtime": {
      "command": "/absolute/path/to/Godot-Mcp/start-mcp-bridge.sh",
      "args": []
    }
  }
}
```

`start-mcp-bridge.sh` 只负责启动 C# 程序；也可以直接运行 `dotnet RuntimeHost/bin/Release/net8.0/GodotRuntimeMcp.dll mcp --project /path/to/game`。即使没有运行的 Godot 实例，MCP `initialize`、`tools/list`、`ping` 以及以下管理工具仍正常工作。

MCP 额外 5 工具：`godot_status`、`godot_list_instances`、`godot_list_projects`、`godot_setup_guide`、**`godot_install_backend`**。安装工具示例参数：`{"project":"/path/to/game","source":"auto"}`。

可选常驻 HTTP：

```bash
./bin/godot-mcp serve --port 18777 --token LOCAL_SECRET
```

仅 `127.0.0.1:18777/mcp` 的 **stateless Streamable HTTP POST JSON-RPC**；要求 `Authorization: Bearer LOCAL_SECRET`，不提供 GET SSE。默认生成随机 token 并只显示在 stderr。普通桌面 Agent 建议优先使用 stdio。

## Releases / Skills

项目 [Releases](https://github.com/MingHuiLiu/godot4-runtime-mcp/releases) 提供原生 CLI/MCP 和 Godot 插件 ZIP。也可以直接通过 C# CLI 生成手动发布包，无需 Actions：

```bash
./bin/godot-mcp pack --path . --out godot-mcp-addon.zip
```

`--source auto` 优先下载 Release 中名字包含 addon/plugin 的 ZIP，否则使用对应 Release 标签源码 ZIP。如果 Release 不存在/网络不可达，则使用与 CLI 一起编译和分发的内嵌后端。明确使用 `--source release --version vX.Y` 会严格检查该版本，不会安装未验证的旧 GitHub main 分支代码。

Agent Skills 详细指导：`skills/godot-runtime/SKILL.md`（也同步在 `AGENT_SKILL.md`）。

## 测试

```bash
dotnet build RuntimeHost/RuntimeHost.csproj
dotnet run --project Tests/Tests.csproj
dotnet build ExampleProject/ExampleProject.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path ExampleProject
```

已使用本机 Godot 4.7 .NET 验证实际进程：同项目两进程独立端口、实例发现、模糊目标拒绝、指定实例读取场景树、未认证请求 401。独立 C# Tests 覆盖离线 MCP/CLI、安装、升级备份、ZIP、安全检查。

**注意**：`execute_csharp` 是历史占位能力，没有捆绑 Roslyn，不执行任何 C# 代码；必须按“不支持”处理。不要把带修改能力的 API 暴露给不可信的本机用户。此实现不支持远程跨机器调用，同一 OS 用户可读取自己进程的注册表令牌。

历史 `McpServer/`、`GodotPlugin/` 和大量旧文档描述的是过时架构，不再作为 v3 入口，旧固定端口协议不兼容新版本。
