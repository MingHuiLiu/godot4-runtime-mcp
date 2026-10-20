#!/bin/sh
# Keep the standalone Godot demo in sync with the canonical plugin source.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
SRC="$ROOT/addons/godot_mcp"
DEST="$ROOT/ExampleProject/addons/godot_mcp"
mkdir -p "$DEST"
for source in "$SRC"/*; do
    [ -f "$source" ] || continue
    cp "$source" "$DEST/${source##*/}"
done
printf 'Synced canonical backend to ExampleProject. Run .NET tests to check for stale files.\n'
