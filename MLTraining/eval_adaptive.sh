#!/bin/zsh
# Adaptive horde experiments (build guide §3): run every variant of the V2 commander on the SAME eval
# player, so the only difference between batches is the command-line flag, then compare each with plain V2.
#
# Usage: MLTraining/eval_adaptive.sh [GAME_MINUTES] [--set adaptive|reaction] [--ablations] [--repeats N] [--reference BATCH]
#   GAME_MINUTES  per run, default 40
#   --set         which variants: adaptive (default, layer 1: Thompson direction, ambush bandit) or reaction (layer 2:
#                 V2, Reaction = -v2Reaction, Reaction-p0.8 = the same with P(no reaction) 0.8 a priori)
#   --ablations   also run the bandit ablations (set adaptive; see below)
#   --repeats N   batches per variant, default 2: the repeats are pooled, and their spread is the run-to-run noise
#   --reference   an older plain-V2 batch to sanity-check the new build against (default 20260926_0138)
# Variants: V2 (control), TS = -v2Thompson (part A), Bandit = -v2Bandit (part B), TS+Bandit.
# Ablations: Bandit-joint (bandit also picks the sector, P × θ, as in run 1), Bandit-tail0 (no contact window after
#   the escape ends: run 2's reward), Bandit-every (redraw the distance after every escape, also before the site was
#   tried: run 2's Bandit-every), Bandit-g1 (γ = 1), Bandit-shared (one arm per distance for all sectors), Bandit-keep
#   (no reset between episodes). Run 2's Bandit-r2.5 (run 1's contact radius) is gone: both runs showed ~no contacts.
# The repeats run as rounds (every variant once, then again), so slow drift can't line up with one variant.
#
# Rebuild the eval player first (Tools > Enemy AI > Build Eval Player, or MLTraining/build_eval_player.sh).
# Output: MLTraining/results/adaptive_eval/<stamp>/
#   batches.tsv   variant, telemetry batch prefixes (comma-separated, one per repeat), flags
#   report.md     arena-report --compare (V2 vs each variant, old V2 vs new V2) + adaptive_metrics.py
set -euo pipefail
HERE="${0:A:h}"
cd "$HERE/.."

MINUTES=40
SET=adaptive
ABLATIONS=0
REPEATS=2
REFERENCE=20260926_0138
while (( $# )); do
  case $1 in
    --set) shift; SET=$1 ;;
    --ablations) ABLATIONS=1 ;;
    --repeats) shift; REPEATS=$1 ;;
    --reference) shift; REFERENCE=$1 ;;
    *) MINUTES=$1 ;;
  esac
  shift
done

ROOT="$HOME/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry"
APP="MLTraining/builds/eval/MLArena_Eval.app"
[[ -d $APP ]] || { echo "no eval player at $APP; build it first" >&2; exit 2; }
OUT="MLTraining/results/adaptive_eval/$(date +%Y%m%d_%H%M%S)"
mkdir -p "$OUT"

case $SET in
  adaptive)
    names=(V2 TS Bandit TS+Bandit)
    flags=("" "-v2Thompson" "-v2Bandit" "-v2Thompson -v2Bandit")
    if (( ABLATIONS )); then
      names+=(Bandit-joint Bandit-tail0 Bandit-every Bandit-g1 Bandit-shared Bandit-keep)
      flags+=("-v2Bandit -v2BanditJoint" "-v2Bandit -v2ContactTail 0" "-v2Bandit -v2RedrawEveryEscape"
              "-v2Bandit -v2Gamma 1" "-v2Bandit -v2BanditShared" "-v2Bandit -v2BanditKeep")
    fi ;;
  reaction)
    names=(V2 Reaction Reaction-p0.8)
    flags=("" "-v2Reaction" "-v2Reaction -v2ReactionPrior 0.8") ;;
  *) echo "unknown --set $SET (adaptive or reaction)" >&2; exit 2 ;;
esac

typeset -A batch
echo "eval player built $(stat -f '%Sm' "$APP/Contents/MacOS"), set $SET: ${#names} variants × $REPEATS repeats × $MINUTES game-minutes → $OUT"
for round in {1..$REPEATS}; do
for i in {1..${#names}}; do
  before=("$ROOT"/*_MLArena_Eval_V2_*(N/:t))
  echo "\n=== ${names[i]} (${flags[i]:-no flags}), repeat $round of $REPEATS"
  MLTraining/eval.sh MLArena_Eval_V2 "$MINUTES" ${=flags[i]}
  after=("$ROOT"/*_MLArena_Eval_V2_*(N/:t))
  new=(${after:|before})
  (( ${#new} )) || { echo "no new telemetry folders for ${names[i]}" >&2; exit 1; }
  # the batch prefix: what the new folders' timestamps (yyyyMMdd_HHmmss) have in common; it must not
  # also match older folders (arena-report selects batches by this prefix)
  prefix=$(python3 - "$ROOT" $new <<'PY'
import os, sys
root, new = sys.argv[1], set(sys.argv[2:])
prefix = os.path.commonprefix([n[:15] for n in new])
others = [d for d in os.listdir(root) if "_MLArena" in d and d[:15].startswith(prefix) and d not in new]
if others:
    sys.exit(f"batch prefix {prefix} would also select {len(others)} older folders, e.g. {others[0]}")
print(prefix)
PY
)
  batch[${names[i]}]=${batch[${names[i]}]:+${batch[${names[i]}]},}$prefix
  echo "batch ${names[i]} #$round: $prefix (${#new} arenas)"
done
done

printf "variant\tbatch\tflags\n" > "$OUT/batches.tsv"
for i in {1..${#names}}; do
  printf "%s\t%s\t%s\n" "${names[i]}" "${batch[${names[i]}]}" "${flags[i]}" >> "$OUT/batches.tsv"
done

v2=$(awk -F'\t' '$1=="V2"{print $2}' "$OUT/batches.tsv")
{
  echo "# Adaptive horde eval, $(date '+%Y-%m-%d %H:%M')"
  echo
  echo "Set $SET, $MINUTES game-minutes per run, $REPEATS repeats per variant (pooled), scene MLArena_Eval_V2, same eval player for every run."
  echo
  echo '```'
  cat "$OUT/batches.tsv"
  echo '```'
  echo
  echo "## New metrics (adaptive_metrics.py)"
  echo
  python3 MLTraining/analysis/adaptive_metrics.py --markdown "$OUT/batches.tsv"
  tail -n +3 "$OUT/batches.tsv" | while IFS=$'\t' read -r name batch flag; do
    echo
    echo "## arena-report: V2 vs $name"
    echo
    python3 .claude/tools/arena-report.py --compare "$v2" "$batch" --markdown
  done
  reference=("$ROOT"/${REFERENCE}*_MLArena_Eval_V2_*(N/))
  if (( ${#reference} )); then
    echo
    echo "## Sanity check: old V2 batch $REFERENCE vs the new build's V2"
    echo
    python3 .claude/tools/arena-report.py --compare "$REFERENCE" "$v2" --markdown
  fi
} > "$OUT/report.md"
echo "\nreport: $OUT/report.md"
