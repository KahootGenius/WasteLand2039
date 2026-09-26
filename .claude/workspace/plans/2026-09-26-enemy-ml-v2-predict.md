# Plan: learning enemies, V2 (supervised escape prediction + scripted tactics)

*2026-09-26, branch `experiment/enemy-ml`. Follows `2026-09-24-enemy-ml-v1-rl.md` (§8 Q1: option a) and the Phase 4 result (`reports/2026-09-25-phase4-rl-commander.md`).*

## 1. Why V2, and what it has to show

V1 (PPO, two runs) learned *pressure*, not *anticipation*:
- it was far more lethal than Baseline;
- but it never put zombies on the route the bot was about to take (the plan §4 test failed; top-1 route prediction was below chance).

V2 splits the problem:
- **Learn to predict** where this player will escape (supervised, from logged play plus the live profile).
- **Hand-written rules act** on the prediction: hold an ambush squad there, off camera, before the flee starts.

This is the comparison the V1 plan set up: "learn to act" (V1) against "learn to predict, then rules act" (V2). Both use the same inputs (`HordeContext`: zones, profile, tracker) and the same order interface.

**Exit criterion for V2** (plan §4 test, same arenas, personas and metrics as V1):
- For RunnerB80, the most-occupied route at flee onset is the bot's route **far more often than chance (1/3)**.
- Interception or ambush at the chosen route **rises over the episode**, as the profile fills in.
- The commander is **not worse than Baseline** on ambush and damage for any persona.

## 2. Design

### 2.1 Predictor (pure logic, `EnemyAI/Prediction/`, unit-tested)

`IEscapePredictor.Predict(PredictionInput) → float[ZoneCount]`: a probability for each zone that the *next* escape ends there. `PredictionInput` contains:
- the profile (its observation vector and its route counts);
- the player's current zone;
- a threat histogram (enemies per direction around the player).

The tactics aggregate zone probabilities by sector.

Two predictors (the comparison inside V2):

| Predictor | How | Training |
|---|---|---|
| `FrequencyPredictor` (control) | Markov, backed off: P(end \| start zone) from the profile's route counts, then P(end) from the profile's destinations, then uniform. Blended by count, so a few escapes don't dominate. | None. Online, per player. |
| `LearnedPredictor` (the "supervised" part) | Multinomial logistic regression over the features: profile vector (33) + current zone (17, one-hot) + threat directions (8) + bias. Pure C# inference; weights in a JSON `TextAsset`. | Offline, on arena telemetry: each valid escape → (features at onset, end zone). NumPy training script in `MLTraining/`. |

**Training data.**
- A data scene `MLArena_Data`: Baseline commander, the full persona pool, **routes shuffled per episode** (as in V1 training), events-only telemetry, and profile snapshots.
- Features are rebuilt from telemetry: the last `profile` snapshot before `escape_start`, the start zone, and `enemy_zones` relative to the player.
- Data is split by session for held-out evaluation.

**Offline accuracy tool.** `arena-predict.py` reports top-1 / top-3 accuracy per persona for both predictors, against chance, and against the number of escapes seen in the episode (the "adaptation speed" metric, plan §7.3).

### 2.2 `PredictiveCommander` (scripted, `EnemyAI/Commanders/`)

- **Squads.**
  - *Pressure:* autonomous, which is exactly Baseline behaviour. Keeps engagements happening.
  - *Ambush:* `HoldAt` the predicted escape zone's hold point, engage radius about 6. The outer ring's point is about 25.5 units out, practically the refuge.
- **When to ambush.** Only once the prediction is confident: at least `minEscapes` escapes observed and top-sector probability ≥ `minProbability`. Until then, everything behaves like Baseline. This is the explicit "nothing to read yet" rule that V1 had to learn.
- **Where.**
  - The top predicted zone.
  - If the second sector is close (probability ≥ `splitRatio` × the top one) and there are enough enemies, split the ambush across both.
  - Keep the target (hysteresis) unless the prediction clearly changes. This limits Adaptive's over-commitment exploit and avoids thrash.
- **Reinforcements.**
  - Up to `ambushSize` (default 2 of the 5 alive) spawn *at* the ambush point, only if at least 10 units from the player (off camera, the plan's anti-cheating rule).
  - Otherwise they walk there from the Baseline spawn ring.
  - The rest spawn as in Baseline.
- **Not reactive by default.** At flee onset the ambush is **not** redirected to the actual flee direction, so the eval measures prediction, not reaction. `reactToFlee` can be switched on as a separate variant.
- **Telemetry.**
  - `commander_order` events (same format as RL).
  - A `prediction` event at each flee onset (top zones and probabilities), so accuracy can be measured online too.

### 2.3 Evaluation

- **Scenes.** `MLArena_Eval_V2` (plus `Arena_V2.prefab`), with the same 8 fixed personas, lockstep clock and 40 game-minutes as the Baseline and RL batches. The eval player carries the Baseline, RL and V2 scenes.
- **Metrics** (`arena-report.py --compare`): per-flee-decision route prediction, zombie on route, ambushed, ambush by wave, escape interception, damage, deaths, hold orders by route. Plus the offline predictor accuracy.
- **Variants:** V2 with `FrequencyPredictor` and with `LearnedPredictor`; optionally `reactToFlee`.

### 2.4 MainGame

- `EnemyAISettings.CommanderChoice` gains `Predictive` (V2).
- `EnemyAIBootstrap` adds a `PredictiveCommander` to spawners without one. The default stays Baseline.
- The persisted profile (2026-09-26) means V2 starts a new session already knowing the player.

## 3. Steps

| Step | Deliverables | Exit |
|---|---|---|
| **5a. Predictors** | `IEscapePredictor`, `FrequencyPredictor`, `LearnedPredictor` (inference); logic tests | Tests pass; on synthetic runners the frequency predictor's top-1 is the true route after about 3 escapes |
| **5b. Commander + eval** | `PredictiveCommander`; `Arena_V2`, `MLArena_Eval_V2`; eval with `FrequencyPredictor` | §4 test met for RunnerB80; not worse than Baseline elsewhere |
| **5c. Learned predictor** | `MLArena_Data`, dataset builder + trainer (NumPy), `arena-predict.py`, trained weights; eval with `LearnedPredictor` | Offline top-1 ≥ frequency on held-out sessions (especially with few escapes and for Adaptive); arena eval at least as good as 5b |
| **5d. Wrap-up** | MainGame hook; report; `IDEAS.md`; commits | Report with the V1 / V2 / Baseline comparison |

## 4. Risks and notes

- **The bots' route choice ignores context:** a weighted roll, with the Adaptive persona's penalty after ambushes. On the arena personas the frequency predictor is close to optimal, so the learned predictor can only win on low counts, adaptation and threat context. It may tie. That is still a result, and human players (Phase 6) are where context should matter.
- **Adaptive persona:** a perfect ambush makes it switch routes. The hysteresis and the confidence gate are the counter. Report its route mix like in Phase 4.
- **Too visible or too strong:** ambushers spawn off camera and hold still. If playtests feel unfair, cap the ambush size or delay the ambush after wave start.
- **Everything stays isolated:** new files, thin hooks, experiment branch only. The Baseline control group is untouched.
