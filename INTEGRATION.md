# C# MCP / CLI 集成说明（v3）

所有 Agent 入口都调用同一个 `RuntimeHost/ToolService`，与 Godot 运行时通过私有回环 HTTP 接口通信。不存在固定端口 7777。

## MCP stdio

配置 Agent 的 `command` 为 `/absolute/path/to/Godot-Mcp/start-mcp-bridge.sh`，`args` 可选 `["--project", "/absolute/path/to/game"]`。启动时不要求 Godot 已运行。支持 `initialize`、`tools/list`、`tools/call`、`ping` 和通知。

## MCP Streamable HTTP（可选）

```bash
./bin/godot-mcp serve --port 18777 --token LOCAL_SECRET
```

使用 `POST http://127.0.0.1:18777/mcp`，`Authorization: Bearer LOCAL_SECRET`，`Content-Type: application/json`。返回 JSON-RPC JSON 结果。当前不提供 SSE GET 流。主机仅绑定 loopback，所有运行时节点操作经游戏主线程队列执行。

## CLI / Skills

```bash
./bin/godot-mcp status
./bin/godot-mcp install --project /game --source auto
./bin/godot-mcp instances
./bin/godot-mcp call get_time --project /game --instance ID
```

MCP 直接安装：`godot_install_backend`，`arguments={"project":"/game","source":"auto"}`。下载优先 GitHub Release，未发布则回退同版本内嵌后端。`godot_status`、`godot_list_projects`、`godot_list_instances`、`godot_setup_guide` 在离线状态仍可用。

细节见 [README](README.md)。
