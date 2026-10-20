#!/bin/sh
# Compatibility shim for existing MCP clients. Source development requires .NET SDK.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$ROOT/scripts/godot-mcp-dev.sh" mcp "$@"
