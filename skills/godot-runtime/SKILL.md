---
name: godot-runtime-mcp
description: Godot 4 .NET runtime inspection/control through the C# CLI and standalone MCP server; support offline installation and multiple instances.
---

# Godot Runtime MCP — Agent Skill

The Godot plugin is **not** an MCP protocol server. The independent C# host serves CLI, stdio MCP, and optional localhost HTTP. Use the native release executable `godot-mcp`, or from a source checkout `./scripts/godot-mcp-dev.sh` (requires .NET SDK).

## Workflow

1. `godot-mcp status` and `godot-mcp tools` work even if Godot is not running (46 runtime + 5 management tools).
2. Install if needed: `godot-mcp install --project /absolute/path/to/game`. Default auto source tries GitHub Releases, then the compatible embedded C# backend when Releases/network are unavailable. Local alternative: `--source local --path /path/to/addon.zip`. A changed install requires explicit `--force` (backup is created).
3. The target game must use Godot 4 **.NET C#**. Build the project's C# csproj and launch the game; installer registers `GodotRuntimeBackend` Autoload.
4. `godot-mcp instances` lists running projects and per-process instance IDs. Never guess a PID or port.
5. `godot-mcp call get_scene_tree_simple --project /absolute/path/to/game --instance INSTANCE_ID --args '{"maxDepth":2}'` targets an exact instance.
6. Configure an MCP client `command` with the absolute path of the native `godot-mcp-server` executable (no arguments), or the source development compatibility shim `start-mcp-bridge.sh`.

Offline MCP management tools: `godot_status`, `godot_list_projects`, `godot_list_instances`, `godot_setup_guide`, `godot_install_backend`. Runtime tools require a real running game. On multiple matches, supply `project` and `instance` to avoid controlling the wrong process. `execute_csharp` is unsupported (no Roslyn); do not claim arbitrary C# was executed. Mutating operations affect live game state; use only with trusted local agents.
