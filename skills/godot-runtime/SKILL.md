---
name: godot-runtime-mcp
description: C# CLI and MCP tools for Godot 4 .NET. Automatically install the backend from GitHub Releases or the bundled source even if Godot is not installed/running.
---

# Godot Runtime MCP — C# Agent Skill

**Do not assume a game or plugin is running.** Do not use the legacy fixed port 7777, Python bridge, Node, Docker, or GitHub Actions. Both CLI and MCP are implemented in C#.

## Offline-first workflow

1. Run `./bin/godot-mcp status` to list known projects and running instances. No Godot needed.
2. Run `./bin/godot-mcp tools` to discover 46 Godot runtime tools + 5 management tools offline.
3. If the addon is missing, run `./bin/godot-mcp install --project /absolute/path/to/project`. The **C# CLI first downloads the addon from GitHub Releases** (https://github.com/MingHuiLiu/godot4-runtime-mcp/releases). If no latest Release exists or GitHub is unavailable, it automatically installs the **bundled compatible C# backend**.
4. Offline source alternative: `./bin/godot-mcp install --project /path/to/project --source local --path /path/to/repository`. A strict published version: `--source release --version TAG`. For existing changed addon, explicitly add `--force` (previous files are backed up outside the game tree).
5. Build the Godot 4 **.NET C#** game's csproj (`dotnet build /path/to/game/Game.csproj`), then launch the game. Installer registers `GodotMcpServer` Autoload. Pure GDScript games need a .NET C# project before the runtime backend can work.
6. Run `./bin/godot-mcp instances`. Each **process** has its own `projectPath`, `instanceId`, `pid`, and loopback port.
7. Call `./bin/godot-mcp call get_scene_tree_simple --project /path/to/project --instance INSTANCE_ID --args '{"maxDepth":2}'`.

**Multiple projects and multiple game processes:** never select an arbitrary port or process. If routing is ambiguous, list instances and explicitly specify `--project` and `--instance`.

## MCP mode without Godot

Use `/absolute/path/to/Godot-Mcp/start-mcp-bridge.sh` in a standard stdio MCP configuration (or `dotnet /path/to/GodotRuntimeMcp.dll mcp`). The C# MCP host will answer `initialize`, `tools/list`, and these always-available management tools without any backend:

- `godot_status`, `godot_list_projects`, `godot_list_instances`, `godot_setup_guide`.
- **`godot_install_backend`**: e.g. `{"project":"/path/to/game","source":"auto"}`.

All runtime tools accept optional `project` and `instance`. If Godot is stopped, runtime tools return an MCP `isError: true` with a useful explanation, never a false positive.

`execute_csharp` is a legacy Roslyn placeholder and is unavailable; do not claim to have executed C#. Mutating runtime tools may affect game state; make sure the selected instance is intentional. Read `README.md` for full architecture and installation details.
