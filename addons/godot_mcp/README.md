# Godot Runtime MCP v3 — C# Backend

This is the **single** Godot runtime backend used by the independent C# MCP server, CLI, and Agent Skills.

- `GodotMcpServer.cs`: 46 Godot operations, executed on the game's main thread.
- `GodotRuntimeTransport.cs`: unique per-process local HTTP listener, private token, instance registry and bounded request queue.
- `mcp_server.tscn`: Godot autoload scene.

Install with `./bin/godot-mcp install --project /absolute/path/to/game` (from the repository) or MCP `godot_install_backend`. The installer writes `[autoload] GodotMcpServer="*res://addons/godot_mcp/mcp_server.tscn"` into the game's `project.godot`.

Build and launch the **Godot 4 .NET** game. No EditorPlugin registration or global port 7777 is required; multiple games/processes may run concurrently. Discover active instances with `godot-mcp instances` and call using `--project` and `--instance`. Management and installation tools work even without the backend running.

The historical `execute_csharp` tool has no Roslyn implementation and must report unsupported.
