# Adaptive horde eval, run 4 (2026-10-08): layer 2, the player reaction model

Batch folder: `MLTraining/results/adaptive_eval/20261008_022011/` (`report.md` has every table). Run log:
`MLTraining/results/adaptive_eval/run_20261008_021839.log`. Launcher: `run_reaction_eval.command` =
`eval_adaptive.sh 40 --set reaction --repeats 6`. Code: the layer-2 working tree on top of `f3e1f246` (committed after
the run, unchanged).

## What was tested

`ReactionModel` learns whether a player avoids a direction after meeting zombies there (lose-shift; candidate
avoidance strengths 1 = no reaction, 0.6, 0.35, 0.2, 0.1; Bayesian update from the player's choices; probability is
moved only between routes the player has used). `PredictiveCommander -v2Reaction` applies it to V2's from-base
prediction; the rest of V2 is unchanged. The model observes "outings": the player flees from within 10 units of the
base, its route is the first valid escape's direction, and "met" = any zombie within 6 units on the way out.

Variants: V2, Reaction (prior P(no reaction) 0.5), Reaction-p0.8 (prior 0.8); 6 repeats each, 40 game-minutes.

Before the run: in a toy model of the Adaptive bot, the model raised ambushes by an ambusher from ~18% to ~23%. In the
logic tests, top-1 went from 30% to 40% for a player following the Adaptive rule. In the scripted harness with a
reacting player it went from 33% to 57%. In all three it left a non-reacting player alone.

## Setup checks

- Compile check: editor and player OK (117 scripts, 7 warnings each, the baseline). Logic tests: all passed, including
  the new reaction-model tests.
- 3 variants × 6 repeats, about 21 minutes. V2 matches the old V2 batch and runs 2–3 (Adaptive ambushed 56%,
  RunnerA ambush on route 78%, RunnerB80 ambushed 66%).
- Outings were counted as intended: Adaptive 516–557 outings for 614–646 flee decisions (about 85%).

## Main numbers (6 repeats pooled)

| | V2 | Reaction | Reaction-p0.8 |
|---|---|---|---|
| Adaptive: ambushed by an ambusher | 23% (140/614) | 21% (137/646) | 23% (145/629) |
| Adaptive: commander top-1 on outings | 28% (142/516) | 32% (180/557) | 33% (173/526) |
| Adaptive: same outings, base predictor's top-1 | 28% | 36% (198/557) | 33% (174/526) |
| Adaptive: probability on the route taken | 0.22 | 0.23 | 0.22 |
| Adaptive: P(no reaction) at episode end | – | 0.65 ± 0.28 | 0.75 ± 0.18 |
| RunnerA: ambushed by an ambusher | 44% | 42% | 44% |
| RunnerA: commander top-1 (base, same outings) | 82% | 77% (81%) | 80% (80%) |
| RunnerB80: ambushed by an ambusher | 30% | 31% | 28% |
| Mixed / Random: ambushed by an ambusher | 22% / 18% | 22% / 16% | 21% / 14% |

Run-to-run noise, Adaptive "by an ambusher", each repeat: V2 25 / 17 / 22 / 25 / 28 / 20%, Reaction
19 / 22 / 17 / 23 / 22 / 25%; commander top-1: V2 32 / 28 / 33 / 27 / 25 / 20%.

P(no reaction) at episode end, Reaction: RunnerA 0.75, RunnerB80 0.74, Adaptive 0.65, Mixed 0.51, Random 0.51,
Kiter 0.51.

## Findings

1. **Layer 2 doesn't raise real ambushes.** Adaptive 23% → 21% / 23%, within the repeat spread (17–28%). Other
   personas are unchanged.
2. **The adjustment doesn't even improve prediction.** The clean test compares, on the same outings, the commander's
   adjusted top-1 with the base predictor's. With prior 0.5 the adjustment lost 18 hits for Adaptive (36% → 32%) and
   about 4 points for RunnerA. With prior 0.8 it did nothing. The higher top-1 of the Reaction runs than V2's (32–33% vs
   28%) is not the adjustment: their own base prediction was just as good, and V2's repeats range from 20% to 33%.
3. **The model can't tell the Adaptive bot from players who don't react.** Its end-of-episode belief in "no reaction"
   is 0.65 for Adaptive, between the runners (0.74–0.75) and the near-random personas (0.51).
4. **Likely cause: the contact signal isn't what the bot reacts to.** The commander counted contact on 71–74% of
   Adaptive's outings, but the bot's own check fired on 55–56% of its flees. The commander counts any zombie within
   6 units, including chasers behind the player. The bot only reacts to a zombie within 4 units ahead on its route, one
   at the refuge, or damage. In the toy model, giving the commander's signal that many false alarms (41%, plus 15%
   misses) cuts the gain from +4.6 to about +2 points, inside the noise. The arena did worse than that, so the signal
   may not be the only problem. For example, an outing that ends because the next one starts is observed only after
   the next ambush has been placed.

## Where this leaves the project

After four runs, plain V2 is still the best commander. Neither layer 1 (Thompson direction, ambush bandit) nor layer
2 (player reaction model) raises ambushes by holding zombies for any persona. The tools built along the way stay:
the ground truth, the contact window, outing telemetry, repeats and ablations. All adaptive options are off by default.

A further layer-2 attempt would change "met" to what an ambush feels like to the player: a zombie ahead of them on
the way out, or damage. That matches the bot's check and plausibly a human's. Expected gain, even with a perfect
signal: about +4 points, which 6 repeats can just detect. The ambushers would also have to reach a different refuge in
time.
