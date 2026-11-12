#!/usr/bin/env bash
# Builds MarsProbe, MarsMod and the installer, and puts MarsSetup.exe into dist/.
# Needs the .NET 8 SDK and the reference assemblies in lib/ (see lib/README.md).
set -euo pipefail
cd "$(dirname "$0")/.."
for f in lib/BepInEx.Core.dll lib/Il2Cppmscorlib.dll lib/interop/Assembly-CSharp.dll lib/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791.zip; do
  [ -f "$f" ] || { echo "missing $f, see lib/README.md" >&2; exit 1; }
done
./native/dstorage-proxy/build.sh >/dev/null
dotnet build MarsProbe -c Release -nologo -v q
dotnet build MarsMod -c Release -nologo -v q
dotnet run --project tools/jsoncheck -nologo -v q 2>&1 | grep -v NETSDK1138 || { echo "save data check failed" >&2; exit 1; }
dotnet build MarsSetup -c Release -nologo -v q
mkdir -p dist
cp MarsSetup/bin/Release/net48/MarsSetup.exe dist/MarsSetup.exe
echo "dist/MarsSetup.exe $(du -h dist/MarsSetup.exe | cut -f1)"
