# Phase 5 report: V2 predictive commander (escape prediction + scripted ambush)

*2026-09-26, branch `experiment/enemy-ml`, committed locally. Plan: `plans/2026-09-26-enemy-ml-v2-predict.md`. Follows the Phase 4 report (`2026-09-25-phase4-rl-commander.md`).*

## Summary

- **V2 passes the plan's §4 test where V1 failed.** V2 is "learn to predict, rules act": predict where this player will run, and hold an ambush there before they flee.

  | | RunnerA (always A) | RunnerB80 (80% B) |
  |---|---|---|
  | Ambush assigned to the bot's route at flee onset (chance 33%) | **83%** [73–90] | **48%** [41–55]; about 65% once the profile has 2+ escapes |
  | Zombie actually on the chosen route (Baseline / V1 / V2) | 1% / 2–4% / **55%** | 3% / 7–8% / **33%** |
  | Flee ambushed by wave 1 → 4 of the episode (Baseline flat, 26–28%) | 54 → 68 → 54 → 59% | **49 → 73 → 65 → 72%** |

  The first wave of each arena episode is blind, because the arena resets the profile every episode. In MainGame the profile now persists across sessions, so that cost mostly disappears.
- **V1 and V2 are opposite strategies.**
  - V1 (RL) learned pressure: it's far more lethal (Fighter dies in up to 116 of 122 waves) but never anticipates a route.
  - V2 anticipates routes and stays close to Baseline lethality for non-runners: 2 of the 5 active zombies are diverted to the ambush.
- **The "supervised" half of V2 is a qualified result.**
  - The learned predictor is clearly more accurate *at flee onset*: 67% vs 45% top-1 direction on held-out sessions, 74% vs 53% on the unseen RunnerB80.
  - But that accuracy comes from the live threat around the player at that moment, which an ambush placed in advance can't use; online it made the ambush thrash.
  - A history-only learned model doesn't beat the counting model at the question the ambush needs answered ("which way from the base?": RunnerB80 63% vs 78%).
  - So the shipped default is the frequency predictor; the learned one is an option.
- **Also done in this phase:**
  - player-profile persistence across sessions with a switch (your decision on Q3);
  - Phase 4 committed;
  - a MainGame hook for V2 (off by default).

## What was built

| Piece | Where | Notes |
|---|---|---|
| Profile persistence | `EnemyAI/PlayerProfileStore.cs`, `PlayerProfile.ExportState/TryImportState`, `EnemyAISettings.persistPlayerProfile` | One JSON file per scene under `persistentDataPath/EnemyAIProfiles/`. Loaded at start; saved after every wave, on pause and on exit. The switch is on by default. Arenas never persist. Menus: *Persist Player Profile* (toggle), *Open Saved Player Profiles Folder*, *Delete Saved Player Profiles*. Play-tested through MCP. |
| Profile statistics for V2 | `PlayerProfile` | Direction counts per start zone, decayed per start zone. The direction of the **first escape of each engagement**. Both are saved; older saves still load. |
| Predictors (pure, unit-tested) | `EnemyAI/Prediction/` | `EscapeFeatures` (66 values: profile, current zone, threat directions, first-escape directions); `IEscapePredictor` (8 escape directions); `FrequencyPredictor` (counts with Dirichlet back-off); `LearnedPredictor` (softmax regression from JSON, pure C#). |
| Commander | `EnemyAI/Commanders/PredictiveCommander.cs` | The pressure squad behaves like Baseline. The ambush holds the outer zone in the predicted direction, set before the flee: sticky, moves only past a margin, splits for non-adjacent alternatives, spawns at the post only when at least 10 units from the player (off camera). Options: predict from the base or from the player's zone, and a reactive variant. Command line: `-v2Predictor`, `-v2From`, `-v2React`. |
| Telemetry format 3 | `PlayerBehaviourMonitor` | `escape_start.features` holds the exact predictor input, so training and inference match. The commander logs `prediction` (at each flee onset) and `commander_order`. |
| Arena assets | builder, `Arena_V2`, `MLArena_Eval_V2`, `MLArena_Data` | The eval scene uses the same 8 personas and protocol as Baseline and RL. The data scene uses Baseline, the full persona pool and shuffled routes. |
| Training / evaluation | `MLTraining/predictor/` | `train_predictor.py` (NumPy, held-out sessions, `--no-threat`), `evaluate_predictors.py` (per persona and by escapes seen; `--decisions` = route predicted from the base at every bot flee decision). The Python replay of the frequency predictor matches the game's rules; the C# and Python learned models were checked equal to 5 decimals. |
| Report tool | `arena-report.py` | New metric "ambush on route (V2)" from `prediction` events. |
| MainGame hook | `EnemyAISettings.CommanderChoice.Predictive`, `EnemyAIBootstrap`, menu *Game Commander: V2 (Predictive)* | The default stays Baseline. Smoke-tested: installed automatically, and with a seeded "flees east" profile it posted 2 zombies at E2. |

## How it got there

The first V2 run already read the profile: 43% of RunnerB80's hold orders went to B, against 2% for V1. But it only put a zombie on the route at 22% of flee decisions. Each fix below came from the telemetry:

1. **Follow the player → anchor on the base.**
   - Conditioning on the player's current zone made the ambush chase the bot's next hop out of a refuge.
   - Almost all escapes (about 1,100 per RunnerB80 session) are the bot being flushed out of a refuge; only about 30 start at the base.
   - Now the prediction is "where do they run from home", and the ambush is kept when the prediction is momentarily unsure.
2. **End zone → movement direction.**
   - Zones are centred on the base, but the bot starts 4.5 units south of it, so an early-ending flee toward B lands in W1.
   - Measured by direction instead, 67% of RunnerB80's base-start escapes point along route B, against 51% by end zone.
3. **Global decay → per-start-zone decay.** Refuge re-escapes were erasing the few base-start counts within two or three flees.
4. **"Starts at the base" → "first escape of an engagement".**
   - The tracker only notices an escape once the bot is already on its way (NW1), so base-start escapes are rare: 47 of 338 RunnerB80 flee decisions.
   - The first escape of each engagement is the route choice: NW 71% for RunnerB80, E 95% for RunnerA.
   - It is *not* corrupted by V2's own ambush (66% and 79% under V2), unlike later escapes.

   With this, the frequency predictor names the bot's actual route from the base at 78% of RunnerB80 decisions (true rate 80%), 99% for RunnerA and 56% for Mixed.
5. **The learned predictor's online failure.**
   - Trained on escape onsets, it leans on the threat direction at that moment. That's why it can be right on an episode's very first escape, and why its target moved every second online (10 orders per game-minute; the frequency variant issued 2.5).
   - Placing the ambush in advance means not knowing that threat, so the base-anchored ambush now ignores it. The shipped learned model is trained without it.

## Evaluation

The protocol is the same as Phase 4: the headless eval player, 8 arenas with fixed personas, 4 waves per episode, lockstep 0.02 s, 40 game-minutes per arena.

Batches:
- Baseline `20260926_0055`;
- V1 `20260925_0834` (`v1_ppo_01`) and `20260925_1042` (`v1_ppo_02`), both deterministic;
- V2 frequency `20260926_0138`;
- V2 learned (history only) `20260926_0139`.

The RL batches were run on the Phase 4 build; the arena code relevant to the metrics is unchanged.

**Route anticipation.** "On route" = a zombie was in the chosen route's sector when the bot decided to flee (bot outside it). "Predicted" = the most-occupied route sector was the bot's route (chance 33%; small n under Baseline). "Ambushed" = the bot's own check fired on that flee.

| Persona | Metric | Baseline | V1 ppo_01 | V1 ppo_02 | V2 freq | V2 learned |
|---|---|---|---|---|---|---|
| RunnerA | on route | 1% | 4% | 2% | **55%** | 50% |
| RunnerA | predicted | 25% (n 8) | 0% | 6% | **77%** | 55% |
| RunnerA | ambushed | 8% | 53% | 77% | 58% | 61% |
| RunnerB80 | on route | 3% | 8% | 7% | **33%** | 30% |
| RunnerB80 | predicted | 52% (n 21) | 23% | 19% | **52%** (n 87) | 13% |
| RunnerB80 | ambushed | 27% | 70% | 73% | 63% | 54% |
| Mixed | on route | 5% | 21% | 9% | **29%** | 29% |
| Mixed | ambushed | 34% | 78% | 75% | 59% | 55% |
| Adaptive | on route | 5% | 7% | 3% | 30% | **36%** |
| Adaptive | ambushed | 31% | 70% | 79% | 56% | 59% |
| Random | on route | 0% | 0% | 5% | **39%** (n 18) | 5% |

**Pressure** (player damage per wave / deaths):

| Persona | Baseline | V1 ppo_01 | V1 ppo_02 | V2 freq | V2 learned |
|---|---|---|---|---|---|
| RunnerA | 1 / 0 of 26 | 23 / 3 of 27 | 19 / 3 of 28 | 5 / 1 of 26 | 8 / 2 of 26 |
| RunnerB80 | 10 / 3 of 53 | 61 / 40 of 76 | 43 / 24 of 68 | 23 / 11 of 58 | 15 / 6 of 56 |
| Mixed | 60 / 17 of 41 | 96 / 63 of 68 | 92 / 53 of 59 | 47 / 10 of 34 | 57 / 12 of 35 |
| Adaptive | 12 / 2 of 26 | 48 / 12 of 31 | 39 / 11 of 32 | 19 / 3 of 27 | 6 / 0 of 26 |
| Random | 65 / 25 of 49 | 93 / 60 of 65 | 90 / 55 of 63 | 60 / 22 of 43 | 71 / 26 of 47 |
| Kiter | 34 / 11 of 37 | 85 / 44 of 52 | 87 / 45 of 53 | 36 / 10 of 33 | 26 / 7 of 32 |
| Fighter | 63 / 26 of 64 | 88 / 59 of 80 | 98 / 116 of 122 | 62 / 27 of 63 | 59 / 27 of 64 |

Escape interception (a zombie within 2.5 units) under V2 is within noise of Baseline. Kiter 47% → 39% and Random 30% → 23%, both with overlapping intervals: diverting 2 of 5 zombies costs some pressure against players who don't run a fixed route.

**Offline predictor accuracy** (top-1 direction, chance 12.5%):

| | Held-out data sessions: all escapes | Base-start escapes | Eval batch, RunnerB80 (unseen persona) |
|---|---|---|---|
| Frequency | 45.1% | 28.6% | 53% |
| Learned, with threat | 67.3% | 58.2% | 74% |
| Learned, history only | 55.7% | 34.7% | 59% |

At bot flee decisions, predicting from the base (`--decisions`):

| | Frequency | Learned (history) |
|---|---|---|
| RunnerB80 | **78%** | 63% |
| RunnerA | **99%** | 98% |
| Mixed | **56%** | 53% |

## §4 test verdict

- **"A squad at B before flee onset far more often than chance (1/3)": met.**
  - The ambush is assigned to the bot's route at 83% of RunnerA and 48% of RunnerB80 flee decisions. For RunnerB80 that's about 65% once the profile has evidence; whenever an ambush is set, it is on NW about 85% of the time.
  - A zombie is physically on route B at 33% of decisions, against 3% under Baseline and 7–8% under V1.
- **"Interception at B rises over the session": met for RunnerB80 (ambushes), not for strict interception.**
  - Ambushes rise from 49% in wave 1 to 65–73% in waves 2–4, with Baseline flat at about 27%.
  - The strict ≤ 2.5-unit interception stays low for all commanders against runners: they're caught by the ambush reaching them, not by a zombie standing exactly in the path.
- **Not worse than Baseline:** met for pressure on non-runners (damage and deaths are similar or lower). Kiter and Random interception dip slightly, not significantly.

## Notes and caveats

- **The bots choose routes by a weighted roll that ignores context.** On these personas a counting model is near-optimal; context and learning can only matter for humans. The learned predictor's offline advantage at onset suggests threat context does predict *the escape's early direction* (evasion). That's useful for a reactive tactic, not for an ambush placed in advance.
- **Adaptive persona:** V2 ambushes push it to spread its routes (A 42 / B 27 / C 36 vs 74 / 23 / 53 under Baseline, first V2 run), which is the over-commitment effect the plan wanted to see.
- **The ambush can leave its post** to chase (engage radius 6), so "assigned" (48%) is higher than "physically there" (33%) for RunnerB80.
- **Pre-existing, not from this work:**
  - `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` (TMP's default font) is missing from the project and has never been in git, so MainGame logs "LiberationSans SDF Font Asset was not found" (about 117 times in a previous Editor session log).
  - A third unhandled zombie animation event, `OnDeathComplete` on `001Z_Death`, logs a warning on each death.
- **Test artifacts cleaned up:** the saved-profile file seeded during the persistence test, and 4 MainGame telemetry sessions from today's MCP tests, were deleted.

## For you to check or decide

1. **Try V2 yourself in MainGame:** *Tools > Enemy AI > Game Commander: V2 (Predictive)*.
   - With the profile now persisted, it starts ambushing once it has seen you flee a couple of times.
   - Press `` ` `` for the overlay (profile and origin).
   - Does the ambush feel fair (off-camera spawns, 2 zombies)?
2. **Next phase (plan Phase 6, evaluation).** Options:
   - human playtests (the §7 questionnaire), which is where the learned predictor might show its value;
   - more personas that react to threats;
   - an Adaptive-specific counter (randomize between the top two routes).
3. **Still open:** nothing from this list. *(Update 2026-09-26: the user kept `kiteGapSeconds = 1.5`; the weapon-asset bug, the animation events and the missing TMP font were fixed on both branches, see below.)*

## Update 2026-09-26: game fixes (both branches)

At the user's request, three pre-existing bugs were fixed. The fixes are three small commits, cherry-picked to `main`, and `main` compiles with 0 errors.

1. **TMP default font restored.** `LiberationSans SDF.asset` (standard GUID) was restored from TMP 3.0.7's Essential Resources; nothing else from the package was missing. All 52 TextMesh Pro texts in MainGame have their font again.
2. **Weapon asset bug.** MainGame's `WeaponManager` now uses the Player's own `Weapon`. Checked in Play mode:
   - it fires from the scene weapon;
   - the fire point is placed 1 unit from the player;
   - reload consumes one Bullet item (30 rounds);
   - `Weapon.prefab` stays clean.

   **Gameplay change:** reloads are no longer free. The starter kit (3 bullets) was given once per machine; since the follow-up below it comes with every start of MainGame.
3. **Zombie animation events.** Empty receivers for `OnAttackHit` / `OnAttackComplete` / `OnDeathComplete` in `Enemy.cs`, with no behaviour change. No "no receiver" messages in about 500 game-seconds of arena play.

Noticed, not changed: the Bullet item's display name is "New Item" (`Assets/Items/Bullet.asset`, `itemName`).

Follow-ups the same day (both branches): the Bullet item is named "Bullet"; the spawn-ring fix and the `VERBOSE_LOGS` log gating were cherry-picked to `main`; and, at the user's decision, the starter kit is given at every start of MainGame (`StarterItemGiver.giveItemsOnlyOnce` off; checked in Play mode: 3 bullets with the old PlayerPrefs flag still set). The user also kept `kiteGapSeconds = 1.5`.

