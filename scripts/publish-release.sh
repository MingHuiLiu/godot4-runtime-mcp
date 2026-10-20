#!/bin/sh
# Build self-contained native C# CLI/MCP executables and addon ZIP (no Actions/Docker).
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
TAG=${1:?Usage: scripts/publish-release.sh TAG [osx-arm64|osx-x64]}
RID=${2:-osx-arm64}
case "$TAG" in *[!a-zA-Z0-9._-]*|'') echo "Invalid tag" >&2; exit 2;; esac
case "$RID" in osx-arm64|osx-x64) ;; *) echo "Unsupported target: $RID" >&2; exit 2;; esac
DOTNET=${DOTNET_BIN:-dotnet}
if ! command -v "$DOTNET" >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"; fi
OUT="$ROOT/dist/$TAG"
PUBLISH="$OUT/.publish-$RID"
STAGE="$OUT/.stage-$RID"
mkdir -p "$OUT"
rm -rf "$PUBLISH" "$STAGE"
mkdir -p "$PUBLISH" "$STAGE"
"$DOTNET" publish "$ROOT/RuntimeHost/RuntimeHost.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishAot=true -p:StripSymbols=true -p:DebugType=none \
  -p:TreatWarningsAsErrors=true -o "$PUBLISH"
EXPECTED_VERSION=${TAG#v}
ACTUAL_VERSION=$("$PUBLISH/GodotRuntimeMcp" version)
case "$ACTUAL_VERSION" in
  "godot-runtime-cli $EXPECTED_VERSION ("*) ;;
  *) echo "Tag $TAG does not match executable version: $ACTUAL_VERSION" >&2; exit 2;;
esac
cp "$PUBLISH/GodotRuntimeMcp" "$STAGE/godot-runtime-cli"
cp "$PUBLISH/GodotRuntimeMcp" "$STAGE/godot-mcp-server"
chmod 755 "$STAGE/godot-runtime-cli" "$STAGE/godot-mcp-server"
tar -czf "$OUT/godot-runtime-tools-$TAG-$RID.tar.gz" \
  -C "$STAGE" godot-runtime-cli godot-mcp-server
# Recreate the addon for each build: never publish a stale ZIP after source changes.
rm -f "$OUT/godot-runtime-addon-$TAG.zip"
"$STAGE/godot-runtime-cli" pack --path "$ROOT" --out "$OUT/godot-runtime-addon-$TAG.zip"
(cd "$OUT" && shasum -a 256 "godot-runtime-tools-$TAG-"*.tar.gz "godot-runtime-addon-$TAG.zip" > SHA256SUMS.txt)
printf 'Release artifacts: %s\n' "$OUT"
ls -lh "$OUT"/*.tar.gz "$OUT"/*.zip "$OUT"/SHA256SUMS.txt
