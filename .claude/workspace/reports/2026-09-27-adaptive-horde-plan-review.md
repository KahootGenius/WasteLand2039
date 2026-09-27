# Review: `MLTraining/adaptive_horde_plan.md` (Adaptive Horde)

2026-09-27. The plan is the user's (untracked, not committed). This review checks it against the game's code and against what the V2 experiment (Phase 5) already built and measured.

## Verdict

**Worth doing, but not as written.** About two thirds of the plan re-implements what V2 already has, and its core model (a tick-level Markov chain on a grid, with per-tick decay) fits this game worse than V2's escape-level model. Two ideas in it are new and worth building:
1. **Outcome feedback for ambush sites** (the Thompson-sampling bandit);
2. **The player-adaptation study** (H2: route entropy, human playtests).

Built as a delta on V2, that's roughly 1–1.5 weeks, not 4.

## What already exists on `experiment/enemy-ml`

| Plan element | Status | Where |
|---|---|---|
| Trajectory logging, CSV for offline analysis | Done (10 Hz `samples.csv`, `events.jsonl`) | `PlayerBehaviourMonitor`, `TelemetryWriter` |
| Decayed player model persisted as JSON across sessions | Done, at escape level | `PlayerProfile`, `PlayerProfileStore` |
| Context (chased / calm) | Done as Fight / Flee / Kite / Passive | `EngagementTracker` |
| "Where the player goes when fleeing" | Done: 8 directions, per start zone, first escape per engagement | `FrequencyPredictor`, `LearnedPredictor` |
| Some zombies chase, some wait on likely routes | Done: pressure squad + ambush placed before the flee | `PredictiveCommander` |
| Cut-off (intercept the current flee) | Not built | — |
| Habitual and random bots | Done (RunnerA, RunnerB80, Random, plus Fighter, Mixed, Adaptive, Kiter) | `BotBrain`, `MLArena` |
| Loot-greedy bot | Missing (arena zombies drop nothing) | — |
| Condition toggles, batch runs | Done (Baseline / V1 / V2, headless eval player) | `eval.sh`, `arena-report.py --compare` |
| Prediction accuracy metric | Done (top-1 by direction, at bot flee decisions) | `evaluate_predictors.py --decisions` |
| Route entropy, waiting-vs-chasing contact share, heatmap | Not computed; the data is logged | — |
| Human playtests | Not done (Phase 6) | — |
| Ambush bandit | Missing | — |
| Claim-based coordination | Missing; V2 assigns centrally | `IHordeCommander`, `Squad` |

## What doesn't fit this game

1. **There is no nav graph or pathfinding to build on.** `Enemy.PathfindingUpdate` steers straight at its target, and a raycast only detects an obstacle. MainGame is an open field: 10 solid box colliders (base, props, NPCs), a tilemap without a collider, and no corridors. "Nodes" would be a plain grid, and the habit worth learning is *which way the player runs from the base*, which is what V2 already models.
2. **There is no working horde message system.** `JoinSwarmChase` exists, but `Enemy` overwrites its target every frame (known issue). On `main` there's no way to give a zombie a destination that sticks. The plan needs the experiment branch's order layer (`EnemyOrder`, `Squad`, `IHordeCommander`), or a rebuild of it.
3. **Zombies are slower than the player** (001Z `moveSpeed` 3, player 5). Most rollout nodes 5–10 s ahead are unreachable, so intercepting from a rollout rarely works. The ambush has to be placed *before* the flee. That makes it an escape-level prediction problem, not a tick-level one.
4. **The decay erases cross-session learning.** `T *= 0.995` every 0.5 s gives a half-life of about 69 s: after a 10-minute session, weight from its start is 0.995^1200 ≈ 0.0025. H1 ("over successive sessions") would see almost nothing from earlier sessions. Decay per observed event instead; V2 decays ×0.85 per escape.
5. **The data is sparse.** Four contexts × grid cells × next cells, and fleeing is a small share of ticks. Rows for rarely visited cells will be noise.
6. **The bandit as specified won't converge, and it double-counts.**
   - **Too many arms:** one per cell, but a session has tens of flees at most.
   - **Confounded reward:** "contact within N s" mostly measures whether the player went there, which `P_k` already scores, so θ × P_k counts it twice.
   - **No discounting:** Beta counts never forget, while the plan expects the player to adapt (H2).

   Fixes:
   - use a few arms (for example the 8 outer-ring zones, or the refuge approaches);
   - make the reward "contact, given the player came through that sector";
   - discount α and β per trial.
7. **The H3 ablation compares against a strawman.** Without claims, every zombie picks the same best node. Compare claims against central greedy assignment, which V2 already does.
8. **Survival time is a weak metric with bots.** Runner bots rarely die: RunnerA had 0–3 deaths in 26 waves under every commander. Use damage per wave, contact rate and ambush rate, as the V2 report does.

## What's new and worth building

1. **Outcome feedback for the ambush (the bandit, fixed as above).** V2's measured weak spot is exactly here: being on the route doesn't turn into contact.

   | Commander | RunnerA: zombie on route | RunnerA: ambushed |
   |---|---|---|
   | V2 | 55% | 58% |
   | V1 (just chases hard) | 2–4% | 53–77% |

   A reward on contact is the right signal. Thompson sampling also gives the exploration the V2 report lists as next for the Adaptive persona ("randomize between the top two routes").
2. **H2, the player adapting.** V2 already showed it with bots: the Adaptive persona spread its routes (A 42 / B 27 / C 36, against 74 / 23 / 53 under Baseline). Route entropy per session, and the human playtests, would make that a real result.
3. **An explainability overlay** ("went to NW2 because P = 0.61"). It's cheap: `prediction` events already carry the distribution.
4. **Optional: a short-horizon intercept for chasers only**, the one role V2 lacks. It needs a good reachability test because zombies are slower.

Suggested order: bandit on V2 ambush sites, then the metrics (route entropy, contact share), then an A/B against V2 in `MLArena`, then human playtests.

## Authorship

The plan says the commit history, logs and notebooks are "evidence that the work is your own and was done end-to-end", for a portfolio. On `experiment/enemy-ml`, the infrastructure above, V1 and V2 were written by Claude; the commits say so (`Co-Authored-By`). If this goes into a portfolio, either build the new parts yourself (each is small), using the existing branch only as clearly credited tooling, or describe the AI assistance the way the portfolio's rules require.
