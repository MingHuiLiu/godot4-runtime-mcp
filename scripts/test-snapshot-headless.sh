#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
GODOT_BIN=${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}
dotnet build "$ROOT/ExampleProject/ExampleProject.csproj" -c Debug
"$GODOT_BIN" --headless --path "$ROOT/ExampleProject" --scene res://SnapshotCaptureProbe.tscn --quit-after 60
