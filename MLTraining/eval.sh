#!/bin/zsh
# Headless evaluation run of one commander in the eval arenas (8 fixed personas, 4 waves per
# episode, full telemetry, lockstep 0.02 s of game time per frame, so results don't depend on speed).
#
# Usage: MLTraining/eval.sh SCENE [GAME_MINUTES] [player args...]
#   SCENE = MLArena_Eval_Baseline | MLArena_Eval_RL   (default 40 game-minutes per arena)
#   e.g. MLTraining/eval.sh MLArena_Eval_RL 40 -stochastic   (sample actions instead of argmax;
#        use it for policies that are still far from deterministic, see ArenaManager)
#
# The eval player (MLTraining/builds/eval/MLArena_Eval.app) contains both scenes. The RL scene uses
# whatever model Arena_RL had when the player was built: after Tools > Enemy AI > Install Latest
# Commander Model (or ArenaAssetBuilder.InstallModel), rebuild the eval player.
# Telemetry lands in ~/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry/
# <yyyyMMdd_HHmmss>_<SCENE>_arenaN_<persona>/; compare with
#   python3 MLTraining/tools/arena-report.py --compare <baseline batch prefix> <RL batch prefix>
set -euo pipefail
SCENE=${1:?usage: eval.sh SCENE [GAME_MINUTES]}
MINUTES=${2:-40}
shift $(( $# >= 2 ? 2 : 1 ))
HERE="${0:A:h}"
cd "$HERE/.."
APP="MLTraining/builds/eval/MLArena_Eval.app/Contents/MacOS/Waste Land 2039"
LOG="$PWD/MLTraining/results/eval_logs/$(date +%Y%m%d_%H%M%S)_${SCENE}.log"
mkdir -p "${LOG:h}"

"$APP" -batchmode -nographics -arenaScene "$SCENE" -arenaMinutes "$MINUTES" -logFile "$LOG" "$@"
echo "player log: $LOG"
grep -a "\[ArenaManager\]" "$LOG" | tail -2 || true
grep -a "\-stochastic" "$LOG" || true
grep -a -m1 "遥测记录到" "$LOG" || true
