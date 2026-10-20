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

# Real MCP/CLI host-to-Godot handoff, after the recording game instance has exited.
dotnet build "$ROOT/RuntimeHost/RuntimeHost.csproj" -c Release >/dev/null
HOST_DLL="$ROOT/RuntimeHost/bin/Release/net8.0/GodotRuntimeMcp.dll"
SNAPSHOT_ID=$(basename "$SNAPSHOT_DIR")
GODOT_MCP_CONFIG_DIR="$TEMP_RECORDING_DIR" dotnet "$HOST_DLL" \
    godot-snapshot-export-tscn --project "$ROOT/ExampleProject" \
    --snapshot-id "$SNAPSHOT_ID" --godot-binary "$GODOT_BIN" > "$OUTPUT" 2>&1
cat "$OUTPUT"
EXPORT_JOB_ID=$(sed -n -E 's/.*"jobId": "([^"]+)".*/\1/p' "$OUTPUT" | head -1)
test -n "$EXPORT_JOB_ID"
SUCCESS=0
for attempt in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
    GODOT_MCP_CONFIG_DIR="$TEMP_RECORDING_DIR" dotnet "$HOST_DLL" \
        godot-snapshot-export-status --project "$ROOT/ExampleProject" \
        --job-id "$EXPORT_JOB_ID" > "$OUTPUT" 2>&1
    if grep -q '"status": "completed"' "$OUTPUT"; then
        SUCCESS=1
        break
    fi
    if grep -q '"status": "failed"' "$OUTPUT"; then
        cat "$OUTPUT"
        break
    fi
    sleep 0.5
done
if [ "$SUCCESS" -ne 1 ]; then echo 'FAIL: offline export job not completed' >&2; exit 1; fi
cat "$OUTPUT"
test -s "$TEMP_RECORDING_DIR/recordings/$(basename "$(dirname "$(dirname "$SNAPSHOT_DIR")")")/exports/$EXPORT_JOB_ID/output/scene.tscn"
echo 'SNAPSHOT_HOST_EXPORT_PASS: CLI initiated asynchronous offline .tscn export'
