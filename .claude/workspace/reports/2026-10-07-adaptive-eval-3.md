# Adaptive horde eval, run 3 (2026-10-07)

Batch folder: `MLTraining/results/adaptive_eval/20261007_050453/` (`report.md` has every table). Run log:
`MLTraining/results/adaptive_eval/run_20261007_050335.log`. Code: branch `adaptive-horde` at commit `c179d99c` (the
run-3 changes from `workspace/reports/2026-10-07-adaptive-eval-2.md`: `bot_ambushed` records ambusher vs chaser, the
direction is re-chosen after every escape and an untried site keeps its distance, contact is watched for up to 5 s
after the escape ends; ablation Bandit-tail0 replaced Bandit-r2.5).

## Setup checks

- Compile check: editor and player OK (7 warnings each, the baseline). Logic tests: all passed.
- Eval player rebuilt in 33 s; the URP asset and TMP fallback font were restored, `git status` clean afterwards.
- 10 variants × 2 repeats × 40 game-minutes, about 25 minutes for the whole run.
- Old V2 batch `20260926_0138` vs this build's V2: within noise (Adaptive ambushed 56% vs 59%, RunnerA route
  predicted 77% vs 85%). Fighter (never flees) takes the same damage under every variant (61–67 per wave): a control.
- Damage is noisy: V2's Kiter damage was 19 per wave here, 37 in run 2 and 36 in the old batch, so the Kiter gap
  (19 vs 26–41 for every other variant) is mostly V2 having a low run. Pressure looks within noise of V2 overall.

## The new ground truth

Share of the bots' flee decisions that ended "ambushed", split by the zombie behind it (V2, 2 repeats pooled):

| Persona | Ambushed | By an ambusher (holding) | By a chaser | Ambushers' share | Cause |
|---|---|---|---|---|---|
| RunnerA | 58% | 42% | 16% | 72% | ahead on route 66, at the refuge 43 |
| RunnerB80 | 64% | 27% | 37% | 42% | damage 8, ahead 196, at the refuge 36 |
| Adaptive | 59% | 22% | 37% | 37% | ahead 107, at the refuge 13 |
| Mixed | 63% | 21% | 42% | 33% | damage 2, ahead 43, at the refuge 8 |
| Random | 51% | 20% | 31% | 39% | ahead 23, at the refuge 2 |

So "bot ambushed" was mostly an ambush measure for RunnerA but mostly a chaser measure for the others.
"By an ambusher" is the outcome the adaptive parts are meant to raise.

## Main numbers (2 repeats pooled)

"By an ambusher" = share of flee decisions ambushed by a holding zombie. Contact = an ambusher in the ran sector came
within 6 units (per valid escape); "+ tail" includes the 5 s after the escape ends, "escape only" is run 2's reward.

| | V2 | TS | Bandit | TS+Bandit | Bandit-tail0 | Bandit-every | Bandit-joint |
|---|---|---|---|---|---|---|---|
| RunnerA: by an ambusher | 42% | 36% | 40% | 38% | 40% | 38% | 32% |
| RunnerB80: by an ambusher | 27% | 22% | 27% | 22% | 28% | 28% | 28% |
| Adaptive: by an ambusher | 22% | 12% | 23% | 14% | 21% | 21% | 20% |
| RunnerA: ambush on route at flee onset | 75% | 70% | 79% | 68% | 73% | 71% | 70% |
| RunnerB80: same | 58% | 53% | 50% | 45% | 52% | 56% | 52% |
| RunnerA: contact on the ran route, + tail | 16% | 18% | 20% | 21% | 11% | 20% | 21% |
| RunnerA: same, escape only | 5% | 5% | 10% | 12% | 11% | 13% | 12% |
| RunnerA: hold orders per escape | 0.12 | 0.99 | 0.25 | 1.04 | 0.30 | 1.45 | 1.77 |

Run-to-run noise (each repeat on its own), "by an ambusher": V2 RunnerA 48% / 38%, RunnerB80 27% / 27%, Adaptive
18% / 27%; Bandit 40% / 39%, 29% / 24%, 25% / 22%. Bandit-g1, -shared and -keep are within noise of Bandit or a little below it (see
`report.md`).

Bandit arms, contact rate when the player ran into the site's sector (all personas, both repeats):

| Route | 12 | 18 | 25 |
|---|---|---|---|
| A, with the tail (Bandit) | 75% (55/73) | 74% (66/89) | 73% (61/83) |
| B, with the tail | 74% (89/121) | 62% (50/80) | 69% (72/105) |
| C, with the tail | 68% (32/47) | 57% (12/21) | 62% (26/42) |
| A, escape only (Bandit-tail0) | 60% (49/81) | 37% (22/59) | 25% (17/67) |
| B, escape only | 66% (91/138) | 30% (26/86) | 36% (37/102) |
| C, escape only | 59% (19/32) | 61% (14/23) | 20% (4/20) |

## Findings

1. **The escape window was the problem with the reward.** With the tail, V2's contact on the ran route roughly
   triples (RunnerA 5% → 16%, RunnerB80 6% → 12%, Adaptive 4% → 9%). The tracker ends most escapes before the bot
   reaches the ambush at the refuge, as suspected in run 2.
2. **Run 2's "wait 12 units out" was an artifact of that window.** Bandit-tail0 reproduces it (route A 60% / 37% / 25%),
   but watched to the end every distance gets about 70% contact. In this arena the bots run straight down a route
   to a fixed refuge, so any point on the route meets them; the distance bandit has nothing to learn here.
3. **No variant beats plain V2 on the ground truth.** Bandit matches V2 (RunnerA 40% vs 42%, RunnerB80 27% vs 27%,
   Adaptive 23% vs 22%). Its extra contact on the ran route (16% → 20%) doesn't become more real ambushes.
4. **Re-choosing after every escape fixed run 2's drop for RunnerA** (ambush on route at flee onset 79% vs V2's 75%;
   run 2: 61% vs 73%) with little churn (0.25 hold orders per escape; Bandit-every 1.45). RunnerB80 stays a little
   below V2 (50% vs 58%; run 2: 45% vs 58%).
5. **Thompson direction is worse for every persona**, including the one it was meant for: Adaptive by an ambusher
   22% → 12% (z ≈ 2.6), RunnerA 42% → 36%, RunnerB80 27% → 22%. With the direction re-drawn after every escape it
   also churns: RunnerB80 2,348 hold orders vs V2's 241. By the counts, most re-draws come at the ends of escapes too
   short to count as trials (no trial to watch, so they re-plan at once), which the "re-decisions" row doesn't see.
6. **The tail is usually cut short.** For the runners about 80% of trials were scored when the next escape started,
   1.6–1.9 s after the end on average (Kiter about half, 3.1–3.6 s). In practice "until the run ends" means "until
   the bot flees again", and the adaptive modes re-decide after only 16–29% of the runners' and Adaptive's valid
   escapes.
7. **Against Adaptive the limit is prediction, not placement.** V2's top-1 route prediction for Adaptive is 24%
   (run 2: 30%), below the 33% chance level, and Adaptive's route entropy is at the ceiling (1.54 of 1.58 bits) in
   every variant. The bot steers away from routes where it was ambushed, so a frequency count of past routes points
   at the route it is about to avoid. Sampling from that same count (Thompson) can't fix it.

## Where this leaves the build guide's hypotheses

- Part A (Thompson direction against adaptive players): not supported. It lowers real ambushes for every persona.
- Part B (ambush bandit): the bandit learns its reward, but with an honest reward this arena gives it nothing to
  choose between, and it ends up equal to V2.
- What would make adaptation matter: a predictor that models the player's reaction to being ambushed (Adaptive
  avoids the route it was last ambushed on), or players for whom distance matters (stopping short of the refuge,
  detours, human play). Neither is tested yet.
