#!/usr/bin/env python3
"""Summarize ML-arena telemetry: what each bot persona did vs what the player profiler measured.

Usage:
  python3 .claude/tools/arena-report.py                 # latest batch of MLArena sessions
  python3 .claude/tools/arena-report.py DIR [DIR ...]   # specific session directories
  python3 .claude/tools/arena-report.py --markdown      # markdown tables (for reports)

A "batch" = all session folders written by one Play session (same yyyyMMdd_HHmmss prefix).
Ground truth comes from the bots' `bot_decision` events; the profiler's view comes from the
per-sample tracker labels (samples.csv) and the `profile` snapshots written at each episode end.

Label recall: for every bot decision (Fight / Flee / Kite), the share of the engaged tracker
samples in the following 3 s (game time) that carry the same label. It measures how well the
tracker recognizes each behaviour, independent of how often the persona chooses it.
"""
import csv
import glob
import json
import os
import statistics
import sys
from collections import Counter, defaultdict

DEFAULT_ROOT = os.path.expanduser(
    "~/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry")
RECALL_WINDOW = 3.0
STATES = ["Fight", "Flee", "Kite", "Passive"]


def latest_batch(root):
    dirs = sorted(d for d in glob.glob(os.path.join(root, "*_MLArena_*")) if os.path.isdir(d))
    if not dirs:
        sys.exit(f"no MLArena sessions under {root}")
    prefix = os.path.basename(dirs[-1])[:15]  # yyyyMMdd_HHmmss
    return [d for d in dirs if os.path.basename(d).startswith(prefix)]


def load_session(path):
    session = json.load(open(os.path.join(path, "session.json")))
    events = [json.loads(line) for line in open(os.path.join(path, "events.jsonl")) if line.strip()]
    with open(os.path.join(path, "samples.csv")) as fh:
        samples = list(csv.DictReader(fh))
    return session, events, samples


def profile_from_observation(obs, zones):
    k = len(zones)
    shares = dict(zip(STATES, obs[0:4]))
    zone_p = obs[8:8 + k]
    top = sorted(((p, zones[i]) for i, p in enumerate(zone_p) if p > 0), reverse=True)[:3]
    return {
        "shares": shares,
        "consistency": obs[4],
        "confidence": obs[7],
        "top": [(z, p) for p, z in top],
    }


def pct(x):
    return f"{x * 100:.0f}%"


def summarize(path):
    session, events, samples = load_session(path)
    zones = session["zones"]
    name = os.path.basename(path).split("_MLArena_")[-1]

    persona = next((e.get("persona", "?") for e in events if e["type"] == "episode_start"), "human")
    decisions = [e for e in events if e["type"] == "bot_decision"]
    waves = [e for e in events if e["type"] == "arena_wave_end"]
    episode_profiles = [e for e in events if e["type"] == "profile" and e.get("reason") == "episode_end"]
    ambushes = [e for e in events if e["type"] == "bot_ambushed"]
    episodes = max((e.get("episode", 0) for e in events if e["type"] == "episode_start"), default=0)

    # tracker label time over the whole session (engaged samples only)
    rows = []
    for r in samples:
        try:
            rows.append((float(r["t"]), r["state"]))
        except ValueError:
            pass
    engaged = [s for _, s in rows if s not in ("None", "")]
    label_time = Counter(engaged)
    total = sum(label_time[s] for s in STATES) or 1

    # label recall per decided mode
    times = [t for t, _ in rows]
    recall = defaultdict(list)
    import bisect
    for d in decisions:
        mode = d["mode"]
        i0 = bisect.bisect_right(times, d["t"])
        i1 = bisect.bisect_right(times, d["t"] + RECALL_WINDOW)
        window = [s for _, s in rows[i0:i1] if s not in ("None", "")]
        if window:
            recall[mode].append(sum(1 for s in window if s == mode) / len(window))

    per_episode = [profile_from_observation(p["observation"], zones) for p in episode_profiles]
    mean_shares = {s: statistics.mean(p["shares"][s] for p in per_episode) if per_episode else 0.0 for s in STATES}
    top_counter = Counter(p["top"][0][0] for p in per_episode if p["top"])

    return {
        "arena": name,
        "persona": persona,
        "episodes": episodes,
        "complete_episodes": len(per_episode),
        "waves": Counter(w["outcome"] for w in waves),
        "wave_duration": statistics.mean(w["duration"] for w in waves) if waves else 0.0,
        "decisions": Counter(d["mode"] for d in decisions),
        "routes": Counter(d["route"] for d in decisions if d["route"]),
        "ambushes": len(ambushes),
        "label_share": {s: label_time[s] / total for s in STATES},
        "episode_mean_share": mean_shares,
        "episode_top_zone": top_counter,
        "consistency": statistics.mean(p["consistency"] for p in per_episode) if per_episode else 0.0,
        "recall": {m: statistics.mean(v) for m, v in recall.items()},
        "recall_n": {m: len(v) for m, v in recall.items()},
        "samples": len(samples),
    }


def main(argv):
    markdown = "--markdown" in argv
    paths = [a for a in argv if not a.startswith("--")]
    if not paths:
        paths = latest_batch(DEFAULT_ROOT)
    results = [summarize(p) for p in sorted(paths)]

    def fmt_counter(c, keys=None):
        keys = keys or sorted(c)
        return " ".join(f"{k} {c[k]}" for k in keys if c.get(k))

    header = ["Arena", "Persona", "Episodes", "Waves (outcome)", "Bot decisions", "Routes", "Ambushes",
              "Tracker labels (whole session) F/Fl/K/P", "Episode profile mean F/Fl/K/P",
              "Top escape zone per episode", "Consistency", "Label recall (n)"]
    lines = []
    for r in results:
        labels = "/".join(pct(r["label_share"][s]) for s in STATES)
        ep = "/".join(pct(r["episode_mean_share"][s]) for s in STATES)
        recall = " ".join(f"{m} {pct(v)} ({r['recall_n'][m]})" for m, v in sorted(r["recall"].items()))
        lines.append([
            r["arena"], r["persona"], f"{r['complete_episodes']} done / {r['episodes']}",
            fmt_counter(r["waves"]) + f", avg {r['wave_duration']:.0f}s",
            fmt_counter(r["decisions"], ["Fight", "Flee", "Kite"]), fmt_counter(r["routes"], ["A", "B", "C"]) or "-",
            str(r["ambushes"]), labels, ep,
            fmt_counter(r["episode_top_zone"]) or "-", f"{r['consistency']:.2f}", recall or "-",
        ])

    if markdown:
        print("| " + " | ".join(header) + " |")
        print("|" + "---|" * len(header))
        for line in lines:
            print("| " + " | ".join(line) + " |")
    else:
        for line in lines:
            print("\n".join(f"{h:>42}: {v}" for h, v in zip(header, line)))
            print()


if __name__ == "__main__":
    main(sys.argv[1:])
