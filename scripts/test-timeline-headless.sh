#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
GODOT_BIN=${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}
DATA=$(mktemp -d "${TMPDIR:-/tmp}/godot-mcp-timeline.XXXXXX")
LOG=$(mktemp "${TMPDIR:-/tmp}/godot-mcp-timeline-log.XXXXXX")
trap 'rm -rf "$DATA"; rm -f "$LOG"' EXIT
dotnet build "$ROOT/ExampleProject/ExampleProject.csproj" -c Debug > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
GODOT_MCP_CONFIG_DIR="$DATA" "$GODOT_BIN" --headless --path "$ROOT/ExampleProject" \
    --scene res://TimelineProbe.tscn --quit-after 1500 > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
cat "$LOG"
grep -q 'TIMELINE_PROBE_PASS:' "$LOG"
