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

`HordeEventSpawner` and `GameTimer` sit on the **`DayManager`** GameObject in `MainGame`. The spawner uses the commander in its Inspector field; failing that, a commander component on the same object; failing that, it adds a `BaselineCommander` at runtime.

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

## Active work

- **Learning enemies, for comparative analysis only.** Branch `experiment/enemy-ml`. Plan: `workspace/plans/2026-09-24-enemy-ml-v1-rl.md`.
  - **V1** = RL commander (ML-Agents PPO).
  - **V2** = supervised escape-zone prediction plus scripted squad tactics (option a).
  - **Status:** Phases 0 and 1 done 2026-09-24. Next is Phase 2: zones, player profiler, telemetry.
  - The user says this is **experimental**. They have **another design of their own that they expect to perform better**. Keep the ML work isolated (new files, thin hooks, experiment branch), and don't push it into `main` or into their design.
  - Training files will live in `MLTraining/` at the repo root, outside `Assets/`.

## Tools

- `.claude/tools/compile-check.sh` compiles `Assembly-CSharp` with Unity's bundled Roslyn twice: as the Editor would and as a macOS player build would. It takes about 4 s and works whether Unity is open or closed. Run it after every C# change and report the result. Baseline on 2026-09-24: 0 errors, 7 warnings in each version. It needs Unity to have compiled the project at least once, since it reads `Library/Bee/.../Assembly-CSharp.rsp`.

## Working rules for agents

1. **Never touch generated folders:** `Library/` (about 3 GB), `Temp/`, `obj/`, `Logs/`, `.vs/`, `UserSettings/`. Don't read or search them either unless the task specifically needs it (for example, Unity package sources in `Library/PackageCache/`).
2. **`.meta` files:** when moving or renaming anything under `Assets/`, move or rename its `.meta` file with it. Deleting or regenerating a `.meta` file breaks every reference to that asset's GUID. Prefer doing moves inside the Unity Editor.
3. **Scenes and prefabs** (`.unity`, `.prefab`) are YAML. Only hand-edit them for small, well-understood changes. Otherwise describe the Editor steps to the user. The Unity Editor is often open (`Temp/UnityLockfile` exists). If you edit a scene on disk while it's open in the Editor, the Editor may overwrite your change on its next save, so ask the user to close that scene first.
4. **ScriptableObject configs** (`HordeEvent`, `Item`, `CraftingRecipe`, `SurvivalManualData`) are the source of truth for tuning. Change data there rather than hard-coding values.
5. **You can compile but not play.** Use `.claude/tools/compile-check.sh` to verify compilation. You still can't run Play mode, so say what you changed and what the user should check in the Editor.
6. **New assets need `.meta` files.** When creating a new script or folder under `Assets/`, also create its `.meta` with a fresh GUID (copy the format from a sibling `.meta`), so the commit is complete before Unity next opens.
7. **Reports and plans** go in `.claude/workspace/reports|plans/` with a date prefix, not in chat-only or project-root files.
