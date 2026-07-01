# Godot MCP Addon v2.0 — Unified HTTP+SSE MCP Server

## 🎯 概述

这是 **McpServer (.NET)** 和 **GodotPlugin** 的合并版本。一个 Godot 插件自包含 MCP 服务，**无需外部 .NET 进程**。

### 架构对比

**旧架构 (v1.x):**
```
Agent → STDIO (MCP) → .NET McpServer → HTTP → GodotPlugin → Godot Engine
                                           ↑
                                    两层架构，需要独立进程
```

**新架构 (v2.0):**
```
Agent → HTTP+SSE (MCP) ─────────────────→ GodotMcpServer → Godot Engine
                                           ↑
                                    一层架构，插件自包含
```

## 🚀 快速开始

### 1. 安装插件

将 `addons/godot_mcp/` 文件夹复制到你的 Godot 项目的 `addons/` 目录。

### 2. 启用插件

在 Godot 编辑器: **项目设置 → 插件 → 启用 "Godot MCP Server"**

### 3. 运行项目

启动 Godot 项目，控制台会显示：
```
[GodotMcp] Godot MCP v2.0 — Unified HTTP+SSE MCP Server
[GodotMcp] ✓ MCP HTTP+SSE server: http://127.0.0.1:7777/
[GodotMcp]   SSE endpoint:  GET  /sse
[GodotMcp]   MCP endpoint:  POST /messages?session_id=...
```

### 4. 连接 Agent

#### 方式 A: MCP HTTP+SSE (推荐)
Agent 直接通过 MCP 协议连接到 Godot:
- **SSE**: `http://127.0.0.1:7777/sse`
- **消息**: `POST http://127.0.0.1:7777/messages?session_id=X`

#### 方式 B: Stdio Bridge (兼容 Claude Desktop)
使用 `start-mcp-bridge.sh` 脚本桥接 stdio → HTTP:
```bash
./start-mcp-bridge.sh
```

然后在 `claude_desktop_config.json` 中配置:
```json
{
  "mcpServers": {
    "godot": {
      "command": "/path/to/start-mcp-bridge.sh"
    }
  }
}
```

#### 方式 C: 旧 HTTP API (向后兼容)
所有旧的 HTTP 端点仍然可用:
```bash
curl -X POST http://127.0.0.1:7777/get_scene_tree \
  -H "Content-Type: application/json" \
  -d '{"includeProperties": false}'
```

## 📋 MCP Tools (48个)

所有工具都通过 MCP `tools/list` 和 `tools/call` 暴露:

| 类别 | 工具 |
|------|------|
| **场景树** | get_scene_tree, get_scene_tree_simple, get_node_info, create_node, delete_node, load_scene, get_node_children, get_node_parent, find_nodes_by_type, find_nodes_by_name, find_nodes_by_group, get_node_ancestors, get_scene_tree_stats, node_exists, get_node_subtree, search_nodes, get_node_context |
| **属性** | get_property, set_property, list_properties |
| **方法** | call_method, list_methods |
| **资源** | list_resources, load_resource, get_resource_info |
| **脚本** | execute_csharp, get_global_variables |
| **信号** | get_node_signals, get_signal_connections, connect_signal, disconnect_signal, emit_signal, start_signal_monitoring, stop_signal_monitoring, get_signal_events, clear_signal_events |
| **调试** | get_logs, get_logs_filtered, get_log_stats, export_logs, clear_logs, add_custom_log, get_performance_stats, take_screenshot, get_time |

## 🔌 MCP 协议详情

### 初始化
```json
// → POST /messages?session_id=xxx
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "capabilities": {},
    "clientInfo": { "name": "my-agent", "version": "1.0.0" }
  }
}

// ← 200 OK
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": { "tools": {} },
    "serverInfo": { "name": "godot-mcp", "version": "2.0.0" }
  }
}
```

### 列出工具
```json
// → POST /messages?session_id=xxx
{
  "jsonrpc": "2.0",
  "id": "2",
  "method": "tools/list"
}
```

### 调用工具
```json
// → POST /messages?session_id=xxx
{
  "jsonrpc": "2.0",
  "id": "3",
  "method": "tools/call",
  "params": {
    "name": "get_scene_tree",
    "arguments": {
      "includeProperties": true
    }
  }
}
```

## ⚙️ 配置

服务器默认监听 `http://127.0.0.1:7777/`。如需修改端口，编辑 `GodotMcpServer.cs` 中的 `HttpPort` 常量。

## 📁 文件结构

```
addons/godot_mcp/
├── plugin.cfg            # 插件配置
├── GodotMcpPlugin.cs      # 编辑器插件（注册 AutoLoad）
└── GodotMcpServer.cs      # MCP 服务端（HTTP+SSE + 所有工具 + 信号监控 + 日志）
```

## 🔄 从 v1.x 迁移

1. 移除旧的 `addons/mcp_client/` 文件夹
2. 添加新的 `addons/godot_mcp/` 文件夹
3. 移除 `.NET McpServer` 项目（不再需要）
4. 移除 Claude Desktop 配置中的 `dotnet run` 命令
5. 如果你的 MCP 客户端支持 HTTP+SSE，直接连接即可
6. 如果需要 stdio 兼容，使用 `start-mcp-bridge.sh`
