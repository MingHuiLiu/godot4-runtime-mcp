# Godot Runtime MCP v3.0.0 — Native AOT + Godot C# Backend

This release replaces the legacy in-Godot MCP HTTP/SSE design with one Godot C# backend and a standalone C# host, supporting both CLI and MCP.

## Artifacts

- `godot-mcp-v3.0.0-osx-arm64.tar.gz` — Apple Silicon macOS Native AOT. Contains **two independent native executable files**, `godot-mcp` (CLI) and `godot-mcp-server` (stdio MCP).
- `godot-mcp-v3.0.0-osx-x64.tar.gz` — Intel macOS Native AOT; same two executables.
- `godot-mcp-addon-v3.0.0.zip` — Godot 4 .NET plugin, containing `addons/godot_mcp/` (C# backend, local transport, tool manifest, scene and README).
- `SHA256SUMS.txt` — SHA-256 checksums for all 3 archives.

**No .NET installation is required on the CLI/MCP host machine for these AOT binaries.** Running the Godot backend still requires a Godot 4 **.NET** game build. The macOS executables are ad-hoc signed, not Apple-notarized.

## Usage

Extract the appropriate macOS archive, and run:

```sh
./godot-mcp status
./godot-mcp install --project /absolute/path/to/your/godot-csharp-project
./godot-mcp instances
./godot-mcp call get_scene_tree_simple --project /absolute/path/to/your/godot-csharp-project --instance ID
```

Set an MCP client's `command` to the full path of `godot-mcp-server` (no args required), or use `godot-mcp mcp`. Both work offline for initialization, tool discovery, and installing the backend.

The CLI downloads the latest GitHub Release's addon ZIP automatically; `--source local --path godot-mcp-addon-v3.0.0.zip` works offline. Existing differing addons are **not overwritten unless `--force` is specified**; forced upgrades create backups.

## Architecture and known limitations

- 46 Godot runtime operations + 5 offline management tools; CLI and MCP use the same C# `ToolService`.
- One authenticated random localhost endpoint per Godot game instance with multi-project and multi-instance routing.
- `execute_csharp` remains unsupported (previously a nonfunctional placeholder).
- No Docker, GitHub Actions, or additional packages used. Publish is reproducible using `scripts/publish-release.sh v3.0.0 osx-arm64` (or `osx-x64`).
- Native AOT is already a single native binary; combining `PublishAot` with `PublishSingleFile` is invalid on .NET 8.
- Linux/Windows AOT builds are **not** included; no unverified cross-OS executables are shipped.

## Verification

- Full solution builds with 0 C# warnings and 0 errors.
- All 27 standalone C# regression assertions pass.
- Native ARM64 CLI `version`, `tools`, `status`, `pack`, `install`, and `projects` tested.
- Native ARM64 MCP stdio `initialize` and `tools/list` tested.
- Intel x64 Native AOT binary executed and returned its version on the available Mac.
- Both archives and plugin ZIP integrity verified; SHA-256 digests computed.
