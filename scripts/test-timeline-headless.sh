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
grep -q 'TIMELINE_EVENT_PASS:' "$LOG"
grep -q 'TIMELINE_SCREENSHOT_PASS:' "$LOG"
grep -q 'TIMELINE_REDACTION_PASS:' "$LOG"
grep -q 'TIMELINE_SCOPE_PASS:' "$LOG"
grep -q 'TIMELINE_OVERFLOW_PASS:' "$LOG"
grep -q 'TIMELINE_OPTOUT_PASS:' "$LOG"
grep -q 'TIMELINE_STOP_FLUSH_PASS:' "$LOG"
grep -q 'TIMELINE_STOP_PASS:' "$LOG"

# Query the real sessions with a distinct CLI process, after the Godot game quits.
dotnet build "$ROOT/RuntimeHost/RuntimeHost.csproj" -c Release > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
HOST="$ROOT/RuntimeHost/bin/Release/net8.0/GodotRuntimeMcp.dll"
run_host() {
    if [ -n "${GODOT_RUNTIME_NATIVE_HOST:-}" ]; then
        GODOT_MCP_CONFIG_DIR="$DATA" "$GODOT_RUNTIME_NATIVE_HOST" "$@"
    else
        GODOT_MCP_CONFIG_DIR="$DATA" dotnet "$HOST" "$@"
    fi
}

run_host godot-timeline-sessions \
    --project "$ROOT/ExampleProject" > "$LOG"
COMPLETED=$(python3 - "$LOG" <<'PY'
import json,sys
sessions=json.load(open(sys.argv[1]))
assert len(sessions)==3, sessions
assert {s['status'] for s in sessions}=={'completed','stopped'}, sessions
completed=next(s for s in sessions if s['status']=='completed')
assert completed['captureInput'] and completed['captureSignals'] and completed['droppedObservedEvents']>0, sessions
print(next(s['sessionId'] for s in sessions if s['status']=='completed'))
PY
)
run_host godot-timeline-events \
    --project "$ROOT/ExampleProject" --session-id "$COMPLETED" \
    --offset 0 --limit 10 > "$LOG"
python3 - "$LOG" <<'PY'
import json,sys
result=json.load(open(sys.argv[1]))
assert result['total']==4, result
kinds={ev['kind'] for batch in result['items'] for ev in batch['events']}
assert {'node_added','node_renamed','node_removed'} <= kinds,kinds
observed=[e for batch in result['items'] for e in batch.get('observedEvents',[])]
assert {('input','key'),('signal','renamed')} <= {(e['kind'],e['name']) for e in observed}, observed
impacts=[e for e in observed if e['kind']=='signal' and e['name']=='impact']
assert len(impacts)==1 and impacts[0]['fields']['arg0']['data']==9 and impacts[0]['fields']['arg1']['type']=='redacted', impacts
assert 'SHOULD_NEVER_BE_PERSISTED' not in json.dumps(result),result
assert not any(e['name']=='outside_scope' for e in observed),observed
assert sum(batch.get('droppedObservedEvents',0) for batch in result['items'])>0,result
assert not any('unicode' in name.lower() or 'text' in name.lower()
               for e in observed for name in e['fields']), observed
PY
run_host godot-timeline-info \
    --project "$ROOT/ExampleProject" --session-id "$COMPLETED" > "$LOG"
python3 - "$LOG" <<'PY'
import json,sys
info=json.load(open(sys.argv[1]))
assert info['status']=='completed' and info['sampleCount']==4,info
PY
echo 'TIMELINE_OFFLINE_PASS: separate CLI lists, pages and inspects completed/stopped sessions'
# An offline host can render a standalone, script-free gallery after Godot exits.
run_host godot-timeline-export-gallery \
    --project "$ROOT/ExampleProject" --session-id "$COMPLETED" \
    --offset 0 --limit 10 > "$LOG"
python3 - "$LOG" <<'PY'
import json,sys,pathlib
output=json.load(open(sys.argv[1]))
assert output['total']==4 and output['includedSamples']==4,output
assert output['imageCount']+output['unavailableCount']==4,output
html=pathlib.Path(output['htmlPath']).read_text()
assert 'Content-Security-Policy' in html and '<script' not in html,html[:900]
assert 'node_added' in html and 'node_removed' in html,html[:1300]
assert 'Runtime Black Box' in html and 'godot' not in output['sessionId'],output
if output['unavailableCount']:
    assert 'renderer_unavailable' in html or 'viewport_unavailable' in html,html[-500:]
PY
echo 'TIMELINE_GALLERY_PASS: offline CLI renders escaped private static HTML aligned with 4 frames'
