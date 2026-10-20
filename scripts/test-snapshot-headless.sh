#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
GODOT_BIN=${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}
dotnet build "$ROOT/ExampleProject/ExampleProject.csproj" -c Debug
TEMP_RECORDING_DIR=$(mktemp -d "${TMPDIR:-/tmp}/godot-mcp-snapshot-test.XXXXXX")
OUTPUT=$(mktemp "${TMPDIR:-/tmp}/godot-mcp-snapshot-output.XXXXXX")
trap 'rm -rf "$TEMP_RECORDING_DIR"; rm -f "$OUTPUT"' EXIT
GODOT_MCP_CONFIG_DIR="$TEMP_RECORDING_DIR" "$GODOT_BIN" --headless --path "$ROOT/ExampleProject" --scene res://SnapshotCaptureProbe.tscn --quit-after 300 >"$OUTPUT" 2>&1 || { cat "$OUTPUT"; exit 1; }
cat "$OUTPUT"
grep -q 'SNAPSHOT_PROBE_PASS:' "$OUTPUT"
grep -q 'SNAPSHOT_JOB_PASS:' "$OUTPUT"
