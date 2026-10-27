#!/bin/zsh
# Train the V1 RL horde commander against the headless MLArena_RL build.
#
# Usage: [ARENA_BUILD=path] [CONFIG=yaml] MLTraining/train.sh RUN_ID [NUM_ENVS] [extra mlagents-learn args...]
#   e.g. MLTraining/train.sh v1_ppo_01 3
#        MLTraining/train.sh v1_ppo_01 3 --resume
#        ARENA_BUILD=MLTraining/builds/retask/MLArena_RL MLTraining/train.sh v1_ppo_02 3
#        CONFIG=MLTraining/config/commander_ppo_headon.yaml MLTraining/train.sh v1_ppo_03 3
# ARENA_BUILD (default MLTraining/builds/MLArena_RL) is the player without ".app". The player's
# Arena_RL prefab decides the action scheme (ArenaAssetBuilder.ConfigureRLArena).
# CONFIG (default MLTraining/config/commander_ppo.yaml) is the trainer config.
#
# Build the player first (MLArena_RL.unity only; Build Settings untouched), e.g. via the Unity MCP
# manage_build tool or File > Build with only that scene, output MLTraining/builds/MLArena_RL.app.
#
# Timing matters: ML-Agents fixes the capture frame rate (default 60), so each frame advances
# time_scale / capture_frame_rate seconds of game time. Bots, zombie targeting and firing run in
# Update, so keep that at 0.02 s (one physics step per frame): time scale 20, capture rate 1000.
# The player logs "[ArenaManager] ... fps, game time per frame ..." every 60 s in
# results/RUN_ID/run_logs/Player-*.log; check it after starting a run.
set -euo pipefail
RUN_ID=${1:?usage: train.sh RUN_ID [NUM_ENVS] [args...]}
NUM_ENVS=${2:-3}
shift $(( $# >= 2 ? 2 : 1 ))
HERE="${0:A:h}"
cd "$HERE/.."

exec /opt/miniconda3/envs/mlagents/bin/mlagents-learn "${CONFIG:-MLTraining/config/commander_ppo.yaml}" \
  --env="${ARENA_BUILD:-MLTraining/builds/MLArena_RL}" --no-graphics --num-envs="$NUM_ENVS" \
  --time-scale=20 --capture-frame-rate=1000 \
  --results-dir=MLTraining/results --run-id="$RUN_ID" --seed=1 "$@"
