# Adaptive Horde on top of V2: what to build, what to reuse

2026-09-27. A build guide for the user's own implementation of their `adaptive_horde_plan.md` (kept outside the repo), narrowed to what's new over V2 (see `reports/2026-09-27-adaptive-horde-plan-review.md`). The user writes the code; this file only says what to write and where it plugs in.

## 0. The idea in one paragraph

V2 already learns *which way* the player flees (from the profile) and puts an ambush there. V2 is measured and deterministic, but it has no feedback: RunnerA had a zombie on its route 55% of the time but was ambushed only 58% of the time. Your version adds two learning pieces on top:

- **A. Direction by Thompson sampling.** Sample the ambush direction from the profile's posterior instead of always taking the most likely one. This explores, and it keeps a player who adapts (the Adaptive bot, or humans) from simply routing around a fixed ambush.
- **B. Placement by a contact-rewarded bandit.** Learn *where along the predicted direction* waiting actually produces contact. Arms are hold distances from the base; the reward is "an ambusher reached the player", counted only when the player really ran that way.

Plus the measurement that makes it a result: contact rate, route entropy (H2), learning curves, and human playtests.

**Drop from the original plan:**
- the grid Markov model with per-tick decay (the V2 profile replaces it);
- the k-step rollouts (unreachable at zombie speed 3 against player speed 5);
- claim messages (the central assignment in `PredictiveCommander` already does it, and "no claims" is a strawman);
- the loot-greedy bot (arena zombies drop nothing; optional later).

## 1. Setup

- Branch from `experiment/enemy-ml`, for example `git switch -c adaptive-horde experiment/enemy-ml`. Your commits on that branch are the authorship record; the base branch's commits carry `Co-Authored-By: Claude`.
- Unity 2022.3, Editor open. After every C# change, run `.claude/tools/compile-check.sh` (about 4 s; it checks both the editor and player builds). Run `.claude/tools/logic-tests.sh` after touching pure logic.

## 2. What to write

### 2.1 `Assets/Scripts/EnemyAI/Prediction/AmbushBandit.cs` (new, pure C#)

Put it in `Prediction/`: `logic-tests.sh` already compiles every file there, so it's testable outside Unity with no setup. Use only `System` (plus `UnityEngine.Mathf` if you like); no `Time`, `Debug`, `Random` or scene objects.

Responsibilities:
- **Arms:** `sector (0..7) × distance index` (for example distances `{12, 18, 25}` from the base center). Also support a *shared* mode where the arms are the distances only, across all sectors (3 arms). That's an experiment: is "where to wait" a property of the map or of each route?
- **State:** `alpha[]`, `beta[]`, starting at the prior (1, 1).
- **Choosing:** for each candidate arm, draw θ ~ Beta(α, β) and score it `P(sector) × θ`. Return the best arm, and optionally a second best in a non-adjacent sector (V2 splits into up to 2 sites; keep that).

  This is your plan's "weight by P_k", and it's no longer double counting, because θ now means P(contact | the player ran this way, we waited at this distance).
- **Updating:** `Update(sector, distanceIndex, bool contact)`. Discount toward the prior first, then add: `α ← 1 + γ(α − 1)`, `β ← 1 + γ(β − 1)`, then `α += contact ? 1 : 0` and `β += contact ? 0 : 1`. Try γ ≈ 0.9. Decaying toward the prior (not toward 0) keeps shapes ≥ 1 and keeps the posterior from collapsing.
- **Sampling:** Beta(a, b) = X / (X + Y) with X ~ Gamma(a), Y ~ Gamma(b). Gamma uses Marsaglia–Tsang (for shape ≥ 1), plus the boost `Gamma(a) = Gamma(a + 1) · U^(1/a)` for shape < 1, which part A needs.
- **Randomness:** use your own `System.Random` with a seed, **not** `UnityEngine.Random`. Spawn positions use `UnityEngine.Random` (`HordeContext.DefaultSpawnPosition`), so extra draws from it would shift every spawn and add noise to the V2 comparison.
- **State export and import** (for persistence, 2.4): mirror `PlayerProfile.ExportState()` / `TryImportState()` (`PlayerModel/PlayerProfile.cs:173` and `:216`).

**Part A in the same file (or a small static helper): Dirichlet sampling of the direction.**
- `FrequencyPredictor` is a Dirichlet posterior mean. From the base, its parameters are `a_d = P_d × (profile.FirstEscapeEvidence + 2)`, where 2 is its `routePrior`. So you don't need the counts, only the probabilities `PredictiveCommander` already has.
- Draw `g_d ~ Gamma(a_d)`, normalize, and take the argmax. That's Thompson sampling for direction.
- Because the profile's counts decay (×0.85 per escape), the evidence saturates at about 6.7. Exploration never fully stops, which is right for a player who adapts.

### 2.2 `.claude/tools/logic-tests/BanditTests.cs` (new)

The test class is `partial` across files: add `static void BanditTests()` and call it from `Main` in `LogicTests.cs` (next to `PredictionTests();`, line 32). Use the existing `Check(bool, name, detail)`. Tests worth having:
- the Gamma and Beta samplers match the known mean and variance over about 10k draws;
- against a simulated table of true contact probabilities, the bandit picks the best arm most of the time after N trials;
- **non-stationary:** swap the best arm halfway through; with γ = 0.9 the bandit switches within about 10 trials, and with γ = 1 it's much slower (this is also a figure for your write-up);
- `Update` changes only the updated arm;
- the export → import round trip gives identical state;
- Dirichlet sampling: the mean of the samples ≈ P, and the spread shrinks as evidence grows.

### 2.3 Hooks in `Assets/Scripts/EnemyAI/Commanders/PredictiveCommander.cs` (modify)

Keep V2's behaviour as the default so the A/B is clean. Edits:

1. **Modes and flags.** Add two fields, `directionChoice {Argmax, Thompson}` and `placement {FixedRing, Bandit}`. Parse `-v2Thompson` and `-v2Bandit` in `ApplyCommandLine()` (line 122, next to `-v2React`). Then the existing `MLArena_Eval_V2` scene and eval player run every variant, with no new scenes.
2. **Decide at decision points only.** Today `Replan()` runs every `predictionInterval` (1 s) and on each escape end. With sampling, re-drawing every second makes the ambushers walk back and forth.
   - In the sampling modes, draw at `OnWaveStarted` and in `HandleEscapeEnded` (line 487) only, once per trial.
   - Keep the 1 s timer for the Argmax / FixedRing mode.
   - Keep the `minEvidence` gate (no ambush before 2 escapes) in every mode, as V2 does.
3. **Sites by position, not zone.**
   - `SetSite()` (line 325) derives a zone through `ZoneInSector()` and holds at its center. Add a position (`center + direction(sector) × distance`) to `AmbushSite`.
   - Use `EnemyOrder.HoldAt(position, holdEngageRadius)`.
   - Use the same position in `ChooseSpawnPosition()` (line 193, which currently uses `GetZoneCenter(site.Zone)`).
   - Direction of a sector: angle `sector × 45°`, the same convention as `RadialZoneMap.GetZoneCenter`. The center is `context.Zones.GetZoneCenter(0)`.
4. **Trial bookkeeping** (the reward):
   - In `HandleEscapeStarted` (line 470), snapshot the active sites: sector, distance index, squad. Set each snapshot's `minDistance = ∞`.
   - In `Tick()` (line 223), while `context.Engagement.ActiveEscape` is this escape and more than 1 s has passed since `StartTime`, update each snapshot's `minDistance` to the nearest alive member of its squad. Squad members die and get retasked, so re-read `squad.Members` each tick.
   - In `HandleEscapeEnded`, skip the trial unless `escape.IsValid` and `escape.Displacement.magnitude > 1` (the same filter the profile uses).
   - Then compute the ran-to sector with `RadialZoneMap.DirectionToSector(escape.Displacement, 8)`. For each snapshot in that sector, `bandit.Update(sector, distanceIndex, minDistance <= 2.5f)`. Snapshots in other sectors weren't tested, so don't update them.
   - 2.5 and 1 s match the monitor's `interceptRadius` / `interceptGraceSeconds` (`PlayerBehaviourMonitor.cs:47–48`), so your contact lines up with its `intercepted` field.
   - Also record the pressure squad's min distance in the same way; that gives the "contact by waiting vs chasing" split.
5. **Episode reset.** Implement `IArenaEpisodeListener` (`Arena/ArenaTraining.cs:8`). The arena calls `ResetProfile` and then `OnArenaEpisodeStarting` on the commander (`ArenaEnvironment.cs:186–188`).
   - Direction (part A) resets on its own, because it reads the profile.
   - For the placement bandit, add a switch: reset per episode, or keep it.
6. **Telemetry** via `monitor.LogEvent(...)` (see `LogPrediction`, line 510, for the pattern):
   - a `bandit_update` event: escape id, sector ran, arm, contact, pressure contact, α, β;
   - in the existing `prediction` event, add the drawn direction and the chosen distance.

   Everything lands in `events.jsonl` next to the bot ground truth.

Size estimate: about 150–200 changed or added lines in this file.

### 2.4 Persistence across sessions (for H1 with humans; skip for the arena)

- **New file:** `Assets/Scripts/EnemyAI/AmbushBanditStore.cs`, modeled on `PlayerProfileStore.cs`: the same folder, `<scene>.bandit.json`, and a check of the distance list / arm count on load.
- **When to load and save:** only when `monitor.PersistProfile` is true (arenas set it false). Load in `OnWaveStarted` the first time; save in `OnWaveCompleted`.
- **Settings:** add `v2Thompson` and `v2Bandit` to `EnemyAISettings.cs`. Pass them through `Configure(...)` in `EnemyAIBootstrap.InstallPredictiveCommander` (`EnemyAIBootstrap.cs:42`), so MainGame can run your version. Optionally add a menu toggle next to *Game Commander: V2 (Predictive)* in `Arena/Editor/ArenaAssetBuilder.cs` (around line 724).

### 2.5 Analysis (Python; your own script or notebook, for example `MLTraining/analysis/`)

Data: `~/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry/<batch>_*/`, with `events.jsonl` and `samples.csv`. Header: `t,wave,px,py,vx,vy,hp,zone,state,engagement,escape,near,nearest,retreat,shot_rate,shots,damage,enemies`.
- **Already computed by `.claude/tools/arena-report.py --compare A B`** (reuse; don't rewrite): per persona, "zombie on route", "ambushed" (bot ground truth), ambushed by wave in the episode (learning curve), pressure (damage and deaths per wave), with Wilson intervals (its `wilson()`).
- **New, from your `bandit_update` events:**
  - ambush contact rate per trial;
  - the share of contacts made by ambushers vs chasers;
  - posterior mean per arm over time;
  - the chosen distance over time.
- **New, H2 route entropy:**
  - bots: the entropy of `bot_decision` routes (mode `Flee`, route A/B/C) per episode and per wave;
  - humans: the entropy of `escape_end` direction sectors (from `dx`, `dy`) per session.

  Compare Argmax against Thompson for the Adaptive persona.
- **Heatmap:** a 2D histogram of `px`, `py` from `samples.csv` per persona and commander.

## 3. Experiments

1. Rebuild the eval player after your code changes; it must contain the four `Assets/ML/Arena/MLArena_Eval_*` / `MLArena_Data` scenes, output `MLTraining/builds/eval/MLArena_Eval.app`.
   - There's no menu for this yet (it was built through Unity MCP). A small editor script calling `BuildPipeline.BuildPlayer` with those four scenes (macOS standalone) avoids editing the game's Build Settings.
   - A build re-serializes two assets that aren't yours. Afterwards run `git checkout -- Assets/Settings/UniversalRP.asset "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset"`.
2. Runs, 40 game-minutes each, all on the **same new build**, so the only difference is the flag:
   - `MLTraining/eval.sh MLArena_Eval_V2 40`: plain V2 (control);
   - `... -v2Thompson`: part A only;
   - `... -v2Bandit`: part B only;
   - `... -v2Thompson -v2Bandit`: both.

   Old reference batches: Baseline `20260926_0055`, V2 `20260926_0138`. Use them only as a sanity check that the new build's plain V2 matches.
3. Compare with `python3 .claude/tools/arena-report.py --compare <V2 batch> <variant batch>`, plus your new metrics.
   - **Main hypotheses:** B raises "ambushed" for runners without losing pressure; A raises route entropy and keeps the ambush rate against Adaptive.
   - **Ablations:** γ = 1 against γ = 0.9 (vs Adaptive); per-sector against shared arms.
4. **Humans:**
   - Set MainGame's commander to your version (settings from 2.4), with persistence on.
   - Profiles and bandit files are **per machine and scene**. If testers share a machine, delete the saved files between testers: *Tools > Enemy AI > Delete Saved Player Profiles*, and your bandit file too. Otherwise one person's data trains the next.
   - Several sessions per person. Collect entropy per session plus the plan's questionnaire.

## 4. Reuse map

| Need | Reuse as is | Where |
|---|---|---|
| Player habits (direction per start zone, first escape per engagement, decay, persistence) | `PlayerProfile`, `PlayerProfileStore` | `PlayerModel/PlayerProfile.cs`, `PlayerProfileStore.cs` |
| Escape start/end events, escape data (displacement, route, damage) | `EngagementTracker.EscapeStarted/EscapeEnded`, `EscapeEpisode` | `PlayerModel/EngagementTracker.cs:83–91`, `EngagementTypes.cs:106` |
| Direction prediction (8 sectors) | `IEscapePredictor`, `FrequencyPredictor`, `LearnedPredictor`, `EscapeSectors` | `Prediction/` |
| Zones, sector math | `RadialZoneMap` (`DirectionToSector`, `GetZoneCenter`) | `Zones/RadialZoneMap.cs` |
| Giving zombies orders that stick | `EnemyOrder.HoldAt/MoveTo/Chase`, `Squad` | `Orders/` |
| Commander lifecycle, spawn control, pressure + ambush squads | `PredictiveCommander` (extend it; don't copy it) | `Commanders/PredictiveCommander.cs` |
| Telemetry, custom events | `PlayerBehaviourMonitor.LogEvent(type).Add(...).Write()` | `PlayerBehaviourMonitor.cs:604` |
| Bots incl. Adaptive (route weight ×0.2 when ambushed, 5% recovery per clean escape) | `BotBrain`, `BotPersonaPresets` | `Bots/` |
| Arena with 3 routes (A east, B NW, C SW; refuges at 26), 8 fixed personas, 4 waves per episode | `MLArena_Eval_V2.unity`, `Arena_V2.prefab` | `Assets/ML/Arena/` |
| Headless batch runs | `MLTraining/eval.sh` (extra args pass through to the player) | `MLTraining/` |
| Bot-ground-truth metrics + comparison tables | `arena-report.py` | `.claude/tools/` |
| Pure-logic tests outside Unity | `logic-tests.sh` (auto-compiles `Prediction/*.cs`) | `.claude/tools/` |

**New:**
- `AmbushBandit.cs`;
- `BanditTests.cs`;
- `AmbushBanditStore.cs` (optional);
- your analysis scripts;
- your playtest protocol.

**Modified:** `PredictiveCommander.cs` (hooks), `EnemyAISettings.cs`, `EnemyAIBootstrap.cs`, optionally `ArenaAssetBuilder.cs` (menu).

## 5. Geometry and numbers to know

- **Zones:** Core within 6 units of the base; inner ring 6–20 (centers at 13); outer ring 20+ (centers at 25.5, the refuges in the arena). V2 holds at the outer-ring center.
- **Speeds:** player 5, zombie 001Z 3. A holder engages when the player comes within `holdEngageRadius` 6 (`PredictiveCommander`), then chases at speed 3, so it mostly catches players who run *into* it. That's why distance matters, and it's what the bandit can learn.
- **Gates:** V2 ambushes only after 2 (decayed) escapes of evidence (`minEvidence`; keep it in every mode for a fair comparison) and when the top direction has at least 35% (`minProbability`; argmax mode only, since sampling handles uncertainty itself).
- **Sample sizes:** the Phase 5 eval had about 26–58 waves per persona per commander. Report confidence intervals; `arena-report` already prints Wilson intervals.

## 6. Time plan (about 1.5 weeks plus playtests)

| Days | Work |
|---|---|
| 1–2 | `AmbushBandit` (samplers, update, choose, export/import) + `BanditTests` |
| 3–4 | `PredictiveCommander` hooks (modes, flags, positions, trials, episode reset, events); play-test in `MLArena` in the Editor (overlay: backquote) |
| 5 | Rebuild the eval player; run the 4 variants; first comparison |
| 6–7 | Analysis: contact rate, entropy, learning curves, heatmaps; ablations |
| 8+ | Persistence + MainGame settings; human playtests; write-up and video |
