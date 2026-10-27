#!/usr/bin/env python3
"""Offline checks for the outcome model (V2 + Outcome)
on existing plain-V2 eval telemetry, without Unity.

Usage:
  python3 MLTraining/analysis/outcome_offline.py [--root DIR] [--markdown] [PREFIX[,PREFIX...]]
  --root    telemetry folder (default: the eval player's EnemyAITelemetry folder, as in arena-report)
  PREFIX    batch prefixes, comma-separated (default: the plain-V2 batches of runs 2-4, as in the 24% analysis)

1. Outing replay check. The commander's outings (PredictiveCommander BeginOuting / TrackOuting / FinishOuting) are
   rebuilt from samples.csv and the escape events: an outing starts with an escape that starts within 10 of the base,
   its sector is the direction of its first valid escape (displacement > 1), it ends when the player is back within
   10 and not escaping (or at the next outing / the wave end), and it counts if the player got 15 or further.
   Checked against the `outing` events the commander logged (run 4 onwards), matched by end time.
2. Calibration of the "caught" signal, per persona. Ground truth: a `bot_ambushed` event inside the outing (the bot's
   own check; the horde must never read it at runtime). Candidates the horde can observe on the outbound leg (the
   samples the commander's contact check uses: 1 s or more after the start, farther than 10 from the base, not yet 5
   back from the farthest point): the nearest zombie came within d (d = 2..6), damage > 0, either of the two; and the
   old reaction model's "met" (a zombie within 6 in the outing's sector or a neighbouring one) for reference.
   The replay below uses the candidate with the best F1 on the Adaptive bot's outings.
3. Observation check: how often the outing's sector is the route the bot chose (its flee decision just before the
   outing started), and how often the next outing (same episode) stays in the same sector after a caught / clean
   one, against the base prediction's own probability for that sector at the next outing's start.
4. Model replay (one-step counterfactual: the bot's trajectory is held fixed). At every bot flee decision with an onset,
   the guess of guess_analysis.py (TOP: top sector of the logged prediction, evidence >= 2, a route sector, mapped
   E=A, NW=B, SW=C) is recomputed after applying an outcome model in the state it would have had at that moment
   (outings finished until then, reset every episode). Paired difference against plain V2, with a 95% bootstrap CI
   over episodes; a model guess on a sector that is not a route drops the decision (as guess_analysis.py does) or,
   in the last column, counts as a miss.
"""
import argparse
import importlib.util
import math
import os
import random
from collections import Counter, defaultdict

# scratch copy (moved out of MLTraining/analysis/ on 2026-10-20): find the project by walking up to MLTraining/tools
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = HERE
while not os.path.exists(os.path.join(ROOT, "MLTraining", "tools", "arena-report.py")) and os.path.dirname(ROOT) != ROOT:
    ROOT = os.path.dirname(ROOT)
_spec = importlib.util.spec_from_file_location("arena_report", os.path.join(ROOT, "MLTraining", "tools", "arena-report.py"))
ar = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ar)

# the plain-V2 batches of runs 2-4
DEFAULT_BATCHES = ("20261007_031841,20261007_032916,20261007_050455,20261007_051503,20261008_022013,"
                   "20261008_022317,20261008_022626,20261008_023015,20261008_023333,20261008_023643")
PERSONAS = ["Adaptive", "RunnerA", "RunnerB80"]

# PredictiveCommander defaults
OUTING_BASE_RADIUS = 10.0
OUTING_MIN_DISTANCE = 15.0
OUTING_TURN_BACK = 5.0
CONTACT_GRACE = 1.0
CONTACT_RADIUS = 6.0
MIN_EVIDENCE = 2.0
ONSET_WINDOW = 2.0
DECISION_LEAD = 3.0   # a flee decision at most this long before the outing's start is "the decision behind it"

SECTORS = ["E", "NE", "N", "NW", "W", "SW", "S", "SE"]
ROUTE_INDEX = {route: SECTORS.index(sector) for route, sector in ar.ROUTE_SECTOR.items()}
INDEX_ROUTE = {i: route for route, i in ROUTE_INDEX.items()}


def direction_sector(dx, dy):
    """RadialZoneMap.DirectionToSector: 8 sectors centred on the axes, 0 = east, counter-clockwise"""
    angle = math.atan2(dy, dx) % (2 * math.pi)
    return int(math.floor(angle / (2 * math.pi / 8) + 0.5)) % 8


def sector_distance(a, b):
    d = abs(a - b) % 8
    return min(d, 8 - d)


def top(p):
    """EscapeSectors.Top: first strictly highest, skipping p <= 0; -1 if none"""
    best = -1
    for s, v in enumerate(p):
        if v > 0 and (best < 0 or v > p[best]):
            best = s
    return best


# ---------------------------------------------------------------- outings and flee decisions

def read_samples(rows):
    out = []
    for r in rows:
        nearest = r["nearest"]
        out.append((float(r["t"]), float(r["px"]), float(r["py"]), int(r["escape"]),
                    float(nearest) if nearest not in ("", "null") else math.inf, float(r["damage"])))
    return out


def rebuild_outings(session, events, samples):
    """Outings in the order they finished, plus the `outing` events the commander logged (if any)"""
    cx, cy = session["zone_center_x"], session["zone_center_y"]
    outings, logged = [], []
    current = None
    episode = 0
    in_wave = False
    last_decision = None
    i = 0

    def finish(t, reason):
        nonlocal current
        o, current = current, None
        if o is None or o["sector"] < 0 or o["max_distance"] < OUTING_MIN_DISTANCE:
            return
        o["end"], o["reason"] = t, reason
        o["met"] = any(sector_distance(s, o["sector"]) <= 1 for s in o["contact_sectors"])
        outings.append(o)

    def track(sample):
        t, x, y, escape, nearest, damage = sample
        if current is None:
            return
        d = math.hypot(x - cx, y - cy)
        current["max_distance"] = max(current["max_distance"], d)
        if d > OUTING_BASE_RADIUS and d >= current["max_distance"] - OUTING_TURN_BACK and t - current["start"] >= CONTACT_GRACE:
            current["min_nearest"] = min(current["min_nearest"], nearest)
            current["damage"] += damage
            if nearest <= CONTACT_RADIUS:
                current["contact_sectors"].add(direction_sector(x - cx, y - cy))
        if escape < 0 and d <= OUTING_BASE_RADIUS:
            finish(t, "returned")

    for e in events:
        t = e["t"]
        # samples before this event (escape events fire inside the sample step of the same time)
        while i < len(samples) and samples[i][0] < t - 1e-6:
            if in_wave:
                track(samples[i])
            i += 1
        kind = e["type"]
        if kind == "episode_start":
            episode = e.get("episode", episode + 1)
            current = None
        elif kind == "wave_start":
            in_wave = True
        elif kind in ("arena_wave_end", "wave_abort"):
            if in_wave:
                finish(t, "wave_end")
            in_wave = False
        elif kind == "bot_decision":
            last_decision = e
        elif kind == "escape_start" and in_wave:
            d = math.hypot(e["x"] - cx, e["y"] - cy)
            if d <= OUTING_BASE_RADIUS:
                if current is not None:
                    finish(t, "next_outing")
                current = dict(episode=episode, start=t, start_escape=e["id"], sector=-1, max_distance=d,
                               contact_sectors=set(), min_nearest=math.inf, damage=0.0, prediction=None,
                               decision=last_decision, ambushes=0)
        elif kind == "prediction":
            if current is not None and current["prediction"] is None and e.get("escape") == current["start_escape"]:
                current["prediction"] = e["sector_probabilities"]
        elif kind == "escape_end":
            if current is not None and current["sector"] < 0 and e.get("valid") and math.hypot(e["dx"], e["dy"]) > 1.0:
                current["sector"] = direction_sector(e["dx"], e["dy"])
        elif kind == "bot_ambushed":
            if current is not None:
                current["ambushes"] += 1
        elif kind == "outing":
            logged.append(e)
    return outings, logged


def chosen_route(outing):
    """The route of the bot's flee decision just before the outing started, or None"""
    d = outing["decision"]
    if d is None or d.get("mode") != "Flee" or d.get("route") not in ROUTE_INDEX or outing["start"] - d["t"] > DECISION_LEAD:
        return None
    return d["route"]


def on_route_share(outings):
    """Share of outings whose sector is the route the bot chose"""
    return sum(ROUTE_INDEX[chosen_route(o)] == o["sector"] for o in outings) / len(outings) if outings else math.nan


def flee_decisions(events):
    """Bot flee decisions with an onset (first escape_start with enemy_zones within 2 s) and the prediction logged then"""
    out = []
    episode = 0
    for i, e in enumerate(events):
        if e["type"] == "episode_start":
            episode = e.get("episode", episode + 1)
        if e["type"] != "bot_decision" or e.get("mode") != "Flee" or e.get("route") not in ROUTE_INDEX:
            continue
        onset, predictions = None, []
        for g in events[i + 1:]:
            if g["type"] in ("bot_decision", "episode_start") or g["t"] - e["t"] > ONSET_WINDOW:
                break
            if onset is None and g["type"] == "escape_start" and "enemy_zones" in g:
                onset = g
            if g["type"] == "prediction":
                predictions.append(g)
        if onset is None:
            continue
        prediction = next((g for g in predictions if g.get("escape") == onset.get("id")), predictions[0] if predictions else None)
        if prediction is None:
            continue
        out.append(dict(episode=episode, t=onset["t"], route=e["route"], p=prediction["sector_probabilities"],
                        evidence=prediction.get("evidence", 0.0)))
    return out


def load(root, prefixes):
    sessions = []
    for path in sorted(ar.batch_dirs(root, prefixes)):
        session, events, rows = ar.load_session(path)
        persona = next((e.get("persona") for e in events if e["type"] == "episode_start"), None)
        outings, logged = rebuild_outings(session, events, read_samples(rows))
        sessions.append(dict(name=os.path.basename(path), persona=persona, outings=outings, logged=logged,
                             decisions=flee_decisions(events)))
    return sessions


# ---------------------------------------------------------------- outcome models (pure-Python mirrors)

class OutcomeModel:
    """The spec's model. kind "spec": p = (stays_o + kappa * base[s]) / (n_o + kappa).
    kind "relative": p = base[s] * (stays_o + kappa) / (expected_o + kappa), expected = sum of base_at_start[prev sector]
    (stays relative to what the base prediction already expected; 1 for a player who doesn't react). gamma decays the
    counts before each observation."""

    def __init__(self, kind="spec", kappa=4.0, gamma=1.0):
        self.kind, self.kappa, self.gamma = kind, kappa, gamma
        self.reset()

    def reset(self):
        self.last = None
        self.counts = {True: [0.0, 0.0, 0.0], False: [0.0, 0.0, 0.0]}  # stays, moves, expected stays

    def observe(self, base_at_start, sector, caught):
        if self.last is not None:
            previous, previous_caught = self.last
            for c in self.counts.values():
                for j in range(3):
                    c[j] *= self.gamma
            c = self.counts[previous_caught]
            c[0 if sector == previous else 1] += 1
            c[2] += base_at_start[previous] if base_at_start else 0.0
        self.last = (sector, caught)

    def apply(self, base):
        if self.last is None:
            return base
        s, caught = self.last
        stays, moves, expected = self.counts[caught]
        b = base[s]
        if b >= 1.0 - 1e-9:
            return base
        if self.kind == "spec":
            p = (stays + self.kappa * b) / (stays + moves + self.kappa)
        else:
            p = min(0.98, b * (stays + self.kappa) / (expected + self.kappa))
        return [p if x == s else (1.0 - p) * base[x] / (1.0 - b) for x in range(len(base))]


def replay(sessions, persona, make_model, caught, observe_truth=False):
    """Per episode: (V2 hit, model hit) at the decisions where V2's guess is usable; model hit = None when the model's
    top sector is not a route sector (guess_analysis.py would drop that decision)"""
    episodes = defaultdict(list)
    for s in sessions:
        if s["persona"] != persona:
            continue
        model = make_model()
        timeline = [(o["end"], 0, o) for o in s["outings"]] + [(d["t"], 1, d) for d in s["decisions"]]
        episode = None
        for _, kind, x in sorted(timeline, key=lambda z: (z[0], z[1])):
            if x["episode"] != episode:
                episode = x["episode"]
                model.reset()
            if kind == 0:
                sector, flag = x["sector"], caught(x)
                if observe_truth and chosen_route(x) is not None:
                    sector, flag = ROUTE_INDEX[chosen_route(x)], x["ambushes"] > 0
                model.observe(x["prediction"], sector, flag)
                continue
            p = x["p"]
            if max(p) - min(p) < 1e-9 or x["evidence"] < MIN_EVIDENCE:
                continue
            g0, g1 = top(p), top(model.apply(p))
            if g0 not in INDEX_ROUTE:
                continue
            hit1 = INDEX_ROUTE[g1] == x["route"] if g1 in INDEX_ROUTE else None
            episodes[(s["name"], episode)].append((INDEX_ROUTE[g0] == x["route"], hit1))
    return episodes


def paired(episodes, strict, B=2000, seed=1):
    """Paired accuracy difference with an episode bootstrap. strict: a model guess off the routes is a miss;
    otherwise that decision is dropped for both (guess_analysis.py's rule)"""
    clusters = []
    for v in episodes.values():
        pairs = [(a, bool(b)) for a, b in v if strict or b is not None]
        if pairs:
            clusters.append((sum(a for a, _ in pairs), sum(b for _, b in pairs), len(pairs)))
    n = sum(c[2] for c in clusters)
    if n == 0:
        return None
    rng = random.Random(seed)
    diffs = []
    for _ in range(B):
        k0 = k1 = m = 0
        for _ in range(len(clusters)):
            c = clusters[rng.randrange(len(clusters))]
            k0, k1, m = k0 + c[0], k1 + c[1], m + c[2]
        diffs.append((k1 - k0) / m)
    diffs.sort()
    return dict(n=n, v2=sum(c[0] for c in clusters) / n, model=sum(c[1] for c in clusters) / n,
                lo=diffs[int(0.025 * B)], hi=diffs[int(0.975 * B) - 1])


# ---------------------------------------------------------------- report

def pc(x):
    return "-" if x is None or x != x else f"{100 * x:.1f}%"


def table(header, lines, markdown):
    if markdown:
        ar.table(header, lines, True)
        return
    widths = [max(len(str(r[j])) for r in [header] + lines) for j in range(len(header))]
    for r in [header] + lines:
        print("  ".join(str(v).ljust(w) if j < 2 else str(v).rjust(w) for j, (v, w) in enumerate(zip(r, widths))).rstrip())


def candidates():
    out = [("old reaction model's met (6, sector ±1)", lambda o: o["met"])]
    out += [(f"nearest ≤ {d}", lambda o, d=d: o["min_nearest"] <= d) for d in (2, 3, 4, 5, 6)]
    out += [("damage > 0", lambda o: o["damage"] > 0)]
    out += [(f"damage > 0 or nearest ≤ {d}", lambda o, d=d: o["damage"] > 0 or o["min_nearest"] <= d) for d in (2, 3, 4, 5, 6)]
    return out


def agreement(outings, signal):
    tp = fp = fn = tn = 0
    for o in outings:
        p, t = signal(o), o["ambushes"] > 0
        tp += p and t
        fp += p and not t
        fn += t and not p
        tn += not p and not t
    n = tp + fp + fn + tn
    precision = tp / (tp + fp) if tp + fp else math.nan
    recall = tp / (tp + fn) if tp + fn else math.nan
    f1 = 2 * precision * recall / (precision + recall) if tp else 0.0
    return dict(n=n, fires=(tp + fp) / n, accuracy=(tp + tn) / n, precision=precision, recall=recall, f1=f1,
                cells=(tp, fp, fn, tn))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("batches", nargs="?", default=DEFAULT_BATCHES)
    parser.add_argument("--root", default=ar.DEFAULT_ROOT)
    parser.add_argument("--markdown", action="store_true")
    args = parser.parse_args()
    md = args.markdown
    h = "### " if md else ""

    sessions = load(args.root, args.batches)
    by_persona = defaultdict(list)
    for s in sessions:
        by_persona[s["persona"]] += s["outings"]
    print(f"{len(sessions)} sessions, batches {args.batches}")

    # 1. outing replay check
    print(f"\n{h}1. Rebuilt outings vs the commander's `outing` events\n")
    c = Counter()
    for s in sessions:
        if not s["logged"]:
            continue
        free = list(s["outings"])
        for event in s["logged"]:
            match = next((o for o in free if abs(o["end"] - event["t"]) < 0.25), None)
            c["logged"] += 1
            if match is None:
                continue
            free.remove(match)
            c["matched"] += 1
            c["same sector"] += SECTORS[match["sector"]] == event["sector"]
            c["same met"] += match["met"] == event["met"]
            c["same reason"] += match["reason"] == event["reason"]
        c["rebuilt, not logged"] += len(free)
    if c["logged"]:
        m = c["matched"]
        print(f"logged {c['logged']}, matched by end time {m} ({pc(m / c['logged'])}); of those: same sector "
              f"{pc(c['same sector'] / m)}, same met {pc(c['same met'] / m)}, same end reason {pc(c['same reason'] / m)}; "
              f"rebuilt but not logged {c['rebuilt, not logged']}")
    else:
        print("no `outing` events in these batches (logged since run 4)")

    # 2. calibration
    print(f"\n{h}2. Calibration of the caught signal (truth: bot_ambushed inside the outing; outbound leg)\n")
    lines = []
    best = None
    for persona in PERSONAS:
        outings = by_persona.get(persona, [])
        if not outings:
            continue
        truth = sum(o["ambushes"] > 0 for o in outings)
        lines.append([persona, f"caught (truth): {pc(truth / len(outings))} of {len(outings)} outings", "", "", "", "", "", ""])
        for name, signal in candidates():
            a = agreement(outings, signal)
            lines.append([persona, name, pc(a["fires"]), pc(a["accuracy"]), pc(a["precision"]),
                          pc(a["recall"]), f"{a['f1']:.3f}", "/".join(map(str, a["cells"]))])
            if persona == "Adaptive" and (best is None or a["f1"] > best[2]):
                best = (name, signal, a["f1"])
    table(["Persona", "Signal", "Fires", "Accuracy", "Precision", "Recall", "F1", "TP/FP/FN/TN"], lines, md)
    if best is None:
        return
    signal_name, signal, _ = best
    print(f"\nreplay signal: {signal_name} (best F1 on the Adaptive bot's outings)")

    # 3. observation check
    print(f"\n{h}3. What the commander observes\n")
    lines = []
    for persona in PERSONAS:
        outings = by_persona.get(persona, [])
        known = [o for o in outings if chosen_route(o) is not None]
        caught = [o for o in known if o["ambushes"] > 0]
        stays = defaultdict(lambda: [0, 0, 0.0])
        for s in sessions:
            if s["persona"] != persona:
                continue
            for a, b in zip(s["outings"], s["outings"][1:]):
                if a["episode"] != b["episode"] or b["prediction"] is None:
                    continue
                k = stays[signal(a)]
                k[0] += 1
                k[1] += a["sector"] == b["sector"]
                k[2] += b["prediction"][a["sector"]]
        cells = []
        for flag in (True, False):
            n, st, base = stays[flag]
            cells.append(f"{pc(st / n)} vs base {pc(base / n)} (n={n})" if n else "-")
        lines.append([persona, f"{pc(on_route_share(known))} (n={len(known)})",
                      f"{pc(on_route_share(caught))} (n={len(caught)})" if caught else "-"] + cells)
    table(["Persona", "outing sector = chosen route", "… on caught outings (truth)",
           f"next outing stays, after caught ({signal_name})", "… after clean"], lines, md)

    # 4. model replay
    print(f"\n{h}4. Model replay: top-1 guess at bot flee decisions (one-step counterfactual)\n")
    variants = [
        ("spec: κ 4", lambda: OutcomeModel("spec", 4.0), signal, False),
        ("spec: κ 4, γ 0.9", lambda: OutcomeModel("spec", 4.0, 0.9), signal, False),
        ("spec: κ 8", lambda: OutcomeModel("spec", 8.0), signal, False),
        ("relative: κ 4", lambda: OutcomeModel("relative", 4.0), signal, False),
        ("spec: κ 4, true caught", lambda: OutcomeModel("spec", 4.0), lambda o: o["ambushes"] > 0, False),
        ("spec: κ 4, true route + true caught", lambda: OutcomeModel("spec", 4.0), signal, True),
    ]
    lines = []
    for persona in PERSONAS:
        for name, make, caught_signal, truth in variants:
            episodes = replay(sessions, persona, make, caught_signal, truth)
            r, strict = paired(episodes, False), paired(episodes, True)
            if r is None:
                continue
            fmt = lambda x: f"{100 * (x['model'] - x['v2']):+.1f} [{100 * x['lo']:+.1f}, {100 * x['hi']:+.1f}]"
            lines.append([persona, name, str(r["n"]), pc(r["v2"]), pc(r["model"]), fmt(r),
                          str(strict["n"] - r["n"]), fmt(strict)])
    table(["Persona", "Model", "Decisions", "V2", "Model", "Diff, pp [95% CI]", "Off-route guesses",
           "Diff if those are misses"], lines, md)


if __name__ == "__main__":
    main()
