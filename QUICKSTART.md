# Quickstart (CLI + MCP)

For full instructions see README.md and skills/godot-runtime/SKILL.md.

    npm link
    godot-mcp doctor --project /absolute/game
    godot-mcp install /absolute/game
    # Compile and start Godot .NET game
    godot-mcp instances
    godot-mcp call get_time --project /absolute/game

Use node bin/godot-mcp.js mcp as the standalone MCP stdio server. Its godot_install_backend tool works with no running game. Publish a Release asset godot-mcp-addon.zip manually (npm run pack); there are no GitHub Actions.
