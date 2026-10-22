## Before the run (2026-10-22)
Version: Full = Style + cut-off chasers (-v2Style -v2Intercept; the cut-off keeps its 1-second
  rule). No outcome observation: it was dropped after the offline replay predicted no gain
  (rerun on the 19 current-bot V2 rounds: Adaptive route guessing +0.6 [−1.3, +2.5]; RunnerA
  −2.9, RunnerB80 −4.7). Eval scene unchanged (RunnerB80 in arenas 2 and 7).
Run: MLTraining/eval_adaptive.sh --set full --repeats 20 --seed 20261023 (V2, Style, Full; fresh
  seeds, about 80 min). Stale-build check: every Full arena must log
  "commander":"V2-Frequency+Intercept+Style" (Style's: V2-Frequency+Style); expect 160 of them:
    for b in $(awk -F'\t' '$1=="Full"{print $2}' MLTraining/results/adaptive_eval/<stamp>/batches.tsv | tr , ' ')
    do grep -ho -m1 '"commander":"[^"]*"' MLTraining/results/telemetry/${b}*/events.jsonl; done | sort | uniq -c
Analysis: python3 MLTraining/analysis/paired_damage.py MLTraining/results/adaptive_eval/<stamp>
  gives V2 → Style and V2 → Full. For Full − Style, the same script on a copy of batches.tsv
  with Style first:
    d=$(mktemp -d); awk -F'\t' 'NR==1 || $1=="Style" || $1=="Full"' \
      MLTraining/results/adaptive_eval/<stamp>/batches.tsv > $d/batches.tsv
    python3 MLTraining/analysis/paired_damage.py $d
Primary: 8-arena average damage per wave, Full − V2, paired by round. Full wins if the 95% range
  is above 0. Prediction: +5.6 [+4.6, +6.7] (arena-sim.sh compare 64 7 Style=true Intercept=true).
  The two real trials so far (Style, cut-off) landed 1.6 and 2.0 below the simulator, so about +4.
  No trial of Full yet.
Cut-off test: runners' change (Adaptive, RunnerA, RunnerB80), Full − Style (the second analysis's
  runners line), same rule. Full and Style differ only in -v2Intercept, so only a pass here credits
  the cut-off. Prediction: +2.7 (simulator: Style=true Intercept=true minus Style=true, 64 seeds
  each). The range will be about ±2, so it passes only if most of the +2.7 is real.
Secondary (reported, no pass/fail):
  - 8-arena average, Full − Style: about +0.5 (simulator). The cut-off moves damage from Mixed
    and Random to the runners, so the total barely moves.
  - Full − Style by bot (simulator): RunnerA +4, Adaptive +2, RunnerB80 +2, Kiter +2, Mixed −4,
    Random −4.
  - Style − V2 again on fresh seeds (predicted +4.7); runners' change, Full − V2 (simulator +5.5,
    Style alone +2.8).
  - Caught per flee, Full − Style: RunnerA up about 7 (simulator +6.5; the cut-off alone raised it
    10 and 13 points in its two trials); Adaptive, RunnerB80 and Mixed about level.
  - Kiter, Full − V2: simulator +15 (Style's trial gave +1 against its simulator +13).
  - Fighter: a placebo. No arm acts differently against a bot that never flees (simulator 0.0
    everywhere), so its change should straddle 0; its range is the eval's own noise.
Guardrail: no bot's damage per wave more than 5 below V2's.
Report whatever happens.