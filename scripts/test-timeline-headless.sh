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
grep -q 'TIMELINE_STOP_PASS:' "$LOG"

# Query the real sessions with a distinct CLI process, after the Godot game quits.
dotnet build "$ROOT/RuntimeHost/RuntimeHost.csproj" -c Release > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
HOST="$ROOT/RuntimeHost/bin/Release/net8.0/GodotRuntimeMcp.dll"
GODOT_MCP_CONFIG_DIR="$DATA" dotnet "$HOST" godot-timeline-sessions \
    --project "$ROOT/ExampleProject" > "$LOG"
COMPLETED=$(python3 - "$LOG" <<'PY'
import json,sys
sessions=json.load(open(sys.argv[1]))
assert len(sessions)==2, sessions
assert {s['status'] for s in sessions}=={'completed','stopped'}, sessions
print(next(s['sessionId'] for s in sessions if s['status']=='completed'))
PY
)
GODOT_MCP_CONFIG_DIR="$DATA" dotnet "$HOST" godot-timeline-events \
    --project "$ROOT/ExampleProject" --session-id "$COMPLETED" \
    --offset 0 --limit 10 > "$LOG"
python3 - "$LOG" <<'PY'
import json,sys
result=json.load(open(sys.argv[1]))
assert result['total']==4, result
kinds={ev['kind'] for batch in result['items'] for ev in batch['events']}
assert {'node_added','node_renamed','node_removed'} <= kinds,kinds
PY
GODOT_MCP_CONFIG_DIR="$DATA" dotnet "$HOST" godot-timeline-info \
    --project "$ROOT/ExampleProject" --session-id "$COMPLETED" > "$LOG"
python3 - "$LOG" <<'PY'
import json,sys
info=json.load(open(sys.argv[1]))
assert info['status']=='completed' and info['sampleCount']==4,info
PY
echo 'TIMELINE_OFFLINE_PASS: separate CLI lists, pages and inspects completed/stopped sessions'
