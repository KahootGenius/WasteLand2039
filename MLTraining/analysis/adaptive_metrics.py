#!/usr/bin/env python3
"""Adaptive horde metrics (build guide §2.5) for MLArena eval batches, one column per variant.

Usage:
  python3 MLTraining/analysis/adaptive_metrics.py [--markdown] BATCHES.tsv
  python3 MLTraining/analysis/adaptive_metrics.py [--markdown] NAME=PREFIX[,PREFIX...] [NAME=...]
BATCHES.tsv is what MLTraining/eval_adaptive.sh writes (variant <tab> batch prefixes, comma-separated <tab> flags).
A variant with several batches (repeats) is pooled in the main table; the repeat table shows each batch on its own,
which is the run-to-run noise.

Per persona and variant:
  trials                     valid escapes the commander recorded (`ambush_trial`, one per valid escape)
  ambush on route (onset)    a site with a member in the bot's chosen route sector at flee onset (arena-report)
  bot ambushed               the bot's own check (`bot_ambushed`: zombie within 4 ahead or damage), as in arena-report
  contact on the ran route   a site in the sector the player actually ran to had a member within the contact radius,
                             1 s or more after flee onset (what the bandit learns from; the radius is in the batch:
                             2.5 before 2026-10-07, 6 since)
  closest on-route ambusher  distribution of that site's closest distance: ≤2.5 / 2.5–4 / 4–6 / 6–10 / >10 / never
  chaser contact             a pressure-squad member came within the same radius
  contacts by                of escapes with any contact: ambushers only / both / chasers only
  route contact by wave      "contact on the ran route" by wave in the episode (profile and bandit reset each episode)
  hold orders per escape     HoldAt orders / trials: how often ambushers are sent somewhere else (churn)
  re-decisions               share of escapes after which the commander re-chose its sites (`redecided`, since 2026-10-07)
  route entropy (H2)         entropy (bits) of the bot's flee routes A/B/C per episode, mean ± sd; max log2(3) = 1.58
  damage / deaths            pressure check: should stay near V2
Bandit variants also get: contact rate per arm (route × distance, pooled over episodes and repeats) and where sites were
held at flee onset (`prediction.ambush_sites`).
Intervals are 95% Wilson score intervals (from arena-report).
"""
import importlib.util
import math
import os
import statistics
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location(
    "arena_report", os.path.join(HERE, "..", "..", ".claude", "tools", "arena-report.py"))
ar = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ar)

ROUTES = ["A", "B", "C"]
DISTANCE_BINS = [(2.5, "≤2.5"), (4.0, "2.5–4"), (6.0, "4–6"), (10.0, "6–10"), (math.inf, ">10")]
NOISE_PERSONAS = ["RunnerA", "RunnerB80", "Adaptive"]


def parse_batches(args):
    if len(args) == 1 and args[0].endswith(".tsv"):
        rows = [line.rstrip("\n").split("\t") for line in open(args[0]) if line.strip()]
        return [(r[0], r[1]) for r in rows[1:]]
    return [tuple(a.split("=", 1)) for a in args]


def site_sector(site):
    # "E@25" -> ("E", 25.0)
    sector, _, distance = site.partition("@")
    return sector, float(distance) if distance else None


def on_route_min(trial):
    """Closest distance of a site in the ran sector; None = no such site, inf = no member ever came near."""
    if "on_route_min_distance" in trial:
        d = trial["on_route_min_distance"]
        if d is None:   # written as null: either no on-route site or no member ever approached
            return math.inf if any(":on_route:" in s for s in trial.get("sites", [])) else None
        return d
    # batches before 2026-10-07: parse "E@25:on_route:8.95" strings
    values = [s.rsplit(":", 1)[1] for s in trial.get("sites", []) if ":on_route:" in s]
    if not values:
        return None
    return min(math.inf if v == "null" else float(v) for v in values)


def entropy(counts):
    n = sum(counts.values())
    if n == 0:
        return None
    return -sum(c / n * math.log2(c / n) for c in counts.values() if c)


def new_metrics():
    return {
        "trials": 0, "ambush": 0, "on_route": 0, "chaser": 0, "redecided": 0, "redecided_n": 0, "holds": 0,
        "split": Counter(), "closest": Counter(),
        "by_wave": defaultdict(lambda: [0, 0]),
        "arms": defaultdict(lambda: [0, 0]),
        "held": Counter(),
        "episode_routes": [],
        "radius": set(),
    }


def session_metrics(path):
    _, events, _ = ar.load_session(path)
    starts = [e for e in events if e["type"] == "episode_start"]
    m = new_metrics()
    m["persona"] = starts[0].get("persona", "human") if starts else "human"
    wave = 0
    routes = Counter()
    for e in events:
        t = e["type"]
        if t == "episode_start":
            if routes:
                m["episode_routes"].append(routes)
            routes = Counter()
            wave = 0
        elif t == "wave_start":
            wave += 1
        elif t == "bot_decision" and e.get("mode") == "Flee" and e.get("route") in ROUTES:
            routes[e["route"]] += 1
        elif t == "commander_order" and e.get("order") == "HoldAt":
            m["holds"] += 1
        elif t == "ambush_trial":
            m["trials"] += 1
            m["radius"].add(e.get("contact_radius", 2.5))
            ambush, on_route, chaser = e["ambush_contact"], e["ambush_contact_on_route"], e["pressure_contact"]
            m["ambush"] += ambush
            m["on_route"] += on_route
            m["chaser"] += chaser
            if ambush or chaser:
                m["split"]["ambushers only" if not chaser else "chasers only" if not ambush else "both"] += 1
            if "redecided" in e:
                m["redecided"] += bool(e["redecided"])
                m["redecided_n"] += 1
            closest = on_route_min(e)
            if closest is not None:
                m["closest"][next(label for limit, label in DISTANCE_BINS if closest <= limit) if closest != math.inf else "never"] += 1
            w = m["by_wave"][wave]
            w[0] += on_route
            w[1] += 1
        elif t == "bandit_update":
            arm = m["arms"][(ar.SECTOR_ROUTE.get(e["sector"], e["sector"]), float(e["distance"]))]
            arm[0] += bool(e["contact"])
            arm[1] += 1
        elif t == "prediction":
            for site in e.get("ambush_sites", []):
                sector, distance = site_sector(site)
                m["held"][(ar.SECTOR_ROUTE.get(sector, sector), distance)] += 1
    if routes:
        m["episode_routes"].append(routes)
    return m


def merge(sessions):
    total = new_metrics()
    for s in sessions:
        for k in ("trials", "ambush", "on_route", "chaser", "redecided", "redecided_n", "holds"):
            total[k] += s[k]
        for k in ("split", "held", "closest"):
            total[k] += s[k]
        total["episode_routes"] += s["episode_routes"]
        total["radius"] |= s["radius"]
        for w, (k, n) in s["by_wave"].items():
            total["by_wave"][w][0] += k
            total["by_wave"][w][1] += n
        for a, (k, n) in s["arms"].items():
            total["arms"][a][0] += k
            total["arms"][a][1] += n
    return total


def fmt_split(split):
    n = sum(split.values())
    if n == 0:
        return "-"
    return " / ".join(f"{ar.pct(split[k] / n)}" for k in ("ambushers only", "both", "chasers only")) + f" (n={n})"


def fmt_closest(closest):
    n = sum(closest.values())
    if n == 0:
        return "-"
    labels = [label for _, label in DISTANCE_BINS] + ["never"]
    return " / ".join(ar.pct(closest[label] / n) for label in labels) + f" (n={n})"


def fmt_entropy(episode_routes):
    values = [entropy(r) for r in episode_routes if sum(r.values()) >= 2]
    if not values:
        return "-"
    sd = statistics.stdev(values) if len(values) > 1 else 0.0
    return f"{statistics.mean(values):.2f} ± {sd:.2f} ({len(values)} episodes)"


def fmt_by_wave(by_wave):
    return " ".join(f"w{w} {ar.pct(k / n)}" for w, (k, n) in sorted(by_wave.items()) if n and w > 0) or "-"


def flee(reports, key):
    f = ar.flee_stats([x for s in reports for x in s["flees"]])
    count_key = "ambush_n" if key == "ambush_on_route" else "n"
    return ar.fmt_prop(f[key], f[count_key]) if f[count_key] else "-"


def damage(reports):
    waves = sum(s["wave_count"] for s in reports)
    if not waves:
        return "-"
    dmg = sum(s["damage_per_wave"] * s["wave_count"] for s in reports) / waves
    return f"{dmg:.0f} / {sum(s['deaths'] for s in reports)} of {waves}"


def load(prefix):
    """persona -> (merged metrics, arena-report summaries) for one or more comma-separated batch prefixes"""
    per_persona = defaultdict(list)
    reports = defaultdict(list)
    for p in sorted(ar.batch_dirs(ar.DEFAULT_ROOT, prefix)):
        s = session_metrics(p)
        per_persona[s["persona"]].append(s)
        reports[s["persona"]].append(ar.summarize(p))
    return {k: merge(v) for k, v in per_persona.items()}, reports


def main(argv):
    markdown = "--markdown" in argv
    batches = parse_batches([a for a in argv if a != "--markdown"])
    if not batches:
        sys.exit(__doc__)

    data, summary = {}, {}
    for name, prefix in batches:
        data[name], summary[name] = load(prefix)

    names = [n for n, _ in batches]
    personas = sorted({p for n in names for p in data[n]})
    lines = []
    for persona in personas:
        def row(metric, fn):
            lines.append([persona, metric] + [fn(data[n].get(persona), summary[n].get(persona, [])) for n in names])

        row("trials (valid escapes)", lambda d, r: str(d["trials"]) if d else "-")
        row("ambush on route at flee onset", lambda d, r: flee(r, "ambush_on_route"))
        row("bot ambushed (flee decisions)", lambda d, r: flee(r, "ambushed"))
        row("contact radius", lambda d, r: "/".join(f"{x:g}" for x in sorted(d["radius"])) if d and d["radius"] else "-")
        row("contact on the ran route", lambda d, r: ar.fmt_prop(d["on_route"], d["trials"]) if d else "-")
        row("closest on-route ambusher ≤2.5/–4/–6/–10/>10/never",
            lambda d, r: fmt_closest(d["closest"]) if d else "-")
        row("ambush contact (any site)", lambda d, r: ar.fmt_prop(d["ambush"], d["trials"]) if d else "-")
        row("chaser contact", lambda d, r: ar.fmt_prop(d["chaser"], d["trials"]) if d else "-")
        row("contacts by ambushers / both / chasers", lambda d, r: fmt_split(d["split"]) if d else "-")
        row("route contact by wave in episode", lambda d, r: fmt_by_wave(d["by_wave"]) if d else "-")
        row("hold orders per escape", lambda d, r: f"{d['holds'] / d['trials']:.2f}" if d and d["trials"] else "-")
        row("re-decisions", lambda d, r: ar.fmt_prop(d["redecided"], d["redecided_n"]) if d and d["redecided_n"] else "-")
        row("route entropy per episode (H2, bits)", lambda d, r: fmt_entropy(d["episode_routes"]) if d else "-")
        row("damage per wave / deaths", lambda d, r: damage(r))
    ar.table(["Persona", "Metric"] + names, lines, markdown)

    # Run-to-run noise: each repeat on its own, for the metrics the hypotheses are about
    repeated = [(n, p.split(",")) for n, p in batches if "," in p]
    if repeated:
        print()
        print(f"{'### ' if markdown else ''}Repeats (each batch on its own: the run-to-run noise)")
        print()
        noise = []
        per_batch = {(n, b): load(b) for n, prefixes in repeated for b in prefixes}
        for persona in NOISE_PERSONAS:
            for metric, fn in [
                ("ambush on route at flee onset", lambda d, r: flee(r, "ambush_on_route")),
                ("bot ambushed", lambda d, r: flee(r, "ambushed")),
                ("contact on the ran route", lambda d, r: ar.fmt_prop(d["on_route"], d["trials"]) if d else "-"),
            ]:
                cells = []
                for n, prefixes in repeated:
                    values = []
                    for b in prefixes:
                        d, r = per_batch[(n, b)]
                        values.append(fn(d.get(persona), r.get(persona, [])).split(" [")[0])
                    cells.append(" / ".join(values))
                noise.append([persona, metric] + cells)
        ar.table(["Persona", "Metric"] + [n for n, _ in repeated], noise, markdown)

    for name in names:
        merged = merge(list(data[name].values()))
        if not merged["arms"]:
            continue
        print()
        print(f"{'### ' if markdown else ''}Bandit arms, {name} (all personas, pooled over episodes and repeats)")
        print()
        arm_lines = []
        for (route, distance), (k, n) in sorted(merged["arms"].items(), key=lambda kv: (str(kv[0][0]), kv[0][1])):
            held = merged["held"].get((route, distance), 0)
            arm_lines.append([str(route), f"{distance:g}", str(n), ar.fmt_prop(k, n), str(held)])
        ar.table(["Route", "Distance", "Updates", "Contact rate", "Held at flee onset"], arm_lines, markdown)


if __name__ == "__main__":
    main(sys.argv[1:])
