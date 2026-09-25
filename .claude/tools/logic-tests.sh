#!/bin/zsh
# Build and run the EnemyAI pure-logic tests OUTSIDE Unity, using Unity's bundled Roslyn and
# .NET 6 runtime. Compiles the game's logic sources (zones, player model, telemetry writer)
# together with .claude/tools/logic-tests/LogicTests.cs against UnityEngine.CoreModule.dll.
# Only classes that don't touch Unity engine internals (Time, Debug, scene) can be tested here.
#
# Usage: .claude/tools/logic-tests.sh   -> exit 0 when all tests pass
set -u
ROOT="${0:A:h:h:h}"
U=/Applications/Unity/Hub/Editor/2022.3.62f1/Unity.app/Contents
FX=$(ls -d "$U"/NetCoreRuntime/shared/Microsoft.NETCore.App/*/ | head -1)
FX_VERSION=$(basename "$FX")
ENGINE="$U/PlaybackEngines/MacStandaloneSupport/Variations/mono/Managed"
OUT="$ROOT/.claude/workspace/scratch/logic-tests"
mkdir -p "$OUT"
cd "$ROOT" || exit 2

SOURCES=(
  Assets/Scripts/EnemyAI/Zones/*.cs(N)
  Assets/Scripts/EnemyAI/PlayerModel/*.cs(N)
  Assets/Scripts/EnemyAI/Telemetry/*.cs(N)
  .claude/tools/logic-tests/LogicTests.cs
)

REFS=()
for dll in "$FX"*.dll; do REFS+=("-r:$dll"); done
REFS+=("-r:$ENGINE/UnityEngine.CoreModule.dll")

"$U/NetCoreRuntime/dotnet" exec "$U/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib \
  -target:exe -langversion:9.0 -nullable:disable -warn:0 \
  -out:"$OUT/LogicTests.dll" "${REFS[@]}" "${SOURCES[@]}" || { echo "BUILD FAILED"; exit 2; }

cp "$ENGINE/UnityEngine.CoreModule.dll" "$OUT/"
[[ -f "$ENGINE/UnityEngine.SharedInternalsModule.dll" ]] && cp "$ENGINE/UnityEngine.SharedInternalsModule.dll" "$OUT/"
cat > "$OUT/LogicTests.runtimeconfig.json" <<EOF
{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "$FX_VERSION" } } }
EOF

"$U/NetCoreRuntime/dotnet" "$OUT/LogicTests.dll"
