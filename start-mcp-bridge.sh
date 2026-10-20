#!/bin/sh
# Bootstrap only; the MCP server, CLI and installer are all implemented in C#.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DOTNET=$(printenv DOTNET_BIN || :)
if [ -z "$DOTNET" ]; then
    if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
    elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
    else echo ".NET 8+ SDK is required" >&2; exit 127
    fi
fi
"$DOTNET" build "$ROOT/RuntimeHost/RuntimeHost.csproj" -c Release -v:q 1>&2
exec "$DOTNET" "$ROOT/RuntimeHost/bin/Release/net8.0/GodotRuntimeMcp.dll" mcp "$@"
