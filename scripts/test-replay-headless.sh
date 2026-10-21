#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
GODOT_BIN=${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}
DIR=$(mktemp -d "${TMPDIR:-/tmp}/godot-replay-test.XXXXXX")
LOG=$(mktemp "${TMPDIR:-/tmp}/godot-replay-log.XXXXXX")
trap 'rm -rf "$DIR"; rm -f "$LOG"' EXIT
dotnet build "$ROOT/ExampleProject/ExampleProject.csproj" -c Debug > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
GODOT_MCP_CONFIG_DIR="$DIR" "$GODOT_BIN" --headless --path "$ROOT/ExampleProject" \
    --scene res://ReplayCaptureProbe.tscn --quit-after 700 > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
cat "$LOG"
grep -q 'REPLAY_CAPTURE_PASS:' "$LOG"
SESSION=$(sed -n -E 's/.*REPLAY_CAPTURE_PASS: session=([a-zA-Z0-9_]+).*/\1/p' "$LOG" | head -1)
test -n "$SESSION"
PROJECT=$(python3 - "$ROOT/ExampleProject" <<'PY'
import hashlib,sys,pathlib
print(hashlib.sha256(str(pathlib.Path(sys.argv[1]).resolve()).encode()).hexdigest())
PY
)
S_DIR="$DIR/recordings/$PROJECT/$SESSION"
test -s "$S_DIR/timeline/status.json"
python3 - "$S_DIR/timeline/status.json" <<'PY'
import json,sys
s=json.load(open(sys.argv[1]))
assert s['status']=='completed' and s['sampleCount']==3 and s['captureInput'],s
assert s['droppedSamples']==s['droppedObservedEvents']==0,s
PY
for mode in success mismatch; do
  OUTPUT="$DIR/replay-$mode"
  JOB="$DIR/replay-$mode.json"
  python3 - "$ROOT/ExampleProject" "$S_DIR" "$OUTPUT" "$JOB" <<'PY'
import json,sys
json.dump({'projectPath':sys.argv[1], 'sessionDirectory':sys.argv[2],
           'outputDirectory':sys.argv[3]},open(sys.argv[4],'w'))
PY
  if [ "$mode" = mismatch ]; then
    if GODOT_MCP_REPLAY_TEST_DIVERGE=1 GODOT_MCP_REPLAY_WORKER=1 GODOT_MCP_CONFIG_DIR="$DIR" \
      "$GODOT_BIN" --headless --path "$ROOT/ExampleProject" \
      --scene res://addons/godot_mcp/Recording/replay_worker.tscn \
      -- --replay-job "$JOB" > "$LOG" 2>&1; then
        echo 'FAIL: divergent script falsely matched' >&2; exit 1
    fi
  else
    GODOT_MCP_REPLAY_WORKER=1 GODOT_MCP_CONFIG_DIR="$DIR" \
      "$GODOT_BIN" --headless --path "$ROOT/ExampleProject" \
      --scene res://addons/godot_mcp/Recording/replay_worker.tscn \
      -- --replay-job "$JOB" > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
  fi
  cat "$LOG"
  test -s "$OUTPUT/replay_report.json"
  python3 - "$OUTPUT/replay_report.json" "$mode" <<'PY'
import json,sys
r=json.load(open(sys.argv[1]))
if sys.argv[2]=='success':
    assert r['status']=='matched' and r['comparedSamples']==2 and r['matchedSamples']==2,r
    assert r['injectedEvents']==1,r
else:
    assert r['status']=='diverged' and any(
        i['code']=='replay_property_diverged' for i in r['issues']),r
PY
  if [ "$mode" = success ]; then
    echo 'REPLAY_MATCH_PASS: independent Godot worker injected genuine key and compared observed states'
  else
    echo 'REPLAY_DIVERGED_PASS: changed script behavior reported state mismatch'
  fi
done

# Real offline CLI -> host manager -> independent worker, after the source game exited.
dotnet build "$ROOT/RuntimeHost/RuntimeHost.csproj" -c Release > "$LOG" 2>&1 || { cat "$LOG"; exit 1; }
HOST="$ROOT/RuntimeHost/bin/Release/net8.0/GodotRuntimeMcp.dll"
run_host() {
  if [ -n "${GODOT_RUNTIME_NATIVE_HOST:-}" ]; then
    GODOT_MCP_CONFIG_DIR="$DIR" "$GODOT_RUNTIME_NATIVE_HOST" "$@"
  else
    GODOT_MCP_CONFIG_DIR="$DIR" dotnet "$HOST" "$@"
  fi
}
for mode in success mismatch; do
  if [ "$mode" = mismatch ]; then
    GODOT_MCP_REPLAY_TEST_DIVERGE=1 run_host godot-replay-start \
      --project "$ROOT/ExampleProject" --session-id "$SESSION" \
      --godot-binary "$GODOT_BIN" > "$LOG"
  else
    run_host godot-replay-start \
      --project "$ROOT/ExampleProject" --session-id "$SESSION" \
      --godot-binary "$GODOT_BIN" > "$LOG"
  fi
  JOB_ID=$(python3 - "$LOG" <<'PY'
import json,sys
value=json.load(open(sys.argv[1]))
assert value['status']=='running',value
print(value['jobId'])
PY
)
  test -n "$JOB_ID"
  READY=0
  for attempt in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
    run_host godot-replay-status --project "$ROOT/ExampleProject" --job-id "$JOB_ID" > "$LOG"
    if python3 - "$LOG" <<'PY'
import json,sys
v=json.load(open(sys.argv[1]))
sys.exit(0 if v['status'] in ('matched','diverged','inconclusive','failed') else 1)
PY
    then READY=1; break; fi
    sleep 0.5
  done
  if [ "$READY" -ne 1 ]; then cat "$LOG"; echo 'FAIL: replay manager never completed' >&2; exit 1; fi
  run_host godot-replay-report --project "$ROOT/ExampleProject" --job-id "$JOB_ID" > "$LOG"
  python3 - "$LOG" "$mode" <<'PY'
import json,sys
r=json.load(open(sys.argv[1]))
if sys.argv[2]=='success':
    assert r['status']=='matched' and r['matchedSamples']==2 and r['injectedEvents']==1,r
else:
    assert r['status']=='diverged' and any(
        x['code']=='replay_property_diverged' for x in r['issues']),r
PY
  echo "REPLAY_HOST_${mode}_PASS: CLI spawned independent worker and returned verified report"
done
