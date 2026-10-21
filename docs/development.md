# 开发、回归测试与发布

## 仓库结构

```text
addons/godot_mcp/                 Godot 后端唯一规范源码/工具清单
RuntimeHost/                      System.CommandLine / 官方 MCP SDK / 强类型 DTO / 安装器
Tests/                            无第三方测试框架的 C# 回归断言
ExampleProject/                   可直接打开的 Godot .NET 集成示例
skills/godot-runtime/SKILL.md     Agent 操作指南
scripts/godot-runtime-cli-dev.sh          **需要 SDK** 的源码开发启动器
scripts/sync-example-addon.sh     同步规范插件到示例项目
scripts/publish-release.sh        构建 macOS Native AOT 发行包
start-mcp-bridge.sh               兼容老 MCP 配置的开发入口
docs/                             当前架构、集成、开发及历史发布说明
```

`ExampleProject/addons/godot_mcp/` 故意保留规范插件的快照：示例项目可直接被 Godot 编辑器打开、编译和运行，而不依赖软链接或构建期间下载；这里不是另一个插件实现。修改 `addons/godot_mcp/` 后运行 `scripts/sync-example-addon.sh`，回归测试会检查两个目录中发行插件文件的**逐字节一致性**。Godot 生成的 `.uid` 是资源身份文件，不属于插件 ZIP。

## 本机验证

```sh
dotnet build Godot-Mcp.sln -c Release
dotnet run --project Tests/Tests.csproj -c Release
./scripts/godot-runtime-cli-dev.sh status
./scripts/godot-runtime-cli-dev.sh pack --path . --out /tmp/godot-runtime-addon.zip
dotnet build ExampleProject/ExampleProject.csproj
GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot sh scripts/test-snapshot-headless.sh
# 具体 Godot 可执行文件位置因机器而异
godot --headless --path ExampleProject
```

验证多实例：同时启动两份示例进程，用 `./scripts/godot-runtime-cli-dev.sh instances` 查看各自的 `instanceId`，并按 `--project`、`--instance` 指向具体进程。没有明确目标时不应该让运行时调用成功。

## 修改工具

在 `GodotRuntimeBackend.HandleToolOperation` 添加 Godot 主线程操作，同时修改唯一工具清单 `addons/godot_mcp/tool_manifest.json`；然后执行同步脚本和回归测试。若修改内部请求/响应协议，也需要同时更新 `GodotRuntimeTransport` 和 `RuntimeRegistry` 并测试实例验证、权限和超时路径。

## macOS Native AOT

```sh
./scripts/publish-release.sh v3.1.0 osx-arm64
./scripts/publish-release.sh v3.1.0 osx-x64
```

产物位于忽略 Git 的 `dist/TAG/`，包含 `godot-runtime-cli` 和 `godot-mcp-server` 两个独立可执行文件的 tar.gz、后端插件 ZIP 与 SHA256SUMS。Native AOT 原生输出已是单文件，**不要**同时设置 `PublishSingleFile=true`。构建和测试不会自动创建 GitHub Release。版本号统一由 `RuntimeHost/HostVersion.cs` 定义，打包脚本会拒绝与宿主实际版本不符的标签；正式发布前须去掉 `-dev` 并验证对应构建。

## 兼容与历史

从旧 Autoload `GodotMcpServer` 迁移时，安装器会检查并替换成 `GodotRuntimeBackend`，不会留两个自动加载实例；旧版有差异的插件必须显式 `--force` 更新。若游戏自身 C# 代码曾直接引用旧类名 `GodotMcpServer`，也需要改用 `GodotRuntimeBackend`。旧源码和文档仍可通过 Git 的 `v3.0.0` 标签查看，不在当前工作树保留 `Legacy/`。


## 基础依赖（宿主）

- `System.CommandLine` 2.0.11：CLI 子命令、帮助、选项类型与验证。
- `ModelContextProtocol.AspNetCore` 2.2.0：官方 MCP SDK，包含 stdio 与 Streamable HTTP 传输；HTTP 使用 ASP.NET Core/Kestrel。
- `System.Text.Json` + `RuntimeJsonContext`：AOT 源生成序列化；`ToolDefinition`、`BackendCallRequest`、`InstallResult` 等强类型 record。
- `System.IO.Compression.ZipArchive`（BCL）：带路径校验的 ZIP 读取/打包；GitHub Release 元数据使用 `GithubRelease` record。

**不要再自行解析 MCP JSON-RPC 或 HTTP 会话。** 项目采用官方 SDK；只在 Godot Variant 和自定义工具 JSON Schema 的边界操作 `JsonElement`/`JsonObject`。项目代码可做零警告常规构建；Native AOT 在 .NET 8 的 ASP.NET Core Framework Reference 内仍可能发出外部依赖裁剪分析提示（目前 IL2026/IL3053），并非自行忽略的安全保证。改动 Native AOT 依赖后必须真实发布、使用官方 MCP SDK 客户端运行测试。

## 场景快照 / .tscn 还原开发

新增 Recording C# 文件只能放在 `addons/godot_mcp/Recording/` 作为单一规范来源；共享数据契约 `SnapshotContracts.cs` / `CheckpointArchive.cs` / `SnapshotJsonContext.cs` 也被 RuntimeHost.csproj 编译。先运行 `sh scripts/sync-example-addon.sh` 将嵌套插件复制到 ExampleProject，再执行 .NET 单元测试与真实 Godot Mono Headless 探针。

`scripts/test-snapshot-headless.sh` 覆盖动态 Owner-null 节点、循环/共享 Resource 的内存重建、容量限制、异步落盘、独立进程的 `.tscn` 严格加载验证，以及游戏退出后由 CLI 启动的离线导出。脚本使用独立临时录制目录，避免覆盖用户项目文件。继续扩展时先加失败测试，再实现。

默认输出位于 `GODOT_MCP_CONFIG_DIR/recordings/<project-id>/<session-id>/<snapshot-id>/` 下；离线导出存储于项目录制目录的 `exports/<job-id>/output/`。如需使用另一个 Godot Mono 程序，通过 `GODOT_BIN` 或 CLI 的 `--godot-binary` 指定。
