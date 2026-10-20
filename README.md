# Godot Runtime MCP

**Godot 4 .NET 的 C# 运行时调试工具：一个 Godot Backend，同时服务于独立的 MCP Server、CLI 和 Agent Skills。**

- 46 项 Godot 运行时操作与 5 项离线管理工具；未启动 Godot 也能发现工具和安装插件。
- 每个游戏进程拥有独立的随机本机端口、实例 ID 与 Bearer Token；支持多项目、多进程。
- CLI 与 MCP stdio/HTTP 均调用同一 C# ToolService；Godot 主线程负责真实操作。
- macOS Release 可使用原生 Native AOT 二进制，不要求客户端机器安装 .NET；**Godot 游戏仍必须使用 .NET 版 Godot 并编译 C#**。

## 下载与启动

[GitHub Releases](https://github.com/MingHuiLiu/godot4-runtime-mcp/releases) 发布以下类型的文件（**标签不等于已创建 Release**）：

| 文件 | 内容 |
| --- | --- |
| `godot-mcp-vTAG-osx-arm64.tar.gz` | Apple Silicon CLI + MCP Server（两个独立 AOT 可执行文件） |
| `godot-mcp-vTAG-osx-x64.tar.gz` | Intel Mac CLI + MCP Server |
| `godot-mcp-addon-vTAG.zip` | Godot C# 插件（可由 CLI 自动安装） |
| `SHA256SUMS.txt` | SHA-256 校验信息 |

将对应 tar.gz 解压后直接使用：

```sh
./godot-mcp status
./godot-mcp install --project /absolute/path/to/your/godot-dotnet-game
# 编译目标游戏的 C# csproj，然后启动 Godot 游戏
./godot-mcp instances
./godot-mcp call get_scene_tree_simple --project /absolute/path/to/your/godot-dotnet-game --instance INSTANCE_ID
```

Agent 的 MCP 配置中，令 `command` 指向解压后的 `godot-mcp-server` **绝对路径**，无需添加 `mcp` 参数。没有运行中的游戏时，MCP 初始化、工具清单、项目查询及插件安装依然可用。

### 从仓库源码开发

源码启动器的名称特意包含 `dev`，避免与无需 .NET 的 AOT 可执行文件混淆：

```sh
dotnet build Godot-Mcp.sln -c Release
./scripts/godot-mcp-dev.sh status
./scripts/godot-mcp-dev.sh install --project /path/to/game --source local --path .
./scripts/godot-mcp-dev.sh mcp
```

源码启动器需要本地 .NET 8 SDK。旧的 `start-mcp-bridge.sh` 仅为已有 MCP 配置保留的**兼容入口**，实际启动的是独立 C# MCP 宿主，没有 Python 桥接器。

## 安装与隔离

安装器优先下载 GitHub Release 插件 ZIP；Release 不可用时，默认 `--source auto` 会使用**已内嵌到 C# 宿主的后端源码**。也可明确指定 `--source release --version TAG`、`--source github`，或 `--source local --path /path/to/addon.zip`。

安装器校验归档、更新 `project.godot` 的 `GodotRuntimeBackend` Autoload，迁移以前的 `GodotMcpServer` Autoload；已有不同插件不会被覆盖，除非显式提供 `--force`（旧文件会备份）。

`project` 指向 Godot 项目；一个项目内存在多个运行中实例时，必须进一步指定 `instance`，避免操作错误的游戏进程。所有网络接口只允许本机访问，不是面向公网的远程管理服务。

## 文档导航

- [架构与数据流](docs/architecture.md) — 模块职责、实例路由、线程模型及安全边界
- [集成指南](docs/integration.md) — MCP stdio、HTTP、CLI 与安装示例
- [开发与验证](docs/development.md) — 目录说明、示例、构建、回归测试和打包
- [Godot 后端](addons/godot_mcp/README.md) / [集成示例](ExampleProject/README.md)
- [Agent Skill](skills/godot-runtime/SKILL.md)
- [v3.0.0 历史发布说明](docs/releases/v3.0.0.md)

## 已知边界

`execute_csharp` 保留了旧接口名称，但不含 Roslyn，调用会明确返回不支持；本项目不提供任意 C# 代码执行。MCP HTTP 为本机认证 JSON-RPC POST（不支持 GET/SSE 流），优先使用 stdio。Release 二进制未经过 Apple 公证；请从可信来源获取并校验 SHA-256。运行时操作会修改真实游戏状态，只对可信 Agent 开放。
