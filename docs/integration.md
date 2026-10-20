# 接入指南

## MCP stdio（推荐）

下载适合 macOS 架构的原生 AOT 包并解压，确保 `godot-mcp-server` 可执行。Agent 配置示例：

```json
{
  "mcpServers": {
    "godot-runtime": {
      "command": "/absolute/path/to/godot-mcp-server",
      "args": []
    }
  }
}
```

这是独立的 C# MCP Server，未运行 Godot 时也能列出工具、执行 `godot_install_backend` 等管理功能。如果用源码开发，则将配置中的 `command` 改为仓库绝对路径的 `start-mcp-bridge.sh`（需要本地 .NET SDK），或者以 `./scripts/godot-mcp-dev.sh mcp` 启动。

## CLI

AOT 版示例：

```sh
./godot-mcp status
./godot-mcp install --project /path/to/game
./godot-mcp instances
./godot-mcp call get_time --project /path/to/game --instance INSTANCE_ID
```

源码模式把 `./godot-mcp` 换成 `./scripts/godot-mcp-dev.sh`。安装后需在目标 Godot 4 **.NET** 项目中编译 C# 并启动游戏；安装器**不会**代替开发者启动游戏。多个实例时必须同时指定准确目标，避免歧义。

## 插件来源

```sh
./godot-mcp install --project /path/to/game --source local --path ./godot-mcp-addon.zip
./godot-mcp install --project /path/to/game --source release --version v3.0.0
./godot-mcp install --project /path/to/game --force
```

`auto` 优先取最新 Release，网络失败或不存在 Release 时使用宿主内嵌的后端。`release` 模式不静默回退；`--force` 才允许覆盖有差异的插件并备份旧版本。

## 可选本机 MCP HTTP

```sh
./godot-mcp serve --port 18777 --token YOUR_LOCAL_TOKEN
```

对 `http://127.0.0.1:18777/mcp` 发送 `POST`、`Content-Type: application/json` 及 `Authorization: Bearer YOUR_LOCAL_TOKEN`。只支持无状态 JSON-RPC POST，不支持 SSE GET。普通桌面 Agent 建议选 stdio。

## 离线管理工具

`godot_status`、`godot_list_projects`、`godot_list_instances`、`godot_setup_guide`、`godot_install_backend` 在未运行游戏时可用。其余 46 项 Godot 运行时工具只有匹配的游戏实例运行时才能成功。

参见 [架构](architecture.md) 与 [仓库 README](../README.md)。
