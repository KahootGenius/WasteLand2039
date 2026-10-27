#!/bin/zsh
# The arena simulator (MLTraining/tools/arena-sim/): the eval arena with the game's bot, profile and predictor and V2's rules
# in plain C#, to try horde ideas before spending real eval runs. Built and run OUTSIDE Unity with Unity's bundled Roslyn
# and .NET runtime, like logic-tests.sh, from the game's logic sources plus MLTraining/tools/arena-sim/*.cs.
#
# Usage: MLTraining/tools/arena-sim.sh [MODE] [SEEDS] [EPISODES] [Option=value ...]
#   MODE = calibrate (default) | compare | intercept | ablate   (see arena-sim/Program.cs)
#   e.g. MLTraining/tools/arena-sim.sh calibrate               -> V2 in the simulator next to the real eval
#        MLTraining/tools/arena-sim.sh compare 16 7 Intercept=true
set -u
ROOT="${0:A:h:h:h}"
U=/Applications/Unity/Hub/Editor/2022.3.62f1/Unity.app/Contents
FX=$(ls -d "$U"/NetCoreRuntime/shared/Microsoft.NETCore.App/*/ | head -1)
FX_VERSION=$(basename "$FX")
ENGINE="$U/PlaybackEngines/MacStandaloneSupport/Variations/mono/Managed"
OUT="$ROOT/MLTraining/tools/.out/arena-sim"
mkdir -p "$OUT"
cd "$ROOT" || exit 2

SOURCES=(
  Assets/Scripts/EnemyAI/Zones/*.cs(N)
  Assets/Scripts/EnemyAI/PlayerModel/*.cs(N)
  Assets/Scripts/EnemyAI/Telemetry/*.cs(N)
  Assets/Scripts/EnemyAI/Bots/*.cs(N)
  Assets/Scripts/EnemyAI/Learning/*.cs(N)
  Assets/Scripts/EnemyAI/Prediction/*.cs(N)
  MLTraining/tools/arena-sim/*.cs(N)
)

REFS=()
for dll in "$FX"*.dll; do REFS+=("-r:$dll"); done
REFS+=("-r:$ENGINE/UnityEngine.CoreModule.dll")

"$U/NetCoreRuntime/dotnet" exec "$U/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib \
  -target:exe -langversion:9.0 -nullable:disable -warn:0 -optimize+ \
  -out:"$OUT/ArenaSim.dll" "${REFS[@]}" "${SOURCES[@]}" || { echo "BUILD FAILED"; exit 2; }

cp "$ENGINE/UnityEngine.CoreModule.dll" "$OUT/"
[[ -f "$ENGINE/UnityEngine.SharedInternalsModule.dll" ]] && cp "$ENGINE/UnityEngine.SharedInternalsModule.dll" "$OUT/"
cat > "$OUT/ArenaSim.runtimeconfig.json" <<JSON
{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "$FX_VERSION" } } }
JSON

"$U/NetCoreRuntime/dotnet" "$OUT/ArenaSim.dll" "$@"
