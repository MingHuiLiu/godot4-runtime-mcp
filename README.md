# Godot Runtime MCP

**让 AI 直接检查和调试正在运行的 Godot 4 .NET 游戏。** 提供独立 `godot-runtime-cli`、标准 MCP Server 和可自行安装的 [Agent Skill](skills/godot-runtime/SKILL.md)。

支持场景树、节点属性、方法调用、信号、日志、截图等 **46 项运行时操作**，另有 **5 项离线管理工具**。CLI 和 MCP 共享官方 MCP C# SDK / 强类型接口，支持同一台机器上的多个 Godot 项目及进程。

## 交给 AI 安装（推荐）

把下面这句话发给**有终端执行权限**的 AI 助手：

> 请阅读 [Godot Runtime MCP Skill](https://raw.githubusercontent.com/MingHuiLiu/godot4-runtime-mcp/main/skills/godot-runtime/SKILL.md)，按文档自行下载、校验并安装 `godot-runtime-cli`，为我指定的 Godot 4 .NET 项目安装运行时后端，并在启动游戏后验证连接。操作前确认项目路径，不要覆盖已有改动。

Skill 是**单文件、自包含**的：无需先克隆仓库或安装 .NET SDK。自动下载目前支持 **macOS Apple Silicon / Intel**，从 [GitHub Releases](https://github.com/MingHuiLiu/godot4-runtime-mcp/releases/latest) 获取已发布且可校验的 Native AOT 可执行文件。其他系统暂不提供已验证的预编译包。

## 手动安装与使用

从 [最新 Release](https://github.com/MingHuiLiu/godot4-runtime-mcp/releases/latest) 下载对应架构的 `godot-runtime-tools-*-osx-*.tar.gz`，校验同一 Release 中的 `SHA256SUMS.txt` 后解压。包内包含 `godot-runtime-cli` 和 `godot-mcp-server`，不要求客户端安装 .NET 运行时。

```sh
./godot-runtime-cli status
./godot-runtime-cli install --project /absolute/path/to/your/game
# 使用 Godot 4 .NET 编译 C# 项目并启动游戏
./godot-runtime-cli instances
./godot-runtime-cli get-scene-tree-simple --project /absolute/path/to/your/game
```

每个命令自带参数帮助，无须手写 MCP JSON：

```sh
./godot-runtime-cli tools --search node
./godot-runtime-cli get-node-info --help
./godot-runtime-cli get-node-info --node-path /root/Main/Player --project /absolute/path/to/your/game
```

MCP 客户端配置中把 `command` 指向解压后 **`godot-mcp-server` 的绝对路径**，不需要额外参数。CLI 和 MCP 的工具发现、项目查询和后端安装可以在没有运行中的 Godot 游戏时使用。

## 要求与安全

- **游戏端**必须使用 Godot 4 .NET/C#，安装后编译游戏并运行；纯 GDScript 项目不适用。
- 插件安装会更新项目的 Autoload；已有不同版本的文件必须显式指定 `--force` 才会备份并覆盖。
- 多实例操作使用 `--project` / `--instance` 选择目标，避免误改游戏。MCP HTTP 与游戏后端均仅绑定本机地址。
- 部分工具可修改真实运行状态；只授权可信 AI 使用。旧 `execute_csharp` 工具不支持执行动态 C#。
- macOS 预编译程序未做 Apple 公证；下载时核对 SHA-256。

## 文档

[Agent Skill](skills/godot-runtime/SKILL.md) · [架构](docs/architecture.md) · [集成与 MCP 配置](docs/integration.md) · [开发、测试与发布](docs/development.md) · [Godot 示例项目](ExampleProject/README.md) · [许可证](LICENSE)
