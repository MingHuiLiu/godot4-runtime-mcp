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

SNAPSHOT_MANIFEST=$(find "$TEMP_RECORDING_DIR" -name manifest.json -type f | head -1)
test -n "$SNAPSHOT_MANIFEST"
SNAPSHOT_DIR=$(dirname "$SNAPSHOT_MANIFEST")
EXPORT_DIR="$TEMP_RECORDING_DIR/export-result"
JOB_FILE="$TEMP_RECORDING_DIR/export-job.json"
printf '{"snapshotDirectory":"%s","exportDirectory":"%s","strict":true}\n' "$SNAPSHOT_DIR" "$EXPORT_DIR" > "$JOB_FILE"
GODOT_MCP_EXPORT_WORKER=1 GODOT_MCP_CONFIG_DIR="$TEMP_RECORDING_DIR" \
    "$GODOT_BIN" --headless --path "$ROOT/ExampleProject" \
    --scene res://addons/godot_mcp/Recording/export_worker.tscn \
    -- --snapshot-job "$JOB_FILE" >"$OUTPUT" 2>&1 || { cat "$OUTPUT"; exit 1; }
cat "$OUTPUT"
grep -q 'SNAPSHOT_EXPORT_PASS:' "$OUTPUT"
test -s "$EXPORT_DIR/scene.tscn"
test -s "$EXPORT_DIR/reconstruction_report.json"
test -s "$EXPORT_DIR/runtime_state.json"
test -s "$EXPORT_DIR/dependencies.json"

# Native ResourceSaver may return OK even after printing a resource serialization error.
if grep -q '^ERROR:' "$OUTPUT"; then
    echo 'FAIL: Godot reported native export errors despite a successful return code' >&2
    exit 1
fi
grep -q '"quality":"checkpoint_exact"' "$EXPORT_DIR/reconstruction_report.json"
