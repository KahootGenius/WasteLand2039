# Phase 4 report: V1 RL commander (ML-Agents PPO)

*2026-09-25, branch `experiment/enemy-ml`. Plan: `plans/2026-09-24-enemy-ml-v1-rl.md`, §4 and §6 Phase 4. **Decision 2026-09-26:** the user accepted the recommendation. V1 is closed as is, work moves to V2, and the profile will persist across sessions (with a switch).*

## Summary

- **The pipeline works end to end.**
  - ML-Agents (Release 21) runs in the project.
  - A headless training player trains a PPO commander against the Phase 3 bots.
  - A separate headless eval player scores any commander against Baseline, with the same personas and a lockstep clock.
  - The trained `.onnx` can drive the hordes in `MainGame`. This is off by default (*Tools > Enemy AI > Game Commander*).
- **First run, `v1_ppo_01`: 2 M decisions, 1.5 h.** The RL commander is **much more dangerous than Baseline against every persona**:
  - fleeing bots are ambushed on their route 2–5× as often;
  - player damage per wave is up for all 7 personas;
  - deaths per wave are up for all 7 personas.
- **It does not do what the plan's §4 test asks: it doesn't predict the route.**
  - It learned "chase, plus a fixed ring of holding squads" (mostly N1, NE2, SW2). The ring is the same whichever way the bot runs.
  - For RunnerB80 (flees via B 80% of the time), the most-occupied route at flee onset matched the bot's route 23% of the time, below chance (33%). On B flees it matched only 1 time in 32.
  - Only 2% of its hold orders went to B's sector.
- **Exit criterion:**
  - "Beats the baseline on interception vs every persona": met on the bot-side ambush measure. On the stricter monitor measure (a zombie within 2.5 units), it's met for 4 of 6 fleeing personas; RunnerA and Mixed are not significant.
  - Pre-positioning (§4): **not met**.
- **Correction to an earlier number.** An interim check showed "pre-positioned 7% → 44%". That metric mostly counted zombies already chasing the bot into a sector. `arena-report.py` now also measures per bot decision, which is the clean test. Details under *Measuring pre-positioning*.
- **Second run, `v1_ppo_02` (2 M decisions, 2 h): same picture.** It targeted the two likely causes:
  - a smaller action space (RetaskOne: re-task at most one squad every 2 s);
  - a reward bonus when the intercepting zombie comes from **in front** of the fleeing player.

  Its policy is sharper (entropy 67% of maximum vs 82%), and its holds now depend on the persona: fighters and kiters get the south-west, and RunnerA gets more east. But it almost never holds B (0% of hold orders in deterministic play, 2–6% sampled). Route prediction for RunnerB80 stays below chance: 19% deterministic, 23% sampled.
- **Conclusion for V1.** In this setup, PPO learns *pressure* (chasing plus a persona-independent, south-west-heavy holding ring) but not *route anticipation*. Both runs, each in deterministic and sampled play, pass the interception part of the exit criterion on the bot-side ambush measure, and all four fail the §4 test.
  - The recommended next step is **V2** (supervised escape-zone prediction plus scripted squad tactics), which attacks exactly this gap. The RL runs serve as its control.
  - Ideas for a further RL attempt are listed at the end.
- **Installed model:** `v1_ppo_02`, in `Arena_RL` and `EnemyAISettings`. MainGame is still on Baseline.

## What was built

| Piece | Where | Notes |
|---|---|---|
| Python toolchain | `MLTraining/setup_env.sh`, conda env `mlagents` | mlagents 1.0.0, torch 2.1.2, Python 3.10.12. Apple Silicon pins are recorded in the script and in `IDEAS.md`. |
| Unity package | `com.unity.ml-agents` 3.0.0-exp.1 (+ Sentis 1.2.0-exp.2) | The last release that supports 2022.3. |
| Action scheme (pure) | `EnemyAI/Learning/CommanderActions.cs` | 3 squads. Orders per squad: keep / autonomous (= Baseline behaviour) / chase / hold zone *k* (17 zones), plus which squad the next reinforcement joins. Two layouts: **PerSquad** (every squad every 1 s, branches [20, 20, 20, 3]) and **RetaskOne** (one squad every 2 s, branches [4, 19, 3]). The installed model's action-mask size selects the layout automatically. |
| Reward (pure) | `EnemyAI/Learning/CommanderRewards.cs` | See *Reward*. Unit-tested in `logic-tests/CommanderTests.cs`. |
| Agent | `EnemyAI/Commanders/RLCommander.cs` | `Agent` + `IHordeCommander` + `IArenaEpisodeListener`. 111 observations. Decides at wave start, at every flee onset, and periodically. Reinforcements for a holding squad spawn at its zone, at least 10 units from the player (off camera; the plan's anti-cheating rule). The heuristic is roughly Baseline. |
| Curriculum | `EnemyAI/Arena/ArenaTraining.cs`, `ArenaEnvironment`, `MLTraining/config/*.yaml` | `persona_level` 0 = runners, then + Mixed / Kiter / Fighter, then + Adaptive / Random. Advances on training progress (20% / 50%). **Routes are shuffled every episode**, so a policy can only find the route by reading the profile. |
| Training arena | `Arena_RL.prefab`, `MLArena_RL.unity` | Same arena as Phase 3; RL commander; 4 waves per episode; telemetry off. |
| Eval arenas | `MLArena_Eval_Baseline.unity`, `MLArena_Eval_RL.unity` | 8 arenas with fixed personas: Fighter, RunnerA, RunnerB80, Mixed, Adaptive, Kiter, Random, RunnerB80. 4 waves per episode, full telemetry, lockstep 0.02 s per frame. `RunnerB80` is the plan's §4 test persona (flees 100%, route weights A 0.1 / B 0.8 / C 0.1); it is eval-only. |
| Scripts | `MLTraining/train.sh`, `eval.sh` | `train.sh RUN_ID [NUM_ENVS]` with `ARENA_BUILD` / `CONFIG` overrides. `eval.sh SCENE [GAME_MINUTES]`. |
| Model install | *Tools > Enemy AI > Install Latest Commander Model* (`ArenaAssetBuilder.InstallModel`) | Copies to `Assets/ML/Models/`, detects the scheme, and configures `Arena_RL` and `EnemyAISettings`. |
| MainGame hook | `EnemyAIBootstrap`, `EnemyAISettings` (`Assets/ML/Resources/`) | With *Game Commander: RL*, adds an inference-only `RLCommander` to spawners that have no commander. There are no scene edits, and the default is **Baseline**. Smoke-tested in MainGame through MCP: squads chased and held zones, no errors. |
| Telemetry additions | `PlayerBehaviourMonitor`, `RLCommander` | Format 2: `escape_start.enemy_zones`, `escape_end.min_enemy_distance` / `intercepted`, `escape_type` (fixes a duplicate JSON key). New `commander_order` events (squad, order, zone). |
| Report tool | `MLTraining/tools/arena-report.py --compare A B [--markdown]` | Wilson 95% intervals. Per-decision route metrics (below). |

**Reward** (per wave; kept simple as the plan asks):
- +1 per escape intercepted (a zombie within 2.5 units after the first 1 s);
- +0.01 per HP of player damage;
- +1 if the player dies;
- +0.002 per second engaged;
- −0.5 if the wave ends with the player unharmed;
- `v1_ppo_02` only: +2 when the interception is head-on.

**Throughput:** 3 headless players × 8 arenas at 0.02 s of game time per frame gives about 370 decisions/s, so 2 M decisions take about 1.5 h. The eval player runs at about ×37 real time, so 40 game-minutes take about 70 s.

## Evaluation protocol

- The same eval player and the same 8 personas for both commanders. Each arena runs 40 game-minutes: roughly 7–8 episodes and 26–80 waves per persona, depending on how fast waves end.
- Persona parameters are re-sampled per episode with fixed seeds.
- Batches:
  - Baseline: `20260925_0727`;
  - `v1_ppo_01` at 600k: `20260925_0732`;
  - `v1_ppo_01` final: `20260925_0834`.

  All are in `~/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry/`.
- Command: `python3 MLTraining/tools/arena-report.py --compare 20260925_0727 20260925_0834`.
- **Deterministic vs sampled play.** By default the eval takes the policy's most likely action, which is what `MainGame` would use. For a policy that is still close to uniform, this can collapse into a single repeated action. The `v1_ppo_02` 400k checkpoint re-tasked only squad 2, back and forth between W1 and SE1, for every persona. So `eval.sh ... -stochastic` also evaluates sampled play, which is how the policy behaved in training.
  - ML-Agents caches one inference runner per model and ignores the deterministic flag when reusing it, so the flag has to be set before the arenas are created. A first attempt that changed it afterwards silently stayed deterministic; batch `20260925_0913` is invalid for that reason.

## Results: `v1_ppo_01` (final, 2 M) vs Baseline

95% Wilson intervals in brackets.

| Persona | Metric | Baseline | RL `v1_ppo_01` |
|---|---|---|---|
| RunnerA | flee ambushed (bot's own check) | 10% [6–14] (20/209) | **53%** [43–62] (57/108) |
| RunnerA | escapes intercepted (≤ 2.5 u) | 1% [0–3] | 2% [1–4] (n.s.) |
| RunnerA | damage per wave / deaths | 5 / 1 of 26 | 23 / 3 of 27 |
| RunnerB80 | flee ambushed | 27% [22–32] (94/350) | **70%** [63–76] (143/204) |
| RunnerB80 | escapes intercepted | 2% [1–3] | **11%** [9–14] |
| RunnerB80 | damage per wave / deaths | 10 / 4 of 54 | 61 / 40 of 76 |
| Mixed | flee ambushed | 42% [30–56] | **78%** [66–86] |
| Mixed | escapes intercepted | 6% [3–13] | 9% [6–13] (n.s.) |
| Mixed | damage per wave / deaths | 64 / 16 of 41 | 96 / 63 of 68 |
| Adaptive | flee ambushed | 21% [15–27] | **70%** [61–78] |
| Adaptive | escapes intercepted | 0% [0–2] | **9%** [6–12] |
| Adaptive | damage per wave / deaths | 5 / 1 of 26 | 48 / 12 of 31 |
| Random | flee ambushed | 37% [22–54] | **81%** [57–93] |
| Random | escapes intercepted | 23% [17–31] | **67%** [61–73] |
| Kiter | escapes intercepted | 46% [39–54] | **95%** [92–97] |
| Kiter | damage per wave / deaths | 32 / 11 of 41 | 85 / 44 of 52 |
| Fighter | damage per wave / deaths | 66 / 28 of 62 | 88 / 59 of 80 |

- **"Flee ambushed"** is the bot's own check (`bot_ambushed`, Phase 3): after the first 1 s of a flee, it takes damage or meets a zombie within 4 units ahead on its route.
- **"Escapes intercepted"** is the monitor's commander-independent measure. It counts every escape, including the bot being flushed out of its refuge again.
- The two disagree for runners because most RL "ambushes" are chasers catching up and hitting (damage), not a zombie waiting within 2.5 units.

**Side effect on Adaptive.** Under RL, the Adaptive bot's route choices even out (A 52 / B 28 / C 24, against A 114 / B 16 / C 48 under Baseline). It is ambushed often enough to keep abandoning its favourite route. That's what the persona is designed to do, and it's the "over-commitment" pressure the plan wanted to test. This time it's caused by pressure everywhere, not by prediction.

## Measuring pre-positioning (§4 test)

The plan's test: *"Runner-B flees via B 80%. Success means the commander has a squad at B before flee onset far more often than chance (1/3), and interception at B rises over the session."*

**Why the first metric was wrong.** The monitor's escape-level "pre-positioned" metric asks whether a zombie was in the escape's destination zone at onset. But of the 862 escapes in one RunnerB80 session (600k eval), only 54 started at the base; most started at a refuge, with chasers already around the bot. So that metric mostly measures chasing. For example, even under Baseline, 64% of RunnerB80 escapes to A had a zombie at A at onset.

**Per-decision metrics (new in `arena-report.py`).** There is one sample per bot flee decision, using the bot's ground-truth route and enemy positions from the escape that starts within 2 s. The bot's route choice is a pure weighted roll, so zombies don't bias which route it picks.
- **Zombie on route:** a zombie was in the chosen route's sector when the bot decided. It only counts decisions made outside that sector, so chasers don't count.
- **Route predicted (top-1):** the route sector with the most zombies is the one the bot takes. It counts only decisions made outside all route sectors, with at least one route occupied. Chance is 1/3.
- **Ambushed, by wave in the episode:** the profile resets each episode, so a commander that learns the player should improve from wave 1 to wave 4.
- **Hold orders by route sector,** from `commander_order`.

**`v1_ppo_01` result: no route prediction.**

| | RunnerA (always A) | RunnerB80 (80% B) | Mixed (B > C) |
|---|---|---|---|
| Route predicted (chance 33%) | 0% (0/40) | 23% [13–37] (11/48) | 50% [31–69] (11/22) |
| Zombie on the chosen route at decision | 4% | 8% | 21% |
| Hold orders to A / B / C sector | 11% / 3% / 17% | 6% / 2% / 20% | 5% / 2% / 26% |
| Ambushed, wave 1 → 4 | 67% → 47% | 63% → 74% | 76% → 73% |

The hold-zone distribution is nearly the same for every fleeing persona. RunnerA and RunnerB80 both get about 20% N1, 21% NE2, 15% SW2 and 11–15% S2, and 1–2% NW2 (the B refuge). The policy does distinguish fighter types from runners (fighters and kiters get more W1 and SW), but not *which way* a runner goes.

Every "route predicted" hit is a C flee, because the fixed ring is heavy on the south-west:
- Mixed: 11 of 12 C flees, 0 of 10 B flees.
- RunnerB80: 10 of 11 C flees, 1 of 32 B flees.

So Mixed's 50% is an artifact, and on its main route B the policy never had the most zombies.

## Diagnosis

1. **The action space is large for the budget.** PerSquad chooses 3 × 20 options every second. Entropy was still 8.3 of a possible 10.1 at 2 M steps, so the policy is still fairly random per decision. A coherent "hold B and keep holding it" has to be repeated every second.
2. **The reward doesn't separate an ambush from a chase.** The interception reward pays the same for a zombie that waited on the route as for one that ran the player down, and chasing is much easier to discover. The observation contains what's needed to predict the route (the profile's zone shares and direction bins), but nothing rewards using it specifically.
3. *(For completeness.)* The profile resets each episode (4 waves). In wave 1 there's nothing to read yet. The curriculum's route shuffling is deliberate: it stops the policy memorising "runners go east".

## `v1_ppo_02`: RetaskOne + head-on bonus

- **Action scheme:** RetaskOne. Every 2 s, at wave start and at flee onset, re-task at most one squad; the others keep their orders. Branches [4, 19, 3].
- **Reward:** as `v1_ppo_01`, plus `head_on_bonus = 2`. It is paid when an interception happens and the nearest zombie is ahead of the player's movement (cos ≥ 0.5). Config: `MLTraining/config/commander_ppo_headon.yaml`. The parameter is read from the trainer's environment parameters, and the Unity default is 0.
- **Changes two things at once,** because both causes above are plausible and a run costs 1.5 h. If it works, a single-change ablation can follow (PerSquad + bonus, or RetaskOne without it).

**Training.**
- 2 M decisions in 2.0 h at about 280 decisions/s, the same curriculum, and 0.02 s per frame.
- Entropy fell from 94% of maximum at 250k to 67% at 2 M (`v1_ppo_01`: 82%), so this policy committed more.
- Episodes got shorter (236 → 148 decisions), because waves end sooner once the bot dies.
- Reward is not comparable with `v1_ppo_01`, because of the bonus.

**Evaluation.**
- Five batches, all on the same eval player code and personas:
  - Baseline `20260925_0727`;
  - `v1_ppo_01` deterministic `0834` and sampled `1048`;
  - `v1_ppo_02` deterministic `1042` and sampled `1044`.
- "Deterministic" takes the most likely action, which is what MainGame uses. "Sampled" is `-stochastic`, which is how the policy played in training.

*Pressure* (95% Wilson intervals):

| Persona | Metric | Baseline | 01 det | 01 sampled | 02 det | 02 sampled |
|---|---|---|---|---|---|---|
| RunnerA | ambushed | 10% [6%–14%] | 53% [43%–62%] | 57% [47%–67%] | 77% [66%–85%] | 59% [48%–69%] |
| RunnerA | intercepted | 1% [0%–3%] | 2% [1%–4%] | 2% [1%–4%] | 2% [1%–4%] | 2% [1%–5%] |
| RunnerA | damage / deaths | 5 / 1 of 26 | 23 / 3 of 27 | 21 / 5 of 27 | 19 / 3 of 28 | 38 / 12 of 32 |
| RunnerB80 | ambushed | 27% [22%–32%] | 70% [63%–76%] | 52% [46%–58%] | 73% [66%–78%] | 68% [61%–74%] |
| RunnerB80 | intercepted | 2% [1%–3%] | 11% [9%–14%] | 8% [6%–11%] | 6% [4%–8%] | 8% [6%–11%] |
| RunnerB80 | damage / deaths | 10 / 4 of 54 | 61 / 40 of 76 | 46 / 23 of 69 | 43 / 24 of 68 | 48 / 28 of 63 |
| Mixed | ambushed | 42% [30%–56%] | 78% [66%–86%] | 65% [53%–76%] | 75% [64%–84%] | 70% [58%–80%] |
| Mixed | intercepted | 6% [3%–13%] | 9% [6%–13%] | 5% [3%–9%] | 9% [6%–13%] | 4% [2%–7%] |
| Mixed | damage / deaths | 64 / 16 of 41 | 96 / 63 of 68 | 95 / 48 of 54 | 92 / 53 of 59 | 85 / 46 of 54 |
| Adaptive | ambushed | 21% [15%–27%] | 70% [61%–78%] | 57% [48%–66%] | 79% [69%–86%] | 71% [61%–79%] |
| Adaptive | intercepted | 0% [0%–2%] | 9% [6%–12%] | 6% [4%–9%] | 5% [3%–8%] | 5% [3%–8%] |
| Adaptive | damage / deaths | 5 / 1 of 26 | 48 / 12 of 31 | 43 / 14 of 35 | 39 / 11 of 32 | 38 / 9 of 30 |
| Random | ambushed | 37% [22%–54%] | 81% [57%–93%] | 70% [53%–83%] | 95% [77%–99%] | 72% [52%–86%] |
| Random | intercepted | 23% [17%–31%] | 67% [61%–73%] | 29% [23%–36%] | 59% [53%–64%] | 57% [51%–64%] |
| Random | damage / deaths | 63 / 22 of 46 | 93 / 60 of 65 | 90 / 61 of 70 | 90 / 55 of 63 | 94 / 64 of 68 |
| Kiter | intercepted | 46% [39%–54%] | 95% [92%–97%] | 62% [56%–68%] | 92% [89%–95%] | 89% [85%–92%] |
| Kiter | damage / deaths | 32 / 11 of 41 | 85 / 44 of 52 | 66 / 29 of 47 | 87 / 45 of 53 | 72 / 32 of 46 |
| Fighter | damage / deaths | 66 / 28 of 62 | 88 / 59 of 80 | 94 / 69 of 82 | 98 / 116 of 122 | 97 / 88 of 93 |

*Route anticipation.* "On route" = a zombie was in the chosen route's sector when the bot decided to flee (bot outside it). "Predicted" = the most-occupied route sector was the bot's route (chance 33%).

| Persona | Metric | Baseline | 01 det | 01 sampled | 02 det | 02 sampled |
|---|---|---|---|---|---|---|
| RunnerA | on route | 1% (3/209) | 4% (2/56) | 15% (6/41) | 2% (1/49) | 21% (10/48) |
| RunnerA | predicted | 33% (3/9) | 0% (0/40) | 29% (5/17) | 6% (1/17) | 36% (9/25) |
| RunnerB80 | on route | 2% (7/349) | 8% (12/159) | 12% (24/196) | 7% (11/153) | 11% (16/146) |
| RunnerB80 | predicted | 29% (7/24) | 23% (11/48) | 19% (13/68) | 19% (10/52) | 23% (14/62) |
| Mixed | on route | 2% (1/51) | 21% (11/53) | 19% (11/58) | 9% (5/56) | 12% (6/52) |
| Mixed | predicted | 33% (1/3) | 50% (11/22) | 43% (9/21) | 22% (5/23) | 21% (4/19) |
| Adaptive | on route | 1% (1/173) | 7% (6/85) | 17% (16/93) | 3% (2/78) | 5% (5/91) |
| Adaptive | predicted | 10% (1/10) | 19% (5/27) | 34% (11/32) | 10% (2/20) | 25% (5/20) |
| Random | on route | 0% (0/30) | 0% (0/13) | 7% (2/28) | 5% (1/19) | 5% (1/19) |
| Random | predicted | – | 0% (0/3) | 20% (2/10) | 10% (1/10) | 0% (0/6) |

**Route occupancy by persona.** This is the share of flee decisions where each route sector held a zombie; it's the direct profile-reading test.

| Batch | RunnerA: A / B / C | RunnerB80: A / B / C | Top hold zones (RunnerB80) |
|---|---|---|---|
| Baseline | 1 / 1 / 2% | 3 / 2 / 2% | – |
| 01 det | 4 / 5 / 35% | 2 / 3 / 23% | NE2, N1, SW2, W1 |
| 01 sampled | 15 / 9 / 9% | 11 / 10 / 22% | spread thinly; orders change constantly |
| 02 det | 2 / 3 / 19% | 3 / 2 / 26% | S2, N1, SW1, E1 |
| 02 sampled | 21 / 5 / 16% | 15 / 3 / 22% | E1, SW1, S2, S1 |

**Reading it.**
- **Pressure:**
  - Every RL variant beats Baseline on bot-side ambushes for every fleeing persona, and on damage and deaths for every persona.
  - The monitor's stricter interception (a zombie within 2.5 units) is not significant for RunnerA in any variant, or for Mixed. Runners are caught mostly by chasers landing hits, not by zombies waiting in the path.
  - The deterministic `v1_ppo_02` is the most lethal against fighters: they die in 116 of 122 waves.
- **Route anticipation:**
  - No variant singles out B for the runner that uses B 80% of the time.
    - In three variants B is occupied at 2–3% of RunnerB80's flee decisions.
    - Sampled `v1_ppo_01` reaches 10%, but only because it spreads zombies everywhere: A is at 11% and C at 22% for the same persona.
  - The only persona-dependent route signal is east for RunnerA under `v1_ppo_02`. Deterministic play gives E1 36% of its hold orders, against 15% for RunnerB80; sampled play has A occupied at 21%, against 15%. It's weak and not significant.
  - Three of the four variants keep the south-west occupied (16–38% of flee decisions) whatever the persona. That's why C flees score "predicted" hits.
- **Over the session:** ambushes at B for RunnerB80 rise from wave 1 to wave 4 only in `v1_ppo_02` deterministic (64% → 72% → 72% → 85%). The intervals overlap and Baseline also drifts up (26% → 33%), so this is a hint at best.
- **Why never B?**
  - Not a bug: training routes are a correct per-episode Fisher–Yates shuffle, and the bot receives the shuffled weights.
  - Not geometry: B flees end in NW1 / NW2 as expected.
  - Both policies simply learned that holding the south-west, north and east pays on average. Nothing in the reward singles out *this runner's* route, and the chase-and-hit reward is available on every route.

## Practical notes (also in `IDEAS.md`)

- **ML-Agents fixes the capture frame rate at 60.** Game time per frame is time scale ÷ capture rate: 20 ÷ 60 = 0.33 s, which breaks `Update`-based bots and targeting. `train.sh` passes `--capture-frame-rate=1000`, giving 0.02 s per frame. The player log prints the actual value.
- **Player builds save all dirty assets.** URP re-serializes `UniversalRP.asset`, and TMP empties the fallback font's glyph table. After each build, run `git checkout --` on both. Neither is our change.
- **The weapon-asset bug has a save hazard.** After playing MainGame, `Weapon.prefab` is dirty in memory, so any project-wide save writes the moved `Firepoint`. This happened once in this phase and was restored with `git checkout`. The builder now only saves its own assets.
- **Throughput and memory.** The machine runs near its 8 GB limit with the Editor open and 3 training players. Don't run two trainings at once.

## For you to check or decide

1. **Phase 4 exit criterion: partly met.**
   - The interception part is met on the bot-side ambush measure, for every fleeing persona, in both runs.
   - The §4 pre-positioning part is **not met** by either run.
   - My recommendation: close Phase 4 with this result and move to **V2**, which predicts the escape zone directly (supervised) and scripts the ambush. Keep `v1_ppo_01` / `v1_ppo_02` as the RL arm of the comparison.

   If you'd rather push RL further first, the most promising changes, roughly in order of cost:
   - **Hindsight route reward.** Pay once per escape, after it ends, if a squad was holding the escape's *destination* sector at onset. This directly rewards anticipation; the destination is known in hindsight, so it's legal for training.
   - **Route-level actions.** Give holds at A / B / C / none instead of 17 zones, plus the per-squad chase. This fits the arena but not MainGame's zone map, so it's a diagnostic rather than a product.
   - **Longer training** (10 M decisions, about 9 h) and longer episodes (5+ waves, as Phase 3 suggested), so the profile has more to say.
   - **An ablation** of `v1_ppo_02`'s two changes. This is only worth it if one of the above works.
2. **Whether the RL commander is "fair" in MainGame.** It is much more lethal: RunnerB80 dies in 23–40 of about 70 waves, against 4 of 54 under Baseline, and the Fighter persona in up to 116 of 122. That's fine for comparative analysis, but it would need tuning (squad caps, a less aggressive reward) before anyone plays against it.
3. **Carried over from Phase 3, still open:**
   - `kiteGapSeconds = 1.5`;
   - the weapon-asset bug and the zombie animation events on `main`;
   - Open question 3 (profile persistence).
