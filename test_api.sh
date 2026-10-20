#!/bin/sh
# Offline/installer regression suite, implemented in C# (.NET 8).
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec dotnet run --project "$ROOT/Tests/Tests.csproj" --no-launch-profile
