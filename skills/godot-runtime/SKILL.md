---
name: godot-runtime-mcp
description: >-
  Inspect, debug, and safely operate a live Godot 4 .NET game through Godot Runtime
  MCP or the godot-runtime-cli. Use for scene-tree exploration, node and property
  inspection, signals, logs, screenshots, runtime method calls, and explicit
  scene/property changes. Also use when setting up the Godot runtime backend,
  connecting an MCP client, or bootstrapping the CLI on a machine that has only
  this SKILL.md. Prefer this skill for live-game questions even when the user
  does not mention MCP or the CLI by name.
compatibility: >-
  A local terminal or a connected Godot Runtime MCP client is needed to execute
  operations. Standalone binary bootstrap supports macOS arm64 and x86_64 with
  curl, plutil, tar, shasum and install; a published GitHub Release is required.
  Runtime access requires a running, compiled Godot 4 .NET project.
---

# Godot Runtime MCP

Operate a **running Godot 4 .NET game**, not the Godot editor's static scene files. The current source build exposes 48 runtime tools and 10 offline management/snapshot tools via an MCP server and a standalone CLI. This skill is deliberately **self-contained**: assume no other file, script, repository checkout, SDK, or MCP configuration accompanies it.

## Workflow

1. **Choose an available interface.** If this agent already has working Godot Runtime MCP tools, use them directly (their names use `snake_case`). Otherwise find `godot-runtime-cli` in `PATH` or `$HOME/.local/bin/`. Use the existing executable if it works. Only if it is absent, follow [Standalone bootstrap](#standalone-bootstrap).
2. **Discover before acting.** Run `status` and `instances` to see installed projects and verified live game processes. No running game is required for discovery, help, or backend installation.
3. **Identify the target.** Select the user's intended project and instance, not an arbitrary available game. Always specify `--project` and, when relevant, `--instance`. If multiple instances match and the user has not identified one, ask which process to use before changing state.
4. **Consult the live tool schema.** Use `tools --search TERM` and `COMMAND --help` rather than guessing argument names. Read state first; perform only changes within the user's requested scope.
5. **Verify and report.** Re-read the affected node/property or inspect fresh logs/signals after a change. Report the project, instance, operation, result, and any limitation or error; do not claim unverified success.

## Run commands

Resolve the binary once per shell session (environment variables are not retained between independent terminal calls):

```sh
CLI=$(command -v godot-runtime-cli 2>/dev/null || true)
[ -n "$CLI" ] || CLI="$HOME/.local/bin/godot-runtime-cli"
"$CLI" version
"$CLI" status
"$CLI" instances
"$CLI" tools --search scene
"$CLI" get-node-info --help
```

If `version` fails, use [Standalone bootstrap](#standalone-bootstrap) only after diagnosing an absent or incompatible executable; do not silently overwrite an existing installation.

The CLI accepts **direct kebab-case commands** for all tools (58 in this source build; older releases may differ). Options are generated from the embedded tool manifest; use `--help` to discover the actual parameters and required fields. You can also run `tools --json` for the complete schema.

### Read-only inspection

Replace example paths and IDs with results from `instances` and `get-scene-tree-simple`:

```sh
"$CLI" get-scene-tree-simple --project "/absolute/path/to/game" --max-depth 3
"$CLI" get-node-info --project "/absolute/path/to/game" --node-path "/root/Main/Player"
"$CLI" get-property --project "/absolute/path/to/game" --node-path "/root/Main/Player" --property-name visible
"$CLI" get-logs --project "/absolute/path/to/game" --count 50
"$CLI" get-signal-events --project "/absolute/path/to/game" --count 20
```

These paths are illustrative, not guaranteed nodes. Inspect the actual tree before choosing a path. To disambiguate processes, append `--instance ACTUAL_INSTANCE_ID`; a unique prefix is accepted. A screenshot tool is also available: discover `take-screenshot --help`, then inspect its returned result and file path rather than assuming an image was saved.

### Record and reconstruct a scene checkpoint (source build)

Use these commands only with a compiled, trusted Godot 4 .NET project and a backend version that advertises the snapshot tools. The existing published v3.1.0 release may not contain these additions.

```sh
"$CLI" godot-snapshot-capture --project "/absolute/path/to/game" --strict true
"$CLI" godot-snapshot-job-status --project "/absolute/path/to/game" --job-id JOB_ID
"$CLI" godot-snapshot-list --project "/absolute/path/to/game"
"$CLI" godot-snapshot-nodes --project "/absolute/path/to/game" --snapshot-id SNAPSHOT_ID --limit 50
"$CLI" godot-snapshot-export-tscn --project "/absolute/path/to/game" --snapshot-id SNAPSHOT_ID
"$CLI" godot-snapshot-export-status --project "/absolute/path/to/game" --job-id EXPORT_JOB_ID
```

The export command uses a separate Godot Mono process and remains possible after the original game exits. Inspect `reconstruction_report.json` before treating `scene.tscn` as complete: internal nodes, unsupported properties and cyclic Resources can prevent strict restoration. Script private fields, live threads, and deterministic time-travel are not captured.

### Explicit runtime modifications

Operations such as `set-property`, `create-node`, `call-method`, and signal changes act on the real running game. Obtain the user's authorization for modifications if not already clear from the request. Inspect the target and current value first, execute the smallest intended change, and verify afterward.

```sh
"$CLI" get-property --project "/absolute/path/to/game" --node-path "/root/Main/Player" --property-name visible
"$CLI" set-property --project "/absolute/path/to/game" --node-path "/root/Main/Player" --property-name visible --value false
"$CLI" get-property --project "/absolute/path/to/game" --node-path "/root/Main/Player" --property-name visible
```

`--value` accepts JSON scalars (`false`, `42`, `3.14`, `null`), ordinary text, or a quoted JSON object where the property supports it. Arrays and objects must be passed as correctly quoted JSON. For example, `call-method --args '[1,2]'` supplies that **tool's method-argument array**, whereas `--json '{"nodePath":"/root/Main"}'` supplies an entire tool argument object. The older `call TOOL --args '{"...":...}'` syntax is supported but not preferred.

## If the game is offline

- **CLI missing:** use the bootstrap below on supported macOS; do not attempt runtime operations until the executable works.
- **Backend not installed:** with the user's permission, run `"$CLI" install --project "/absolute/path/to/game"`. This edits the target project's addon and Autoload configuration. The installer uses Release-first discovery and can fall back to its **embedded addon**; if a different addon already exists, it refuses to overwrite unless `--force` is explicitly supplied. Explain the change and backup before forcing an upgrade.
- **Backend installed but no instance listed:** verify this is a Godot **4 .NET/C#** project, build its C# assembly, and start the game (not just the editor). Re-run `instances`. Do not interpret installation as proof that the game is running.
- **Multiple instances:** preserve the user's choice with `--project` and `--instance` on subsequent commands. Never mutate an arbitrarily selected instance.
- **Unsupported tool:** `execute-csharp` is a legacy placeholder; dynamic Roslyn execution is not bundled. Report that no C# code was run.
- **Unexpected error:** inspect the CLI's stderr, `status`, and live-process list; resolve the cause before retrying. Never report a timed-out operation as definitely applied.

For a provided local addon archive, use `"$CLI" install --project "/absolute/path/to/game" --source local --path "/absolute/path/to/addon.zip"`. Avoid upgrading a working user's project merely because a newer package exists.

## Standalone bootstrap

Use this section **only when no suitable CLI is available and the user has authorized installing the tool**. It works when this document is the **only local skill file**. The macOS download contains both `godot-runtime-cli` and `godot-mcp-server`; neither needs .NET, Python, Node, Homebrew, or a repository checkout.

The following block is self-contained. Run it with a local macOS shell, not by pasting guessed download links:

```sh
(
  set -eu
  REPO="MingHuiLiu/godot4-runtime-mcp"
  case "$(uname -s)/$(uname -m)" in
    Darwin/arm64) RID="osx-arm64" ;;
    Darwin/x86_64) RID="osx-x64" ;;
    *) echo "No verified native download for this OS/architecture." >&2; exit 1 ;;
  esac

  TMP=$(mktemp -d)
  trap 'rm -rf "$TMP"' 0
  trap 'exit 1' 1 2 3 15

  # Fetch release metadata; a Git tag without published assets is insufficient.
  curl -fLsS --retry 2 --connect-timeout 10 \
    "https://api.github.com/repos/$REPO/releases/latest" -o "$TMP/release.json" || {
      echo "No accessible published GitHub Release. Do not guess an asset URL." >&2
      exit 1
    }
  TAG=$(/usr/bin/plutil -extract tag_name raw -o - "$TMP/release.json") || {
    echo "Release metadata has no valid tag_name." >&2; exit 1;
  }
  case "$TAG" in
    ""|*[!a-zA-Z0-9._-]*) echo "Unsafe Release tag." >&2; exit 1 ;;
  esac

  ASSET="godot-runtime-tools-$TAG-$RID.tar.gz"
  BASE="https://github.com/$REPO/releases/download/$TAG"
  curl -fLsS --retry 2 --connect-timeout 10 \
    "$BASE/$ASSET" -o "$TMP/$ASSET" || {
      echo "Release $TAG does not provide $ASSET." >&2; exit 1;
    }
  curl -fLsS --retry 2 --connect-timeout 10 \
    "$BASE/SHA256SUMS.txt" -o "$TMP/SHA256SUMS.txt"

  # Refuse missing or mismatched checksums before extracting executables.
  (
    cd "$TMP"
    awk -v asset="$ASSET" '$2 == asset {print; found++} END {if (found != 1) exit 1}' \
      SHA256SUMS.txt > CHECKSUM.txt
    shasum -a 256 -c CHECKSUM.txt
  ) || { echo "Missing checksum or SHA-256 mismatch; nothing installed." >&2; exit 1; }

  # The published package may contain only these two top-level files.
  ENTRIES=$(tar -tzf "$TMP/$ASSET")
  case "$ENTRIES" in
    "godot-runtime-cli
godot-mcp-server"|"godot-mcp-server
godot-runtime-cli") ;;
    *) echo "Unexpected archive contents; refusing extraction." >&2; exit 1 ;;
  esac

  mkdir -p "$TMP/unpacked" "$HOME/.local/bin"
  tar -xzf "$TMP/$ASSET" -C "$TMP/unpacked"
  for name in godot-runtime-cli godot-mcp-server; do
    [ -f "$TMP/unpacked/$name" ] && [ ! -L "$TMP/unpacked/$name" ] || {
      echo "Missing or unsafe executable: $name" >&2; exit 1;
    }
  done
  install -m 755 "$TMP/unpacked/godot-runtime-cli" "$HOME/.local/bin/godot-runtime-cli"
  install -m 755 "$TMP/unpacked/godot-mcp-server" "$HOME/.local/bin/godot-mcp-server"
  "$HOME/.local/bin/godot-runtime-cli" version
)
```

After installation, run `"$HOME/.local/bin/godot-runtime-cli" status` and `instances`. Use this absolute path when `~/.local/bin` is not on `PATH`.

**Bootstrap limits:** This procedure supports only the published **macOS arm64/x86_64** asset names. On Linux or Windows, an existing compatible executable may work, but do not download or execute a macOS binary. If GitHub's Release API returns 404, the asset or checksum is absent, or verification fails, stop and explain that a **published release with assets** is required. A source tag alone is not a distributable binary. SHA-256 from the same release helps detect corruption but does not independently establish publisher identity. Do not bypass macOS security prompts or clear quarantine attributes automatically.

## Optional MCP connection

If a working Godot Runtime MCP integration is already present, use it instead of installing a second interface. To configure a new local MCP client, point its stdio `command` at the **absolute path** to `$HOME/.local/bin/godot-mcp-server` (expand `$HOME` in the configuration). No arguments are required; the executable starts the official MCP stdio server by default.

The CLI can also run `mcp` for stdio or `serve --port PORT --token SECRET` for **loopback-only**, authenticated MCP Streamable HTTP at `http://127.0.0.1:PORT/mcp`. Prefer stdio for a local Agent. Treat HTTP tokens as secrets and do not expose the server to the network.

## Operating boundaries

- Runtime operations are for **trusted local Godot projects**. Installation may modify project files; game tools may mutate live state or invoke methods. Use the user's permissions and requested scope.
- Prefer read-only commands to establish facts, then make a targeted change and verify it. Avoid broad exploratory mutations, unsupported promises, and unnecessary upgrades.
- This skill does not require companion files. The CLI's embedded catalog is the source of truth for exact tool names and flags.
- If terminal access, an authorized MCP connection, a published macOS binary, or a running Godot 4 .NET process is missing, clearly state which prerequisite is unavailable. Do not fabricate execution results.
