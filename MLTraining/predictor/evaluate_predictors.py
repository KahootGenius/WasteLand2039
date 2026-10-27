"""Offline accuracy of the V2 escape predictors on arena telemetry (format 3), per persona.

Usage:
  python MLTraining/predictor/evaluate_predictors.py --batch 20260926_0XXX [--weights Assets/ML/Predictors/X.json]

For every valid escape: did the predictor's most likely *direction* match the direction the player
actually ran (plan §7 metric 1, "escape prediction accuracy")? Reported for
  chance      1 / (number of sectors)                      (12.5%)
  frequency   FrequencyPredictor, replayed from the telemetry (profile counts at onset)
  learned     LearnedPredictor on the logged feature vector (only with --weights)
split by how many escapes the profile had seen earlier in the episode (adaptation speed, §7 metric 3),
and for escapes that start at the base (Core), which is where an ambush has to be placed in advance.

--decisions: the test that matters for the ambush. At every bot flee decision (arena ground truth), predict
from the base the way PredictiveCommander does before the flee (current zone = Core, no threat features,
profile as it was at that moment) and check the most likely direction against the bot's chosen route
(A = east, B = north-west, C = south-west).
"""
import glob
import json
import argparse
import os
import sys
from collections import defaultdict

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import escape_data  # noqa: E402

BUCKETS = [(0, 0, "1st"), (1, 2, "2-3"), (3, 5, "4-6"), (6, 10 ** 9, "7+")]


def hit(p, s, zones):
    return int(np.argmax(p)) == s["label"]


def fmt(hits, n):
    return f"{hits / n:4.0%} ({n})" if n else "   -    "


ROUTE_DIRECTION = {"A": 0, "B": 3, "C": 5}


def decision_samples(prefixes):
    """(persona, route direction, frequency prediction, decision-time features) per bot flee decision."""
    out = []
    for path in escape_data.batch_dirs(prefixes):
        zones = json.load(open(os.path.join(path, "session.json"), encoding="utf-8"))["zones"]
        index = {z: i for i, z in enumerate(zones)}
        k = len(zones)
        events = [json.loads(line) for line in open(os.path.join(path, "events.jsonl"), encoding="utf-8")]
        profile = escape_data.ReplayProfile(k)
        persona = "?"
        for i, e in enumerate(events):
            kind = e.get("type")
            if kind == "episode_start":
                persona = e.get("persona", persona)
            elif kind == "profile_reset":
                profile = escape_data.ReplayProfile(k)
            elif kind == "escape_end" and e.get("valid"):
                profile.add(index.get(e.get("start_zone"), -1), e.get("dx", 0.0), e.get("dy", 0.0), e.get("engagement", -1))
            elif kind == "bot_decision" and e.get("mode") == "Flee" and e.get("route") in ROUTE_DIRECTION:
                # profile part of the features: from the next escape_start (the profile doesn't change in between)
                nxt = next((g for g in events[i + 1:] if g.get("type") == "escape_start" and g.get("features")), None)
                features = None
                if nxt is not None:
                    x = np.asarray(nxt["features"][:8 + k + 8] + [0.0] * (k + 8), dtype=np.float64)
                    x[8 + k + 8 + 0] = 1.0                      # current zone = Core
                    x = np.concatenate([x, profile.first_distribution()])
                    features = x
                out.append((persona, ROUTE_DIRECTION[e["route"]], profile.frequency_predict(0), features, profile.evidence))
    return out


def decisions_report(args, model):
    rows = defaultdict(lambda: defaultdict(lambda: [0, 0]))
    for persona, route, freq, x, evidence in decision_samples(args.batch):
        bucket = "early" if evidence < 2 else "later"
        for name, p in (("frequency", freq), ("learned", model.predict(x) if model is not None and x is not None else None)):
            if p is None:
                continue
            for b in (bucket, "all"):
                cell = rows[(persona, name)][b]
                cell[0] += int(np.argmax(p)) == route
                cell[1] += 1
    print("Route predicted from the base at bot flee decisions (chance 12.5% over 8 directions; 1/3 over the 3 routes)")
    print(f"{'Persona':10s} {'Predictor':10s} {'evidence < 2':>14s} {'evidence >= 2':>14s} {'all':>14s}")
    for (persona, name), cells in sorted(rows.items()):
        print(f"{persona:10s} {name:10s} " + " ".join(f"{fmt(*cells[b]):>14s}" for b in ("early", "later", "all")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--batch", nargs="+", required=True)
    ap.add_argument("--weights", help="LearnedPredictor JSON")
    ap.add_argument("--markdown", action="store_true")
    ap.add_argument("--no-threat", action="store_true", help="zero the threat features before the learned model")
    ap.add_argument("--decisions", action="store_true", help="score predictions from the base at bot flee decisions")
    args = ap.parse_args()

    model = escape_data.LearnedModel(args.weights) if args.weights else None
    if args.decisions:
        decisions_report(args, model)
        return
    zones, samples = escape_data.load(args.batch)
    predictors = [("frequency", lambda s: s["freq"])]
    if model is not None:
        def learned(s):
            if s["features"] is None:
                return None
            x = s["features"].copy()
            if args.no_threat:
                x[escape_data.threat_slice(len(zones))] = 0.0
            return model.predict(x)
        predictors.append(("learned" + (" (no threat)" if args.no_threat else ""), learned))

    by_persona = defaultdict(list)
    for s in samples:
        by_persona[s["persona"]].append(s)

    header = ["Persona", "Predictor"] + [f"after {label}" for _, _, label in BUCKETS] + ["all", "from Core"]
    rows = []
    for persona in sorted(by_persona):
        group = by_persona[persona]
        for name, predict in predictors:
            cells = []
            for lo, hi, _ in BUCKETS:
                sub = [s for s in group if lo <= s["index"] <= hi]
                preds = [(predict(s), s) for s in sub]
                preds = [(p, s) for p, s in preds if p is not None]
                cells.append(fmt(sum(hit(p, s, zones) for p, s in preds), len(preds)))
            preds = [(predict(s), s) for s in group]
            preds = [(p, s) for p, s in preds if p is not None]
            cells.append(fmt(sum(hit(p, s, zones) for p, s in preds), len(preds)))
            core = [(p, s) for p, s in preds if s["start_zone"] == "Core"]
            cells.append(fmt(sum(hit(p, s, zones) for p, s in core), len(core)))
            rows.append([persona, name] + cells)

    print("Top-1 direction accuracy (chance 12.5%), by escapes already seen in the episode; (n)")
    if args.markdown:
        print("| " + " | ".join(header) + " |")
        print("|" + "---|" * len(header))
        for r in rows:
            print("| " + " | ".join(r) + " |")
    else:
        widths = [max(len(str(r[i])) for r in rows + [header]) for i in range(len(header))]
        for r in [header] + rows:
            print("  ".join(str(c).ljust(w) for c, w in zip(r, widths)))


if __name__ == "__main__":
    main()
