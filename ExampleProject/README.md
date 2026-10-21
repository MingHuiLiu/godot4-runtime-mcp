# 集成示例：Godot 4.7 .NET

本目录是一个可直接编译与启动的真实 Godot .NET 演示项目，已配置新的 `GodotRuntimeBackend` Autoload。 `addons/godot_mcp/` 是仓库规范插件的随示例发布副本，不是另一个独立实现。修改规范插件后，从仓库根目录运行 `./scripts/sync-example-addon.sh`，测试会验证内容一致。

```sh
dotnet build ExampleProject/ExampleProject.csproj
godot --headless --path ExampleProject
# 在另一个终端：
./scripts/godot-runtime-cli-dev.sh instances
./scripts/godot-runtime-cli-dev.sh call get_scene_tree_simple --project ExampleProject
```

若同时运行两个演示游戏进程，最后一条调用必须补充 `--instance ID`，否则宿主应拒绝模糊目标。详细说明请见 [架构](../docs/architecture.md)。


## Runtime scene checkpoint integration probe

From the repository root, run `sh scripts/test-snapshot-headless.sh` (set `GODOT_BIN` if Godot Mono is not installed at the macOS default path). The probe builds the C# example, captures a dynamic Owner-null node tree, verifies an isolated reconstruction, persists a checkpoint, exits the game, and verifies both direct and CLI-triggered Headless `.tscn` export. The test uses temporary directories and cleans up its files.
