#!/usr/bin/env python3
"""Summarize ML-arena telemetry: what each bot persona did vs what the profiler measured, and how
the commander did against it.

Usage:
  python3 .claude/tools/arena-report.py                      # latest batch of MLArena* sessions
  python3 .claude/tools/arena-report.py DIR [DIR ...]        # specific session directories
  python3 .claude/tools/arena-report.py --batch 20260925_0707  # batch by timestamp prefix
  python3 .claude/tools/arena-report.py --compare BATCH_A BATCH_B   # commanders side by side
  (a batch can be several comma-separated prefixes, e.g. two repeats of one variant: they are pooled)
  add --markdown for markdown tables (reports)

A "batch" = all session folders written by one Play session (same yyyyMMdd_HHmmss prefix).

Profiler view (tables 1): ground truth from `bot_decision` events vs the tracker labels in
samples.csv and the per-episode `profile` snapshots. Label recall = share of the engaged samples in
the 3 s after each bot decision that carry the decided label.

Commander view (table 2, and --compare), per valid escape (flee/kite episode >= 1 s):
  intercepted     an enemy came within 2.5 units after the first 1 s of the escape (monitor field)
  pre-positioned  at flee onset an enemy was already in the escape's destination zone
                  (zone = exact; sector = same compass sector, either ring)
  Caveat: most escapes start out at a refuge with zombies already on the bot's heels, so these two
  mostly measure chasing. The per-decision metrics below are the cleaner route-anticipation test.
Per bot flee decision (ground truth route; the escape_start within 2 s supplies enemy positions):
  zombie on route  a zombie was in the chosen route's sector (either ring) when the bot decided,
                   counting only decisions made outside that sector (so chasers don't count)
  ambushed         the bot's own check fired on that flee: after 1 s it took damage or met a zombie
                   within 4 units ahead on its route (`bot_ambushed`); also split by wave in the episode
                   (the profile resets every episode, so a learning commander should improve over it)
  by an ambusher   ground truth (telemetry with `by_ambusher`): the zombie behind that ambush was holding
                   (HoldAt, a commander's ambush squad) rather than chasing / roaming; on damage it is the
                   nearest zombie within 2.5 units. Share of all flees, so it adds up to the ambushed rate
                   together with the ambushes by chasers; the cause row says which check fired (damage,
                   a zombie ahead on the route, a zombie already at the refuge)
  route predicted  plan §4 test: the route sector holding the most zombies (unique max) is the route
                   the bot takes; only decisions made outside all route sectors with some route
                   occupied count; chance = 1/3
  ambush on route  (V2 only, `prediction` events at flee onset) the commander had an ambush site with at
                   least one member in the chosen route's sector: the plan §4 wording ("has a squad at B
                   before flee onset"), whether or not the squad is standing there at that moment
Hold orders (RL / V2, `commander_order` events): share of hold-zone orders per route sector.
Per wave: player damage (100 - HP at the end, 100 if the player died) and deaths.
Intervals are 95% Wilson score intervals.
"""
import bisect
import csv
import glob
import json
import math
import os
import statistics
import sys
from collections import Counter, defaultdict

DEFAULT_ROOT = os.path.expanduser(
    "~/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry")
RECALL_WINDOW = 3.0
FLEE_ONSET_WINDOW = 2.0
# MLArena route layout (ArenaAssetBuilder): refuges east, north-west, south-west of the base
ROUTE_SECTOR = {"A": "E", "B": "NW", "C": "SW"}
SECTOR_ROUTE = {v: k for k, v in ROUTE_SECTOR.items()}
STATES = ["Fight", "Flee", "Kite", "Passive"]
CAUSES = ["Damage", "AheadOnRoute", "AtRefuge"]  # BotBrain.AmbushCause


# ---------------------------------------------------------------- loading

def batches(root):
    dirs = sorted(d for d in glob.glob(os.path.join(root, "*_MLArena*")) if os.path.isdir(d))
    grouped = defaultdict(list)
    for d in dirs:
        grouped[os.path.basename(d)[:15]].append(d)
    return grouped


def batch_dirs(root, prefix=None):
    """Session folders of the newest batch, or of the batches whose timestamp starts with prefix.
    Several prefixes can be joined with commas (e.g. two repeats of one variant): their sessions are pooled."""
    grouped = batches(root)
    if not grouped:
        sys.exit(f"no MLArena sessions under {root}")
    if prefix is None:
        return grouped[sorted(grouped)[-1]]
    dirs = []
    for p in prefix.split(","):
        matches = [k for k in grouped if k.startswith(p)]
        if not matches:
            sys.exit(f"no batch starting with {p}; batches: {', '.join(sorted(grouped))}")
        dirs += [d for k in sorted(matches) for d in grouped[k] if d not in dirs]
    return dirs


def first_type_wins(pairs):
    # Telemetry format 1 wrote escape_end with two "type" keys (event type, then escape type);
    # keep the event type and move the second one to escape_type
    d = {}
    for k, v in pairs:
        if k in d:
            if k == "type":
                d["escape_type"] = v
            continue
        d[k] = v
    return d


def load_session(path):
    session = json.load(open(os.path.join(path, "session.json")))
    events = [json.loads(line, object_pairs_hook=first_type_wins)
              for line in open(os.path.join(path, "events.jsonl")) if line.strip()]
    samples_path = os.path.join(path, "samples.csv")
    samples = []
    if os.path.exists(samples_path):
        with open(samples_path) as fh:
            samples = list(csv.DictReader(fh))
    return session, events, samples


# ---------------------------------------------------------------- stats helpers

def pct(x):
    return f"{x * 100:.0f}%"


def wilson(k, n, z=1.96):
    if n == 0:
        return (0.0, 0.0, 0.0)
    p = k / n
    denom = 1 + z * z / n
    center = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return (p, max(0.0, center - half), min(1.0, center + half))


def fmt_prop(k, n):
    if n == 0:
        return "-"
    p, lo, hi = wilson(k, n)
    return f"{pct(p)} [{pct(lo)}–{pct(hi)}] ({k}/{n})"


def sector(zone):
    return zone.rstrip("0123456789") if zone and zone != "Core" else zone


def profile_from_observation(obs, zones):
    k = len(zones)
    zone_p = obs[8:8 + k]
    top = sorted(((p, zones[i]) for i, p in enumerate(zone_p) if p > 0), reverse=True)[:3]
    return {"shares": dict(zip(STATES, obs[0:4])), "consistency": obs[4], "top": [(z, p) for p, z in top]}


# ---------------------------------------------------------------- per session

def summarize(path):
    session, events, samples = load_session(path)
    zones = session["zones"]
    name = os.path.basename(path).split("_MLArena", 1)[-1].lstrip("_")

    starts = [e for e in events if e["type"] == "episode_start"]
    persona = starts[0].get("persona", "human") if starts else "human"
    commander = starts[0].get("commander", "") if starts else ""
    decisions = [e for e in events if e["type"] == "bot_decision"]
    waves = [e for e in events if e["type"] == "arena_wave_end"]
    episode_profiles = [e for e in events if e["type"] == "profile" and e.get("reason") == "episode_end"]

    # escapes: join start (enemy zones at onset) and end (destination, interception)
    escape_start = {}
    escapes = []
    for e in events:
        if e["type"] == "escape_start":
            escape_start[e["id"]] = e
        elif e["type"] == "escape_end" and e.get("valid"):
            s = escape_start.get(e["id"], {})
            enemy_zones = s.get("enemy_zones")
            dest = e.get("end_zone", "")
            escapes.append({
                "dest": dest,
                "intercept_known": "intercepted" in e,  # telemetry format >= 2
                "intercepted": bool(e.get("intercepted")),
                "has_onset": enemy_zones is not None,
                "zone_hit": enemy_zones is not None and dest in enemy_zones,
                "sector_hit": enemy_zones is not None and dest != "Core"
                              and sector(dest) in {sector(z) for z in enemy_zones if z != "Core"},
            })

    # flee decisions: enemy positions at onset (next escape_start) and whether the bot was ambushed
    flees = []
    wave_in_episode = 0
    # the ground truth is known when the session's ambush events carry it (or there were none: then no flee
    # was ambushed, by an ambusher or otherwise); older telemetry: None for every flee, so it isn't pooled
    ambush_events = [e for e in events if e["type"] == "bot_ambushed"]
    has_truth = all("by_ambusher" in e for e in ambush_events)
    for i, d in enumerate(events):
        if d["type"] == "episode_start":
            wave_in_episode = 0
        elif d["type"] == "wave_start":
            wave_in_episode += 1
        if d["type"] != "bot_decision" or d.get("mode") != "Flee" or d.get("route") not in ROUTE_SECTOR:
            continue
        onset, ambushed, prediction = None, False, None
        by_ambusher, cause = False, None
        for g in events[i + 1:]:
            if g["type"] in ("bot_decision", "episode_start"):
                break
            if onset is None and g["type"] == "escape_start" and "enemy_zones" in g \
                    and g["t"] - d["t"] <= FLEE_ONSET_WINDOW:
                onset = g
            if prediction is None and g["type"] == "prediction" and g["t"] - d["t"] <= FLEE_ONSET_WINDOW:
                prediction = g
            if g["type"] == "bot_ambushed" and g.get("route") == d["route"]:
                ambushed = True
                by_ambusher = by_ambusher or bool(g.get("by_ambusher"))
                cause = cause or g.get("cause")
        if onset is None:
            continue
        route_sector = ROUTE_SECTOR[d["route"]]
        bot_sector = sector(d.get("zone", ""))
        counts = {r: sum(1 for z in onset["enemy_zones"] if sector(z) == sec) for r, sec in ROUTE_SECTOR.items()}
        best = max(counts.values())
        flees.append({
            "route": d["route"],
            "wave": wave_in_episode,
            "ambushed": ambushed,
            "by_ambusher": by_ambusher if has_truth else None,
            "cause": cause,  # Damage / AheadOnRoute / AtRefuge (None: not ambushed, or older telemetry)
            "on_route": counts[d["route"]] > 0 if bot_sector != route_sector else None,
            # top-1 prediction: the route sector with the most zombies is the one the bot takes
            # (only when the bot is outside all route sectors and some route has zombies; chance 1/3)
            "predicted": (counts[d["route"]] == best and list(counts.values()).count(best) == 1)
                         if bot_sector not in SECTOR_ROUTE and best > 0 else None,
            "ambush_on_route": (any(sector(a.split(":")[0]) == route_sector and int(a.split(":")[1]) > 0
                                    for a in prediction.get("ambush", []))
                                if prediction is not None and bot_sector != route_sector else None),
        })
    holds = Counter(SECTOR_ROUTE.get(sector(e.get("zone", "")), "other") for e in events
                    if e["type"] == "commander_order" and e.get("order") == "HoldAt")

    # tracker labels
    rows = []
    for r in samples:
        try:
            rows.append((float(r["t"]), r["state"]))
        except (ValueError, KeyError):
            pass
    engaged = [s for _, s in rows if s not in ("None", "")]
    label_time = Counter(engaged)
    total = sum(label_time[s] for s in STATES) or 1
    times = [t for t, _ in rows]
    recall = defaultdict(list)
    for d in decisions:
        i0 = bisect.bisect_right(times, d["t"])
        i1 = bisect.bisect_right(times, d["t"] + RECALL_WINDOW)
        window = [s for _, s in rows[i0:i1] if s not in ("None", "")]
        if window:
            recall[d["mode"]].append(sum(1 for s in window if s == d["mode"]) / len(window))

    per_episode = [profile_from_observation(p["observation"], zones) for p in episode_profiles]
    damage = [100.0 if w["outcome"] == "player_died" else max(0.0, 100.0 - w.get("player_hp", 100.0)) for w in waves]

    return {
        "arena": name,
        "persona": persona,
        "commander": commander,
        "episodes": len(per_episode),
        "waves": Counter(w["outcome"] for w in waves),
        "wave_count": len(waves),
        "wave_duration": statistics.mean(w["duration"] for w in waves) if waves else 0.0,
        "deaths": sum(1 for w in waves if w["outcome"] == "player_died"),
        "damage_per_wave": statistics.mean(damage) if damage else 0.0,
        "decisions": Counter(d["mode"] for d in decisions),
        "routes": Counter(d["route"] for d in decisions if d["route"]),
        "label_share": {s: label_time[s] / total for s in STATES},
        "episode_mean_share": {s: statistics.mean(p["shares"][s] for p in per_episode) if per_episode else 0.0 for s in STATES},
        "episode_top_zone": Counter(p["top"][0][0] for p in per_episode if p["top"]),
        "consistency": statistics.mean(p["consistency"] for p in per_episode) if per_episode else 0.0,
        "recall": {m: statistics.mean(v) for m, v in recall.items()},
        "recall_n": {m: len(v) for m, v in recall.items()},
        "escapes": escapes,
        "flees": flees,
        "holds": holds,
    }


def fmt_counter(c, keys=None):
    keys = keys or sorted(c)
    return " ".join(f"{k} {c[k]}" for k in keys if c.get(k))


def flee_stats(flees):
    known = [f for f in flees if f["on_route"] is not None]
    guessed = [f for f in flees if f["predicted"] is not None]
    planned = [f for f in flees if f.get("ambush_on_route") is not None]
    truth = [f for f in flees if f.get("by_ambusher") is not None]
    return {
        "ambush_n": len(planned),
        "ambush_on_route": sum(f["ambush_on_route"] for f in planned),
        "n": len(flees),
        "ambushed": sum(f["ambushed"] for f in flees),
        "on_route_n": len(known),
        "on_route": sum(f["on_route"] for f in known),
        "predicted_n": len(guessed),
        "predicted": sum(f["predicted"] for f in guessed),
        "truth_n": len(truth),
        "by_ambusher": sum(f["by_ambusher"] for f in truth),
        "by_chaser": sum(f["ambushed"] and not f["by_ambusher"] for f in truth),
        "causes": Counter(f["cause"] for f in truth if f["cause"]),
    }


def ambush_by_wave(flees):
    waves = sorted({f["wave"] for f in flees if f["wave"] > 0})
    return " ".join(f"w{w} {pct(sum(f['ambushed'] for f in flees if f['wave'] == w) / max(1, sum(1 for f in flees if f['wave'] == w)))}"
                    f"/{sum(1 for f in flees if f['wave'] == w)}" for w in waves) or "-"


def fmt_holds(holds):
    total = sum(holds.values())
    if not total:
        return "-"
    return " ".join(f"{k} {pct(holds[k] / total)}" for k in ("A", "B", "C", "other")) + f" ({total})"


def escape_stats(escapes):
    n = len(escapes)
    known = [e for e in escapes if e["intercept_known"]]
    onset = [e for e in escapes if e["has_onset"]]
    return {
        "n": n,
        "intercept_n": len(known),
        "intercepted": sum(e["intercepted"] for e in known),
        "onset_n": len(onset),
        "zone_hit": sum(e["zone_hit"] for e in onset),
        "sector_hit": sum(e["sector_hit"] for e in onset),
    }


# ---------------------------------------------------------------- output

def table(header, lines, markdown):
    if markdown:
        print("| " + " | ".join(header) + " |")
        print("|" + "---|" * len(header))
        for line in lines:
            print("| " + " | ".join(line) + " |")
    else:
        for line in lines:
            print("\n".join(f"{h:>44}: {v}" for h, v in zip(header, line)))
            print()


def report(results, markdown):
    header = ["Arena", "Persona", "Commander", "Episodes", "Waves (outcome)", "Bot decisions", "Routes",
              "Tracker labels F/Fl/K/P", "Episode profile F/Fl/K/P", "Top zone per episode", "Consistency",
              "Label recall (n)"]
    lines = []
    for r in results:
        recall = " ".join(f"{m} {pct(v)} ({r['recall_n'][m]})" for m, v in sorted(r["recall"].items()))
        lines.append([
            r["arena"], r["persona"], r["commander"], str(r["episodes"]),
            fmt_counter(r["waves"]) + f", avg {r['wave_duration']:.0f}s",
            fmt_counter(r["decisions"], ["Fight", "Flee", "Kite"]) or "-", fmt_counter(r["routes"], ["A", "B", "C"]) or "-",
            "/".join(pct(r["label_share"][s]) for s in STATES),
            "/".join(pct(r["episode_mean_share"][s]) for s in STATES),
            fmt_counter(r["episode_top_zone"]) or "-", f"{r['consistency']:.2f}", recall or "-",
        ])
    table(header, lines, markdown)

    header = ["Arena", "Persona", "Commander", "Valid escapes", "Intercepted", "Pre-positioned (zone)",
              "Pre-positioned (sector)", "Player damage / wave", "Deaths / waves"]
    lines = []
    for r in results:
        st = escape_stats(r["escapes"])
        lines.append([
            r["arena"], r["persona"], r["commander"], str(st["n"]),
            fmt_prop(st["intercepted"], st["intercept_n"]), fmt_prop(st["zone_hit"], st["onset_n"]),
            fmt_prop(st["sector_hit"], st["onset_n"]),
            f"{r['damage_per_wave']:.0f}", f"{r['deaths']}/{r['wave_count']}",
        ])
    table(header, lines, markdown)


def compare(a, b, markdown):
    """Pool sessions by persona and put two batches (e.g. Baseline vs RL) side by side."""
    def by_persona(results):
        grouped = defaultdict(list)
        for r in results:
            grouped[r["persona"]].append(r)
        return grouped

    ga, gb = by_persona(a), by_persona(b)
    name_a = a[0]["commander"] if a else "A"
    name_b = b[0]["commander"] if b else "B"
    header = ["Persona", "Metric", name_a, name_b]
    lines = []
    for persona in sorted(set(ga) | set(gb)):
        ra, rb = ga.get(persona, []), gb.get(persona, [])
        ea = escape_stats([e for r in ra for e in r["escapes"]])
        eb = escape_stats([e for r in rb for e in r["escapes"]])
        waves_a = sum(r["wave_count"] for r in ra)
        waves_b = sum(r["wave_count"] for r in rb)
        dmg_a = sum(r["damage_per_wave"] * r["wave_count"] for r in ra) / max(1, waves_a)
        dmg_b = sum(r["damage_per_wave"] * r["wave_count"] for r in rb) / max(1, waves_b)
        lines += [
            [persona, "escapes intercepted", fmt_prop(ea["intercepted"], ea["intercept_n"]), fmt_prop(eb["intercepted"], eb["intercept_n"])],
            [persona, "pre-positioned (zone)", fmt_prop(ea["zone_hit"], ea["onset_n"]), fmt_prop(eb["zone_hit"], eb["onset_n"])],
            [persona, "pre-positioned (sector)", fmt_prop(ea["sector_hit"], ea["onset_n"]), fmt_prop(eb["sector_hit"], eb["onset_n"])],
        ]
        fa = flee_stats([f for r in ra for f in r["flees"]])
        fb = flee_stats([f for r in rb for f in r["flees"]])
        routes_a = Counter(f["route"] for r in ra for f in r["flees"])
        routes_b = Counter(f["route"] for r in rb for f in r["flees"])
        if fa["n"] or fb["n"]:
            lines += [
                [persona, "flee routes (bot)", fmt_counter(routes_a, ["A", "B", "C"]) or "-", fmt_counter(routes_b, ["A", "B", "C"]) or "-"],
                [persona, "flee: zombie on route", fmt_prop(fa["on_route"], fa["on_route_n"]), fmt_prop(fb["on_route"], fb["on_route_n"])],
                [persona, "flee: ambushed", fmt_prop(fa["ambushed"], fa["n"]), fmt_prop(fb["ambushed"], fb["n"])],
                [persona, "flee: ambushed by wave in episode", ambush_by_wave([f for r in ra for f in r["flees"]]),
                 ambush_by_wave([f for r in rb for f in r["flees"]])],
            ]
            if fa["truth_n"] or fb["truth_n"]:
                lines += [
                    [persona, "flee: ambushed by an ambusher (holding)", fmt_prop(fa["by_ambusher"], fa["truth_n"]),
                     fmt_prop(fb["by_ambusher"], fb["truth_n"])],
                    [persona, "flee: ambushed by a chaser", fmt_prop(fa["by_chaser"], fa["truth_n"]),
                     fmt_prop(fb["by_chaser"], fb["truth_n"])],
                    [persona, "flee: ambush cause", fmt_counter(fa["causes"], CAUSES) or "-",
                     fmt_counter(fb["causes"], CAUSES) or "-"],
                ]
            lines += [
                [persona, "flee: route predicted (top-1, chance 33%)", fmt_prop(fa["predicted"], fa["predicted_n"]),
                 fmt_prop(fb["predicted"], fb["predicted_n"])],
            ]
            if fa["ambush_n"] or fb["ambush_n"]:
                lines.append([persona, "flee: ambush on route (V2)", fmt_prop(fa["ambush_on_route"], fa["ambush_n"]),
                              fmt_prop(fb["ambush_on_route"], fb["ambush_n"])])
        holds_a = sum((r["holds"] for r in ra), Counter())
        holds_b = sum((r["holds"] for r in rb), Counter())
        if holds_a or holds_b:
            lines.append([persona, "hold orders by route", fmt_holds(holds_a), fmt_holds(holds_b)])
        lines += [
            [persona, "player damage / wave", f"{dmg_a:.0f} ({waves_a} waves)", f"{dmg_b:.0f} ({waves_b} waves)"],
            [persona, "deaths / waves", f"{sum(r['deaths'] for r in ra)}/{waves_a}", f"{sum(r['deaths'] for r in rb)}/{waves_b}"],
        ]
    table(header, lines, markdown)


def main(argv):
    markdown = "--markdown" in argv
    args = [a for a in argv if a != "--markdown"]
    if args[:1] == ["--compare"]:
        if len(args) != 3:
            sys.exit("usage: --compare BATCH_A BATCH_B")
        a = [summarize(p) for p in sorted(batch_dirs(DEFAULT_ROOT, args[1]))]
        b = [summarize(p) for p in sorted(batch_dirs(DEFAULT_ROOT, args[2]))]
        compare(a, b, markdown)
        return
    if args[:1] == ["--batch"]:
        paths = batch_dirs(DEFAULT_ROOT, args[1])
    else:
        paths = [a for a in args if not a.startswith("--")] or batch_dirs(DEFAULT_ROOT)
    report([summarize(p) for p in sorted(paths)], markdown)


if __name__ == "__main__":
    main(sys.argv[1:])
