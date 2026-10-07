# Adaptive horde eval, run 2 (2026-10-07)

Batch folder: `MLTraining/results/adaptive_eval/20261007_031839/` (`report.md` has every table). Run log:
`MLTraining/results/adaptive_eval/run_20261007_031723.log`. Code: branch `adaptive-horde` at commit `56db2ef9` (the
run-2 changes from `workspace/reports/2026-10-06-adaptive-eval-1.md`: contact radius 6, the bandit picks only the
distance, sites re-chosen only after they were tried or after an escape from the base, 2 repeats per variant).

## Setup checks

- Compile check: editor and player OK (7 warnings each, the baseline). Logic tests: all passed.
- Eval player rebuilt in 33 s; the URP asset and TMP fallback font were restored, `git status` clean afterwards.
- 10 variants × 2 repeats × 40 game-minutes, about 1 minute each (×25–38 real time).
- Run-to-run noise is small for plain V2 (RunnerA "ambushed" 54% / 57%, "ambush on route" 72% / 74%; RunnerB80
  "ambushed" 70% / 66%; Adaptive 53% / 52%) but larger for the bandit (RunnerA "ambushed" 63% / 44%).

## Main numbers (2 repeats pooled)

| | V2 | Bandit | Bandit-every | TS | TS+Bandit | Bandit-r2.5 |
|---|---|---|---|---|---|---|
| RunnerA: bot ambushed | 55% | 52% | 54% | 44% | 38% | 52% |
| RunnerB80: bot ambushed | 68% | 58% | 65% | 59% | 52% | 58% |
| Adaptive: bot ambushed | 52% | 51% | 58% | 46% | 52% | 53% |
| RunnerA: ambush on route at flee onset | 73% | 61% | 72% | 46% | 42% | 67% |
| RunnerB80: same | 58% | 45% | 56% | 47% | 38% | 45% |
| RunnerA: contact on the ran route (per escape) | 4% | 11% | 10% | 5% | 9% | 0% (2.5) |
| RunnerA: closest on-route ambusher ≤ 6 / > 10 | 19% / 54% | 51% / 24% | 45% / 31% | 27% / 56% | 43% / 32% | 45% / 29% |
| RunnerA: hold orders per escape | 0.10 | 0.23 | 2.10 | 0.16 | 0.31 | 0.28 |
| RunnerA: re-decisions | 100% | 30% | 100% | 26% | 30% | 29% |

Bandit arms (contact rate, both repeats, all personas): route A 12/18/25 = 60% / 27% / 37%, B 51% / 35% / 33%,
C 71% / 62% / 30%. The ablations γ = 1, shared arms and keep are within noise of "Bandit" or a little below it
(keep: RunnerA ambushed 43%); see `report.md`.

## Findings

1. **The reward now carries a signal.** With radius 6, about half of the tested trials are contacts (2.5 again gave 0).
2. **The bandit optimizes its reward.** Contact on RunnerA's ran route goes from 4% to 11% of escapes (z ≈ 5.4), and
   the share of tested sites whose nearest member stayed beyond 10 units falls from 54% to 24%. It learns that
   12 units from the base gives the most contact on every route.
3. **That does not translate into more ambushes by the bots' own check.** RunnerA 55% → 52% (noise), RunnerB80
   68% → 58% (z ≈ 2.9, worse), Adaptive 52% → 51%. Likely reason (not yet checked): contact is only watched until the
   tracker ends the escape, which often happens before the bot reaches its refuge. Near sites (12) show contact inside
   that window; V2's site at the refuge (25.5) catches the bot after it. Note "bot ambushed" also counts chasers
   (any zombie within 4 units ahead, or damage), so it is not a pure ambush measure either.
4. **Run 1's churn diagnosis was wrong.** Re-choosing after every escape ("Bandit-every", 2.1 hold orders per
   escape) keeps V2's on-route rate (72% / 56% vs 73% / 58%) and its ambushed rate. The new "re-choose only after a
   trial" rule is what lowered the on-route rate: it fired after only ~30% of escapes, probably because "escape from
   the base" means starting inside the 6-unit core and many first escapes start just outside it, so sites kept a
   direction the prediction had moved away from.
5. **Thompson direction** costs ambushes against predictable runners (RunnerA 55% → 44%, on route 73% → 46%) and does
   not help against Adaptive (46% vs 52%). Adaptive's route entropy is at the ceiling in every variant (1.46–1.51 of
   1.58 bits), so the H2 hypothesis is not testable with this bot.
6. Pressure (damage, deaths) stays roughly within noise of V2 for every persona.

## Changes proposed for run 3 (agreed 2026-10-07)

1. Re-choose the direction after every escape, as V2 does; re-pick a site's distance only after that site was tried
   or its sector changed.
2. Keep watching for contact after the tracker ends the escape, until the player stops or reaches a refuge (with a
   time limit), so the reward sees what happens at the end of the run.
3. Make the ground truth separate ambushers from chasers: `bot_ambushed` records whether the zombie that triggered it
   belonged to an ambush squad.
