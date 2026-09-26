# Waste Land 2039 — Claude project guide

Claude Code loads this file automatically at the start of every session in this project.

## Rule #1: all agent work lives in `.claude/`

Everything Claude (or any AI agent) produces that is *not* a change to the game itself goes in this folder, never in the project tree:

```
.claude/
├── IDEAS.md            this file: project briefing + rules for agents
├── settings.json        (create when needed) shared Claude Code settings / permissions
├── settings.local.json  (create when needed) personal overrides
├── agents/              (create when needed) custom subagents
├── commands/            (create when needed) custom slash commands
├── skills/              (create when needed) project skills
├── tools/               helper scripts for agents (see below)
└── workspace/           (scratch/ and backups/ are git-ignored)
    ├── reports/         scans, reviews, investigations, write-ups  → YYYY-MM-DD-<topic>.md
    ├── plans/           implementation plans, task breakdowns       → YYYY-MM-DD-<topic>.md
    ├── backups/         copies of files taken before risky edits    → YYYY-MM-DD-<topic>/
    └── scratch/         throwaway files, test scripts, temp output (safe to empty any time)
```

Why this matters in a Unity project: anything written under `Assets/` gets imported by Unity, gets a `.meta` file, and any `.cs` file there **compiles into the game**. For example, `Assets/Scripts/WeaponSystem/Backup_HordeSpawner_20250920_121310/` is a backup folder that is still being compiled. Never create backups, notes, docs, or test scripts under `Assets/`. Put them in `.claude/workspace/`.

Real game changes (scripts, prefabs, scenes, ScriptableObjects) still go in their normal place under `Assets/`.

## Project facts

- **Engine:** Unity **2022.3.62f1** (LTS), **URP 2D** (`com.unity.render-pipelines.universal` 14.0.12, `com.unity.feature.2d`).
- **Genre:** 2D top-down zombie-survival / base defense. The player survives up to 30 days; each day can trigger a configured horde.
- **Input:** legacy Input Manager (`Input.GetKeyDown`, etc.). The new Input System package is **not** installed.
- **UI:** a mix of `UnityEngine.UI.Text` and TextMeshPro.
- **Language:** code comments, `Debug.Log` messages, and design docs are in **Chinese**. Match that style when editing.
- **Git** (since 2026-09-24, local only, no remote):
  - `main` is the game as of 2026-09-24. The user's own work belongs there.
  - `experiment/enemy-ml` holds the ML enemy experiment (see Active work). Keep experiment commits off `main`.
  - Commit only when the user asks. Keep commits small and single-purpose so fixes can be cherry-picked to `main`.
- **Logging:** high-frequency info logs use `VerboseLog.Log(...)` (in `Assets/Scripts/VerboseLog.cs`). It is compiled out unless the `VERBOSE_LOGS` scripting define is set. Use `Debug.LogWarning` / `Debug.LogError` only for real problems. *(Currently on `experiment/enemy-ml` only.)*
- **IDE:** Visual Studio / VS Code (`Waste Land 2039.sln`). Rider is also enabled in packages.

### Code map (`Assets/`)

| Area | Location | Key types |
|---|---|---|
| Day / horde flow | `Assets/GameTimer.cs`, `Assets/HordeEventSpawner.cs`, `Assets/Scripts/HordeEvent.cs` | `GameTimer` (holds `DayNum`, `MaxDays`, `dayHordeEvents`), `HordeEvent` SO (menu: *Game/Horde Event*) |
| Game / scene flow | `Assets/Scripts/` | `GameManager`, `SceneController`, `PauseManager`, `GameDataManager`, `PrerequisiteManager` |
| Player | `Assets/Scripts/PlayerController/` | `PlayerController` (implements `IDamageable`), `CameraController` |
| Combat / enemies | `Assets/Scripts/WeaponSystem/` | `Enemy` (~1.5k lines), `EnemySpawner`, `RangedWeapon`, `WeaponManager`, `Bullet` (defines `IDamageable`), `MainBase`, `PlayerHealthRegeneration` |
| Inventory / crafting | `Assets/Scripts/InventorySystem/` (+ `Crafting/`, `Editor/`) | `Item` SO, `Inventory`, `InventoryManager`, `InventoryUI`, `CraftingRecipe` SO, `CraftingManager`, `CraftingUI` |
| UI | `Assets/Scripts/UI/` | `MainMenuUI`, `PauseMenuUI`, `SurvivalManualUI`, enemy health bars |
| Intro / tutorial / dialogue | `Assets/Scripts/IntroSequence/`, `TutorialGuideManager`, `Assets/DialogueSystem.cs` | `IntroController`, `GifPlayer`, `DialogueSystem` |
| Data assets | `Assets/Items/`, `Assets/Resources/CraftingRecipes/`, `Assets/Dialogue/`, `Assets/Day 2.asset`, `Assets/Day 3.asset` | ScriptableObject instances |
| Enemy AI experiment *(branch `experiment/enemy-ml`)* | `Assets/Scripts/EnemyAI/Orders/`, `Assets/Scripts/EnemyAI/Commanders/` | `EnemyOrder` (MoveTo / HoldAt / Chase), `Enemy.Orders.cs` (partial `Enemy`), `Squad`, `IHordeCommander`, `HordeContext`, `BaselineCommander`, `DebugOrdersCommander` |
| Player model + telemetry *(same branch)* | `Assets/Scripts/EnemyAI/Zones/`, `PlayerModel/`, `Telemetry/`, and `EnemyAI/*.cs` | Pure logic (unit-tested outside Unity): `RadialZoneMap`, `EngagementTracker`, `PlayerProfile`, `TelemetryWriter`. Unity side: `PlayerBehaviourMonitor`, `PlayerProfileStore` (cross-session save), `EnemyAIBootstrap`, `Enemy.Registry.cs` |
| RL commander *(same branch)* | `Assets/Scripts/EnemyAI/Learning/` (pure), `EnemyAI/Commanders/RLCommander.cs`, `EnemyAI/EnemyAISettings.cs`, `MLTraining/` | Pure: `CommanderActions` (3 squads × {keep, autonomous, chase, hold zone k} + reinforcement squad), `CommanderRewardTracker`. Unity: `RLCommander` (ML-Agents `Agent` + `IHordeCommander` + `IArenaEpisodeListener`; 111 observations; action schemes PerSquad [20, 20, 20, 3] every 1 s or RetaskOne [4, 19, 3] every 2 s, detected from the model; logs `commander_order` events) |
| V2 predictive commander *(same branch)* | `Assets/Scripts/EnemyAI/Prediction/` (pure), `EnemyAI/Commanders/PredictiveCommander.cs`, `Assets/ML/Predictors/`, `MLTraining/predictor/` | Pure: `EscapeFeatures` (66 values), `IEscapePredictor` (8 escape directions), `FrequencyPredictor`, `LearnedPredictor` (softmax regression from JSON). Unity: `PredictiveCommander` (pressure squad + ambush on the predicted direction, placed before the flee; logs `prediction` / `commander_order`) |
| Bot players + ML arena *(same branch)* | `Assets/Scripts/EnemyAI/Bots/` (pure), `EnemyAI/Arena/` (Unity), `Assets/ML/` (assets), `PlayerController/IPlayerInput.cs` | Pure: `BotBrain`, `BotPersonaSpec` / `BotParams`, `BotPersonaPresets`. Unity: `PlayerBot` (drives the player through `IPlayerInput`), `BotPersona` SO, `ArenaEnvironment`, `ArenaManager`, `ArenaBase`, `ArenaRoute`, `Enemy.Targets.cs`; editor builder `Arena/Editor/ArenaAssetBuilder.cs` |

`HordeEventSpawner` and `GameTimer` sit on the **`DayManager`** GameObject in `MainGame`. The spawner uses the commander in its Inspector field; failing that, a commander component on the same object; failing that, it adds a `BaselineCommander` at runtime.

**Player model at runtime** (experiment branch):
- `EnemyAIBootstrap` adds a `PlayerBehaviourMonitor` to every `HordeEventSpawner` on scene load, unless a monitor was placed by hand.
- The monitor samples at 10 Hz of game time, keeps the `PlayerProfile` up to date, and exposes it to commanders through `HordeContext.Zones` / `.Profile` / `.Engagement`.
- Telemetry is written to `~/Library/Application Support/<company>/<product>/EnemyAITelemetry/<yyyyMMdd_HHmmss>_<scene>/`. It contains `session.json`, `samples.csv`, `events.jsonl` and `profile_final.json`. Menu: *Tools > Enemy AI > Open Telemetry Folder*.
- **Telemetry format 2** (2026-09-25):
  - `escape_end` writes the escape type as `escape_type`. Format 1 wrote a second `"type"` key, which JSON readers resolve to the escape type and so hid the event; `arena-report.py` reads both formats.
  - `escape_start` adds `enemy_zones` (for pre-positioning), and `escape_end` adds `min_enemy_distance` and `intercepted`. These metrics don't depend on the commander.
- **Telemetry format 3** (2026-09-26): `escape_start` adds `features`, the exact `EscapeFeatures` vector the V2 predictors see (58 values in the first format-3 batches, 66 since; `MLTraining/predictor` replays the last 8).
- **The profile persists across sessions** (decided 2026-09-26; plan Q3):
  - It's saved as JSON per scene, `~/Library/Application Support/<company>/<product>/EnemyAIProfiles/<scene>.json`, by `PlayerProfileStore`.
  - The monitor loads it at start and saves after every wave, on pause and on exit. The file records the zone names; if the zone setup changed, the old file is ignored and overwritten.
  - The switch is `EnemyAISettings.persistPlayerProfile` (default on; on when the asset is missing). Menus: *Tools > Enemy AI > Persist Player Profile* (toggle), *Open Saved Player Profiles Folder*, *Delete Saved Player Profiles*.
  - Arenas never persist (`ArenaEnvironment` sets `monitor.PersistProfile = false`).
  - The overlay and `session.json` (`profile_persistent` / `profile_session` / `profile_origin`) show whether the profile came from an earlier session.
  - When play-testing, don't leave synthetic data in the saved file: it biases the user's next session.
- The in-game overlay toggles with the backquote key (`). Zones: `Core` plus `E1…SE1` (6–20 units from the base) plus `E2…SE2` (20+ units).

**ML arena** (experiment branch, `Assets/ML/Arena/MLArena.unity`, not in Build Settings):
- `ArenaManager` instantiates 8 copies of `Arena.prefab`, 150 units apart, and assigns personas round-robin (Fighter, RunnerA, Mixed, Adaptive, Kiter, Random). Each arena has a base (`ArenaBase`, the same sprite and collider as MainGame's base, no game over), routes A = east, B = north-west and C = south-west to refuges 26 units out, a bot-driven copy of the player, and a `Director` (spawner + `BaselineCommander` + monitor).
- Each arena loops episodes: sample persona parameters, reset the profile, then 3 waves of `ArenaWave` (Day 3 values, no-drop zombie variant). A wave ends when cleared, after 90 s, or when the player dies. Bot decisions are logged as `bot_decision` events, the ground truth for evaluating the profiler.
- Keys: `[` / `]` switch arena, `O` overview, `` ` `` overlay, `H` human takes over the focused arena, `-` / `=` time scale.
- Isolation hooks, all off by default so MainGame is unchanged: `HordeEventSpawner.bindEnemiesToSpawner` / `mainBaseTransform` / `AbortHordeEvent()`, `Enemy.AssignTargets`, `PlayerBehaviourMonitor.trackSpawnerEnemiesOnly` / `mainBaseOverride`, and `PlayerController` / `WeaponManager.InputOverride`.
- Assets are generated by *Tools > Enemy AI > Build ML Arena*, which creates only what is missing. *Rebuild Arena Player From MainGame* re-copies the player after you change it in MainGame. Persona defaults live in `BotPersonaPresets` (the tests use the same values); after creation, the `.asset` files are the source of truth.

Scenes and flow: `Assets/MainMenu.unity` → `Assets/IntroScene.unity` (used as a loading screen by `SceneController`) → `Assets/MainGame.unity`. That is exactly the Build Settings order. Scene names are loaded by string, so if you rename a scene, grep the code and the saved scene fields (`mainMenuSceneName`, `gameSceneName`, `loadingSceneName`, `SceneName`). `Assets/Scenes/SampleScene.unity` is unused and not in the build.

### Known issues (as of 2026-09-24, see `workspace/reports/2026-09-24-repo-scan.md`)

- Note: a bare `using UnityEditor;` in runtime code does **not** break player builds (the player `UnityEngine.CoreModule.dll` defines that namespace). Only actual editor API calls outside `#if UNITY_EDITOR` do. Don't flag the bare `using` as a build breaker.
- `Assets/Resources/` is 162 MB and ships in full. `ZombieWar/` (156 MB) is almost entirely unreferenced. Don't add more to Resources unless the asset is loaded by name in code.
- `Resources.LoadAll<Item>("")` finds nothing, because `Item` assets live in `Assets/Items/`. The `GameManager` / `GameDataManager` prefabs that `SceneController` loads from Resources don't exist.
- **Spawn bug:** `HordeEventSpawner.RandomPointOnRing` used `(cos, 0, sin)`, the X/Z plane, so enemies spawned on a horizontal line through the player. **Fixed on `experiment/enemy-ml` only** (commit "Fix horde spawn ring…"); cherry-pick it to `main` if wanted.
- **Log spam:** fixed on `experiment/enemy-ml` via `VerboseLog`. Still present on `main`.
- **The baseline enemy AI is simpler than its code suggests.** Two features never take effect, because `Enemy` overwrites `currentTarget` every frame (`DetectMainBase` / `DetectPlayer`, now also `UpdateOrderTarget`):
  - **Swarm alerts:** `JoinSwarmChase` sets the target to the player once, and it's replaced by the base on the next frame.
  - **Stuck escape and obstacle avoidance:** the random nudge and the avoidance waypoint are overwritten the same way.

  So in practice a zombie chases the player within 5 units, and otherwise walks in a straight line to the base. Describe the baseline this way in the comparison, and don't "fix" it on the experiment branch without the user's say-so, because it would change the control group.
- `SceneController.InitializeSceneSpecificSystems` has `case "GameScene":`, but the game scene is `MainGame`, so that branch never runs. It currently does nothing useful anyway (see the missing `GameManager` prefab above).
- `HordeEvent_使用说明.txt` (project root) describes `DayManager` / `HordeSpawner`, which have since been removed or commented out. Horde config now lives on `GameTimer.dayHordeEvents`.
- There are about 21 debug, fixer, tester, and demo scripts in `InventorySystem/`. The fixer scripts checked are not referenced by any scene, so they are likely leftovers.
- **Fixed 2026-09-26 on both `main` and `experiment/enemy-ml`** (same three commits, cherry-picked):
  - **Weapon asset bug.** MainGame's `WeaponManager.availableWeapons[0]` referenced the prefab asset `Assets/Prefabs/Weapon.prefab`, not the Player's own child `Weapon`, so the game fired from and moved the asset and reloads were free.
    - It now references the child. Reloading takes one `Bullet` item (30 rounds) from the inventory. Starter kit: 3 bullets, given **once per machine** (PlayerPrefs `StarterItemsGiven`); in Play mode, the `StarterItemGiver` component's context menu *强制发放物资* gives them again. Bullets can be crafted.
    - Playing MainGame no longer dirties `Weapon.prefab`. The asset keeps a stray `Firepoint` offset, which is harmless because `WeaponManager` places the fire point every frame. Still, in editor code, save only your own assets (`AssetDatabase.SaveAssetIfDirty`).
  - **Zombie animation events.** `Enemy.cs` has empty receivers for `OnAttackHit`, `OnAttackComplete` and `OnDeathComplete` (001Z clips); behaviour is unchanged. This replaces the experiment-only `EnemyAI/Enemy.AnimationEvents.cs`.
  - **TMP default font.** `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` was missing (never committed), so 52 MainGame texts had no font. It's restored from TMP 3.0.7 Essential Resources with its standard GUID.
    - Lesson: **saving a scene while an asset it references is missing rewrites those references to null.** Check `git diff` after any scene save.
  - **Saving `MainGame` on the experiment branch** also writes experiment-only serialized fields (for example `HordeEventSpawner.mainBaseTransform`, `endlessWaves`, `commanderComponent`). Strip them before committing a scene change meant for `main`.
  - Still open, for the user: `Assets/Items/Bullet.asset` has `itemName: New Item` (it shows as "New Item").

## Active work

- **Learning enemies, for comparative analysis only.** Branch `experiment/enemy-ml`. Plan: `workspace/plans/2026-09-24-enemy-ml-v1-rl.md`.
  - **V1** = RL commander (ML-Agents PPO).
  - **V2** = supervised escape prediction plus scripted squad tactics (option a). Plan: `workspace/plans/2026-09-26-enemy-ml-v2-predict.md`.
  - **Status:** Phases 0–1 done 2026-09-24; Phases 2–3 done 2026-09-25 (report `workspace/reports/2026-09-25-phase3-arena-bots.md`). **Phase 4 partly met** (2026-09-25; report `workspace/reports/2026-09-25-phase4-rl-commander.md`):
    - `v1_ppo_01` (PerSquad) and `v1_ppo_02` (RetaskOne + `head_on_bonus`), 2 M decisions each;
    - both are far more lethal than Baseline, but **neither anticipates routes** (§4 test not met);
    - `v1_ppo_02` is installed in `Arena_RL` and `EnemyAISettings`, and MainGame is still on Baseline;
    - interim checkpoints are in `MLTraining/results/checkpoints/` (git-ignored);
    - **decision 2026-09-26:** V1 closed as is; next is V2 (Phase 5).
  - **Phase 5 (V2) done 2026-09-26** (report `workspace/reports/2026-09-26-phase5-v2-predictive.md`, committed locally):
    - `PredictiveCommander` with `FrequencyPredictor` passes the §4 test. The ambush is on the bot's route at 83% of RunnerA / 48% of RunnerB80 flee decisions (chance 33%), and RunnerB80's ambush rate rises from wave 1 to 4. It stays near Baseline lethality for non-runners.
    - The learned predictor is more accurate at flee onset (it uses the live threat), but not for an ambush placed in advance; it ships as an option.
    - MainGame is still on Baseline (menu *Game Commander: V2 (Predictive)*). Next: plan Phase 6 (evaluation, human playtests), the user's call.
  - **Pending the user's review:** the tracker's `kiteGapSeconds = 1.5` (it changes the Phase 2 Kite definition; 0 reverts it). The weapon-asset bug, the animation events and the missing TMP font were fixed on both branches on 2026-09-26, at the user's request.
  - **Open question 3, decided 2026-09-26:** persist the player profile across sessions, behind a switch. Implemented and play-tested the same day (see *Player model at runtime*).
  - The user says this is **experimental**. They have **another design of their own that they expect to perform better**. Keep the ML work isolated (new files, thin hooks, experiment branch), and don't push it into `main` or into their design.
  - Training files will live in `MLTraining/` at the repo root, outside `Assets/`.

## Tools

- `.claude/tools/compile-check.sh` compiles `Assembly-CSharp` with Unity's bundled Roslyn twice: as the Editor would and as a macOS player build would. It takes about 4 s and works whether Unity is open or closed. Run it after every C# change and report the result. Baseline on 2026-09-24: 0 errors, 7 warnings in each version. It needs Unity to have compiled the project at least once, since it reads `Library/Bee/.../Assembly-CSharp.rsp`.

- `.claude/tools/logic-tests.sh` builds the pure EnemyAI logic (zones, player model, telemetry writer, bots) together with `.claude/tools/logic-tests/*.cs`, and runs it outside Unity with Unity's .NET 6 runtime. It takes about 5 s. Run it after touching `EnemyAI/Zones`, `PlayerModel`, `Telemetry` or `Bots`, and extend it when you add logic. Only classes that don't use Unity engine internals (`Time`, `Debug`, scene objects) can be tested this way, so keep new decision logic in that style. The tests cover:
  - synthetic fighter, runner, kiter and adaptation players, plus a file-format check under a comma-decimal locale (`LogicTests.cs`);
  - profile save / restore: a JSON round trip, continued learning after a restore, and rejection of a mismatched zone map (`ProfilePersistenceTests.cs`);
  - V2 predictors: feature layout, direction prediction, start-zone memory, first escape per engagement, and the learned model's maths (`PredictionTests.cs`);
  - every bot persona driving a simulated arena through the real tracker and profile (`BotTests.cs`). It uses the same geometry and rules as `MLArena`, and prints a breakdown of tracker label by bot mode.

- `.claude/tools/arena-report.py` summarizes the latest `MLArena` telemetry batch, or given session folders; add `--markdown` for tables. Per arena it reports persona, wave outcomes, bot decisions and routes (ground truth), tracker label shares, per-episode profile, and **label recall** (the share of tracker samples in the 3 s after each bot decision that carry the decided label). `--compare` adds commander metrics per persona:
  - escape interception;
  - per-flee-decision metrics (zombie on the chosen route, bot ambushed, ambush by wave in the episode, top-1 route prediction against 1/3 chance);
  - hold orders by route sector;
  - for V2, "ambush on route" (from `prediction` events).

  **The escape-level "pre-positioned" metric mostly measures chasing** (most escapes start at a refuge with chasers around). Use the per-decision metrics for the plan's §4 test.

- **Unity MCP** (MCP for Unity by CoplayDev, **v10.2.0**, experiment branch only; installed and verified end to end on 2026-09-25: 47 tools, bridge on `127.0.0.1:6400`). Tools appear as `mcp__UnityMCP__*` in sessions started after setup. Unity must be open for them to work.
  - **Package:** `com.coplaydev.unity-mcp` is pinned in `Packages/manifest.json`. Don't bump it casually: a version change makes the plugin rewrite the Claude registration, and the rewrite drops the telemetry opt-out.
  - **Claude Code registration:** local scope (stored in `~/.claude.json`, not in the repo), **stdio** transport, command `uvx --from mcpforunityserver==10.2.0 mcp-for-unity --transport stdio`, env `UNITY_MCP_DISABLE_TELEMETRY=true`. `.claude/settings.json` sets the same variable as a fallback.
  - **Telemetry:** the server sends usage telemetry to `api-prod.coplay.dev` **by default**. Keep the opt-out in place.
  - **Unity side:** *Window > MCP for Unity* must stay on **Transport: stdio**. If you switch to HTTP, the plugin re-registers Claude Code over HTTP without the opt-out. The stdio bridge auto-starts when the Editor loads; after changing transport, press **Start Session**.
  - **Don't press *Install Skills*.** It writes a global skill to `~/.claude/skills/`.
  - **Claude Desktop:** the plugin's first-run wizard also added a `unityMCP` entry to Claude Desktop's `claude_desktop_config.json`, with no telemetry opt-out. See the Active work notes for what the user decided.

- **ML-Agents toolchain** (Phase 4, experiment branch):
  - **Unity side:** `com.unity.ml-agents` **3.0.0-exp.1** (Release 21, the last release that supports 2022.3), which pulls Sentis 1.2.0-exp.2 and Burst 1.8.21.
  - **Python side:** conda env `mlagents` at `/opt/miniconda3/envs/mlagents` (Python 3.10.12, mlagents 1.0.0, torch 2.1.2, about 700 MB). Recreate it with `MLTraining/setup_env.sh`. The script records the Apple Silicon workarounds:
    - numpy 1.21.6 instead of the unbuildable 1.21.2;
    - grpcio 1.48.0 from conda-forge;
    - onnx 1.13.1 + protobuf 3.20.3 (torch's exporter needs onnx; verified with a gRPC message round-trip);
    - `setuptools<70` for `pkg_resources`.

    `pip check` complains about the declared pins; that's expected.
  - **Training:** `MLTraining/train.sh RUN_ID [NUM_ENVS]` runs the headless `MLTraining/builds/MLArena_RL.app` (build only the `MLArena_RL` scene; the MCP `manage_build` action with `scenes` leaves Build Settings alone). Config: `MLTraining/config/commander_ppo.yaml`, or `CONFIG=...` (for example `commander_ppo_headon.yaml`, which sets the `head_on_bonus` environment parameter that `RLCommander` reads each episode). Use `ARENA_BUILD=...` for another player (for example `builds/retask/MLArena_RL` for RetaskOne). The action scheme is baked into the build: `ArenaAssetBuilder.ConfigureRLArena(scheme, null)`, then build. Output goes to `MLTraining/results/RUN_ID/` (git-ignored, like `builds/`). Don't rebuild the player while a run is using it.
  - **Timing:** ML-Agents fixes the capture frame rate, so game time per frame = time scale / capture rate. Bots, zombie targeting and firing run in `Update`, so `train.sh` uses time scale 20 with capture rate 1000, giving 0.02 s per frame. The default 60 would give 0.33 s per frame. Check the `[ArenaManager] ... fps, game time per frame` lines in `results/RUN_ID/run_logs/Player-*.log`.
  - **Player builds have side effects.** The MCP `manage_build` tool saves all dirty assets first. URP re-serializes `Assets/Settings/UniversalRP.asset` (adds default fields), and TMP clears the glyph table of `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset`. After building, check `git status` and `git checkout --` those two files (they are not our changes). Editor-mode ML-Agents runs write `Assets/ML-Agents/Timers/*.json` (git-ignored).
  - **Evaluation:** `MLTraining/eval.sh MLArena_Eval_Baseline|MLArena_Eval_RL|MLArena_Eval_V2|MLArena_Data [GAME_MINUTES] [args]` runs the headless eval player (build all four scenes into it; V2 options `-v2Predictor Frequency|Learned`, `-v2From Base|PlayerZone`, `-v2React`) (`MLTraining/builds/eval/`; rebuild it after installing a model). It uses lockstep 0.02 s per frame, about 25–37× real time, and quits after N game-minutes. Compare with `python3 .claude/tools/arena-report.py --compare <baseline batch> <RL batch>`.
    - Add `-stochastic` after the minutes to **sample** actions instead of taking the most likely one. Use it for policies that are still far from deterministic, where argmax degenerates: the `v1_ppo_02` 400k checkpoint re-tasked only squad 2, between two zones.
    - ML-Agents caches one inference runner per model (`Academy.GetOrCreateModelRunner` ignores the deterministic flag). So the flag must be set before the first agent with that model initializes; changing `BehaviorParameters.DeterministicInference` later has no effect. `ArenaManager` sets it on the prefab before instantiating.
  - **Throughput:** 3 envs give about 400 decisions/s (Editor: about 110 at a 60 fps cap). The machine runs near its 8 GB RAM limit with the Editor open.
  - **Models:** *Tools > Enemy AI > Install Latest Commander Model* copies the newest `results/*/HordeCommander.onnx` to `Assets/ML/Models/` and assigns it to `Arena_RL` (deterministic inference) and to `Assets/ML/Resources/EnemyAISettings.asset`.
  - **Using RL in MainGame:** *Tools > Enemy AI > Game Commander: Baseline / RL* switches which commander normal scenes use. `EnemyAIBootstrap` adds an inference-only `RLCommander` next to spawners without a commander; there are no scene edits and the default is Baseline.

- **V2 predictor** (`MLTraining/predictor/`, conda env `mlagents` or any NumPy):
  - Data comes from `eval.sh MLArena_Data 240` (Baseline, shuffled routes, events only).
  - Train: `train_predictor.py --data <batch> [--no-threat] --out Assets/ML/Predictors/X.json`. The shipped `EscapePredictor_v1.json` is history-only (`--no-threat`).
  - Evaluate: `evaluate_predictors.py --batch <batch> [--weights X.json] [--decisions]`. `--decisions` measures route prediction from the base at bot flee decisions, which is what the ambush needs.
  - The Python replay of the frequency predictor mirrors `PlayerProfile` / `FrequencyPredictor`; change both together.
- `.claude/tools/unity-mcp-call.py` is a minimal stdio MCP client. It launches the UnityMCP server exactly as Claude Code does (telemetry off) and lists or calls tools, for example `python3 .claude/tools/unity-mcp-call.py call read_console '{"action":"get","types":["error"]}'`. Use it when the `mcp__UnityMCP__*` tools aren't loaded in the current session. Unity must be open with an active stdio session.

## Working rules for agents

1. **Never touch generated folders:** `Library/` (about 3 GB), `Temp/`, `obj/`, `Logs/`, `.vs/`, `UserSettings/`. Don't read or search them either unless the task specifically needs it (for example, Unity package sources in `Library/PackageCache/`).
2. **`.meta` files:** when moving or renaming anything under `Assets/`, move or rename its `.meta` file with it. Deleting or regenerating a `.meta` file breaks every reference to that asset's GUID. Prefer doing moves inside the Unity Editor.
3. **Scenes and prefabs** (`.unity`, `.prefab`) are YAML. Only hand-edit them for small, well-understood changes. Otherwise describe the Editor steps to the user. The Unity Editor is often open (`Temp/UnityLockfile` exists). If you edit a scene on disk while it's open in the Editor, the Editor may overwrite your change on its next save, so ask the user to close that scene first.
4. **ScriptableObject configs** (`HordeEvent`, `Item`, `CraftingRecipe`, `SurvivalManualData`) are the source of truth for tuning. Change data there rather than hard-coding values.
5. **Compile first, then play-test through Unity MCP when the Editor is open.** Always run `.claude/tools/compile-check.sh`. With the MCP bridge connected you can enter and exit Play mode (`manage_editor`), inspect live state (`execute_code`, which compiles as C# 6: no local functions, write `UnityEngine.Object`), read the console, and take screenshots. Notes:
   - When Unity isn't the focused app, Play mode is frozen unless `Application.runInBackground` is true. `ArenaManager` sets it; elsewhere, set it at runtime. It does not change Player Settings.
   - Check the active scene isn't dirty before switching scenes.
   - What you can't do is give real keyboard or mouse input. For anything that needs a human (feel, UI), say what the user should check.
6. **New assets need `.meta` files.** When creating a new script or folder under `Assets/`, also create its `.meta` with a fresh GUID (copy the format from a sibling `.meta`), so the commit is complete before Unity next opens.
7. **Reports and plans** go in `.claude/workspace/reports|plans/` with a date prefix, not in chat-only or project-root files.
