#!/usr/bin/env sh
# Trench Sim test center (Linux): ./test-trench-sim.sh  See docs/testing.md.
cd "$(dirname "$0")" || exit 3
if ! command -v dotnet >/dev/null 2>&1; then
  echo "The .NET 10 SDK is needed: https://dotnet.microsoft.com/download/dotnet/10.0"
  exit 3
fi
exec dotnet run --project tools/TestCenter -c Release -v q -- "$@"
