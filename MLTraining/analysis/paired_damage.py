#!/usr/bin/env python3
"""Paired comparison of an eval_adaptive.sh run: the control (first variant in batches.tsv, normally V2) against each
other variant, round by round. Round r of every variant used the same arena seed, so the bots made the same random draws
and the rounds pair up (eval_adaptive.sh --seed).

Usage: python3 MLTraining/analysis/paired_damage.py MLTraining/results/adaptive_eval/<stamp> [TELEMETRY_ROOT]
  TELEMETRY_ROOT  where the batches' telemetry folders are; default $ENEMYAI_TELEMETRY_ROOT, else MLTraining/results/telemetry
                  (eval_adaptive.sh copies every batch there)

Per persona: damage per wave in each round (RunnerB80 has two arenas per round, each its own pair), the mean change with
a 95% interval (t over the pairs), caught per flee (bot_ambushed / Flee decisions), deaths per wave, and damage taken on
the way out (8-20 from the arena centre).
8 arenas: the eval scene's mean damage per wave in each round (RunnerB80 counted twice), its change and 95% interval
(t over rounds). Runners: the mean change of Adaptive, RunnerA and RunnerB80 (its two arenas averaged first).
"""
import csv
import glob
import json
import math
import os
import statistics
import sys

T95 = [None, 12.71, 4.30, 3.18, 2.78, 2.57, 2.45, 2.36, 2.31, 2.26, 2.23, 2.20, 2.18, 2.16, 2.14, 2.13, 2.12, 2.11,
       2.10, 2.09, 2.09, 2.08, 2.07, 2.07, 2.06, 2.06, 2.06, 2.05, 2.05, 2.05, 2.04]
RUNNERS = ("Adaptive", "RunnerA", "RunnerB80")


def interval(values):
    """mean and half-width of the 95% t-interval (nan with fewer than 2 values)"""
    mean = statistics.mean(values)
    if len(values) < 2:
        return mean, float("nan")
    df = len(values) - 1
    t = T95[df] if df < len(T95) else 1.96
    return mean, t * statistics.stdev(values) / math.sqrt(len(values))


def change(values):
    mean, half = interval(values)
    return f"{mean:+.1f}" if math.isnan(half) else f"{mean:+.1f} [{mean - half:+.1f}, {mean + half:+.1f}]"


def summarize(folder):
    """waves, deaths, flee decisions, catches and damage (all, and 8-20 from the centre) of one arena's telemetry"""
    s = dict(waves=0, deaths=0, flees=0, caught=0, damage=0.0, way=0.0)
    with open(os.path.join(folder, "events.jsonl")) as f:
        for line in f:
            try:
                e = json.loads(line)
            except ValueError:
                continue
            kind = e.get("type")
            if kind == "wave_start":
                s["waves"] += 1
            elif kind == "player_died":
                s["deaths"] += 1
            elif kind == "bot_decision" and e.get("mode") == "Flee":
                s["flees"] += 1
            elif kind == "bot_ambushed":
                s["caught"] += 1
    with open(os.path.join(folder, "session.json")) as f:
        session = json.load(f)
    cx, cy = session.get("zone_center_x", 0.0), session.get("zone_center_y", 0.0)
    with open(os.path.join(folder, "samples.csv")) as f:
        for row in csv.DictReader(f):
            try:
                damage = float(row["damage"] or 0)
            except ValueError:
                continue
            if damage <= 0:
                continue
            s["damage"] += damage
            if 8 <= math.hypot(float(row["px"]) - cx, float(row["py"]) - cy) < 20:
                s["way"] += damage
    return s


def per_wave(s, key):
    return s[key] / max(1, s["waves"])


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    run = argv[1].rstrip("/")
    repo = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    root = argv[2] if len(argv) > 2 else os.environ.get("ENEMYAI_TELEMETRY_ROOT") or os.path.join(repo, "MLTraining", "results", "telemetry")

    with open(os.path.join(run, "batches.tsv")) as f:
        rows = [line.rstrip("\n").split("\t") for line in f][1:]
    variants = [(r[0], [b for b in r[1].split(",") if b], r[3] if len(r) > 3 else "") for r in rows if r and r[0]]
    if len(variants) < 2:
        print("batches.tsv needs a control and at least one variant")
        return 2

    # results[variant][persona] = list of (round, arena, summary)
    results = {}
    for name, batches, _ in variants:
        for rnd, batch in enumerate(batches):
            folders = sorted(glob.glob(os.path.join(root, batch + "*")))
            if not folders:
                print(f"no telemetry for batch {batch} under {root}")
                return 2
            for folder in folders:
                persona = folder.rsplit("_", 1)[1]
                arena = folder.rsplit("_arena", 1)[1].split("_")[0]
                results.setdefault(name, {}).setdefault(persona, []).append((rnd, arena, summarize(folder)))

    control, control_batches, seeds = variants[0]
    rounds = len(control_batches)
    print(f"{os.path.basename(run)}: {rounds} rounds per variant, arena seeds {seeds or '?'}; control {control}")
    for name, batches, _ in variants[1:]:
        print(f"\n== {control} -> {name}: damage per wave, change [95% interval over the pairs]")
        print(f"{'persona':10} {'per round, ' + control:38} {'per round, ' + name:38} {'change':24} {'caught/flee':16} {'deaths/wave':14} on the way out")
        eval_control = [0.0] * rounds
        eval_variant = [0.0] * rounds
        runner_changes = {p: [] for p in RUNNERS}
        for persona in sorted(results[control]):
            a = sorted(results[control][persona])
            b = sorted(results[name].get(persona, []))
            pairs = [(x, y) for x, y in zip(a, b) if x[:2] == y[:2]]
            va = [per_wave(x[2], "damage") for x, _ in pairs]
            vb = [per_wave(y[2], "damage") for _, y in pairs]
            for (rnd, _, sa), (_, _, sb) in pairs:
                if rnd < rounds:
                    eval_control[rnd] += per_wave(sa, "damage") / 8
                    eval_variant[rnd] += per_wave(sb, "damage") / 8
            if persona in runner_changes:
                by_round = {}
                for (rnd, _, sa), (_, _, sb) in pairs:
                    by_round.setdefault(rnd, []).append(per_wave(sb, "damage") - per_wave(sa, "damage"))
                runner_changes[persona] = [statistics.mean(v) for _, v in sorted(by_round.items())]

            def total(side, key):
                return sum(s[key] for _, _, s in side)

            caught = " -> ".join(f"{total(side, 'caught') / total(side, 'flees'):.0%}" if total(side, "flees") else "-" for side in (a, b))
            deaths = " -> ".join(f"{total(side, 'deaths') / max(1, total(side, 'waves')):.2f}" for side in (a, b))
            way = " -> ".join(f"{total(side, 'way') / max(1, total(side, 'waves')):.1f}" for side in (a, b))
            print(f"{persona:10} {statistics.mean(va):5.1f} ({' / '.join(f'{v:.1f}' for v in va)})".ljust(49) +
                  f" {statistics.mean(vb):5.1f} ({' / '.join(f'{v:.1f}' for v in vb)})".ljust(39) +
                  f" {change([y - x for x, y in zip(va, vb)]):24} {caught:16} {deaths:14} {way}")
        print(f"{'8 arenas':10} {statistics.mean(eval_control):5.1f} ({' / '.join(f'{v:.1f}' for v in eval_control)})".ljust(49) +
              f" {statistics.mean(eval_variant):5.1f} ({' / '.join(f'{v:.1f}' for v in eval_variant)})".ljust(39) +
              f" {change([y - x for x, y in zip(eval_control, eval_variant)])}")
        if all(runner_changes[p] for p in RUNNERS):
            n = min(len(runner_changes[p]) for p in RUNNERS)
            runners = [statistics.mean(runner_changes[p][r] for p in RUNNERS) for r in range(n)]
            print(f"{'runners':10} mean change of Adaptive, RunnerA and RunnerB80: {change(runners)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
