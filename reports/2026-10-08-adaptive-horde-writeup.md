# Adaptive horde: write-up of the whole experiment (runs 1–4)

2026-10-08. Branch `adaptive-horde` (from `experiment/enemy-ml` at `57181f3b`), local only, not pushed. This file
collects everything needed to write the README or portfolio entry: the question, what was built, how it was
measured, every result, what was learned, the limits, how to reproduce it, and who did what. The per-run reports
it summarizes are in this folder (`2026-10-06-adaptive-eval-1.md` … `2026-10-08-adaptive-eval-4.md`). Every table of
every run is in `MLTraining/results/adaptive_eval/<batch>/report.md`.

## 1. Summary

**Question.** The game's V2 horde already predicts where a player will flee and waits there (Phase 5). Can it do
better by *adapting*?
- Layer 1, part A: explore the ambush direction by Thompson sampling instead of always taking the most likely one.
- Layer 1, part B: learn *where along the route* to wait from whether the ambushers actually reach the player (a
  contact-rewarded bandit).
- Layer 2: learn *how the player reacts* to being ambushed (do they avoid a route after meeting zombies there?) and
  predict around it.

**Answer, after 4 controlled evaluation runs** (65 runs of 40 game-minutes, each with 8 bot arenas in parallel,
about 350 hours of simulated play): **no**. Plain V2 is still the best commander. None of the adaptive parts raises
the share of flees that end in a real ambush by a waiting zombie beyond run-to-run noise, for any bot persona; some
lower it.

| Share of flees ambushed by a waiting zombie | V2 | best adaptive variant | worst |
|---|---|---|---|
| RunnerA (always route A), run 3 | 42% | Bandit 40% | Bandit-joint 32% |
| RunnerB80 (80% route B), run 3 | 27% | Bandit-tail0 / -every / -joint 28% | Thompson 22% |
| Adaptive (avoids where it was ambushed), run 3 | 22% | Bandit 23% | Thompson 12% |
| Adaptive, run 4 (6 repeats) | 23% | Reaction-p0.8 23% | Reaction 21% |

**What the experiment did produce:**
- a measurement chain that could tell real effects from artifacts (a ground truth for "ambushed by a waiting zombie
  vs by a chaser", repeats, ablations, a control in every run);
- three findings that only that chain revealed:
  1. the first reward signal never fired;
  2. an apparent "learned" preference was an artifact of the measurement window;
  3. what the commander can observe is not what the player reacts to.

## 2. Background

**The game.** *Waste Land 2039* is a 2D top-down zombie-survival / base-defense game in Unity 2022.3 (URP 2D). The
enemy-AI experiment lives on branch `experiment/enemy-ml`:
- Phase 4: V1, a PPO reinforcement-learning commander. It was far more lethal, but never anticipated a route.
- Phase 5: V2, "learn to predict, rules act" (`reports/2026-09-26-phase5-v2-predictive.md`).

**V2 (the baseline here).**
- A player profile counts the direction of the first escape of each engagement, with decay.
- `FrequencyPredictor` turns it into P(direction) over 8 sectors.
- `PredictiveCommander` keeps 2 of the zombies holding an ambush in the most likely direction, at the refuge, 25.5
  from the base. The rest chase as usual. It can split between two directions.
- In Phase 5 the ambush was on RunnerA's route at 83% of its flee decisions (chance 33%).

**The arena.**
- A base and three routes: A east, B north-west, C south-west, with refuges 26 units out.
- Player (bot) speed 5, zombie speed 3. A holding zombie engages within 6 units.
- 8 arenas run side by side, each with a bot persona: Fighter, RunnerA (×2), Mixed, Adaptive, Kiter, Random, RunnerB80.
- Episodes of 4 waves. The player profile, and every adaptive model, resets each episode: each episode is a "new
  player".
- A headless eval player runs in lockstep at about 25–38× real time.

**The personas that matter here.**
- **RunnerA:** always flees by route A.
- **RunnerB80:** flees by B 80% of the time.
- **Adaptive:** prefers A (weights 0.8 / 0.1 / 0.1). When it is ambushed on a route, it multiplies that route's weight
  by 0.2, and it recovers 5% toward its preference after a clean escape. It stands in for a player who learns from the
  horde.
- **Mixed, Random, Kiter:** less predictable.
- **Fighter:** never flees, so it serves as a control.

**The bots' own check, "ambushed".** After the first second of a flee, the bot counts itself ambushed if any of these
happens:
- it takes damage;
- a zombie is within 4 units ahead on its route;
- a zombie is already at the refuge when it arrives.

The Adaptive bot reacts to exactly this check.

## 3. What was built

| Piece | Files | Run |
|---|---|---|
| Thompson sampling of the ambush direction (part A): Dirichlet draw from the profile's posterior | `Assets/Scripts/EnemyAI/Prediction/AmbushBandit.cs` (`ThompsonSampling`), `PredictiveCommander` (`-v2Thompson`) | 1 |
| Ambush bandit (part B): arms = sector × distance {12, 18, 25}; Beta posteriors discounted toward the prior (γ 0.9); Marsaglia–Tsang Gamma sampler; shared-arm mode; state export / import | `AmbushBandit.cs`, `PredictiveCommander` (`-v2Bandit`, `-v2BanditShared`, `-v2BanditKeep`, `-v2Gamma`, `-v2Seed`, `-v2BanditJoint`) | 1 |
| Trials: sites snapshotted at flee onset; contact = an ambusher within the contact radius after 1 s; only sites in the sector the player actually ran to are updated | `PredictiveCommander` | 1 |
| Bandit persistence across sessions; MainGame settings and menus | `AmbushBanditStore.cs`, `EnemyAISettings.cs`, `EnemyAIBootstrap.cs`, `ArenaAssetBuilder.cs` | 1 |
| Eval player builder, multi-variant runner with repeats, new metrics, comparison tool with pooled repeats | `Arena/Editor/EvalPlayerBuilder.cs`, `MLTraining/build_eval_player.sh`, `MLTraining/eval_adaptive.sh`, `MLTraining/analysis/adaptive_metrics.py`, `MLTraining/tools/arena-report.py` | 1–2 |
| Contact radius 6 (`-v2ContactRadius`); the bandit picks only the distance in the direction the rule chose | `PredictiveCommander` | 2 |
| **Ground truth:** each `bot_ambushed` event records the cause (damage / ahead / at the refuge) and the order of the zombie behind it (holding = an ambush squad, otherwise a chaser) | `Bots/BotBrain.cs`, `Arena/PlayerBot.cs`, `Arena/ArenaEnvironment.cs` | 3 |
| Contact watched for 5 s after the escape ends (`-v2ContactTail`); re-choose after every escape, keep a site's distance until it was tried (`-v2RedrawEveryEscape` = redraw every time) | `PredictiveCommander` | 3 |
| **Layer 2, player reaction model:** lose-shift learner with candidate avoidance strengths {1 = no reaction, 0.6, 0.35, 0.2, 0.1}, a Bayesian update from the player's choices, probability moved only between routes the player uses; "outings" observed in every mode | `Prediction/ReactionModel.cs`, `PredictiveCommander` (`-v2Reaction`, `-v2ReactionPrior`) | 4 |
| Logic tests outside Unity (samplers, bandit choice and forgetting, state round trip, Dirichlet sampling, ambush causes, reaction model) | `MLTraining/tools/logic-tests/BanditTests.cs`, `ReactionTests.cs`, `BotTests.cs` | all |

All adaptive options are **off by default**. With every option off, the commander gives exactly V2's spawns and
orders. This was checked after each change in a scripted simulation (a C# harness kept outside the repo).

Size: 29 files, about 3,600 added lines, 15 local commits before this write-up (list in §9).

## 4. How it was evaluated

- **One build, one flag.** Every run rebuilt the eval player once and ran all variants on it. The only difference
  between variants is a command-line flag. Plain V2 is the control in every run, and each run checked the new
  build's V2 against an old V2 batch (`20260926_0138`).
- **Repeats.** Runs 2–3 ran each variant twice and run 4 six times. The repeats are pooled, and each repeat is also
  shown on its own as the run-to-run noise. Intervals are 95% Wilson intervals.
- **Rounds.** All variants run once, then all again, so slow drift can't line up with one variant.
- **Metrics.**

  | Metric | Meaning | Since |
  |---|---|---|
  | **Ambushed by an ambusher** (primary outcome) | share of the bots' flee decisions where the bots' own check fired and the zombie behind it was holding an ambush | run 3 |
  | Ambushed by a chaser | the same, for zombies that weren't holding | run 3 |
  | Ambush on route at flee onset | a site with a member in the bot's chosen route sector when it decided to flee | Phase 5 |
  | Contact on the ran route | the bandit's reward: a site in the sector the player ran to got within the contact radius | run 1 |
  | Route prediction (top-1) | at bot flee decisions (arena metric), and per outing (run 4) | Phase 5 / run 4 |
  | Route entropy | of the bot's flee routes per episode (max 1.58 bits) | run 1 |
  | Pressure | player damage and deaths per wave, checked to stay near V2 | Phase 5 |

## 5. Results, run by run

### Run 1 (2026-10-06): the reward never fired

Contact radius 2.5 (the monitor's "intercepted"); the bandit chose sector and distance jointly (score P(sector) × θ);
sites re-chosen after every escape.

| | V2 | Thompson | Bandit | Thompson + Bandit |
|---|---|---|---|---|
| RunnerA: ambush contact on the ran route | 0/338 | 0/360 | 0/277 | 0/306 |
| RunnerA: ambush on route at flee onset | 82% | 76% | 67% | 53% |
| RunnerA: bot ambushed | 62% | 65% | 59% | 49% |

- Fleeing bots almost never come within 2.5 units of a zombie: they are faster, and they turn away from one 4 units
  ahead. So the bandit learned from zeros.
- Arms that got tried kept missing, while untried arms in other directions kept their optimistic prior. The bandit
  therefore pulled ambushes **off** the predicted route.
- Sites were re-placed after every escape, and ambushers spent the waves walking.

### Run 2 (2026-10-07): a reward that fires, but no more ambushes

Contact radius 6 (the radius at which a holding zombie engages). The bandit picks only the distance, in the
direction V2's rule chose. Sites re-chosen only after they were tried. 2 repeats.

| | V2 | Bandit | Bandit-every | Thompson | Thompson + Bandit |
|---|---|---|---|---|---|
| RunnerA: bot ambushed | 55% | 52% | 54% | 44% | 38% |
| RunnerB80: bot ambushed | 68% | 58% | 65% | 59% | 52% |
| Adaptive: bot ambushed | 52% | 51% | 58% | 46% | 52% |
| RunnerA: contact on the ran route | 4% | 11% | 10% | 5% | 9% |

- The bandit optimized its reward: RunnerA's contact went from 4% to 11%, and it learned that waiting 12 units out
  gives the most contact (route A: 12 / 18 / 25 = 60% / 27% / 37%).
- The bots' own "ambushed" rate didn't improve.
- Restricting when sites are re-chosen hurt; re-choosing after every escape (Bandit-every) kept V2's on-route rate.
- Thompson direction cost ambushes against the predictable runners.

### Run 3 (2026-10-07): the ground truth

Three changes:
- `bot_ambushed` now says whether a waiting zombie or a chaser did it;
- contact is watched for 5 s after the escape ends;
- the direction is re-chosen after every escape, while an untried site keeps its distance.

**Ground truth (V2):**

| Persona | Ambushed | By an ambusher | By a chaser | Ambushers' share |
|---|---|---|---|---|
| RunnerA | 58% | 42% | 16% | 72% |
| RunnerB80 | 64% | 27% | 37% | 42% |
| Adaptive | 59% | 22% | 37% | 37% |
| Mixed | 63% | 21% | 42% | 33% |
| Random | 51% | 20% | 31% | 39% |

So "bot ambushed" had been mostly a chaser measure for every persona except RunnerA.

**Outcome, ambushed by an ambusher (2 repeats):**

| | V2 | Thompson | Bandit | Thompson + Bandit | Bandit-tail0 | Bandit-every | Bandit-joint |
|---|---|---|---|---|---|---|---|
| RunnerA | 42% | 36% | 40% | 38% | 40% | 38% | 32% |
| RunnerB80 | 27% | 22% | 27% | 22% | 28% | 28% | 28% |
| Adaptive | 22% | 12% | 23% | 14% | 21% | 21% | 20% |

**The contact window.** Watching past the escape end roughly triples V2's measured contact (RunnerA 5% → 16%). The
tracker ends most escapes before the bot reaches the ambush at the refuge. With the full window, every distance
gets about 70% contact:

| Route A contact rate when the player ran there | 12 | 18 | 25 |
|---|---|---|---|
| with the 5 s window (Bandit) | 75% | 74% | 73% |
| escape only (Bandit-tail0, = run 2's reward) | 60% | 37% | 25% |

**Findings:**
- Run 2's "wait 12 units out" was an artifact of the short window. In this arena distance barely matters, so the
  placement bandit has nothing to learn.
- Thompson direction is worse for every persona, including Adaptive (22% → 12%, z ≈ 2.6). Re-drawing the direction
  after every escape also made it move the ambushers about 10× as often as V2.
- V2 predicts Adaptive's route *below* chance (24% top-1): the bot avoids the route the counts point at.

### Run 4 (2026-10-08): layer 2, the player reaction model

V2, Reaction (prior P(no reaction) 0.5), and Reaction-p0.8; 6 repeats.

Pre-run checks:
- In a toy model of the Adaptive bot, the reaction model raised ambushes by an ambusher from ~18% to ~23%.
- In the logic tests, top-1 prediction went from 30% to 40%; in the scripted harness with a reacting player, from 33%
  to 57%.
- A non-reacting player was left alone in all three.

| | V2 | Reaction | Reaction-p0.8 |
|---|---|---|---|
| Adaptive: ambushed by an ambusher | 23% | 21% | 23% |
| Adaptive: commander top-1 on outings | 28% | 32% | 33% |
| … same outings, before the adjustment | 28% | 36% | 33% |
| Adaptive: model's P(no reaction) at episode end | – | 0.65 | 0.75 |
| RunnerA / RunnerB80: ambushed by an ambusher | 44% / 30% | 42% / 31% | 44% / 28% |

- No effect on real ambushes. The repeats of V2 alone spread from 17% to 28% for Adaptive.
- On the same outings, the adjustment made prediction slightly worse at prior 0.5 and changed nothing at 0.8.
- The model couldn't tell Adaptive (0.65) apart from players who don't react (0.51–0.75).

**Likely cause.**
- The commander counted any zombie within 6 units on the way out as "met", chasers behind the player included. That
  fired on 74% of Adaptive's outings, but the bot's own check fired on 56% of its flees.
- In the toy model, that many false alarms leaves only about +2 points of gain, which is inside the noise.

## 6. What was learned

1. **Proxy metrics misled at every step.**
   - The monitor's "intercepted" (2.5 units) was about 0 for fleeing players.
   - The bandit's reward rose without any rise in real ambushes.
   - "Bot ambushed" mostly counted chasers.

   Splitting ambushers from chasers changed the conclusions. A ground truth is worth building before optimizing.
2. **The measurement window can manufacture a result.** The escape tracker ends escapes early. Run 2's learned
   distance preference disappeared once contact was watched to the end of the run.
3. **In this arena, where to wait along a route doesn't matter.** The bots run straight to fixed refuges, so any
   point on the route meets them. The real bottleneck is *which* route, i.e. prediction.
4. **Exploration has a cost.** Thompson sampling of the direction cost ambushes against predictable players. It did
   not help against the adaptive one either: that bot already spreads its routes almost uniformly (route entropy
   about 1.5 of 1.58 bits), so there was nothing to unlock.
5. **To model how a player reacts, you need to observe what the player reacts to.** The commander's observable
   ("a zombie came near") isn't the player's experience ("I was cut off ahead"), and the model learned from the
   wrong signal.
6. **Controls and repeats were essential.** Several single-run differences of 5–10 points were within the
   run-to-run noise. The pooled repeats and the per-repeat table kept them from being reported as effects.

## 7. Limitations

- **Bots only.** The build guide planned human playtests (several sessions per person, route entropy, a
  questionnaire); they were not done. Humans may react to ambushes differently from the Adaptive bot.
- **One arena layout:** three straight routes and fixed refuges. A map with branching routes or no fixed refuge
  could make placement matter.
- **The Adaptive bot's rule has the same form as the reaction model** (multiplicative avoidance). A win would have
  been partly circular. The loss is informative, because the model failed even with a matching form.
- **Short learning windows.** Every model resets with the profile each episode (4 waves, about 15 flees). In
  MainGame the profile and bandit persist across sessions; the reaction model doesn't (not wired up).
- **Ground-truth approximations.** On damage, the attacker is the nearest zombie within 2.5 units. The 5 s contact
  window was usually cut short by the next escape (about 2 s on average for the runners).
- **Power.** Runs 2–3 had 2 repeats per variant, enough for differences of about 10 points. Run 4 had 6, enough for
  about 5.

## 8. Possible next steps

1. Layer 2 with an aligned signal: count "met" only for a zombie *ahead* of the player on the way out, or damage. It's
   a small change. Even with a perfect signal, the toy model's best case is about +4 points.
2. Human playtests with V2 and V2 + adaptive options, in MainGame with persistence on.
3. A map where placement matters (detours, no fixed refuge) to give the bandit something to learn.

## 9. Reproducing it

On the Mac, with Unity closed:

```
git switch adaptive-horde
MLTraining/tools/compile-check.sh                 # editor + player compile
MLTraining/tools/logic-tests.sh                   # all logic tests, outside Unity
MLTraining/build_eval_player.sh                # builds MLTraining/builds/eval/MLArena_Eval.app
MLTraining/eval_adaptive.sh 40 --ablations     # layer 1: V2, TS, Bandit, TS+Bandit + 6 ablations, 2 repeats (run 3)
MLTraining/eval_adaptive.sh 40 --set reaction --repeats 6   # layer 2 (run 4)
```

- One-click launchers that also run the compile check, the tests and the build:
  `MLTraining/results/adaptive_eval/run_adaptive_eval.command` and `run_reaction_eval.command` (git-ignored).
- Single variants: `MLTraining/eval.sh MLArena_Eval_V2 40 <flags>`.
- V2 flags: `-v2Thompson -v2Bandit -v2BanditShared -v2BanditKeep -v2Gamma G -v2Seed N -v2ContactRadius R
  -v2ContactTail S -v2BanditJoint -v2RedrawEveryEscape -v2Reaction -v2ReactionPrior P`.
- Telemetry goes to `~/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry/`.

| Run | Results folder (`MLTraining/results/adaptive_eval/`) | Code |
|---|---|---|
| 1 | `20261006_193654` | before the first commit |
| 2 | `20261007_031839` | `56db2ef9` |
| 3 | `20261007_050453` | `c179d99c` |
| 4 | `20261008_022011` | layer-2 commits `ccab66bf` … `3de31c3e` (run from the working tree before committing, unchanged) |

Commits on `adaptive-horde` (oldest first):

| Commit | What |
|---|---|
| `8bfbb93f` | ambush bandit and Thompson samplers, tests |
| `a2b0d8ab` | `PredictiveCommander` adaptive modes, `AmbushBanditStore` |
| `1ad62c02` | MainGame settings and menus |
| `664a300c` | eval player builder, eval runner, metrics, `arena-report` changes |
| `56db2ef9`, `452b4348` | notes: run 1, run 2 |
| `796c8f2b` | ground truth: ambusher vs chaser |
| `42b9afb1` | re-choose after every escape, keep untried distances |
| `fd8d3fd3` | contact window after the escape ends |
| `c179d99c`, `f3e1f246` | notes: run 3 changes, run 3 |
| `ccab66bf` | player reaction model and tests |
| `f0fef469` | reaction model in `PredictiveCommander`, outing telemetry |
| `3de31c3e` | eval: reaction variant set, outing metrics |
| `4a3ba801` | notes: layer 2, run 4 |

## 10. Who did what

Every commit on `adaptive-horde` says this too (`Co-Authored-By: Claude`).

- **KahootGenius** authors all the coding.
- **Claude** (Anthropic's AI assistant, through Claude Code), at Lawrence's request:
   - Reviews what KahootGenius've done
   - Write reports based on the performance of the models
