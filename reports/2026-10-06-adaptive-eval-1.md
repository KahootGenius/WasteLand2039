# Adaptive horde eval, run 1 (2026-10-06)

Batch folder: `MLTraining/results/adaptive_eval/20261006_193654/` (`report.md` has every table). Run log:
`MLTraining/results/adaptive_eval/run_20261006_193404.log`. Branch `adaptive-horde`, uncommitted code as of that evening.
The code was written by Claude at Lawrence's request (see IDEAS.md, Active work).

## Setup checks

- Compile check: editor and player OK (7 warnings each, the 2026-09-24 baseline). Logic tests: all passed.
- Eval player rebuilt by `EvalPlayerBuilder` in 93 s. It re-serialized the URP asset and the TMP fallback font
  and restored both; `git status` on them was clean afterwards.
- 7 runs × 40 game-minutes on the same build, ×25 real time, about 1.5 min each.
- The new build's plain V2 reproduces the 2026-09-26 V2 batch (`20260926_0138`): RunnerA "ambush on route"
  82% vs 83%, RunnerB80 54% vs 48%. Between those two identical-commander batches, Adaptive "ambushed" was
  46% vs 56% and RunnerB80 damage/wave 9 vs 23, so differences of about 10 points or 10 damage are run-to-run noise.

## Result: the contact reward almost never fires, so the bandit learns from zeros

| | V2 | TS | Bandit | TS+Bandit | Bandit γ=1 | shared | keep |
|---|---|---|---|---|---|---|---|
| RunnerA: ambush contact on the ran route | 0/338 | 0/360 | 0/277 | 0/306 | 0/309 | 1/320 | 0/321 |
| RunnerB80: same | 0/669 | 0/610 | 0/594 | 0/587 | 0/622 | 0/629 | 0/615 |
| Adaptive: same | 0/317 | 0/308 | 0/291 | 0/287 | 0/314 | 0/291 | 0/286 |
| RunnerA: monitor `intercepted` (any zombie ≤ 2.5) | 1% | 1% | 2% | 1% | 1% | 1% | 1% |
| RunnerA: bot "ambushed" (zombie ≤ 4 ahead or damage) | 62% | 65% | 59% | 49% | 56% | 54% | 52% |
| RunnerA: ambush on route at flee onset | 82% | 76% | 67% | 53% | 66% | 71% | 51% |
| RunnerB80: bot "ambushed" | 65% | 62% | 61% | 52% | 63% | 65% | 49% |
| RunnerB80: ambush on route | 54% | 55% | 45% | 41% | 44% | 51% | 27% |
| Adaptive: bot "ambushed" | 46% | 58% | 62% | 53% | 51% | 56% | 56% |
| Adaptive: route entropy per episode (bits, max 1.58) | 1.52 | 1.52 | 1.52 | 1.51 | 1.55 | 1.49 | 1.48 |

- The commander's contact (an ambusher within 2.5 units, 1 s or more after flee onset) is ~0 for every fleeing
  persona and every variant. The monitor's independent `intercepted` field agrees (1–3%), so this is not a
  bookkeeping bug. Fleeing bots run at 5 against zombies at 3, and they treat a zombie 4 units ahead as an ambush
  and turn away, so they rarely get within 2.5. Only Kiter and Random, who stay in contact, produce contacts.
- With all-miss rewards the bandit actively moves ambushes **off** the predicted route: the arms on the route are
  the ones that get tried (and miss), while arms in other sectors keep their optimistic Beta(1, 1) prior, and the
  score P(sector) × θ lets those high θ draws outweigh the prediction. "Ambush on route" falls from 82% (V2) to
  53% for RunnerA with TS+Bandit (z ≈ 3.6) and from 54% to 27% for RunnerB80 when the bandit is kept across
  episodes (z ≈ 5.0), the variant where on-route arms pile up the most misses.
- The shared-arm variant confirms it. With one θ per distance for all sectors, the sector is always the
  predicted one, and RunnerA's hold orders go 91% to route A (71% ambush on route), against 56–62% with
  per-sector arms.
- Second effect, churn: every sampling variant re-places the sites after each escape. RunnerA got 45 hold orders
  under V2 but 531 (TS) to 1127 (keep); RunnerB80 124 against 1174–2063. Ambushers spend much of the wave walking
  between sites instead of waiting at one.
- Hypothesis "B raises ambushed for runners": not supported in this setup (RunnerA 62% → 59%, RunnerB80 65% → 61%,
  lower with TS+Bandit / keep).
- Hypothesis "A raises route entropy against Adaptive": not testable here. Adaptive is already at 1.52 of 1.58
  bits under plain V2 (ceiling). Adaptive "ambushed" 46% → 58% with TS is within the V2-vs-V2 noise (46% vs 56%).
- Ablations: γ = 1 against 0.9 can't be told apart when the reward carries no signal. Shared arms (closer to V2)
  and keep (furthest from V2) differ only for the mechanical reasons above, not because anything was learned.
- Pressure: runner damage per wave rose in every bandit variant (RunnerA 0 → 7–21, 0 → 2–4 deaths of ~27 waves),
  possibly ambushers held at 12 units acting as extra chasers near the base. Small counts, treat as a hint.

## What to change before the next run (not done; Lawrence's call)

1. **A reward that fires:** count a trial as contact when a site member gets within `holdEngageRadius` (6) of the
   player, i.e. the player ran into the ambush and triggered it (4 would match the bots' own "ambushed" check).
   Make it a flag (`-v2ContactRadius`) and keep 2.5 available. Also log each tested site's minimum distance in a
   histogram, so the radius is chosen from data.
2. **Let B choose the distance only, within the sector A (or argmax) picked.** That is the guide's own framing
   ("where along the predicted direction") and stops untested sectors' optimistic priors from pulling ambushes off
   the route. Keep the current joint P × θ choice as an ablation.
3. **Less churn:** re-draw a site only after it has been tried (a trial in its sector finished), or once per wave,
   instead of after every escape.
4. Re-run with 2 seeds per variant (two batches each) so run-to-run noise can be measured, not guessed.
