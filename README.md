# Godot Runtime MCP

**让 AI 直接检查和调试正在运行的 Godot 4 .NET 游戏。** 提供独立 `godot-runtime-cli`、标准 MCP Server 和可自行安装的 [Agent Skill](skills/godot-runtime/SKILL.md)。

支持场景树、节点属性、方法调用、信号、日志、截图等 **51 项运行时操作**（含快照采集和时间线录制），另有 **17 项离线管理/查询工具**。CLI 和 MCP 共享官方 MCP C# SDK / 强类型接口，支持同一台机器上的多个 Godot 项目及进程。

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

## Runtime Black Box：完整场景 Checkpoint 与 `.tscn` 导出（源码开发版）

在运行中的 Godot 4 .NET 游戏里可捕获**可观察的完整节点拓扑**，包括没有设置 Owner 的动态节点、支持序列化的属性及其共享 Resource 图。快照持久存储在本机，关闭游戏后仍可通过 CLI/MCP 查询，再由**另一个 Godot Mono Headless 进程**导出并重新加载验证 `.tscn`：

```sh
# 通过 tools --search snapshot 获取当前版本支持的确切命令和 Schema
godot-runtime-cli godot-snapshot-capture --project /absolute/path/to/game --strict true
godot-runtime-cli godot-snapshot-job-status --project /absolute/path/to/game --job-id JOB_ID
godot-runtime-cli godot-snapshot-list --project /absolute/path/to/game
godot-runtime-cli godot-snapshot-info --project /absolute/path/to/game --snapshot-id SNAPSHOT_ID
godot-runtime-cli godot-snapshot-nodes --project /absolute/path/to/game --snapshot-id SNAPSHOT_ID --limit 50
# 游戏可已退出；需要完整可编译的原 Godot .NET 项目
godot-runtime-cli godot-snapshot-export-tscn --project /absolute/path/to/game --snapshot-id SNAPSHOT_ID
godot-runtime-cli godot-snapshot-export-status --project /absolute/path/to/game --job-id EXPORT_JOB_ID
```

导出的文件包括 `scene.tscn`、`runtime_state.json`、`dependencies.json` 与 `reconstruction_report.json`。严格导出会比对重新加载的节点、属性和 Resource 引用，发现缺失则拒绝报告完整成功。外部 Resource 还会保留源文件 SHA-256、检查内存是否偏离磁盘，并在导出时验证文件未发生变化。Godot 原生 `.tscn` 不保证支持循环 Resource、任意 C# 私有变量、物理内部状态或确定性回放；不提供确定性回放。

**版本说明：** Runtime Black Box 全功能已纳入 v3.2.0 源码与本地构建。已发布的 GitHub v3.1.0 仍不包含这些功能；发布 v3.2.0 之前请从本仓库构建 CLI 与后端插件，且不要混用新旧版。

## Runtime Black Box：连续观察时间线

在已安装并运行的 Godot 4 .NET 项目中，可按指定间隔**主动启动有界采样录制**，得到一段包含 Scene Checkpoint 和节点变化事件的时间线。每个周期保存可验证的 Checkpoint，随后计算节点新增、移除、移动、改名、组/脚本/Owner、已支持属性和 Resource 属性的可观察变化；同时保留帧序号、前后 Snapshot ID、跳过的采样周期数量。

```sh
# 在运行中的游戏上启动录制；每 30 次 _Process 检查采样一次，最多 60 个样本
godot-runtime-cli godot-timeline-start --project /absolute/path/to/game --sample-every-frames 30 --max-samples 60
godot-runtime-cli godot-timeline-status --project /absolute/path/to/game --session-id SESSION_ID
godot-runtime-cli godot-timeline-stop --project /absolute/path/to/game --session-id SESSION_ID

# 游戏退出后仍可通过独立的 CLI 进程查询
godot-runtime-cli godot-timeline-sessions --project /absolute/path/to/game
godot-runtime-cli godot-timeline-info --project /absolute/path/to/game --session-id SESSION_ID
godot-runtime-cli godot-timeline-events --project /absolute/path/to/game --session-id SESSION_ID --offset 0 --limit 20
```

**选择性事件采集：** 原有的 `godot-timeline-start` 默认仅保存采样状态；另外指定 `--capture-signals true --capture-input true` 才会为该会话启用回调事件记录。输入事件仅保留按键码、按下状态与有限鼠标元数据，不保存 `Unicode`、IME 或输入文本；信号参数中的文本及资源对象会被标记为 `redacted`。现阶段自动连接的是 Godot 零参数信号，另外记录通过 MCP 工具成功发射的带参数信号；不支持宣称所有 C# 自定义带参数信号均被自动监听。每个样本至多保存 256 条回调事件，剩余部分会累计显示在 `droppedObservedEvents`。

**可选截图与可视化时间线：** `godot-timeline-start` 的 `--capture-screenshots true` 只在显式开启时按采样点读取游戏 Viewport 的最近一帧（与该时刻状态可能相差一个渲染帧）。PNG 限制为最大边长 1024 像素、单张 512 KiB、单会话 32 MiB，并在磁盘上保持当前用户私有；每张都绑定 SHA-256。Headless 虚拟渲染器没有实际画面时会明确返回 `renderer_unavailable`，**不会生成伪造的空白截图**。录制结束后执行下方离线命令生成按帧排列截图、状态差异和回调的自包含 HTML，不包含脚本或联网依赖：

```sh
godot-runtime-cli godot-timeline-start --project /absolute/path/to/game --sample-every-frames 30 --max-samples 60 --capture-screenshots true
godot-runtime-cli godot-timeline-export-gallery --project /absolute/path/to/game --session-id SESSION_ID --offset 0 --limit 20
```

该导出会把**已经核验过的截图嵌入 HTML 文件**，并返回本机 `htmlPath`，用户可以通过文件管理器或浏览器打开。HTML 内可能包含屏幕上的隐私内容；它只会在显式导出时生成，谨慎分享。单次画廊最多展示 20 个采样点，可改变 `offset` 生成其他页面。在无截图的会话中仍会展示完整的文本变化时间线。

归档位于原项目对应的私有录制目录内。事件是**两个实际观察点之间的状态差异**，不等于所有中间瞬时发生的事件；因此这不是输入/信号全量追踪、逐帧视频，也不是确定性时间回放。若游戏变化快于采样或文件写入速度，可能漏掉中间状态，报告会保留丢失采样数量。可从时间线中的 Snapshot ID 再用离线场景导出工具恢复具体采样点的 `.tscn`。

## Runtime Black Box：独立输入回放与状态一致性验证

新实现的**受限回放**在独立的 Godot 4 .NET/Mono 进程中从录制的第一个 Scene Checkpoint 重建测试场景，再按记录的相对 Process 帧间隔重新注入**键盘、鼠标按钮和手柄按钮**元数据；每到记录的观察点，重新采集可持久化节点属性并比对拓扑、组和 Resource 图。游戏退出后仍可请求回放，AI 获取到的是 `matched` / `diverged` / `inconclusive` / `failed` 的可检查诊断报告，而**不是原游戏进程的任意时间旅行**。

```sh
# 录制时必须主动保留输入回调，且不得有丢失的采样或事件
godot-runtime-cli godot-timeline-start --project /absolute/path/to/game --sample-every-frames 10 --max-samples 30 --capture-input true
# 游戏退出、录制完成后，在独立进程验证；需要对应项目的原始 C# 脚本和 Resource
godot-runtime-cli godot-replay-start --project /absolute/path/to/game --session-id SESSION_ID
godot-runtime-cli godot-replay-status --project /absolute/path/to/game --job-id REPLAY_JOB_ID
godot-runtime-cli godot-replay-report --project /absolute/path/to/game --job-id REPLAY_JOB_ID
```

回放严格要求已完成／主动停止的会话、启用了输入记录、`sampleEveryFrames` 介于 1..120、无丢失采样／输入、2..60 个 Checkpoint、最多 1200 个原 Process 帧跨度；不满足则返回 `inconclusive` 而不是伪造成功。回放会真实执行项目脚本（并可能加载其他 Autoload），**不是代码沙箱**；与正在运行的用户游戏实例隔离。`matched` 只表示受支持的可观察节点属性、场景结构及部分 Resource 内容在检查点一致；**不能证明物理、随机数、网络、系统时间、线程调度、音频、GPU 或任意未记录信号的确定性**。输入法文字、手写笔、触摸及未录制的信号无法重演。对于更复杂项目，偏差检测应作为诊断，而不是保证严格决定性。

## 要求与安全

- **游戏端**必须使用 Godot 4 .NET/C#，安装后编译游戏并运行；纯 GDScript 项目不适用。
- 插件安装会更新项目的 Autoload；已有不同版本的文件必须显式指定 `--force` 才会备份并覆盖。
- 多实例操作使用 `--project` / `--instance` 选择目标，避免误改游戏。MCP HTTP 与游戏后端均仅绑定本机地址。
- 部分工具可修改真实运行状态；只授权可信 AI 使用。旧 `execute_csharp` 工具不支持执行动态 C#。
- macOS 预编译程序未做 Apple 公证；下载时核对 SHA-256。

## 文档

[Agent Skill](skills/godot-runtime/SKILL.md) · [架构](docs/architecture.md) · [集成与 MCP 配置](docs/integration.md) · [开发、测试与发布](docs/development.md) · [Godot 示例项目](ExampleProject/README.md) · [许可证](LICENSE)
