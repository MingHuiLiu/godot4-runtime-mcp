---
name: godot-runtime
description: Godot 4 .NET scene debugging and runtime control through godot-mcp CLI. Includes automatic installation from GitHub Releases before Godot starts.
---

# Godot Runtime CLI skill

Use godot-mcp (installed via npm link from this repository) or node /absolute/path/to/godot4-runtime-mcp/bin/godot-mcp.js. This CLI and the standalone MCP stdio service share all runtime tools and routing logic.

## Correct workflow

1. Determine the absolute Godot project directory; never silently guess or modify an unrelated game.
2. Run godot-mcp doctor --project "/path/to/game". It works when no backend is installed or running.
3. When backendInstalled is false, run godot-mcp install "/path/to/game". This automatically downloads godot-mcp-addon.zip from the latest GitHub Release and enables the Godot C# addon. To pin versions, add --version vX.Y.Z. If a release has not been published, explain the requirement; never silently substitute a branch download. Development only: use --archive /path/to/local.zip.
4. Start/restart the Godot .NET editor/game and compile C#; actual scene and runtime calls require a running backend. Installing the addon does not implicitly launch the game.
5. Call godot-mcp instances. If multiple processes are running (including multiple instances of the same game), choose the intended exact instance ID.
6. Call godot-mcp tools for argument schemas. Use godot-mcp call TOOL --project "/path/to/game" --instance ID --args '{"nodePath":"/root/Main"}'. Omit --instance only when exactly one process matches.
7. Treat operations changing scenes, nodes, scripts and properties as potentially destructive. Verify the selected process first.

Example:

    godot-mcp doctor --project "/games/demo"
    godot-mcp install "/games/demo"
    godot-mcp instances
    godot-mcp call get_scene_tree_simple --project "/games/demo" --args '{"maxDepth":3}'

If Godot is offline, the CLI still gives help, tool lists, project diagnostics and installation. Do not claim that scene operations ran when there was no active Godot process. Do not use the obsolete fixed :7777 server.

An MCP client can alternatively launch node /absolute/path/to/repo/bin/godot-mcp.js mcp and call godot_install_backend with projectPath before the Godot game has ever run.
