# Godot runtime backend (v3)

This is the single shared C# Godot runtime backend. It implements 46 existing runtime tools executed on Godot's main thread. Both CLI and standalone MCP use it through localhost HTTP.

The installer enables the editor plugin and the mcp_server.tscn autoload. Each process binds a different loopback port and registers a private JSON descriptor in ~/.godot-runtime-mcp/instances/. Requests to GET /health, GET /tools and POST /call require X-Godot-MCP-Token. The direct legacy Godot SSE/MCP endpoint is disabled: use the standalone node CLI/MCP frontend instead.

See repository README.md and skills/godot-runtime/SKILL.md for the installation/selection workflow and release packaging.
