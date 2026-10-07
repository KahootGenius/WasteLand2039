#!/bin/zsh
# Build the headless eval player (MLTraining/builds/eval/MLArena_Eval.app: the MLArena_Eval_Baseline / _RL /
# _V2 and MLArena_Data scenes; Build Settings untouched) from the command line, via
# EvalPlayerBuilder.BuildFromCommandLine. It also re-imports the project, so it doubles as a compile check.
#
# Usage: MLTraining/build_eval_player.sh
# Only works while the Unity Editor does NOT have this project open (batchmode can't open a project
# that is already open). With the Editor open, use Tools > Enemy AI > Build Eval Player instead.
set -euo pipefail
HERE="${0:A:h}"
cd "$HERE/.."
UNITY=/Applications/Unity/Hub/Editor/2022.3.62f1/Unity.app/Contents/MacOS/Unity
LOG="$PWD/MLTraining/results/eval_logs/$(date +%Y%m%d_%H%M%S)_build_eval_player.log"
mkdir -p "${LOG:h}"

if pgrep -fil "Unity.app/Contents/MacOS/Unity.*-projectpath.*${PWD:t}" >/dev/null; then
  echo "The Unity Editor has this project open: use Tools > Enemy AI > Build Eval Player instead." >&2
  exit 3
fi

echo "building the eval player (log: $LOG) ..."
if "$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod EvalPlayerBuilder.BuildFromCommandLine -logFile "$LOG"; then
  grep -a "\[EvalPlayerBuilder\]" "$LOG" | tail -3
else
  status=$?
  grep -a -E "\[EvalPlayerBuilder\]|error CS|Scripts have compiler errors" "$LOG" | tail -20 || true
  echo "build failed (exit $status); see $LOG" >&2
  exit $status
fi
