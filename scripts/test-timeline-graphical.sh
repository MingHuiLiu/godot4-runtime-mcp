#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
GODOT_BIN=${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}
DIR=$(mktemp -d "${TMPDIR:-/tmp}/godot-gui-frames.XXXXXX")
LOG=$(mktemp "${TMPDIR:-/tmp}/godot-gui-frames-log.XXXXXX")
trap 'rm -rf "$DIR"; rm -f "$LOG"' EXIT
dotnet build "$ROOT/ExampleProject/ExampleProject.csproj" -c Debug > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
GODOT_MCP_CONFIG_DIR="$DIR" "$GODOT_BIN" --path "$ROOT/ExampleProject" \
    --scene res://TimelineProbe.tscn --quit-after 1500 > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
grep -q 'TIMELINE_REAL_PIXELS_PASS:' "$LOG" || { cat "$LOG"; exit 1; }
grep -q 'TIMELINE_SCREENSHOT_PASS: 4 captures, 0' "$LOG" || { cat "$LOG"; exit 1; }
count=$(find "$DIR/recordings" -name '*.png' -type f | wc -l | tr -d ' ')
test "$count" = 4
echo 'TIMELINE_GUI_PIXELS_PASS: four actual viewport PNGs contain a rendered color patch'
