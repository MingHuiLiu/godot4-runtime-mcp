---
name: godot-runtime-mcp
description: >-
  Connect AI agents to live Godot 4 C# game runtime via MCP protocol.
  Enables real-time scene tree inspection, node property/read-write,
  signal monitoring, method calls, resource listing, and performance
  diagnostics — all while the game is running.
trigger_keywords:
  - godot
  - godot-mcp
  - godot runtime
  - game debug
  - scene tree
  - godot plugin
apply_to: []
---

# Godot Runtime MCP — Agent Integration Guide

## 🎯 What This Is

A **Godot 4 plugin** (`addons/godot_mcp/`) that exposes a full **MCP HTTP+SSE server** at `http://127.0.0.1:7777/`. It lets AI agents inspect and interact with a running Godot game in real time — query the scene tree, read/write node properties, call methods, monitor signals, fetch logs, and collect performance stats.

**Architecture** (single layer, no external processes):
```
AI Agent  ←── HTTP+SSE (MCP protocol) ──→  GodotMcpServer (plugin inside Godot)
```

## ✅ Check If MCP Is Already Running

```bash
curl -s -m 2 -X POST http://127.0.0.1:7777/get_time \
  -H "Content-Type: application/json" -d '{}'
```

Expected response:
```json
{"success":true,"data":{"unix":...,"datetime":"...","ticks":...}}
```

If this returns `Connection refused`, the server is not running — follow setup below.

---

## 🔧 Setup Guide (for AI Agents)

### Option A: VS Code MCP (HTTP type) — If you have VS Code MCP access

Add to VS Code's `mcp.json`:
```json
{
  "servers": {
    "godot-runtime-mcp": {
      "url": "http://127.0.0.1:7777",
      "type": "http"
    }
  }
}
```

### Option B: Stdio Bridge (for Claude Desktop / VS Code stdio)

```json
{
  "mcpServers": {
    "godot": {
      "command": "/path/to/start-mcp-bridge.sh",
      "args": []
    }
  }
}
```

### Starting the Godot Project

The Godot project must be running. Two approaches:

**Approach 1 — Headless (no window):**
```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path /path/to/godot-project --no-window &
```

**Approach 2 — With editor:**
Just open the project in Godot Editor and press **Play** (F5).

The MCP server auto-starts when the game runs. Wait ~3 seconds after launch, then verify:
```bash
curl -s -m 2 -X POST http://127.0.0.1:7777/get_time \
  -H "Content-Type: application/json" -d '{}'
```

### Building the C# Project

Godot C# projects need to be built before running:
```bash
cd /path/to/godot-project
dotnet build
```

If the MCP plugin is freshly added, clean rebuild once:
```bash
rm -rf .godot/mono
dotnet build
```

---

## 📋 Complete Tool Reference (46 tools)

### Scene Tree (17 tools)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `get_scene_tree` | Full scene hierarchy | `includeProperties` (bool) |
| `get_scene_tree_simple` | Lightweight tree (name/type only) | `rootPath`, `maxDepth` |
| `get_node_info` | Node details (props, methods, signals) | `nodePath` (required) |
| `create_node` | Spawn a new node at runtime | `parentPath`, `nodeType`, `nodeName` |
| `delete_node` | Remove a node | `nodePath` (required) |
| `load_scene` | Switch to another scene | `scenePath` (required) |
| `get_node_children` | Direct children (non-recursive) | `nodePath` (required) |
| `get_node_parent` | Parent info | `nodePath` (required) |
| `find_nodes_by_type` | Search by Godot class name | `nodeType` (required), `rootPath` |
| `find_nodes_by_name` | Search by node name (fuzzy) | `namePattern` (required), `caseSensitive`, `exactMatch` |
| `find_nodes_by_group` | Search by group membership | `groupName` (required), `rootPath` |
| `get_node_ancestors` | Trace ancestry up the tree | `nodePath`, `levels`, `includeSiblings` |
| `get_scene_tree_stats` | Node count, type distribution | `rootPath` |
| `node_exists` | Check if path is valid | `nodePath` (required) |
| `get_node_subtree` | Sub-tree with depth limit | `nodePath`, `maxDepth`, `includeProperties` |
| `search_nodes` | Combined search (name+type+group) | `namePattern`, `nodeType`, `groupName`, `maxResults` |
| `get_node_context` | Parent + siblings + children in one call | `nodePath`, `includeParent`, `includeSiblings`, `includeChildren` |

### Properties (3 tools)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `get_property` | Read a node property at runtime | `nodePath` (r), `propertyName` (r) |
| `set_property` | Write a node property | `nodePath` (r), `propertyName` (r), `value` (r) |
| `list_properties` | List all readable properties | `nodePath` (r) |

### Methods (2 tools)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `call_method` | Call any method on a node | `nodePath` (r), `methodName` (r), `args` |
| `list_methods` | List all callable methods | `nodePath` (r) |

### Resources (3 tools)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `list_resources` | List files in a `res://` directory | `path`, `filter` |
| `load_resource` | Load and inspect a resource | `resourcePath` (r) |
| `get_resource_info` | Get resource metadata | `resourcePath` (r) |

### Script (2 tools)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `execute_csharp` | Execute C# code (requires Roslyn) | `code` (r) |
| `get_global_variables` | List top-level autoloads | — |

### Signals (9 tools)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `get_node_signals` | List all signals on a node | `nodePath` (r) |
| `get_signal_connections` | Inspect signal wiring | `sourceNodePath` (r), `signalName` (r) |
| `connect_signal` | Wire a signal to a method | `sourceNodePath` (r), `signalName` (r), `targetNodePath` (r), `targetMethod` (r) |
| `disconnect_signal` | Unwire a signal | Same as connect |
| `emit_signal` | Fire a signal manually | `nodePath` (r), `signalName` (r), `args` |
| `start_signal_monitoring` | Start recording signal events | `signalName`, `maxEvents` |
| `stop_signal_monitoring` | Stop recording, get stats | — |
| `get_signal_events` | Query recorded signal history | `count`, `nodePath`, `signalName`, `startTime`, `endTime` |
| `clear_signal_events` | Clear signal event buffer | — |

### Debug (8 tools)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `get_logs` | Fetch recent log entries | `count` |
| `get_logs_filtered` | Search logs by level/pattern/time | `level`, `messagePattern`, `startTime`, `endTime`, `maxCount` |
| `get_log_stats` | Log statistics (count per level) | — |
| `export_logs` | Save all logs to a file | `filePath` |
| `clear_logs` | Clear log buffer | — |
| `add_custom_log` | Inject a marker into the log | `message` (r), `level` |
| `get_performance_stats` | FPS, memory, draw calls, node count | — |
| `take_screenshot` | Capture game viewport | `savePath` |
| `get_time` | Current time + engine uptime | — |
| `save_node_as_scene` | Save a node (+ optional children) as .tscn for text analysis | `nodePath` (r), `includeChildren`, `savePath`, `returnContent` |

*(r) = required parameter*

### Export (1 tool)

| Tool | Purpose | Key Parameters |
|------|---------|---------------|
| `save_node_as_scene` | Save node subtree as .tscn for text analysis | `nodePath` (r), `includeChildren`, `savePath`, `returnContent` |

## 🚀 Calling Tools

### Method 1: HTTP REST API (simplest)

```bash
# POST /<tool_name> with JSON body
curl -s -X POST http://127.0.0.1:7777/get_scene_tree \
  -H "Content-Type: application/json" \
  -d '{"includeProperties":false}'
```

Response format:
```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

### Method 2: MCP Protocol (JSON-RPC 2.0 over HTTP)

```bash
# Initialize
curl -s -X POST "http://127.0.0.1:7777/messages?session_id=my_session" \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "1",
    "method": "initialize",
    "params": {
      "protocolVersion": "2024-11-05",
      "capabilities": {},
      "clientInfo": {"name": "my-agent", "version": "1.0"}
    }
  }'

# List tools
curl -s -X POST "http://127.0.0.1:7777/messages?session_id=my_session" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"2","method":"tools/list"}'

# Call a tool
curl -s -X POST "http://127.0.0.1:7777/messages?session_id=my_session" \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "3",
    "method": "tools/call",
    "params": {
      "name": "get_node_info",
      "arguments": {"nodePath": "/root/Main"}
    }
  }'

# SSE endpoint (long-lived connection for server-sent events)
curl -N http://127.0.0.1:7777/sse
```

---

## 📊 Common Use Cases

### "What's in the scene right now?"
```
get_scene_tree(includeProperties: false)
→ Full node hierarchy with names and types
```

### "Find all enemies in the game"
```
find_nodes_by_group(groupName: "enemies")
→ List of nodes with paths and types
```

### "What's the player's current position?"
```
get_property(nodePath: "/root/Main/GameWorld/Player", propertyName: "position")
→ {"x": 400, "y": 300}
```

### "Teleport the player"
```
set_property(
  nodePath: "/root/Main/GameWorld/Player",
  propertyName: "position",
  value: {"x": 500, "y": 200}
)
```

### "Is the game running smoothly?"
```
get_performance_stats()
→ FPS: 144, Node count: 42, Memory: 34.1 MB
```

### "Save this node as a scene file so I can analyze it"
```
# Save Player node with all children as .tscn
save_node_as_scene(
  nodePath: "/root/Main/GameWorld/Player",
  includeChildren: true,
  returnContent: true
)
→ Returns the .tscn text content with file stats (lines, node count, size)

# Save just the node itself, no children
save_node_as_scene(
  nodePath: "/root/Main/GameWorld/Enemies",
  includeChildren: false
)
→ Only the Enemies node, children excluded
```

### "Get me everything about this node"
```
get_node_context(nodePath: "/root/Main/GameWorld/Player")
→ parent, siblings, children in one response
```

---

## 🔍 Troubleshooting

**Symptom: `Connection refused`**
- Godot isn't running. Start the project.
- If headless: rebuild with `dotnet build`, then launch with `--no-window`.

**Symptom: `Cannot instantiate C# script — class not found`**
- Stale build. Clean and rebuild:
  ```bash
  rm -rf .godot/mono
  dotnet build
  ```

**Symptom: tool returns `success: false` with `error: ...`**
- Read the error message. Common causes:
  - Wrong `nodePath` — check with `node_exists`
  - Wrong property/method name — check with `list_properties`/`list_methods`
  - Server overloaded — wait and retry

**Symptom: signal errors flooding console**
- Known harmless issue with parameterized signal auto-connect. Only 0-arg signals are auto-monitored. Use `start_signal_monitoring` for specific named signals.

---

## 📁 Files Reference

| File | Purpose |
|------|---------|
| `addons/godot_mcp/GodotMcpServer.cs` | Core MCP server implementation (~2600 lines) |
| `addons/godot_mcp/GodotMcpPlugin.cs` | EditorPlugin — adds/removes autoload on enable/disable |
| `addons/godot_mcp/mcp_server.tscn` | Scene wrapper for autoload registration |
| `addons/godot_mcp/plugin.cfg` | Plugin metadata |
| `start-mcp-bridge.sh` | Python stdio bridge (for Claude Desktop compat) |
| `ExampleProject/` | Working test project with TestHarness scene |
