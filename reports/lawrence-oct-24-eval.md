## After the run (2026-10-24)
Run: MLTraining/results/adaptive_eval/20261022_100132, 2026-10-22 10:01–11:02 (61 min, not the
  80 registered); 20 rounds × V2, Style, Full; seeds 20261023 + 104729 × (r − 1), shared by the
  three arms of each round.
Setup checks:
  - The criteria were committed (9be67ee, 09:34) before the run started (10:01), and they have not
    changed since.
  - The eval player was built 2026-10-21 20:49 from e787a03, the same build as the 3-round Style
    trial. The uncommitted edits now in the tree date from 2026-10-22 21:22, after the run.
  - Stale-build check passed: 160 of 160 arenas per arm logged V2-Frequency, V2-Frequency+Style
    and V2-Frequency+Intercept+Style. All 480 telemetry folders are there (8 per batch).
  - V2's 8-arena average, 37.0, sits with the earlier V2 batches on this bot (37.0–39.2 in the
    three trials, 38.6 in the 10-round run of 2026-10-20).
Primary: Full − V2 = +4.4 [+3.0, +5.8]. Pass. Full did more damage than V2 in 18 of 20 rounds
  (41.4 vs 37.0 per wave, +12%). Predicted +5.6 by the simulator, about +4 after the trials'
  usual shortfall; the result is 1.2 under the simulator.
Cut-off test: runners' change, Full − Style = +2.0 [+0.5, +3.5]. Pass: the cut-off is credited
  with more damage against runners (predicted +2.7).
Secondary:
  - 8-arena average, Full − Style: +0.6 [−0.7, +1.9] (predicted +0.5). The total barely moves.
  - Full − Style by bot (predicted → result): Adaptive +2 → +5.2 [+2.2, +8.2]; RunnerA +4 → +1.4
    [−1.7, +4.5]; RunnerB80 +2 → −0.5 [−3.4, +2.4]; Kiter +2 → +3.4 [−1.0, +7.9]; Mixed −4 → −2.8
    [−7.7, +2.0]; Random −4 → −3.4 [−7.2, +0.5]. Mixed and Random went down as predicted, but
    almost all of the runners' gain is Adaptive's, where the simulator expected RunnerA's.
  - Style − V2: +3.8 [+2.5, +5.1] (predicted +4.7). Runners' change, Full − V2: +3.6 [+2.4, +4.9]
    (simulator +5.5); Style alone: +1.6 [+0.2, +3.0] (simulator +2.8).
  - Caught per flee, Full − Style: RunnerA 50% → 59% (predicted up about 7); Adaptive 54% → 55%,
    RunnerB80 52% → 52%, Mixed 49% → 49% (predicted level). All as predicted.
  - Kiter, Full − V2: +12.9 [+8.5, +17.2] (simulator +15); deaths per wave 0.53 → 0.66.
  - Fighter placebo: Style − V2 −0.4 [−2.9, +2.2], Full − V2 +1.7 [−0.8, +4.2], Full − Style +2.1
    [−1.3, +5.5]. All three straddle 0, as they should; a range of ±2.5–3.5 is how far one bot
    moves on noise alone.
Guardrail: passed. The lowest Full − V2 change is RunnerB80's +1.6; no bot lost damage.
What it means:
  - Full (Style + cut-off) beats V2 by 4.4 damage per wave, and nearly all of that is Style's
    +3.8. The cut-off adds +0.6 overall, which this run can't tell from 0.
  - The cut-off moves damage rather than adding it: runners +2.0 (significant), Mixed and Random
    about −3 each (not significant).
  - Against Adaptive it makes contact deadlier, not more frequent. Caught per flee stays at
    54–55%, but damage goes from 10.8 to 16.0 per wave and deaths from 39 to 65 in about 540
    waves. Against RunnerA it is the reverse: 9 points more catches, but only +1.4 damage.
  - One reading, not tested here: the cut-off doesn't depend on the route guess. Adaptive is the
    bot whose route the commander can't guess (top-1 on outings 31–32%, chance 33%), while the
    ambush already covers RunnerA (76–78%).
  - The simulator came in high again: by 1.2 on Full − V2, 0.9 on Style − V2 and 0.7–1.9 on the
    runners' lines. It got the cut-off's total (+0.5 vs +0.6) and the fighters' direction right,
    but not how the gain split among the runners.
  - Precision was better than forecast: the 8-arena range was ±1.3–1.4, not ±2.