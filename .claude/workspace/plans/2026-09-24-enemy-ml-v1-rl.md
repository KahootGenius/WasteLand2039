# Plan: learning enemies, V1 (reinforcement learning)

*2026-09-24. Status: **approved; Phases 0–3 done** (branch `experiment/enemy-ml`). V2 = option (a).*

> **Scope note from the user:** this is **experimental, for comparative analysis only**. The user has another design of their own that they expect to perform better. Keep the ML work isolated: new files, thin hooks into existing code, and the experiment branch only. If useful later, the user's design can plug into the same `IHordeCommander` seam as another comparison arm, but only if they want that.

## 1. Verdict

**Yes, it's feasible**, with one important reframing:

> A reinforcement-learning agent **cannot learn from a live human in real time**. Deep RL needs on the order of 10⁵–10⁶ decisions to learn; a human produces maybe a few dozen "flee" events per session.

So the design splits "learning" into two parts that each work at the speed they can:

| Part | What it learns | When it learns | Technique |
|---|---|---|---|
| **Player model** (online) | *This* player's habits: fight vs flee ratio, which way they escape, where they end up | Live, during play. Useful after ~5–10 encounters; persists between sessions | Running statistics over map zones, no neural net |
| **Commander policy** (the RL part) | *How to exploit* any player's habits: where to pre-position squads, when to hold vs chase | Offline, in fast simulation, against many synthetic player "personas" | PPO via Unity ML-Agents |

At play time, the trained policy reads the live player model as part of its input. Because it was trained against many kinds of players, it reacts to *this* player's profile without retraining. For example: "this player flees east through the car gap 80% of the time, so put a squad there before the next wave." This is standard opponent modeling, and it produces exactly the behavior you described.

## 2. What the current game gives us (from the 2026-09-24 scan)

- **Speeds:** player moves at 5, zombies at 3. A fleeing player always outruns a chase, so **interception by prediction is the only way to catch a runner**. That gives the RL a genuine, measurable problem.
- **Enemy AI** (`Enemy.cs`) is a simple state machine: chase the player within 5 units, otherwise march at `MainBase`. It uses raycast obstacle dodging (no pathfinding) and swarm alerts. There is no concept of positions, squads or orders.
- **Spawning** (`HordeEventSpawner`) spawns around the player. **Bug:** `RandomPointOnRing` uses `(cos, 0, sin)`, so enemies spawn on a horizontal line through the player, not a ring. This must be fixed first.
- **Map** (`MainGame`) is an open field (1 tilemap, about 13 box colliders from cars/trucks/barriers) with **no corridors or chokepoints**. "Escape path" therefore needs an explicit zone abstraction (§3.3).
- **Content:** only Day 2 (5 zombies) and Day 3 (20) have hordes, and days advance via dialogue. That's too few encounters per playthrough, so training and experiments need an "endless waves" mode.
- **Performance hazards:** `HordeEventSpawner.Update` logs every frame while spawning, and `Enemy` logs every hit and state change. At the 20× time scale used for training, this log spam will cripple throughput.
- **No version control.** Git is strongly recommended before this change set (§6, Phase 0).

## 3. Architecture

```
                 ┌────────────────────────── observes ─────────────────────────┐
                 │                                                             │
 Player ──► PlayerProfiler ──► profile vector ──► HordeCommander ──► orders ──► Squads ──► Enemy (scripted micro)
 (human or      (online stats:        (fight/flee %,     (swappable brain:     (spawn at zone,  (move-to / hold-ambush /
  bot persona)   engagements,          escape-zone        Baseline | V1-RL |    hold at gate,    chase, existing combat)
                 escape routes)        histogram, …)      V2 …)                 chase)
                                                              │
                                                              └──► TelemetryLogger ──► CSV/JSONL ──► analysis
```

### 3.1 Macro, not micro
RL controls the **commander** (one decision-maker per horde), **not individual zombies**. The commander's action space is small and discrete ("send squad 2 to zone E"), which makes learning tractable and its decisions interpretable for analysis. Individual zombies keep scripted movement and combat, extended with an order API.

### 3.2 Swappable commander (the key to comparative analysis)
`IHordeCommander` has interchangeable implementations, selected by a config ScriptableObject:
- `BaselineCommander`: reproduces today's behavior (spawn around the player, chase, go to base). It's the control group.
- `RLCommander` (**V1**): ML-Agents agent running the trained `.onnx` model.
- `V2Commander` (**V2**, option a): predicts the player's escape zone with a supervised model (Markov/n-gram or small classifier trained on logged play); scripted rules send squads there.

All commanders get **the same observations and the same order API**. Only the "brain" differs, so the comparison is fair.

### 3.3 Zones and operational definitions
- **Zones:** divide the map into K labeled zones (≈ 8–12). In `MainGame`, use hand-placed `ZoneMarker` objects (sectors around the base, gaps between vehicles). In the training arena, zones are built into the level: 3–4 distinct exit routes.
- **Engagement:** starts when ≥ 1 enemy is within *R* (≈ 6 units) of the player.
- **Fight vs flee:** classified per engagement from (a) the player's velocity component away from the enemy centroid, (b) shots fired per second, and (c) change in distance to the enemies. The thresholds get calibrated in Phase 2 against recordings where you deliberately play one way or the other.
- **Escape route:** the sequence of zones the player crosses in the *T* seconds after flee onset, plus the destination zone.
- **Player profile vector** (the "learned pattern"):
  - p(flee), updated with decay
  - escape-destination histogram over K zones
  - top escape route
  - mean flee distance
  - tendency to retreat to the base
  - reliability (how consistent the player is)

## 4. V1 RL design (ML-Agents, PPO)

| Element | Design |
|---|---|
| **Decision cadence** | Event-driven (flee onset, wave start) plus periodic every 1–2 s of game time |
| **Observations** | Profile vector (§3.3), player zone (one-hot), player velocity and HP, enemies remaining, squad count per zone, time into wave |
| **Actions** | Discrete, per squad (e.g. 3–4 squads): {hold, move to zone *k*, ambush-hold at zone *k*, chase player}; also spawn zone for the next reinforcement |
| **Reward** | + interception (enemy reaches attack range of a **fleeing** player); + damage dealt; small + for engaged time; − idle squads far from the action; − wave ending with the player unharmed. Kept deliberately simple, since the goal is analyzable behavior, not a perfect score |
| **Anti-"cheating" rule** | Pre-positioned squads spawn **outside the camera view**, so there's no visible pop-in and it feels fair |
| **Training opponents** | Scripted **bot personas** driving the same `PlayerController`: *Fighter*, *Runner-A* (always flees via route A), *Mixed* (60% flee, prefers B > C), *Adaptive* (switches route after being ambushed; tests over-commitment), *Random*. Persona parameters are randomized every episode |
| **Curriculum** | Fixed-route personas, then mixed, then adaptive (ML-Agents curriculum support) |
| **Important detail** | During training, the commander sees the **same noisy `PlayerProfiler` estimate** it will see from a human, never the persona's true parameters |
| **Throughput (rough)** | 20× time scale × ~8 arena copies in one scene ≈ 150 decisions/s, so ~500k decisions per hour of training (estimate, to be measured) |

**"Intelligence" test case:** in the arena, a *Runner-B* persona flees through exit B 80% of the time. Success means the commander has a squad at B **before** flee onset far more often than chance (1/3), and interception rate at B rises over the session.

## 5. Toolchain

| Item | Choice | Notes |
|---|---|---|
| Unity package | **ML-Agents Release 21**: `com.unity.ml-agents` 3.0.0-exp.1 (Sentis inference) | The last release whose minimum is Unity 2022.3. Fallback: **Release 20** (2.3.0-exp.3, Barracuda). Release 22+ need Unity 2023.2 / Unity 6. Upgrading the engine mid-project is **not** recommended |
| Python | conda env with **Python 3.10.12**, `mlagents` 1.0.0 | Miniconda is already installed. Keep it isolated from the other Pythons on this Mac (3.9/3.12/3.13, pyenv, uv) |
| Apple Silicon fixes | grpcio from conda-forge, protobuf ≈3.20, `numpy<1.24`, torch version per the release's setup.py | Known M-series issues documented by the community |
| Training files | New `MLTraining/` folder at the repo root: PPO YAML config, `results/`, analysis notebooks | Outside `Assets/`, so Unity won't import it. Only the final `.onnx` goes to `Assets/ML/Models/` |
| **Hardware limits** | M2, **8 GB RAM**, **25 GB free disk (95% full)** | Train against a standalone build rather than the Editor if RAM runs out. Keep 1 environment process with several arenas. Torch plus environments need ~3–5 GB of disk, and `results/` grows with checkpoints, so prune it |

Lightweight fallback: if the Python stack fights back, the same commander can be trained with **tabular Q-learning written in C#** inside Unity. The state is small enough for that. It's still RL, with no Python needed, but it's less expressive.

## 6. Phases

| Phase | Deliverables | Exit criteria | Rough size |
|---|---|---|---|
| **0. Groundwork** ✅ | Git init + Unity `.gitignore`; fix spawn-ring bug; gate the log spam behind a debug flag; "endless waves" test mode | Clean commit; hordes spawn in a real ring; 20× time scale runs smoothly | **Done 2026-09-24**, see §10 |
| **1. Order API + commander seam** ✅ | `Enemy` order states (MoveTo / HoldAmbush / Chase); `Squad`; spawner accepts commander spawn requests; `IHordeCommander`; `BaselineCommander` | Game plays the same as today through the baseline commander | ~1 week |
| **2. Zones, profiler, telemetry** ✅ | `ZoneMarker`/`ZoneMap`; `EngagementTracker` (fight/flee classifier); `PlayerProfiler`; `TelemetryLogger` (CSV/JSONL to `Application.persistentDataPath`) | You play 10 min deliberately "fighting", then 10 min "fleeing east", and the profiler reports both correctly | ~1 week |
| **3. Arena + bot personas** ✅ | `MLArena` scene (3–4 exit routes, replicated ×8); input abstraction so `PlayerController`/`WeaponManager` can be driven by a bot; persona ScriptableObjects | Each persona produces the expected profile in the profiler | **Done 2026-09-25**, see §10 and `reports/2026-09-25-phase3-arena-bots.md` |
| **4. V1 training** | ML-Agents package + conda env; `RLCommander` agent; PPO config; curriculum; trained `.onnx`; inference in `MainGame` | Beats the baseline on the interception metrics vs every persona; shows the pre-positioning behavior (§4 test case) | 1–2 weeks (iterative) |
| **5. V2** | Depends on your choice (§8) | Same interface, same metrics | TBD |
| **6. Evaluation** | Automated eval harness (N seeded episodes × persona × commander); human playtests; analysis notebooks and plots | Report-ready comparison tables and figures | ~1 week |

Sizes are rough solo estimates and will firm up after Phase 1.

## 7. Comparative-analysis framework (built in from Phase 2)

**Metrics** (logged per encounter, so any commander can be compared):
1. **Escape prediction accuracy**: was a squad already at the player's actual escape zone at flee onset? (top-1 / top-3; chance = 1/K)
2. **Interception rate** on flee events, and **time-to-contact** after flee onset
3. **Adaptation speed**: encounters until prediction accuracy passes a threshold
4. **Robustness**: performance against the *Adaptive* persona, which deliberately switches routes
5. Player damage taken per wave; wave duration; enemies wasted (never engaged)
6. Compute cost: decision latency and memory
7. *(Human playtests, optional)* perceived intelligence and fairness questionnaire, with within-subject randomized order of Baseline, V1 and V2

**Protocol:** fixed random seeds; same arena and personas for all commanders; report means with confidence intervals across ≥ 30 episodes per cell.

## 8. Open questions (needed before Phase 4)

1. ~~**What is V2?**~~ **Decided 2026-09-24: option (a)**, supervised escape-zone prediction plus scripted squad tactics. The options considered were:
   - **(a) Supervised prediction + scripted tactics:** a model (Markov/n-gram or a small classifier trained on logged play) predicts the next escape zone, and hand-written rules deploy squads there. This compares "learn to act" (RL) with "learn to predict, then rules act". **Recommended.**
   - **(b) Online tabular RL:** Q-learning or a contextual bandit in C# that learns *during* play. This compares offline deep RL with online lightweight RL.
   - (c) Something you already have in mind.
2. **Scope and rigor:** is this for a thesis or course report? That decides whether human playtests and statistical testing are required.
3. **Memory across sessions:** should the player profile persist between play sessions (saved to disk) or reset every session?

## 9. Risks

| Risk | Mitigation |
|---|---|
| Sim-to-real gap (bot personas ≠ humans) | Calibrate persona parameters from real human telemetry (Phase 2 logs); include the *Adaptive* and *Random* personas |
| Reward hacking (e.g. camping the base because players always return there) | Evaluate on metrics that are separate from the reward; inspect decision heatmaps |
| Open map makes "routes" fuzzy | Zone abstraction in `MainGame`; controlled routes in `MLArena` for the experiments |
| ML-Agents 3.0.0-exp.1 is experimental on 2022.3 | Fallback to Release 20 (Barracuda), or the C# tabular fallback |
| 8 GB RAM / 25 GB disk | Standalone-build training, one environment process, prune checkpoints; free disk space before starting |
| Large, untested codebase (`Enemy.cs` ≈ 1.5k lines, no git) | Phase 0 git; changes go into new files plus a thin hook in `Enemy.cs`; baseline commander proves no regression |

## Sources
- ML-Agents releases (versions and minimum Unity per release): https://github.com/Unity-Technologies/ml-agents/releases
- Release 21 installation requirements (Unity 2022.3+, Python 3.10.12): https://github.com/Unity-Technologies/ml-agents/blob/release_21/docs/Installation.md
- Apple Silicon setup notes (grpcio via conda, protobuf ~3.20, numpy<1.24): https://jackmckew.dev/ml-agents-for-unity-on-apple-silicon-m1m2m3

## 10. Progress log

### Phase 0: done 2026-09-24
- `main` (b533bbd): initial snapshot of the game, including the day's three earlier fixes. `.gitignore` covers Unity-generated folders; `.claude/.gitignore` keeps `workspace/scratch/` and `workspace/backups/` local.
- `experiment/enemy-ml`:
  - **Spawn ring fix.** `RandomPointOnRing` now uses X/Y. It's a single commit, so it can be cherry-picked to `main`.
  - **`VerboseLog`.** A `[Conditional("VERBOSE_LOGS")]` helper; 110 calls in 10 combat-loop scripts were converted: all `Debug.Log`, plus 14 routine messages that used `Debug.LogError`. Checked in the compiled player DLL: gated strings are absent and real error strings are still present.
  - **Endless waves.** `HordeEventSpawner.endlessWaves` (off by default) and `endlessWaveDelay`, plus a `CompletedWaves` counter.
- **Verified:** `.claude/tools/compile-check.sh` shows both Editor and player builds at 0 errors and 7 warnings (same as baseline).
- **Not yet verified (needs the Unity Editor):**
  - Hordes visibly spawn in a ring.
  - The Console is quiet during combat.
  - Endless mode restarts waves.
  - The game runs smoothly at a raised `Time.timeScale`.

### Phase 1: done 2026-09-24
- **Order API** (commit 00e11f68):
  - `EnemyOrder` supports None / MoveTo / HoldAt(pos, engageRadius) / Chase.
  - `Enemy` is now `partial`. The order logic is in `EnemyAI/Orders/Enemy.Orders.cs`; `Enemy.cs` only gains hooks in DetectPlayer, DetectTargets, PathfindingUpdate and CheckIfStuck, and every hook is a no-op with no order.
  - `Squad` groups enemies under one order.
  - Orders are drawn as gizmos: yellow = MoveTo, magenta = HoldAt.
- **Commander seam** (commit 7fb1e67b):
  - `IHordeCommander` has 5 callbacks: OnWaveStarted, ChooseSpawnPosition, OnEnemySpawned, Tick, OnWaveCompleted. `HordeContext` is the shared, read-only input to every commander.
  - `HordeEventSpawner` resolves its commander from the Inspector field, else a component on the same object, else a runtime-added `BaselineCommander`. No scene changes are needed.
  - `BaselineCommander` is the control group. It uses the same ring spawn rule with the same order of Random calls, and issues no orders.
  - `DebugOrdersCommander` is a test harness only, not a comparison arm.
- **Verified:** compile check passes in both Editor and player versions (83 scripts, 0 errors, 7 warnings, same as baseline). No GUID collisions. Each order type was traced by hand through `Enemy`'s update loop.
- **Found while tracing** (these are existing behavior, deliberately left as-is because they define the control group):
  - Swarm alerts never take effect.
  - The stuck-escape and obstacle-avoidance targets are overwritten every frame.
  - As a result, baseline zombies chase within 5 units and otherwise walk in a straight line to the base.
- **Not yet verified (needs the Unity Editor):** baseline play looks unchanged. With `DebugOrdersCommander` added to `DayManager`: ambush squads hold their posts, charge when the player comes near, and go back when the player escapes; the MoveTo squad reverts to marching on the base; the chase squad follows from any distance.
- **Implications for later phases:**
  - Because obstacle avoidance never takes effect, ordered squads (like all zombies today) walk straight lines and can get snagged on cars and trucks. Phase 3's arena should keep routes clear of props, or Phase 4 may need a simple waypoint path.
  - `EnemySpawner` (background spawner, off by default) bypasses the commander. Leave it disabled in experiments.

### Phase 2: done 2026-09-25
- **Pure logic** (commit 443c6c6a), unit-tested outside Unity by `.claude/tools/logic-tests.sh`:
  - `RadialZoneMap`: 17 zones around the base, `Core` plus `E1…SE1` (6–20 units) plus `E2…SE2` (20+ units).
  - `EngagementTracker`:
    - An engagement starts when an enemy comes within 6 units, and ends when none has been within 9 units for 2 s.
    - Each sample gets a label from a 0.6 s movement window (distance-weighted speed away from threats) plus a 1.5 s shot window: Fight / Flee / Kite / Passive.
    - Escape episodes record their route, destination zone and end reason.
  - `PlayerProfile`: decayed statistics, a feature vector of length 33 (8 + 17 + 8), and a text description.
  - `TelemetryWriter`: writes CSV and JSON lines, always in InvariantCulture.
- **Unity side** (commit e7d40f0f):
  - `PlayerBehaviourMonitor`: 10 Hz FixedUpdate sampling, telemetry session files, backquote overlay, zone gizmos, and a Tools menu item.
  - `EnemyAIBootstrap` adds the monitor automatically, so there are no scene edits.
  - `Enemy.Registry.cs` provides the list of active enemies and an `AnyDied` event.
  - `HordeContext` gains `Zones` / `Profile` / `Engagement`.
- **Verified:**
  - The compile check passes in both Editor and player versions (92 scripts, 0 errors, 7 warnings).
  - The logic tests all pass: fighter 100% Fight; east runner 92% Flee with E1 learned at 100% consistency; kiter 79% Kite; the profile switches E→N after a change of habit; CSV and JSON stay valid under the de-DE locale.
- **Found and fixed by the tests:**
  1. The 2 s wait after the last enemy died was being counted as Passive, so fighters only reached 65% Fight. Behavior time now counts only while a threat is within 15 units.
  2. A single 1.5 s window delayed flee detection by about 0.7 s, so runners showed 20% Passive. Movement and shooting now use separate windows (0.6 s / 1.5 s).
  - About 8% Passive remains at each flee onset (roughly 0.25 s of lag). Document this as a measurement limitation in the analysis.
- **Not yet verified (needs the Editor):**
  - The overlay and telemetry work in real play.
  - The thresholds (retreat ratio 0.45, 1 shot/s, windows) suit real human input.
  - The exit criterion: play about 10 min deliberately fighting, then about 10 min fleeing east, and check the overlay and `profile_final.json`.
- **Still open:** Q3 (profile persistence across sessions). It resets every session for now.

### Phase 3: done 2026-09-25 (not committed yet)
Full write-up: `reports/2026-09-25-phase3-arena-bots.md`.
- **Input seam.** `IPlayerInput` plus `InputOverride` on `PlayerController` and `WeaponManager` (null means keyboard, unchanged). `PlayerBot` drives the real player under the human rules: no firing while moving, the 0.42 s attack lock.
- **Isolation for 8 arenas in one scene** (all off by default):
  - `Enemy.AssignTargets`, and `Enemy.Start` skips the tag lookup when targets were assigned;
  - `HordeEventSpawner.bindEnemiesToSpawner` / `mainBaseTransform` / `AbortHordeEvent()`;
  - `PlayerBehaviourMonitor.trackSpawnerEnemiesOnly` / `mainBaseOverride` / `ResetProfile()` / `LogEvent()`.
- **Bots.**
  - Pure `BotBrain` + `BotPersonaSpec` → `BotParams` (re-sampled every episode) + `BotPersonaPresets`.
  - Personas: Fighter, RunnerA, Mixed (60% flee, B > C), Adaptive (drops a route after an ambush), Kiter, Random.
  - Ground-truth `bot_decision` events are written to telemetry.
- **Arena.** Built by *Tools > Enemy AI > Build ML Arena*.
  - Open field; base with MainGame's collider; refuges A = E, B = NW, C = SW at 26 units (zones E2 / NW2 / SW2 of the unchanged 17-zone map); Day 3 wave with a no-drop zombie variant.
  - `ArenaManager` replicates it ×8, 150 units apart.
  - Episode = 3 waves; each wave ends when cleared, after 90 s, or on player death.
- **Verified.**
  - Compile check: 0 errors / 7 warnings in both builds.
  - Logic tests: all pass, including every persona in a simulated arena.
  - Unity: 8 arenas at 8× for about 40 game-minutes each; every persona produced its expected profile signature (report §Verification).
  - MainGame smoke test through MCP: unchanged behaviour.
- **Changed the Phase 2 classifier (review).** `EngagementSettings.kiteGapSeconds = 1.5`: stopping to shoot within an escape counts as Kite. Otherwise kiting is invisible in this game: Kite 8% → 19%, and 0 of 3 → 11 of 14 escapes typed Kite. Set it to 0 to revert.
- **Found (game bugs, also on `main`, not fixed).**
  - `WeaponManager.availableWeapons` references the `Weapon.prefab` asset: the arenas shared one gun, MainGame writes to the asset, and reloads are free.
  - The `001Z_Attack` animation events have no receiver.
- **Carry into Phase 4.**
  - Runner profiles are about 40% Passive, a measurement effect of spawning around the player; Flee near 60% already means "runner".
  - Under Baseline, "ambushes" happen by chance often, so Adaptive already drifts.
  - Keep game-seconds per frame ≤ 0.02 when raising the time scale.
  - Consider 5 or more waves per training episode.
