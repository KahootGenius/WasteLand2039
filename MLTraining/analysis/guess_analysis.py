#!/usr/bin/env python3
"""Commander's own top-1 route guess per bot flee decision vs the bot's actual route, its baselines, the
Adaptive bot's lose-shift, within-episode dynamics, and the bot's own (replayed) probability of the guessed route.

Usage: python3 -I guess_analysis.py TELEMETRY_ROOT OUT_CSV

Decision set: bot_decision mode == "Flee", route in A/B/C, with a flee onset (first escape_start carrying
enemy_zones within 2 s, before the next bot_decision / episode_start) -- the arena-report rule -- and the
prediction event logged at that same escape start (prediction.escape == onset.id).
Guess definitions (p = prediction.sector_probabilities, order E NE N NW W SW S SE; A=E, B=NW, C=SW):
  TOP   EscapeSectors.Top(p) (first strictly greater, skip p<=0), the sector the Argmax commander targets;
        usable iff p is not uniform, the logged evidence >= 2 (the commander's own gate), and Top is E/NW/SW.
  TOPNG same without the evidence gate.
  RSTR  argmax over p[E], p[NW], p[SW] with a unique max, gate open.
  DRAWN (Thompson variants) drawn_direction mapped to a route, gate open.
  BASE  (Reaction variants) Top of base_probabilities (before the reaction adjustment), gate open.
"""
import glob
import json
import math
import os
import random
import sys
from collections import Counter, defaultdict

root, out_csv = sys.argv[1], sys.argv[2]

RS = {"A": "E", "B": "NW", "C": "SW"}
SR = {v: k for k, v in RS.items()}
NAMES = ["E", "NE", "N", "NW", "W", "SW", "S", "SE"]
RIDX = {"A": 0, "B": 3, "C": 5}
ROUTES = "ABC"
WINDOW = 2.0
MIN_EVIDENCE = 2.0
PEN, REC = 0.2, 0.05

SETS = [
    ("run1", "V2", ["20261006_193700"]),
    ("run2", "V2", ["20261007_031841", "20261007_032916"]),
    ("run3", "V2", ["20261007_050455", "20261007_051503"]),
    ("run4", "V2", ["20261008_022013", "20261008_022317", "20261008_022626", "20261008_023015",
                    "20261008_023333", "20261008_023643"]),
    ("run2", "TS", ["20261007_031943", "20261007_033023"]),
    ("run2", "Bandit", ["20261007_032050", "20261007_033138"]),
    ("run3", "TS", ["20261007_050555", "20261007_051608"]),
    ("run3", "Bandit", ["20261007_05065", "20261007_051729"]),
    ("run4", "Reaction", ["20261008_022116", "20261008_022417", "20261008_022736", "20261008_023116",
                          "20261008_023437", "20261008_023753"]),
    ("run4", "Reaction-p0.8", ["20261008_022214", "20261008_022517", "20261008_022906", "20261008_023220",
                               "20261008_023537", "20261008_023900"]),
]
PERSONAS = ["Adaptive", "RunnerA", "RunnerB80", "Random"]


def sector(z):
    return z.rstrip("0123456789") if z and z != "Core" else z


def ftw(pairs):
    d = {}
    for k, v in pairs:
        if k in d:
            if k == "type":
                d["escape_type"] = v
            continue
        d[k] = v
    return d


def top_cs(p):
    best = -1
    for s, v in enumerate(p):
        if v <= 0:
            continue
        if best < 0 or v > p[best]:
            best = s
    return best


def norm(w):
    s = sum(w)
    return [x / s for x in w] if s > 0 else [1 / len(w)] * len(w)


def weights_after(base, pref, k):
    f = (1 - REC) ** k
    return [p + f * (b - p) for b, p in zip(base, pref)]


def grouped_dirs(prefixes):
    groups = defaultdict(list)
    for d in sorted(glob.glob(os.path.join(root, "*_MLArena*"))):
        if os.path.isdir(d):
            groups[os.path.basename(d)[:15]].append(d)
    out = []
    for p in prefixes:
        ks = [k for k in groups if k.startswith(p)]
        if not ks:
            sys.exit("no batch " + p)
        for k in sorted(ks):
            out += [d for d in groups[k] if d not in out]
    return out


VALID = Counter()


def process(path, run, variant):
    ev = [json.loads(l, object_pairs_hook=ftw) for l in open(os.path.join(path, "events.jsonl")) if l.strip()]
    persona = next((e.get("persona") for e in ev if e["type"] == "episode_start"), None)
    if persona not in PERSONAS:
        return persona, []
    sess = os.path.basename(path)
    recs = []
    ep = 0
    wave_in_ep = 0
    pref = base = None
    kdef = kamb = 0
    adapt = False
    pending = None          # open flee/kite: dict with "amb"
    flee_ep = flee_wave = 0
    for i, e in enumerate(ev):
        t = e["type"]
        if t == "episode_start":
            ep = e.get("episode", ep + 1)
            wave_in_ep = 0
            pref = list(e.get("route_weights") or [1 / 3] * 3)
            base = list(pref)
            kdef = kamb = 0
            adapt = bool(e.get("adapt_routes"))
            pending = None
            flee_ep = 0
        elif t in ("wave_start", "arena_wave_end"):
            if pending is not None:
                if not pending["amb"] and pending["routed"] and adapt:
                    kamb += 1
                if pending.get("rec") is not None and pending["rec"]["end"] is None:
                    pending["rec"]["end"] = "wave_end"
            pending = None
            if t == "wave_start":
                wave_in_ep += 1
                flee_wave = 0
        elif t == "bot_ambushed":
            logged = list(e["route_weights"])
            if adapt:
                r = ROUTES.index(e["route"])
                errs = []
                for k in range(kdef, kdef + kamb + 1):
                    w = list(weights_after(base, pref, k))
                    w[r] *= PEN
                    w = norm(w)
                    errs.append(max(abs(a - b) for a, b in zip(w, logged)))
                VALID["ambush events checked"] += 1
                VALID["consistent (<=0.0025)"] += min(errs) <= 0.0025
                VALID["max err"] = max(VALID["max err"], min(errs))
            base = logged
            kdef = kamb = 0
            if pending is not None and not pending["amb"]:
                pending["amb"] = True
                rc = pending.get("rec")
                if rc is not None:
                    rc["amb"] = True
                    rc["amb_cause"] = e.get("cause", "")
                    rc["amb_order"] = e.get("enemy_order", "")
                    rc["amb_by"] = e.get("by_ambusher")
                    rc["amb_dt"] = e["t"] - rc["t"]
                    if e["route"] != rc["route"]:
                        VALID["ambush route != flee route"] += 1
            else:
                VALID["ambush without open flee"] += 1
        elif t == "bot_decision":
            if pending is not None and not pending["amb"] and pending["routed"]:
                if adapt:
                    kdef += 1
                if pending.get("rec") is not None:
                    pending["rec"]["end"] = "safe"
            elif pending is not None and pending.get("rec") is not None:
                pending["rec"]["end"] = "safe"   # ambushed, but reached safety later
            mode, route = e.get("mode"), e.get("route")
            if mode == "Fight" or route not in RS:
                pending = None
                continue
            if mode != "Flee":
                pending = {"amb": False, "routed": True, "rec": None}
                continue
            flee_ep += 1
            flee_wave += 1
            wL = weights_after(base, pref, kdef)
            wU = weights_after(base, pref, kdef + kamb)
            onset = pred = None
            preds = []
            for g in ev[i + 1:]:
                if g["type"] in ("bot_decision", "episode_start"):
                    break
                if g["t"] - e["t"] > WINDOW:
                    break
                if onset is None and g["type"] == "escape_start" and "enemy_zones" in g:
                    onset = g
                if g["type"] == "prediction":
                    preds.append(g)
            if onset is not None:
                pred = next((g for g in preds if g.get("escape") == onset.get("id")), None)
                if pred is None and preds:
                    VALID["prediction not paired by escape id"] += 1
                    pred = preds[0]
            rec = dict(run=run, variant=variant, persona=persona, sess=sess, batch=sess[:15], ep=ep,
                       wave=wave_in_ep, fidx=flee_ep, fwave=flee_wave, t=e["t"], route=route,
                       zone=e.get("zone", ""), wL=wL, wU=wU, pref=list(pref), amb=False, amb_cause="",
                       amb_order="", amb_by=None, amb_dt=None, end=None, onset=onset is not None,
                       p=None, basep=None, drawn=None, evidence=None, confident=None, sites=[], manned=[],
                       ar_incl=False, ar_hit=False)
            if onset is not None:
                c = {r: sum(1 for z in onset["enemy_zones"] if sector(z) == s) for r, s in RS.items()}
                best = max(c.values())
                if sector(rec["zone"]) not in SR and best > 0:
                    rec["ar_incl"] = True
                    rec["ar_hit"] = c[route] == best and list(c.values()).count(best) == 1
            if pred is not None:
                rec["p"] = pred["sector_probabilities"]
                rec["basep"] = pred.get("base_probabilities")
                rec["drawn"] = pred.get("drawn_direction")
                rec["evidence"] = pred.get("evidence", 0.0)
                rec["confident"] = bool(pred.get("confident"))
                rec["sites"] = [s.split("@")[0] for s in pred.get("ambush_sites", [])]
                rec["manned"] = [sector(a.split(":")[0]) for a in pred.get("ambush", []) if int(a.split(":")[1]) > 0]
            pending = {"amb": False, "routed": True, "rec": rec}
            recs.append(rec)
    # next flee in the same episode; ambush history before each decision
    cur = None
    for j, r in enumerate(recs):
        r["next"] = None
        if j + 1 < len(recs) and recs[j + 1]["ep"] == r["ep"]:
            r["next"] = recs[j + 1]  # the record itself (not an index: selections mix sessions)
        if r["ep"] != cur:
            cur = r["ep"]
            last_amb, amb_set = None, set()
        r["last_amb"] = last_amb
        r["amb_set"] = set(amb_set)
        if r["amb"]:
            last_amb = r["route"]
            amb_set.add(r["route"])
    return persona, recs


# ---------------------------------------------------------------- guesses

def guess(rec, kind):
    """returns (route or None, reason)"""
    p = rec["p"]
    if p is None:
        return None, "no prediction"
    gate = rec["evidence"] is not None and rec["evidence"] >= MIN_EVIDENCE
    if kind in ("TOP", "TOPNG", "BASE"):
        q = rec["basep"] if kind == "BASE" else p
        if q is None:
            return None, "no base_probabilities"
        if max(q) - min(q) < 1e-9:
            return None, "uniform p (no evidence)"
        if kind != "TOPNG" and not gate:
            return None, "gate closed (evidence < 2)"
        s = top_cs(q)
        if NAMES[s] not in SR:
            return None, "top sector not a route (" + NAMES[s] + ")"
        return SR[NAMES[s]], "ok"
    if kind == "RSTR":
        if max(p) - min(p) < 1e-9:
            return None, "uniform p (no evidence)"
        if not gate:
            return None, "gate closed (evidence < 2)"
        rp = {r: p[i] for r, i in RIDX.items()}
        b = max(rp.values())
        if list(rp.values()).count(b) > 1:
            return None, "tie among route sectors"
        return max(rp, key=rp.get), "ok"
    if kind == "DRAWN":
        if not gate:
            return None, "gate closed (evidence < 2)"
        d = rec["drawn"]
        if not d:
            return None, "no drawn direction"
        if d not in SR:
            return None, "drawn sector not a route (" + d + ")"
        return SR[d], "ok"
    raise ValueError(kind)


# ---------------------------------------------------------------- stats

def wilson(k, n, z=1.959964):
    if n == 0:
        return float("nan"), float("nan")
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return c - h, c + h


NV = 16


def ep_vector(items):
    """items: list of (guess, route, wL, wU) for one episode -> additive vector"""
    v = [0.0] * NV
    gc = Counter()
    rc = Counter()
    for g, r, wL, wU in items:
        v[0] += 1
        v[1] += g == r
        gi, ri = ROUTES.index(g), ROUTES.index(r)
        v[2 + gi] += 1
        v[5 + ri] += 1
        v[9] += wL[gi]
        v[10] += wU[gi]
        for k in range(3):
            v[11 + k] += wL[k]
        v[14] += max(wL)
        gc[g] += 1
        rc[r] += 1
    if v[0]:
        v[8] = sum(gc[x] * rc[x] for x in ROUTES) / v[0]
    return v


def stats_from(v):
    n = v[0]
    if n == 0:
        return {}
    acc = v[1] / n
    ind = sum((v[2 + k] / n) * (v[5 + k] / n) for k in range(3))
    return dict(n=n, k=v[1], acc=acc, indep=ind, diff=acc - ind, strat=v[8] / n, Ew=v[9] / n, EwU=v[10] / n,
                indep_w=sum((v[2 + k] / n) * (v[11 + k] / n) for k in range(3)), oracle=v[14] / n,
                maj=max(v[5:8]) / n, dstrat=acc - v[8] / n,
                cov_w=v[9] / n - sum((v[2 + k] / n) * (v[11 + k] / n) for k in range(3)))


def boot(clusters, keys, B=2000, seed=1):
    rng = random.Random(seed)
    m = len(clusters)
    vals = defaultdict(list)
    for _ in range(B):
        v = [0.0] * NV
        for _ in range(m):
            c = clusters[rng.randrange(m)]
            for j in range(NV):
                v[j] += c[j]
        s = stats_from(v)
        for k in keys:
            if k in s:
                vals[k].append(s[k])
    out = {}
    for k in keys:
        xs = sorted(vals[k])
        out[k] = (xs[int(0.025 * len(xs))], xs[int(0.975 * len(xs)) - 1]) if xs else (float("nan"),) * 2
    return out


def perm_within_episode(eps, B=5000, seed=2):
    """eps: list of lists of (g, r). one-sided P(perm hits <= observed) and >= observed"""
    rng = random.Random(seed)
    obs = sum(g == r for e in eps for g, r in e)
    le = ge = 0
    for _ in range(B):
        h = 0
        for e in eps:
            gs = [g for g, _ in e]
            rng.shuffle(gs)
            h += sum(g == r for g, (_, r) in zip(gs, e))
        le += h <= obs
        ge += h >= obs
    return (le + 1) / (B + 1), (ge + 1) / (B + 1)


def analyse(recs, kind, label, do_perm=False, B=2000):
    usable = []
    reasons = Counter()
    for r in recs:
        if not r["onset"]:
            continue
        g, why = guess(r, kind)
        reasons[why] += 1
        if g is not None:
            usable.append((r, g))
    by_ep = defaultdict(list)
    for r, g in usable:
        by_ep[(r["sess"], r["ep"])].append((g, r["route"], r["wL"], r["wU"]))
    ep_vecs = {k: ep_vector(v) for k, v in by_ep.items()}
    tot = [sum(v[j] for v in ep_vecs.values()) for j in range(NV)]
    s = stats_from(tot)
    if not s:
        return None
    ci_ep = boot(list(ep_vecs.values()), ["acc", "diff", "indep", "Ew", "dstrat", "cov_w"], B=B)
    # conservative: clusters = episode number (same seed => same per-episode params/random stream in every batch)
    by_num = defaultdict(lambda: [0.0] * NV)
    for (sess, ep), v in ep_vecs.items():
        key = (sess.split("_arena")[1], ep)  # arena+persona and episode number
        for j in range(NV):
            by_num[key][j] += v[j]
    ci_num = boot(list(by_num.values()), ["acc", "diff"], B=B, seed=3)
    s.update(label=label, kind=kind, reasons=reasons, total=sum(reasons.values()),
             wil=wilson(int(s["k"]), int(s["n"])), ci_ep=ci_ep, ci_num=ci_num, n_ep=len(ep_vecs),
             n_num=len(by_num), usable=usable)
    if do_perm:
        eps = [[(g, r) for g, r, _, _ in v] for v in by_ep.values()]
        s["perm"] = perm_within_episode(eps)
    return s


def f(x):
    return f"{100 * x:.1f}%"


def fci(c):
    return f"[{100 * c[0]:.1f}, {100 * c[1]:.1f}]"


# ---------------------------------------------------------------- run
ALL = []
for run, variant, prefixes in SETS:
    for d in grouped_dirs(prefixes):
        persona, recs = process(d, run, variant)
        ALL += recs
print("replay / pairing checks:", dict(VALID))

with open(out_csv, "w") as fh:
    cols = ["run", "variant", "persona", "batch", "ep", "wave", "fidx", "fwave", "t", "route", "zone", "onset",
            "evidence", "confident", "g_TOP", "why_TOP", "g_RSTR", "ar_incl", "ar_hit", "amb", "amb_cause",
            "amb_order", "amb_by", "end", "wL_A", "wL_B", "wL_C", "wU_A", "wU_B", "wU_C"]
    fh.write(",".join(cols) + "\n")
    for r in ALL:
        gT, wT = guess(r, "TOP")
        gR, _ = guess(r, "RSTR")
        row = [r["run"], r["variant"], r["persona"], r["batch"], r["ep"], r["wave"], r["fidx"], r["fwave"],
               r["t"], r["route"], r["zone"], int(r["onset"]), r["evidence"], r["confident"], gT or "",
               wT.replace(",", ";"), gR or "", int(r["ar_incl"]), int(r["ar_hit"]), int(r["amb"]), r["amb_cause"],
               r["amb_order"], r["amb_by"], r["end"]] + [round(x, 4) for x in r["wL"] + r["wU"]]
        fh.write(",".join("" if x is None else str(x) for x in row) + "\n")


def sel(runs, variant, persona):
    return [r for r in ALL if r["run"] in runs and r["variant"] == variant and r["persona"] == persona]


GROUPS = [("run2", ("run2",)), ("run3", ("run3",)), ("run4", ("run4",)), ("pooled 2-4", ("run2", "run3", "run4")),
          ("run1", ("run1",))]

# ================================================================ Q1-Q2
print("\n" + "=" * 100)
print("Q1/Q2  commander top-1 route guess vs bot route (V2)")
print("=" * 100)
RESULTS = {}
for persona in PERSONAS:
    for gname, runs in GROUPS:
        recs = sel(runs, "V2", persona)
        if not recs:
            continue
        for kind in ("TOP", "TOPNG", "RSTR"):
            s = analyse(recs, kind, f"{persona} {gname}", do_perm=(kind == "TOP"), B=2000 if kind == "TOP" else 1000)
            if s is None:
                continue
            RESULTS[(persona, gname, kind)] = s
            n_dec = s["total"]
            print(f"\n[{kind}] {persona} {gname}: flee decisions with onset {n_dec}; usable {int(s['n'])}; "
                  f"unusable: " + "; ".join(f"{k} {v}" for k, v in sorted(s["reasons"].items()) if k != "ok"))
            print(f"   accuracy {int(s['k'])}/{int(s['n'])} = {f(s['acc'])}  Wilson {fci(s['wil'])}  "
                  f"episode-boot {fci(s['ci_ep']['acc'])} ({s['n_ep']} eps)  episode-number-boot {fci(s['ci_num']['acc'])} ({s['n_num']} clusters)")
            print(f"   indep baseline sum_r P(g=r)P(route=r) = {f(s['indep'])}; acc - indep = {100 * s['diff']:+.1f} pp "
                  f"episode-boot {fci(s['ci_ep']['diff'])} episode-number-boot {fci(s['ci_num']['diff'])}")
            print(f"   within-episode stratified indep = {f(s['strat'])}; in-sample majority = {f(s['maj'])}; "
                  f"E[w_t(guess)] = {f(s['Ew'])} (U {f(s['EwU'])}); sum_r P(g=r)E[w_t(r)] = {f(s['indep_w'])}; oracle E[max w] = {f(s['oracle'])}")
            print(f"   acc - within-episode indep = {100 * s['dstrat']:+.1f} pp episode-boot {fci(s['ci_ep']['dstrat'])}; "
                  f"state covariance E[w(g)] - sum_r P(g=r)E[w(r)] = {100 * s['cov_w']:+.1f} pp episode-boot {fci(s['ci_ep']['cov_w'])}")
            if "perm" in s:
                print(f"   within-episode permutation (5000): P(perm <= obs) = {s['perm'][0]:.4f}, P(perm >= obs) = {s['perm'][1]:.4f}")


# ---------------------------------------------------------------- Q2 extra online baselines on TOP-usable set
def online_baselines(recs, usable_ids):
    """majority-so-far, decayed route frequency (0.85), last route, using the bot's true earlier flee routes
    in the episode (all flee decisions, incl. those without onset)."""
    res = Counter()
    hist = None
    cur = None
    for r in recs:
        key = (r["sess"], r["ep"])
        if key != cur:
            cur = key
            cnt = Counter()
            dec = {x: 0.0 for x in ROUTES}
            last = None
            order = []
        if id(r) in usable_ids:
            res["n"] += 1
            if cnt:
                m = max(cnt.values())
                tied = [x for x in ROUTES if cnt[x] == m]
                g = max(tied, key=lambda x: max(i for i, y in enumerate(order) if y == x))
            else:
                g = "A"
            res["majority-so-far"] += g == r["route"]
            if any(dec.values()):
                m = max(dec.values())
                tied = [x for x in ROUTES if abs(dec[x] - m) < 1e-12]
                g2 = max(tied, key=lambda x: max(i for i, y in enumerate(order) if y == x))
            else:
                g2 = "A"
            res["decayed-freq(0.85)"] += g2 == r["route"]
            res["last-route"] += (last or "A") == r["route"]
            res["always-A"] += r["route"] == "A"
        cnt[r["route"]] += 1
        for x in ROUTES:
            dec[x] *= 0.85
        dec[r["route"]] += 1
        last = r["route"]
        order.append(r["route"])
    return res


print("\n" + "=" * 100)
print("Q2  route frequencies and online baselines on the TOP-usable decisions (V2)")
print("=" * 100)
for persona in PERSONAS:
    for gname, runs in GROUPS[:4]:
        recs = sel(runs, "V2", persona)
        s = RESULTS.get((persona, gname, "TOP"))
        if not s:
            continue
        ids = {id(r) for r, _ in s["usable"]}
        ob = online_baselines(recs, ids)
        rc = Counter(r["route"] for r, _ in s["usable"])
        gc = Counter(g for _, g in s["usable"])
        n = s["n"]
        print(f"{persona:9s} {gname:10s} n={int(n)}  P(route) A {rc['A'] / n:.3f} B {rc['B'] / n:.3f} C {rc['C'] / n:.3f} | "
              f"P(guess) A {gc['A'] / n:.3f} B {gc['B'] / n:.3f} C {gc['C'] / n:.3f} | commander {f(s['acc'])} | "
              + "  ".join(f"{k} {f(ob[k] / ob['n'])}" for k in ("always-A", "majority-so-far", "decayed-freq(0.85)", "last-route"))
              + f" | in-sample majority {f(s['maj'])} | indep {f(s['indep'])} | oracle {f(s['oracle'])}")

# ================================================================ Q3 confusion
print("\n" + "=" * 100)
print("Q3  confusion matrix guess (rows) x actual route (cols), TOP definition, V2")
print("=" * 100)
for persona in PERSONAS:
    for gname, runs in GROUPS[:4]:
        s = RESULTS.get((persona, gname, "TOP"))
        if not s:
            continue
        m = Counter((g, r["route"]) for r, g in s["usable"])
        n = s["n"]
        print(f"\n{persona} {gname} (n={int(n)})      actual A   actual B   actual C   | P(route=g|guess=g)  P(route=g)")
        for g in ROUTES:
            row = sum(m[(g, a)] for a in ROUTES)
            col = sum(m[(x, g)] for x in ROUTES)
            cond = m[(g, g)] / row if row else float("nan")
            lo, hi = wilson(m[(g, g)], row) if row else (float("nan"),) * 2
            print(f"   guess {g}  " + "".join(f"{m[(g, a)]:10d}" for a in ROUTES) +
                  f"   | {cond:.3f} [{lo:.3f},{hi:.3f}] (n={row})   {col / n:.3f}")

# ================================================================ Q4 lose-shift
print("\n" + "=" * 100)
print("Q4  lose-shift (Adaptive, V2): next flee in the same episode")
print("=" * 100)


def q4(recs, title):
    print(f"\n-- {title}")
    rows = defaultdict(lambda: Counter())
    for r in recs:
        if r["next"] is None:
            continue
        nx = r["next"]
        if r["amb"]:
            if r["amb_by"] is None and r["amb_order"] == "":
                cls = "ambushed (attribution not logged)" if r["run"] in ("run1", "run2") else "ambushed, no enemy identified"
            elif r["amb_by"]:
                cls = "ambushed by ambusher (HoldAt)"
            else:
                cls = "ambushed by chaser/other"
            keys = ["ambushed (all)", cls, "ambushed, cause " + (r["amb_cause"] or "?")]
        else:
            keys = ["clean (all)", "clean, reached safety" if r["end"] == "safe" else "clean, flee ended by wave end/death"]
        for k in keys:
            c = rows[k]
            c["n"] += 1
            c["stay"] += nx["route"] == r["route"]
            c["Ew_stay"] += nx["wL"][ROUTES.index(r["route"])]
            c["same_wave"] += nx["wave"] == r["wave"]
            gT, _ = guess(nx, "TOP") if nx["onset"] else (None, "")
            if gT is not None:
                c["g_usable"] += 1
                c["g_on_r"] += gT == r["route"]
                c["g_on_r_hit"] += gT == r["route"] and nx["route"] == r["route"]
                c["g_hit"] += gT == nx["route"]
                c["g_Ew"] += nx["wL"][ROUTES.index(gT)]
            for rr in ROUTES:
                c["n_" + r["route"]] += 0
            c["r_" + r["route"]] += 1
            c["stay_" + r["route"]] += nx["route"] == r["route"]
    order = ["clean (all)", "clean, reached safety", "clean, flee ended by wave end/death", "ambushed (all)",
             "ambushed by ambusher (HoldAt)", "ambushed by chaser/other", "ambushed, no enemy identified",
             "ambushed (attribution not logged)", "ambushed, cause Damage", "ambushed, cause AheadOnRoute",
             "ambushed, cause AtRefuge"]
    print(f"   {'class':38s} {'n':>5s} {'stay':>14s} {'model E[w_next(r)]':>18s}   per route stay A/B/C          "
          f"| next guess usable  P(guess=r)   hit|guess=r      next-guess acc  E[w(guess)]")
    for k in order:
        c = rows.get(k)
        if not c:
            continue
        n = c["n"]
        lo, hi = wilson(c["stay"], n)
        pr = " ".join(f"{(c['stay_' + x] / c['r_' + x]) if c['r_' + x] else float('nan'):.2f}({c['r_' + x]})" for x in ROUTES)
        gu = c["g_usable"]
        line = (f"   {k:38s} {n:5d} {c['stay'] / n:6.3f} [{lo:.2f},{hi:.2f}] {c['Ew_stay'] / n:12.3f}       {pr:30s}| ")
        if gu:
            hr = c["g_on_r_hit"] / c["g_on_r"] if c["g_on_r"] else float("nan")
            line += (f"{gu:5d}      {c['g_on_r'] / gu:6.3f} ({c['g_on_r']})  {hr:6.3f} ({c['g_on_r_hit']}/{c['g_on_r']})"
                     f"   {c['g_hit'] / gu:6.3f}      {c['g_Ew'] / gu:.3f}")
        print(line)


for gname, runs in GROUPS:
    q4(sel(runs, "V2", "Adaptive"), f"V2 Adaptive {gname}")


def q4b(recs, title):
    """stay probability after ambushed vs clean, holding the pre-flee weight on that route fixed (bins)"""
    print(f"\n-- {title}: stay rate by pre-flee weight w_before(r) on the route just used")
    bins = [(0, 0.2), (0.2, 0.4), (0.4, 0.6), (0.6, 1.01)]
    tot = {"amb": [0, 0, 0.0, 0.0], "clean": [0, 0, 0.0, 0.0]}
    for lo, hi in bins:
        parts = []
        for cls in ("clean", "amb"):
            xs = [r for r in recs if r["next"] is not None and (r["amb"] == (cls == "amb"))
                  and lo <= r["wL"][ROUTES.index(r["route"])] < hi]
            if not xs:
                parts.append(f"{cls}: -")
                continue
            st = sum(r["next"]["route"] == r["route"] for r in xs)
            wb = sum(r["wL"][ROUTES.index(r["route"])] for r in xs) / len(xs)
            wn = sum(r["next"]["wL"][ROUTES.index(r["route"])] for r in xs) / len(xs)
            lo_, hi_ = wilson(st, len(xs))
            parts.append(f"{cls:5s} n={len(xs):3d} stay {st / len(xs):.2f} [{lo_:.2f},{hi_:.2f}] w_before {wb:.2f} model w_next {wn:.2f}")
            t = tot[cls]
            t[0] += len(xs); t[1] += st; t[2] += wb * len(xs); t[3] += wn * len(xs)
        print(f"   w_before in [{lo:.1f},{min(hi, 1):.1f}):  " + "   |   ".join(parts))
    for cls, t in tot.items():
        if t[0]:
            print(f"   all {cls:5s}: n={t[0]} stay {t[1] / t[0]:.3f}  mean w_before {t[2] / t[0]:.3f}  "
                  f"stay - w_before = {t[1] / t[0] - t[2] / t[0]:+.3f}  model w_next {t[3] / t[0]:.3f}")


for gname, runs in GROUPS[:4]:
    q4b(sel(runs, "V2", "Adaptive"), f"V2 Adaptive {gname}")

# ================================================================ Q5 within-episode
print("\n" + "=" * 100)
print("Q5  by flee index within the episode (Adaptive, V2, TOP definition; index counts every flee decision)")
print("=" * 100)
BINS = [(1, 3), (4, 6), (7, 9), (10, 12), (13, 15), (16, 99)]


def q5(recs, title):
    print(f"\n-- {title}")
    print(f"   {'flees':7s} {'dec':>4s} {'usable':>6s} {'acc':>7s} {'Wilson':>14s} {'indep':>7s} {'diff':>7s} "
          f"{'P(rt=A)':>7s} {'P(g=A)':>7s} {'E[wA]':>6s} {'E[w(g)]':>7s} {'gate<2':>6s} {'AR proxy':>12s} {'amb rate':>8s}")
    for lo, hi in BINS:
        rs = [r for r in recs if lo <= r["fidx"] <= hi and r["onset"]]
        if not rs:
            continue
        us = [(r, guess(r, "TOP")[0]) for r in rs]
        us = [(r, g) for r, g in us if g is not None]
        n = len(us)
        gate = sum(1 for r in rs if r["evidence"] is not None and r["evidence"] < MIN_EVIDENCE)
        ar = [r for r in rs if r["ar_incl"]]
        arh = sum(r["ar_hit"] for r in ar)
        amb = sum(r["amb"] for r in rs)
        if n:
            k = sum(g == r["route"] for r, g in us)
            pg = Counter(g for _, g in us)
            pr = Counter(r["route"] for r, _ in us)
            ind = sum(pg[x] * pr[x] for x in ROUTES) / n / n
            ew = sum(r["wL"][ROUTES.index(g)] for r, g in us) / n
            wa = sum(r["wL"][0] for r, _ in us) / n
            w = wilson(k, n)
            print(f"   {lo:2d}-{hi if hi < 99 else '+':<4} {len(rs):4d} {n:6d} {k / n:7.3f} [{w[0]:.2f},{w[1]:.2f}] {ind:7.3f} "
                  f"{k / n - ind:+7.3f} {pr['A'] / n:7.3f} {pg['A'] / n:7.3f} {wa:6.3f} {ew:7.3f} {gate:6d} "
                  f"{arh:4d}/{len(ar):<4d}{(arh / len(ar) if ar else float('nan')):.2f} {amb / len(rs):8.3f}")
        else:
            print(f"   {lo:2d}-{hi:<4} {len(rs):4d} {0:6d}   (no usable guess; gate<2 in {gate})")


for gname, runs in GROUPS[:4]:
    q5(sel(runs, "V2", "Adaptive"), f"V2 Adaptive {gname}")

# Q5b: early/late split
print("\n-- early (flee idx <= 7) vs late (>= 8), V2 Adaptive")
for gname, runs in GROUPS[:4]:
    recs = sel(runs, "V2", "Adaptive")
    for name, cond in (("early", lambda r: r["fidx"] <= 7), ("late", lambda r: r["fidx"] >= 8)):
        s = analyse([r for r in recs if cond(r)], "TOP", name, B=1000)
        print(f"   {gname:10s} {name:5s} {int(s['k'])}/{int(s['n'])} = {f(s['acc'])} Wilson {fci(s['wil'])} ep-boot {fci(s['ci_ep']['acc'])}"
              f"  indep {f(s['indep'])}  diff {100 * s['diff']:+.1f} pp ep-boot {fci(s['ci_ep']['diff'])}  E[w(g)] {f(s['Ew'])}")

# ================================================================ Q6 own weights
print("\n" + "=" * 100)
print("Q6  bot's own (replayed) probability of the guessed route, hits vs misses (TOP definition, V2)")
print("=" * 100)
for persona in PERSONAS:
    for gname, runs in GROUPS[:4]:
        s = RESULTS.get((persona, gname, "TOP"))
        if not s:
            continue
        hits = [r["wL"][ROUTES.index(g)] for r, g in s["usable"] if g == r["route"]]
        miss = [r["wL"][ROUTES.index(g)] for r, g in s["usable"] if g != r["route"]]
        missU = [r["wU"][ROUTES.index(g)] for r, g in s["usable"] if g != r["route"]]
        allw = [r["wL"][ROUTES.index(g)] for r, g in s["usable"]]
        lt = sum(w < 1 / 3 for w in allw)
        mlt = sum(w < 1 / 3 for w in miss)
        hlt = sum(w < 1 / 3 for w in hits)
        mean = lambda xs: sum(xs) / len(xs) if xs else float("nan")
        print(f"{persona:9s} {gname:10s} n={len(allw)}  mean w(guess): all {mean(allw):.3f} hits {mean(hits):.3f} (n={len(hits)}) "
              f"misses {mean(miss):.3f} (U {mean(missU):.3f}, n={len(miss)}) | w(guess)<1/3: all {lt} ({lt / len(allw):.1%}), "
              f"misses {mlt} ({(mlt / len(miss)) if miss else float('nan'):.1%}), hits {hlt} | observed acc {f(s['acc'])} vs E[w(guess)] {f(s['Ew'])}")
        if persona == "Adaptive":
            # decile-ish bins of w(guess)
            bins = [(0, 0.1), (0.1, 1 / 3), (1 / 3, 0.6), (0.6, 1.01)]
            parts = []
            for lo, hi in bins:
                xs = [(r, g) for r, g in s["usable"] if lo <= r["wL"][ROUTES.index(g)] < hi]
                if xs:
                    k = sum(g == r["route"] for r, g in xs)
                    parts.append(f"w in [{lo:.2f},{hi:.2f}): {k}/{len(xs)}={k / len(xs):.2f} (mean w {mean([r['wL'][ROUTES.index(g)] for r, g in xs]):.2f})")
            print("      calibration: " + " | ".join(parts))

print("\n-- Q6b: how often the guess lands on a route the bot currently weights < 1/3, vs an independent guess")
for gname, runs in GROUPS[:4]:
    s = RESULTS.get(("Adaptive", gname, "TOP"))
    us = s["usable"]
    n = len(us)
    pg = Counter(g for _, g in us)
    obs = sum(r["wL"][ROUTES.index(g)] < 1 / 3 for r, g in us)
    ind = sum(sum(pg[x] / n * (r["wL"][ROUTES.index(x)] < 1 / 3) for x in ROUTES) for r, _ in us)
    uni = sum(sum((r["wL"][k] < 1 / 3) for k in range(3)) / 3 for r, _ in us)
    low = [(r, g) for r, g in us if r["wL"][ROUTES.index(g)] < 1 / 3]
    last = sum(g == r["last_amb"] for r, g in low)
    ever = sum(g in r["amb_set"] for r, g in low)
    hit_low = sum(g == r["route"] for r, g in low)
    # among all guesses: guess = most recently ambushed route
    gl = [(r, g) for r, g in us if r["last_amb"] is not None]
    gl_same = [(r, g) for r, g in gl if g == r["last_amb"]]
    gl_other = [(r, g) for r, g in gl if g != r["last_amb"]]
    acc = lambda xs: (sum(g == r["route"] for r, g in xs) / len(xs)) if xs else float("nan")
    ew = lambda xs: (sum(r["wL"][ROUTES.index(g)] for r, g in xs) / len(xs)) if xs else float("nan")
    print(f"   {gname:10s} guesses on w<1/3: commander {obs}/{n} = {obs / n:.1%}; independent guess with the commander's marginal "
          f"{ind / n:.1%}; uniform guess {uni / n:.1%}. Of the commander's w<1/3 guesses: = last-ambushed route {last} "
          f"({last / len(low):.0%}), = a route ambushed earlier this episode {ever} ({ever / len(low):.0%}), hit {hit_low}/{len(low)} = {hit_low / len(low):.1%}")
    print(f"   {gname:10s} decisions with an earlier ambush this episode: {len(gl)}; guess = last-ambushed route {len(gl_same)} "
          f"({len(gl_same) / len(gl):.1%}) acc {acc(gl_same):.3f} E[w(g)] {ew(gl_same):.3f}; guess = other route {len(gl_other)} "
          f"acc {acc(gl_other):.3f} E[w(g)] {ew(gl_other):.3f}; bot took last-ambushed route {sum(r['route'] == r['last_amb'] for r, _ in gl) / len(gl):.3f}")

# ================================================================ Q7 variants
print("\n" + "=" * 100)
print("Q7  other commanders (Adaptive + contrast personas)")
print("=" * 100)
for run, variant, kinds in (("run2", "V2", ("TOP",)), ("run2", "TS", ("TOP", "DRAWN")), ("run2", "Bandit", ("TOP",)),
                            ("run3", "V2", ("TOP",)), ("run3", "TS", ("TOP", "DRAWN")), ("run3", "Bandit", ("TOP",)),
                            ("run4", "V2", ("TOP",)), ("run4", "Reaction", ("TOP", "BASE")),
                            ("run4", "Reaction-p0.8", ("TOP", "BASE"))):
    for persona in PERSONAS:
        recs = sel((run,), variant, persona)
        if not recs:
            continue
        for kind in kinds:
            s = analyse(recs, kind, "", B=1000)
            if s is None:
                continue
            ar = [r for r in recs if r["ar_incl"]]
            arh = sum(r["ar_hit"] for r in ar)
            unus = s["total"] - int(s["n"])
            print(f"{run} {variant:13s} {persona:9s} {kind:5s} usable {int(s['n']):4d}/{s['total']:<4d} acc {f(s['acc']):>6s} "
                  f"Wilson {fci(s['wil']):14s} ep-boot {fci(s['ci_ep']['acc']):14s} indep {f(s['indep']):>6s} "
                  f"diff {100 * s['diff']:+5.1f} pp {fci(s['ci_ep']['diff']):16s} E[w(g)] {f(s['Ew']):>6s} "
                  f"cov_w {100 * s['cov_w']:+5.1f} pp {fci(s['ci_ep']['cov_w']):16s} vs-within-ep {100 * s['dstrat']:+5.1f} pp "
                  f"| AR proxy {arh}/{len(ar)}")

# ================================================================ Q7b: does the commander's next guess react to the ambush?
print("\n" + "=" * 100)
print("Q7b  Adaptive: next flee after ambushed / clean flee on r, by commander (TOP guess; BASE for Reaction)")
print("=" * 100)
for run, variant in (("run2", "V2"), ("run3", "V2"), ("run4", "V2"), ("run2", "TS"), ("run3", "TS"), ("run2", "Bandit"),
                     ("run3", "Bandit"), ("run4", "Reaction"), ("run4", "Reaction-p0.8")):
    recs = sel((run,), variant, "Adaptive")
    changed = sum(1 for r in recs if r["onset"] and r["basep"] is not None and guess(r, "TOP")[0] != guess(r, "BASE")[0])
    for kind in (("TOP", "BASE") if variant.startswith("Reaction") else ("TOP",)):
        out = []
        for cls in ("amb", "clean"):
            xs = [r for r in recs if r["next"] is not None and r["amb"] == (cls == "amb")]
            stay = sum(r["next"]["route"] == r["route"] for r in xs)
            gs = [(r, guess(r["next"], kind)[0]) for r in xs if r["next"]["onset"]]
            gs = [(r, g) for r, g in gs if g is not None]
            on = [(r, g) for r, g in gs if g == r["route"]]
            hit = sum(r["next"]["route"] == g for r, g in on)
            acc = sum(r["next"]["route"] == g for r, g in gs)
            out.append(f"{cls:5s} n={len(xs):3d} stay {stay / len(xs):.3f} | guess=r {len(on)}/{len(gs)}={len(on) / len(gs):.3f} "
                       f"hit|guess=r {hit}/{len(on)}={(hit / len(on)) if on else float('nan'):.3f} next-acc {acc / len(gs):.3f}")
        print(f"{run} {variant:13s} {kind:4s} " + "   ||   ".join(out) + (f"   [TOP != BASE in {changed} decisions]" if kind == "BASE" else ""))

# ================================================================ Q4c: cluster-bootstrap CIs for the key lose-shift numbers
print("\n" + "=" * 100)
print("Q4c  episode-cluster bootstrap (2000) for lose-shift and the commander's next guess, V2 Adaptive")
print("=" * 100)


def q4c(recs, title, B=2000, seed=7):
    eps = defaultdict(lambda: [0] * 8)   # amb_n amb_stay clean_n clean_stay ambG ambG_hit cleanG cleanG_hit
    for r in recs:
        if r["next"] is None:
            continue
        v = eps[(r["sess"], r["ep"])]
        nx = r["next"]
        st = nx["route"] == r["route"]
        g = guess(nx, "TOP")[0] if nx["onset"] else None
        o = 0 if r["amb"] else 2
        v[o] += 1
        v[o + 1] += st
        if g == r["route"]:
            v[4 + o] += 1
            v[5 + o] += st
    cl = list(eps.values())
    def st(v):
        return dict(stay_amb=v[1] / v[0], stay_clean=v[3] / v[2], d_stay=v[1] / v[0] - v[3] / v[2],
                    hit_amb=v[5] / v[4] if v[4] else float("nan"), hit_clean=v[7] / v[6] if v[6] else float("nan"))
    tot = [sum(c[j] for c in cl) for j in range(8)]
    s0 = st(tot)
    rng = random.Random(seed)
    vals = defaultdict(list)
    for _ in range(B):
        v = [0] * 8
        for _ in range(len(cl)):
            c = cl[rng.randrange(len(cl))]
            for j in range(8):
                v[j] += c[j]
        if v[0] and v[2] and v[4] and v[6]:
            for k, x in st(v).items():
                vals[k].append(x)
    parts = []
    for k in ("stay_amb", "stay_clean", "d_stay", "hit_amb", "hit_clean"):
        xs = sorted(vals[k])
        parts.append(f"{k} {s0[k]:.3f} [{xs[int(.025 * len(xs))]:.3f}, {xs[int(.975 * len(xs)) - 1]:.3f}]")
    print(f"   {title:12s} ({len(cl)} eps; amb n={tot[0]}, clean n={tot[2]}, guess=r after amb {tot[4]}, after clean {tot[6]}): " + "; ".join(parts))


for gname, runs in GROUPS[:4]:
    q4c(sel(runs, "V2", "Adaptive"), gname)

# ================================================================ Q2b: one-step counterfactual guesses (expected accuracy = E[w_t(g')])
print("\n" + "=" * 100)
print("Q2b  one-step counterfactual guesses on the TOP-usable decisions, scored by the bot's replayed weights E[w_t(g')]")
print("     (exact expectation for a one-step guess because the bot's roll never sees the guess; the trajectory is held fixed)")
print("=" * 100)
for gname, runs in GROUPS[:4]:
    s = RESULTS[("Adaptive", gname, "TOP")]
    us = s["usable"]
    n = len(us)
    e_cmd = sum(r["wL"][ROUTES.index(g)] for r, g in us) / n
    e_A = sum(r["wL"][0] for r, _ in us) / n
    e_or = sum(max(r["wL"]) for r, _ in us) / n
    sw = 0.0
    nsw = 0
    for r, g in us:
        g2 = g
        if g == r["last_amb"]:
            rp = {x: r["p"][RIDX[x]] for x in ROUTES if x != g}
            g2 = max(rp, key=rp.get)
            nsw += 1
        sw += r["wL"][ROUTES.index(g2)]
    print(f"   {gname:10s} n={n}: commander E[w(g)] {e_cmd:.3f} (observed {s['acc']:.3f}); always-A {e_A:.3f}; uniform 0.333; "
          f"commander but never the last-ambushed route (switch to its best other route, {nsw} switched) {sw / n:.3f}; oracle argmax w {e_or:.3f}")
