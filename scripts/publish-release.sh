#!/bin/sh
# Build native, self-contained C# CLI/MCP binaries and addon ZIP; no Docker/Actions.
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
TAG=${1:?Usage: scripts/publish-release.sh TAG [osx-arm64|osx-x64]}
RID=${2:-osx-arm64}
case "$TAG" in
  *[!a-zA-Z0-9._-]*|'') echo "Invalid release tag" >&2; exit 2 ;;
esac
case "$RID" in
  osx-arm64|osx-x64) ;;
  *) echo "Unsupported RID for this macOS build script: $RID" >&2; exit 2 ;;
esac

DOTNET=${DOTNET_BIN:-dotnet}
if ! command -v "$DOTNET" >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
fi
OUT="$ROOT/dist/$TAG"
PUBLISH="$OUT/.publish-$RID"
STAGE="$OUT/.stage-$RID"
mkdir -p "$OUT"
rm -rf "$PUBLISH" "$STAGE"
mkdir -p "$PUBLISH" "$STAGE"

# NativeAOT already emits a single executable; PublishSingleFile conflicts with PublishAot.
"$DOTNET" publish "$ROOT/RuntimeHost/RuntimeHost.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishAot=true -p:StripSymbols=true -p:DebugType=none \
  -p:TreatWarningsAsErrors=true -o "$PUBLISH"

# Refuse misleading tags: never label a 3.1.0-dev executable as v3.1.0.
EXPECTED_VERSION=${TAG#v}
ACTUAL_VERSION=$("$PUBLISH/GodotRuntimeMcp" version)
case "$ACTUAL_VERSION" in
  "godot-runtime-mcp $EXPECTED_VERSION ("*) ;;
  *) echo "Release tag $TAG does not match native executable version: $ACTUAL_VERSION" >&2; exit 2 ;;
esac

cp "$PUBLISH/GodotRuntimeMcp" "$STAGE/godot-mcp"
cp "$PUBLISH/GodotRuntimeMcp" "$STAGE/godot-mcp-server"
chmod 755 "$STAGE/godot-mcp" "$STAGE/godot-mcp-server"
# Each entry is a standalone executable, not a framework-dependent launcher.
tar -czf "$OUT/godot-mcp-$TAG-$RID.tar.gz" \
  -C "$STAGE" godot-mcp godot-mcp-server

# Pack from the same canonical Godot C# source embedded in the native binary.
if [ ! -f "$OUT/godot-mcp-addon-$TAG.zip" ]; then
  "$STAGE/godot-mcp" pack --path "$ROOT" --out "$OUT/godot-mcp-addon-$TAG.zip"
fi
(cd "$OUT" && shasum -a 256 godot-mcp-"$TAG"-*.tar.gz godot-mcp-addon-"$TAG".zip > SHA256SUMS.txt)
printf 'Ready for manual Release upload: %s\n' "$OUT"
ls -lh "$OUT"/*.tar.gz "$OUT"/*.zip "$OUT"/SHA256SUMS.txt
