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
└── workspace/
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
- **No version control:** not a git repo. Take a backup into `.claude/workspace/backups/` before any large or risky edit.
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

Scenes and flow: `Assets/MainMenu.unity` → `Assets/IntroScene.unity` (used as a loading screen by `SceneController`) → `Assets/MainGame.unity`. That is exactly the Build Settings order. Scene names are loaded by string, so if you rename a scene, grep the code and the saved scene fields (`mainMenuSceneName`, `gameSceneName`, `loadingSceneName`, `SceneName`). `Assets/Scenes/SampleScene.unity` is unused and not in the build.

### Known issues (as of 2026-09-24, see `workspace/reports/2026-09-24-repo-scan.md`)

- Note: a bare `using UnityEditor;` in runtime code does **not** break player builds (the player `UnityEngine.CoreModule.dll` defines that namespace). Only actual editor API calls outside `#if UNITY_EDITOR` do. Don't flag the bare `using` as a build breaker.
- `Assets/Resources/` is 162 MB and ships in full. `ZombieWar/` (156 MB) is almost entirely unreferenced. Don't add more to Resources unless the asset is loaded by name in code.
- `Resources.LoadAll<Item>("")` finds nothing, because `Item` assets live in `Assets/Items/`. The `GameManager` / `GameDataManager` prefabs that `SceneController` loads from Resources don't exist.
- **Spawn bug:** `HordeEventSpawner.RandomPointOnRing` returns `(cos, 0, sin)`, the X/Z plane. In this 2D (X/Y) game, enemies spawn on a horizontal line through the player, not in a ring.
- **Log spam:** `HordeEventSpawner.Update` logs every frame while spawning, and `Enemy` logs every hit and state change. That's fine in normal play, but it cripples fast-forwarded (ML training) runs.
- `SceneController.InitializeSceneSpecificSystems` has `case "GameScene":`, but the game scene is `MainGame`, so that branch never runs. It currently does nothing useful anyway (see the missing `GameManager` prefab above).
- `HordeEvent_使用说明.txt` (project root) describes `DayManager` / `HordeSpawner`, which have since been removed or commented out. Horde config now lives on `GameTimer.dayHordeEvents`.
- There are about 21 debug, fixer, tester, and demo scripts in `InventorySystem/`. The fixer scripts checked are not referenced by any scene, so they are likely leftovers.

## Active work

- **Learning enemies (V1 = RL, V2 = TBD) for comparative analysis.** Plan: `workspace/plans/2026-09-24-enemy-ml-v1-rl.md` (draft, awaiting approval; nothing implemented yet). Training files will live in `MLTraining/` at the repo root, outside `Assets/`.

## Working rules for agents

1. **Never touch generated folders:** `Library/` (about 3 GB), `Temp/`, `obj/`, `Logs/`, `.vs/`, `UserSettings/`. Don't read or search them either unless the task specifically needs it (for example, Unity package sources in `Library/PackageCache/`).
2. **`.meta` files:** when moving or renaming anything under `Assets/`, move or rename its `.meta` file with it. Deleting or regenerating a `.meta` file breaks every reference to that asset's GUID. Prefer doing moves inside the Unity Editor.
3. **Scenes and prefabs** (`.unity`, `.prefab`) are YAML. Only hand-edit them for small, well-understood changes. Otherwise describe the Editor steps to the user. The Unity Editor is often open (`Temp/UnityLockfile` exists). If you edit a scene on disk while it's open in the Editor, the Editor may overwrite your change on its next save, so ask the user to close that scene first.
4. **ScriptableObject configs** (`HordeEvent`, `Item`, `CraftingRecipe`, `SurvivalManualData`) are the source of truth for tuning. Change data there rather than hard-coding values.
5. **Can't run the game or compile from here.** Say what you changed and what the user should check in the Editor (Console errors, Play mode).
6. **Reports and plans** go in `.claude/workspace/reports|plans/` with a date prefix, not in chat-only or project-root files.
