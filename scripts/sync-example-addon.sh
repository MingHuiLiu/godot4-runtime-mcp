#!/bin/sh
# Recursively mirror canonical runtime plugin files into the standalone example,
# leaving Godot-generated .uid/.import files untouched.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
SRC="$ROOT/addons/godot_mcp"
DEST=${1:-"$ROOT/ExampleProject/addons/godot_mcp"}
if [ -L "$DEST" ]; then echo "Refusing symlink destination: $DEST" >&2; exit 1; fi
mkdir -p "$DEST"
find "$SRC" -type f | while IFS= read -r source; do
    relative=${source#"$SRC"/}
    case "$relative" in
        *.uid|*.import|*/.godot/*|.godot/*|*.DS_Store) continue ;;
    esac
    target="$DEST/$relative"
    if [ -L "$target" ]; then echo "Refusing symlink file: $target" >&2; exit 1; fi
    mkdir -p "$(dirname "$target")"
    cp "$source" "$target"
done
printf 'Synced canonical backend recursively to %s.\n' "$DEST"
