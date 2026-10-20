---
name: godot-runtime-mcp
description: Godot 4 .NET inspection/control via a standalone C# Native AOT CLI and MCP server. Bootstrap the CLI directly from GitHub Releases even when this SKILL.md is the only local file.
---

# Godot Runtime MCP — self-contained Skill

You may receive **only this SKILL.md**, with no repository checkout, scripts, CLI, .NET SDK, Godot addon, or MCP configuration. Do not assume anything else is installed. The CLI is named **`godot-runtime-cli`**; the independent MCP stdio executable is `godot-mcp-server`.

## 1. Locate or bootstrap the CLI

First run `command -v godot-runtime-cli` (or check `$HOME/.local/bin/godot-runtime-cli`). If present, use it; otherwise download a **published** release from the official repository. The following self-contained block requires only standard macOS `sh`, `curl`, `tar`, `awk`, and `shasum` tools. It does **not** depend on files alongside this Skill, Python, Node, Docker, Homebrew, or .NET.

**On macOS (Apple Silicon or Intel), execute this complete block:**

```sh
set -eu
REPO="MingHuiLiu/godot4-runtime-mcp"
INSTALL_DIR="$HOME/.local/bin"
if [ "$(uname -s)" != "Darwin" ]; then
  echo "This release provides macOS Native AOT binaries only; do not run a macOS executable on Linux/Windows." >&2
  exit 1
fi
case "$(uname -m)" in
  arm64) RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "Unsupported Mac architecture" >&2; exit 1 ;;
esac
if [ -x "$INSTALL_DIR/godot-runtime-cli" ] && [ -x "$INSTALL_DIR/godot-mcp-server" ]; then
  "$INSTALL_DIR/godot-runtime-cli" version
else
  TMP=$(mktemp -d)
  trap 'rm -rf "$TMP"' EXIT HUP INT TERM
  # 'latest' is the GitHub Release API, not the newest git tag or a guessed URL.
  if ! curl --fail --silent --show-error --location \
    "https://api.github.com/repos/$REPO/releases/latest" -o "$TMP/release.json"; then
    echo "No accessible GitHub Release. A Git tag alone cannot provide an AOT binary; ask the maintainer to upload the release assets." >&2
    exit 1
  fi
  TAG=$(sed -n 's/^[[:space:]]*"tag_name":[[:space:]]*"\([^"]*\)".*/\1/p' "$TMP/release.json" | head -n 1)
  case "$TAG" in
    ""|*[!a-zA-Z0-9._-]*) echo "Missing/invalid Release tag" >&2; exit 1 ;;
  esac
  ASSET="godot-runtime-tools-$TAG-$RID.tar.gz"
  BASE="https://github.com/$REPO/releases/download/$TAG"
  curl --fail --silent --show-error --location "$BASE/$ASSET" -o "$TMP/$ASSET" || {
    echo "Missing $ASSET in Release $TAG; this platform may not be published." >&2
    exit 1
  }
  curl --fail --silent --show-error --location "$BASE/SHA256SUMS.txt" -o "$TMP/SHA256SUMS.txt"
  (cd "$TMP" && awk -v file="$ASSET" '$2 == file { print; found=1 } END { if (!found) exit 1 }' SHA256SUMS.txt > check.txt && shasum -a 256 -c check.txt) || {
    echo "Checksum verification failed: refusing to install." >&2
    exit 1
  }
  # Strict archive allowlist: prevent unsafe paths before extraction.
  ENTRIES=$(tar -tzf "$TMP/$ASSET")
  case "$ENTRIES" in
    "godot-runtime-cli
godot-mcp-server"|"godot-mcp-server
godot-runtime-cli") ;;
    *) echo "Unexpected tar entries: refusing to extract." >&2; exit 1 ;;
  esac
  mkdir -p "$TMP/unpacked" "$INSTALL_DIR"
  tar -xzf "$TMP/$ASSET" -C "$TMP/unpacked"
  test -f "$TMP/unpacked/godot-runtime-cli" && test -f "$TMP/unpacked/godot-mcp-server"
  test ! -L "$TMP/unpacked/godot-runtime-cli" && test ! -L "$TMP/unpacked/godot-mcp-server"
  install -m 755 "$TMP/unpacked/godot-runtime-cli" "$INSTALL_DIR/godot-runtime-cli"
  install -m 755 "$TMP/unpacked/godot-mcp-server" "$INSTALL_DIR/godot-mcp-server"
  "$INSTALL_DIR/godot-runtime-cli" version
fi
```

Always invoke `$HOME/.local/bin/godot-runtime-cli` by absolute path when PATH is not configured. If the GitHub Release API returns 404 or its assets are missing, **do not fabricate download URLs or claim success**. The maintainer must publish a real Release with the assets first. Do not automatically install .NET or clone the repository as an unrequested workaround. Linux/Windows Native AOT downloads require corresponding published, verified platform builds; the current macOS-only release script does not create them.

## 2. CLI discovery and direct commands

All 46 Godot runtime tools plus 5 management tools have direct CLI commands; no `call TOOL --args JSON` wrapper is required. **Every option comes from the shared tool schema**, accepts kebab-case, and can be viewed offline:

```sh
"$HOME/.local/bin/godot-runtime-cli" help
"$HOME/.local/bin/godot-runtime-cli" tools --search scene
"$HOME/.local/bin/godot-runtime-cli" help get-node-info
"$HOME/.local/bin/godot-runtime-cli" get-scene-tree-simple --help
"$HOME/.local/bin/godot-runtime-cli" tools --json
```

Example operations, after selecting the target process:

```sh
"$HOME/.local/bin/godot-runtime-cli" status
"$HOME/.local/bin/godot-runtime-cli" install --project /absolute/path/to/game
# Build the Godot 4 .NET game C# project and start that game process.
"$HOME/.local/bin/godot-runtime-cli" instances
"$HOME/.local/bin/godot-runtime-cli" get-scene-tree-simple --max-depth 2 --project /absolute/path/to/game --instance ID
"$HOME/.local/bin/godot-runtime-cli" get-node-info --node-path /root/Main/Player --project /absolute/path/to/game --instance ID
"$HOME/.local/bin/godot-runtime-cli" set-property --node-path /root/Main/Player --property-name visible --value false --project /absolute/path/to/game --instance ID
```

Flags include `--node-path`, `--max-depth`, `--property-name`, `--value`, etc. Booleans accept `--flag`, `--no-flag`, `--flag true/false`; arrays or objects use JSON, e.g. `--args '[1,2]'` for tools with an `args` array parameter. For the entire argument object use `--json '{"nodePath":"/root/Main"}'`. The legacy `call TOOL --args '{"...":...}'` form remains for compatibility, but prefer direct commands. `tools --search TERM` discovers all commands, not a curated subset.

## 3. Backend installation and safety

The `install` management command uses GitHub Releases when available, falling back to the **embedded matching Godot C# addon** when Release/network access is unavailable. Therefore, after the CLI executable is downloaded, installing the Godot addon no longer requires network access. `--source local --path /path/to/addon.zip` installs a local ZIP; `--source release --version TAG` demands an explicit release. The installer edits the target project's Autoload. A differing addon needs explicit `--force`, which backs up the previous version. The Godot project must use **Godot 4 .NET**, be built as C#, and run its game for runtime tools to work.

With multiple Godot projects/processes, run `instances` and pass `--project` and `--instance` to avoid accidentally mutating the wrong game. The `execute-csharp` legacy tool is unsupported; do not claim any C# code executed. Only use state-mutating tools against trusted projects.

## 4. MCP stdio (optional)

When the user's MCP client permits a local command, use **`$HOME/.local/bin/godot-mcp-server`** (the expanded absolute path, no arguments) in its MCP configuration. MCP init, listing and five management tools work without a running Godot game. A downloaded standalone Skill does not require an MCP client; CLI operation is sufficient.
