# Godot Runtime MCP v3 — one backend, CLI + MCP

Godot 4 .NET/C# runtime debugging with one shared backend, a standalone MCP stdio server, and a zero-dependency Node.js CLI. Both frontends start and provide installation/status commands even without the game running or the addon installed.

## Architecture

    AI / MCP client  ->  node bin/godot-mcp.js mcp  ─┐
                                                      ├─ shared runtime core ─ instance router ─ localhost/token ─ Godot addon
    AI / skill + CLI ->  godot-mcp CLI              ─┘

The only Godot backend is in addons/godot_mcp/. It implements the original 46 runtime operations on Godot's main thread. The CLI and MCP use the same schemas, discovery, routing and installer.

Each running Godot game process binds a DIFFERENT ephemeral 127.0.0.1 port and publishes a private descriptor containing a project path, PID, instance ID and random access token to ~/.godot-runtime-mcp/instances/. The CLI verifies each instance through an authenticated health check, ignoring stale records. No persistent daemon or fixed :7777 port is needed.

## Requirements

- Node 20 or later for CLI/MCP (no npm dependencies).
- Godot 4 **.NET/C#** edition and a project with a .csproj for runtime operations.
- A published GitHub Release asset called godot-mcp-addon.zip for online installation. The repository did not yet have a published Release when this refactor was started. See manual release packaging below.

## CLI setup and use

    git clone https://github.com/MingHuiLiu/godot4-runtime-mcp.git
    cd godot4-runtime-mcp
    npm link
    godot-mcp doctor --project /absolute/path/to/game
    godot-mcp install /absolute/path/to/game
    # Open or restart Godot .NET editor; compile and run the project
    godot-mcp instances
    godot-mcp tools
    godot-mcp call get_scene_tree --project /absolute/path/to/game --args '{"includeProperties":false}'

All commands can also use: node bin/godot-mcp.js COMMAND.

The install command downloads the selected GitHub Release ZIP (latest by default), checks for invalid/traversing ZIP members and GitHub-provided SHA-256 where available, and atomically installs it in addons/godot_mcp. It enables the editor plugin and autoload in project.godot, preserving other plugins. Reinstallation is safe. It never relies on an unversioned branch or GitHub Actions.

    godot-mcp install /absolute/game --version v3.0.0
    godot-mcp install /absolute/game --archive /tmp/godot-mcp-addon.zip

The second form is intended for local development and offline Release-package testing. To *run* runtime calls, Godot must be launched with the plugin available and C# compiled; offline CLI/MCP management tools still work without Godot.

## MCP client setup

Set your MCP client to launch:

    {
      "mcpServers": {
        "godot-runtime": {
          "command": "node",
          "args": ["/absolute/path/to/godot4-runtime-mcp/bin/godot-mcp.js", "mcp"]
        }
      }
    }

MCP stdio initializes without any running Godot. It exposes all 46 runtime tool schemas and four offline-safe management tools: godot_list_instances, godot_backend_status, godot_install_backend, godot_list_runtime_tools.

To install from MCP invoke godot_install_backend with an absolute projectPath. Then compile/start the Godot game and use godot_list_instances to choose a process. Runtime calls return a meaningful error if the selected game is not running; they do not block MCP startup.

## Several game projects and multiple processes per project

    godot-mcp instances
    godot-mcp call get_time --project /games/game-a --instance INSTANCE_ID
    godot-mcp call get_logs --project /games/game-b --args '{"limit":20}'

When one process matches, it is automatically selected. If several match, you MUST specify an instance ID (or the exact project path if that yields only one). No request is silently forwarded to an arbitrary game process. All endpoint requests are authenticated locally; do not disclose private instance descriptors. The CLI environment variable GODOT_MCP_HOME overrides the registry path for tests only, not normal game use.

## Publishing GitHub Releases manually (NO GitHub Actions)

    npm run tools:generate
    npm test
    npm run pack

This creates dist/godot-mcp-addon.zip and prints its SHA-256. Manually upload the file as a GitHub Release asset at https://github.com/MingHuiLiu/godot4-runtime-mcp/releases. Keep the filename exactly godot-mcp-addon.zip. Once a release exists, online installation works on the desired tag or the latest release. No GitHub Actions are used.

## Tests and safety

    npm test
    dotnet build /canonical/path/to/installed/GodotGame.csproj

Node integration tests cover offline mode, secure ZIP install, idempotency, preserving editor plugins, project/process selection, and MCP stdio handshake. Godot .NET compilation and actual game launches should be tested against the project's Godot SDK.

The addon binds only to loopback and requires a different random token per process for every request. Mutations and scene-tree reads are queued on the engine's main thread. As with any runtime debugging tool, method calls and C# execution must only be used with trusted agents.

**Breaking change:** legacy direct Godot MCP over fixed-port HTTP/SSE, and the separate legacy .NET McpServer, are no longer supported entry points. Use the standalone MCP stdio server or the CLI. Earlier architecture documents describe historical versions and are not authoritative for v3.

See skills/godot-runtime/SKILL.md for instructions for AI CLI agents.

### macOS path note

The Godot .NET source generator relies on project paths. If a test project lives under the macOS /tmp symlink, build and launch it consistently via /private/tmp (the real path); otherwise Godot may fail to resolve C# script classes even though compilation succeeds.
