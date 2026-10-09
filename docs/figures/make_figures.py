"""Generate the README figures (docs/figures/*.svg) from the numbers in reports/.

Usage, from the repo root: python3 docs/figures/make_figures.py
Every number is copied from a report (Phase 5 for the scatter, the adaptive-eval reports for the rest),
so update it here and re-run if a report changes. Standard library only.
"""
import html
import math
import os
import sys

OUT = sys.argv[1] if len(sys.argv) > 1 else "docs/figures"
os.makedirs(OUT, exist_ok=True)

FONT = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif"
INK, INK2, MUTED = "#0b0b0b", "#52514e", "#898781"
GRID, AXIS, SURF, CARD = "#e1e0d9", "#c3c2b7", "#fcfcfb", "#ffffff"
C_BASE, C_V1, C_V2, C_ADA, C_VIO, C_V2_LIGHT = "#898781", "#eb6834", "#2a78d6", "#1baf7a", "#4a3aa7", "#9ec5f4"
GOOD, BAD = "#0ca30c", "#d03b3b"


def esc(s):
    return html.escape(str(s), quote=False)


class SVG:
    def __init__(self, w, h, title, desc):
        self.w, self.h = w, h
        self.parts = [
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" '
            f'font-family="{FONT}" role="img" aria-labelledby="title desc">',
            f'<title id="title">{esc(title)}</title>',
            f'<desc id="desc">{esc(desc)}</desc>',
            '<defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" '
            f'orient="auto-start-reverse"><path d="M0,1 L9,5 L0,9 z" fill="{INK2}"/></marker></defs>',
            f'<rect x="0.5" y="0.5" width="{w - 1}" height="{h - 1}" rx="12" fill="{SURF}" stroke="{GRID}"/>',
        ]

    def add(self, s):
        self.parts.append(s)

    def text(self, x, y, s, size=12, fill=INK2, weight=None, anchor="start", extra=""):
        w = f' font-weight="{weight}"' if weight else ""
        a = f' text-anchor="{anchor}"' if anchor != "start" else ""
        self.add(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" fill="{fill}"{w}{a}{extra}>{esc(s)}</text>')

    def rich(self, x, y, runs, size=12, anchor="start"):
        """runs = [(text, fill, weight)]"""
        a = f' text-anchor="{anchor}"' if anchor != "start" else ""
        spans = "".join(
            f'<tspan fill="{f}"{" font-weight=" + chr(34) + str(wt) + chr(34) if wt else ""}>{esc(t)}</tspan>'
            for t, f, wt in runs)
        self.add(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}"{a}>{spans}</text>')

    def lines(self, x, y, items, size=12, lh=None, **kw):
        lh = lh or round(size * 1.42, 1)
        for k, s in enumerate(items):
            self.text(x, y + k * lh, s, size=size, **kw)

    def line(self, x1, y1, x2, y2, stroke=GRID, width=1, extra=""):
        self.add(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{stroke}" '
                 f'stroke-width="{width}"{extra}/>')

    def dot(self, x, y, fill, r=5, ring=SURF):
        self.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="{fill}" stroke="{ring}" stroke-width="2"/>')

    def header(self, title, subtitle_lines):
        self.text(24, 34, title, 17, INK, 600)
        self.lines(24, 55, subtitle_lines, 12.5, 18)

    def legend(self, x, y, items, gap=26):
        """items = [(color, label, kind)] kind: dot | square | diamond"""
        for color, label, kind in items:
            if kind == "square":
                self.add(f'<rect x="{x:.1f}" y="{y - 9:.1f}" width="11" height="11" rx="2" fill="{color}"/>')
            else:
                self.dot(x + 5, y - 3.5, color, 5)
            self.text(x + 16, y, label, 12, INK2)
            x += 16 + 6.4 * len(label) + gap

    def save(self, name):
        self.add("</svg>")
        with open(os.path.join(OUT, name), "w") as f:
            f.write("\n".join(self.parts) + "\n")


def hbar(x0, x1, y, h, fill, round_end=True):
    """Horizontal bar, square at the baseline, 4px rounded data end."""
    r = min(4, (x1 - x0) / 2, h / 2) if round_end else 0
    if r <= 0:
        return f'<rect x="{x0:.1f}" y="{y:.1f}" width="{x1 - x0:.1f}" height="{h}" fill="{fill}"/>'
    return (f'<path d="M{x0:.1f},{y:.1f} H{x1 - r:.1f} Q{x1:.1f},{y:.1f} {x1:.1f},{y + r:.1f} V{y + h - r:.1f} '
            f'Q{x1:.1f},{y + h:.1f} {x1 - r:.1f},{y + h:.1f} H{x0:.1f} Z" fill="{fill}"/>')


def status_icon(s, x, y, kind):
    color = {"good": GOOD, "bad": BAD}.get(kind, MUTED)
    glyph = {"good": "M-3.4,0.2 L-1,2.6 L3.4,-2.4",
             "bad": "M-2.8,-2.8 L2.8,2.8 M2.8,-2.8 L-2.8,2.8",
             "same": "M-3.2,-1.6 H3.2 M-3.2,1.6 H3.2",
             "control": "M-3.2,0 H3.2"}[kind]
    s.add(f'<g transform="translate({x:.1f},{y:.1f})"><circle r="7.5" fill="{color}"/>'
          f'<path d="{glyph}" fill="none" stroke="#ffffff" stroke-width="1.8" stroke-linecap="round" '
          f'stroke-linejoin="round"/></g>')


# ---------------------------------------------------------------- 1. system overview
def fig_overview():
    s = SVG(880, 392, "How the horde experiment is wired",
            "The player (a human or a scripted bot) is observed by a player model; a swappable horde commander "
            "(Baseline, V1 reinforcement learning, V2 predictive, V2 with adaptive options) reads it and orders "
            "squads of zombies. Telemetry is logged and used offline for training and evaluation.")
    s.header("One game loop, swappable horde brains",
             ["Every commander reads the same player model and gives the same kinds of orders. "
              "Only the decision-making differs."])

    def box(x, y, w, h, title, sub=None, body=(), accent=None):
        s.add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="{CARD}" stroke="{AXIS}"/>')
        s.text(x + 14, y + 24, title, 13.5, INK, 600)
        ty = y + 24
        if sub:
            s.text(x + 14, y + 41, sub, 11.5, MUTED)
            ty = y + 41
        s.lines(x + 14, ty + 21, body, 12, 17)

    # feedback: the horde acts on the player
    s.add(f'<path d="M777,124 V86 H97 V121" fill="none" stroke="{MUTED}" stroke-width="1.5" marker-end="url(#arrow)"/>')
    s.add(f'<rect x="334" y="78" width="212" height="16" fill="{SURF}"/>')
    s.text(440, 90, "zombies chase, hold and ambush", 11.5, MUTED, anchor="middle")

    box(24, 124, 146, 120, "Player", None, ["a human at the", "keyboard, or a bot", "persona driving the", "same controls"])
    box(204, 124, 206, 120, "Player model", "PlayerBehaviourMonitor",
        ["fight · flee · kite · passive", "escape directions (8),", "decayed per escape"])
    # commander with four brains
    s.add(f'<rect x="444" y="98" width="220" height="172" rx="10" fill="{CARD}" stroke="{INK2}" stroke-width="1.5"/>')
    s.text(458, 122, "Horde commander", 13.5, INK, 600)
    s.text(458, 139, "IHordeCommander (swappable)", 11.5, MUTED)
    chips = [(C_BASE, "Baseline", "scripted control"), (C_V1, "V1", "RL policy (PPO)"),
             (C_V2, "V2", "predict, then ambush"), (C_ADA, "V2+", "adaptive options")]
    for k, (c, name, desc) in enumerate(chips):
        y = 150 + k * 29
        s.add(f'<rect x="456" y="{y}" width="196" height="25" rx="6" fill="{SURF}" stroke="{GRID}"/>')
        s.dot(470, y + 12.5, c, 5, SURF)
        s.rich(482, y + 17, [(name, INK, 600), ("  " + desc, INK2, None)], 12)
    box(698, 124, 158, 120, "Zombies", "Squad · EnemyOrder", ["orders: MoveTo,", "HoldAt, Chase", "speed 3 (player 5)"])

    for x1, x2 in [(170, 203), (410, 443), (664, 697)]:
        s.line(x1, 184, x2, 184, INK2, 1.5, ' marker-end="url(#arrow)"')

    box(204, 306, 206, 62, "Telemetry", None, ["samples.csv · events.jsonl"])
    box(444, 306, 220, 62, "MLTraining/  (offline)", None, ["PPO · predictor · evaluation"])
    s.line(307, 244, 307, 305, INK2, 1.5, ' marker-end="url(#arrow)"')
    s.text(315, 279, "logged", 11.5, MUTED)
    s.line(410, 337, 443, 337, INK2, 1.5, ' marker-end="url(#arrow)"')
    s.line(600, 306, 600, 271, INK2, 1.5, ' marker-end="url(#arrow)"')
    s.text(610, 293, "trained models (.onnx, .json)", 11.5, MUTED)
    s.lines(698, 322, ["In the ML arena this loop", "runs 8× in parallel,", "headless, 25–37× faster."], 11.5, 16, fill=MUTED)
    s.save("system-overview.svg")


# ---------------------------------------------------------------- 2. arena
def fig_arena():
    s = SVG(880, 462, "The ML arena",
            "Top-down map of one arena: the base in the middle, three routes to refuges 26 units out (A east, "
            "B north-west, C south-west). A fleeing player on route A has chasers behind it and an ambush squad "
            "waiting at the refuge ahead. Candidate ambush distances for the bandit are marked on route B.")
    s.header("The ML arena", ["One of the 8 copies that run side by side. Distances in game units (u)."])
    u = 7.0
    cx, cy = 250, 272

    def at(r_units, deg):
        a = math.radians(deg)
        return cx + r_units * u * math.cos(a), cy - r_units * u * math.sin(a)

    # zone map: core, two rings, eight sectors
    for k in range(8):
        a = 22.5 + 45 * k
        x1, y1 = at(6, a)
        x2, y2 = at(27.5, a)
        s.line(x1, y1, x2, y2, "#ecebe6", 1)
    for r in (6, 20):
        s.add(f'<circle cx="{cx}" cy="{cy}" r="{r * u}" fill="none" stroke="{GRID}"/>')
    s.text(cx, cy - 26, "Core", 10.5, MUTED, anchor="middle")
    for name, r in (("N1", 13), ("N2", 24)):
        x, y = at(r, 90)
        s.text(x, y + 4, name, 10.5, MUTED, anchor="middle")
    # routes
    for deg in (0, 135, 225):
        x1, y1 = at(3.6, deg)
        x2, y2 = at(26, deg)
        s.add(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="#efeee9" '
              f'stroke-width="18" stroke-linecap="round"/>')
        s.line(x1, y1, x2, y2, AXIS, 1.5)
    # base
    s.add(f'<rect x="{cx - 17.5}" y="{cy - 21}" width="35" height="42" rx="3" fill="{INK2}"/>')
    s.text(cx, cy + 4, "base", 11, "#ffffff", 600, "middle")
    # ambush engage radius at the A refuge
    ax, ay = at(25.5, 0)
    s.add(f'<circle cx="{ax:.1f}" cy="{ay:.1f}" r="{6 * u}" fill="{C_V2}" fill-opacity="0.08" '
          f'stroke="{C_V2}" stroke-opacity="0.45"/>')
    # refuges
    for deg, letter in ((0, "A"), (135, "B"), (225, "C")):
        x, y = at(26, deg)
        s.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="11" fill="{CARD}" stroke="{INK2}" stroke-width="1.5"/>')
        s.text(x, y + 4.5, letter, 12, INK, 600, "middle")
    s.dot(ax - 6, ay - 17, C_V2, 5.5)
    s.dot(ax - 6, ay + 17, C_V2, 5.5)
    s.lines(ax - 8, ay - 58, ["ambush squad holds here,", "engages within 6 u"], 11.5, 15, fill=INK2, anchor="middle")
    # fleeing player with chasers behind
    px, py = at(11, 0)
    s.line(px + 9, py, px + 40, py, INK, 1.5, ' marker-end="url(#arrow)"')
    s.dot(px, py, INK, 6)
    s.text(px + 8, py - 22, "player flees (speed 5)", 11.5, INK2, anchor="middle")
    s.dot(px - 28, py - 10, C_BASE, 5)
    s.dot(px - 31, py + 11, C_BASE, 5)
    s.text(px - 26, py + 36, "chasers (speed 3)", 11.5, INK2, anchor="middle")
    # bandit candidate spots on route B
    for r in (12, 18, 25):
        x, y = at(r, 135)
        if r == 25:
            x, y = at(23.6, 135)
        s.add(f'<rect x="{x - 4.5:.1f}" y="{y - 4.5:.1f}" width="9" height="9" fill="{C_ADA}" stroke="{SURF}" '
              f'stroke-width="2" transform="rotate(45 {x:.1f} {y:.1f})"/>')
        lx, ly = x + 12, y + 12
        s.text(lx, ly + 4, str(r), 11, INK2)
    # route labels
    bx, by = at(26, 135)
    s.text(bx - 16, by - 14, "B · north-west", 12, INK, 600, "end")
    c_x, c_y = at(26, 225)
    s.text(c_x - 16, c_y + 22, "C · south-west", 12, INK, 600, "end")
    s.text(ax, ay + 62, "A · east", 12, INK, 600, "middle")

    # right panel: legend and rules
    x0 = 512
    items = [("dot", C_V2, "Ambusher: holds a predicted spot (V2)"),
             ("dot", C_BASE, "Chaser: follows the player"),
             ("dot", INK, "Player: a bot persona, or a human"),
             ("diamond", C_ADA, "Candidate spots for the bandit: 12, 18, 25 u"),
             ("refuge", INK2, "Refuge at the end of a route, 26 u out")]
    y = 104
    for kind, color, label in items:
        if kind == "dot":
            s.dot(x0 + 6, y - 4, color, 5.5)
        elif kind == "diamond":
            s.add(f'<rect x="{x0 + 1.5}" y="{y - 8.5}" width="9" height="9" fill="{color}" '
                  f'transform="rotate(45 {x0 + 6} {y - 4})"/>')
        else:
            s.add(f'<circle cx="{x0 + 6}" cy="{y - 4}" r="6.5" fill="{CARD}" stroke="{color}" stroke-width="1.5"/>')
        s.text(x0 + 20, y, label, 12, INK2)
        y += 24
    s.line(x0, y - 4, 856, y - 4, GRID)
    s.text(x0, y + 20, "Rules that shape the problem", 13, INK, 600)
    paras = [["Player speed 5, zombie speed 3: a chaser can't catch", "a runner, so the horde has to be waiting ahead."],
             ["The bot counts itself ambushed if, after 1 s of fleeing,", "it takes damage, meets a zombie within 4 u ahead",
              "on its route, or finds one at the refuge."],
             ["A wave is Day 3's horde: 20 zombies, 5 alive at once.", "An episode is 4 waves; then the player model resets,",
              "so every episode is a new player."]]
    y += 44
    for p in paras:
        s.add(f'<circle cx="{x0 + 3}" cy="{y - 4}" r="2.2" fill="{INK2}"/>')
        s.lines(x0 + 12, y, p, 12, 17)
        y += 17 * len(p) + 10
    s.save("arena.svg")


# ---------------------------------------------------------------- 3. lineage
def fig_lineage():
    s = SVG(880, 300, "Horde commander versions",
            "Timeline of the five commanders: Baseline (control), V1 reinforcement learning (fails the route test), "
            "V2 predictive (passes, best overall), V2 + layer 1 and V2 + layer 2 adaptive options (no gain over V2).")
    s.header("Five horde commanders, one arena",
             ["Each was scored against the same bot players with the same eval player. The two adaptive layers are "
              "options on top of V2."])
    cards = [
        ("2026-09-24", "Baseline", C_BASE, ["The game's own AI:", "chase the player within", "5 u, else walk straight", "to the base."],
         "control", "the control group", ""),
        ("2026-09-25", "V1 · RL", C_V1, ["A PPO policy orders 3", "squads from 111", "observations; 2 M", "decisions of training."],
         "bad", "fails the route test", "much more lethal"),
        ("2026-09-26", "V2 · Predictive", C_V2, ["Predicts where this", "player will flee, holds", "2 zombies at that", "refuge before it does."],
         "good", "passes the route test", "best overall"),
        ("2026-10-06 → 07", "V2 + layer 1", C_ADA, ["Thompson-samples the", "direction; a bandit", "learns how far out", "to wait. Runs 1–3."],
         "same", "no gain over V2", ""),
        ("2026-10-08", "V2 + layer 2", C_ADA, ["Learns whether the", "player avoids routes", "where it met zombies;", "predicts around it. Run 4."],
         "same", "no gain over V2", ""),
    ]
    w, gap, x = 158, 12, 21
    s.line(x + w / 2, 104, x + 4 * (w + gap) + w / 2, 104, AXIS, 1.5)
    for k, (date, name, color, body, kind, verdict, verdict2) in enumerate(cards):
        cx = x + w / 2
        s.text(cx, 92, date, 11.5, MUTED, anchor="middle")
        s.dot(cx, 104, color, 6)
        stroke = f'stroke="{C_V2}" stroke-width="1.5"' if name.startswith("V2 ·") else f'stroke="{GRID}"'
        s.add(f'<rect x="{x}" y="120" width="{w}" height="164" rx="10" fill="{CARD}" {stroke}/>')
        s.add(f'<path d="M{x + 10},121.5 H{x + w - 10}" stroke="{color}" stroke-width="3" stroke-linecap="round"/>')
        s.text(x + 12, 146, name, 14, INK, 600)
        s.lines(x + 12, 168, body, 12, 17)
        s.line(x + 12, 242, x + w - 12, 242, GRID)
        status_icon(s, x + 19.5, 259, kind)
        s.text(x + 32, 263.5, verdict, 12, INK, 600)
        if verdict2:
            s.text(x + 32, 278, verdict2, 11.5, MUTED)
        x += w + gap
    s.save("commander-lineage.svg")


# ---------------------------------------------------------------- 4. pressure vs anticipation
def fig_scatter():
    s = SVG(880, 418, "Pressure versus anticipation",
            "Two scatter plots (RunnerA, RunnerB80). x: share of flee decisions with a zombie already on the bot's "
            "chosen route; y: player damage per wave. V1 points sit high and left (lethal, no anticipation), V2 points "
            "sit low and right (on the route, near-baseline damage).")
    s.header("V1 learned pressure, V2 learned the route",
             ["Phase 5 evaluation, one point per commander. x: flee decisions where a zombie was already on the route",
              "the bot then took (bot outside that sector). y: damage the player took per wave."])
    s.legend(24, 108, [(C_BASE, "Baseline", "dot"), (C_V1, "V1 · RL (PPO)", "dot"), (C_V2, "V2 · Predictive", "dot")])
    panels = [
        ("RunnerA · always flees by route A", 80,
         [("Baseline", C_BASE, 1, 1, 9, -6, "start"), ("ppo_01", C_V1, 4, 23, 9, -6, "start"),
          ("ppo_02", C_V1, 2, 19, 9, 13, "start"), ("frequency", C_V2, 55, 5, 9, 4, "start"),
          ("learned", C_V2, 50, 8, -6, -11, "end")]),
        ("RunnerB80 · flees by route B 80% of the time", 516,
         [("Baseline", C_BASE, 3, 10, 9, -6, "start"), ("ppo_01", C_V1, 8, 61, 9, 4, "start"),
          ("ppo_02", C_V1, 7, 43, 9, 4, "start"), ("frequency", C_V2, 33, 23, 9, -6, "start"),
          ("learned", C_V2, 30, 15, 9, 12, "start")]),
    ]
    top, bottom, pw = 150, 340, 320
    for title, x0, pts in panels:
        s.text(x0, 138, title, 13, INK, 600)
        X = lambda v: x0 + v / 60 * pw
        Y = lambda v: bottom - v / 70 * (bottom - top)
        for t in (0, 20, 40, 60):
            s.line(x0, Y(t), x0 + pw, Y(t), GRID if t else AXIS)
            s.text(x0 - 8, Y(t) + 4, str(t), 11, MUTED, anchor="end")
            s.text(X(t), bottom + 17, f"{t}%", 11, MUTED, anchor="middle")
        s.text(x0 + pw / 2, bottom + 38, "zombie on the chosen route at the flee decision", 11.5, INK2, anchor="middle")
        yc = (top + bottom) / 2
        s.text(x0 - 34, yc, "damage per wave", 11.5, INK2, anchor="middle",
               extra=f' transform="rotate(-90 {x0 - 34} {yc})"')
        for label, color, vx, vy, dx, dy, anchor in pts:
            s.dot(X(vx), Y(vy), color, 5.5)
            s.text(X(vx) + dx, Y(vy) + dy, label, 11.5, INK2, anchor=anchor)
    s.save("pressure-vs-anticipation.svg")


# ---------------------------------------------------------------- 5. ground truth: ambusher vs chaser
def fig_ground_truth():
    rows = [("RunnerA", "always route A", 42, 16), ("RunnerB80", "route B 80%", 27, 37),
            ("Adaptive", "avoids ambushed routes", 22, 37), ("Mixed", "flees 60%, B over C", 21, 42),
            ("Random", "no fixed habit", 20, 31)]
    h = 128 + 40 * len(rows) + 46
    s = SVG(880, h, "Who ambushed the bot under V2",
            "Stacked bars per persona: share of flee decisions ambushed by a holding zombie (ambusher) and by a "
            "chaser, under plain V2 in run 3. Only RunnerA's ambushes are mostly by ambushers.")
    s.header("Most 'ambushes' were chasers catching up",
             ["Plain V2, run 3, 2 repeats pooled. Share of the bots' flee decisions on which the bot's own ambush check",
              "fired, split by the zombie that triggered it."])
    s.legend(24, 108, [(C_V2, "by an ambusher (a zombie holding an ambush)", "square"),
                       (C_V2_LIGHT, "by a chaser", "square")])
    x0, x1 = 196, 820
    X = lambda v: x0 + v / 100 * (x1 - x0)
    top = 128
    bottom = top + 40 * len(rows) + 4
    for t in (0, 25, 50, 75, 100):
        s.line(X(t), top, X(t), bottom, GRID if t else AXIS)
        s.text(X(t), bottom + 17, f"{t}%", 11, MUTED, anchor="middle")
    for k, (name, desc, amb, cha) in enumerate(rows):
        y = top + 12 + 40 * k
        s.text(24, y + 10, name, 12.5, INK, 600)
        s.text(24, y + 25, desc, 11, MUTED)
        s.add(hbar(X(0), X(amb) - 1, y, 22, C_V2, round_end=False))
        s.add(hbar(X(amb) + 1, X(amb + cha), y, 22, C_V2_LIGHT))
        s.text(X(amb) - 8, y + 15.5, f"{amb}%", 11.5, "#ffffff", 600, "end")
        s.text(X(amb) + 9, y + 15.5, f"{cha}%", 11.5, INK, None)
        share = round(100 * amb / (amb + cha))
        s.rich(X(amb + cha) + 10, y + 15.5, [(f"{amb + cha}% ambushed", INK2, 600),
                                             (f"  ·  {share}% of them by ambushers", MUTED, None)], 11.5)
    s.save("ambusher-vs-chaser.svg")


# ---------------------------------------------------------------- 6. contact window artifact
def fig_contact_window():
    data = {"Route A": ([75, 74, 73], [60, 37, 25]), "Route B": ([74, 62, 69], [66, 30, 36]),
            "Route C": ([68, 57, 62], [59, 61, 20])}
    s = SVG(880, 404, "Contact rate by ambush distance and window",
            "Small multiples for routes A, B, C: contact rate of the bandit's ambush sites at 12, 18 and 25 units. "
            "Watching only during the escape favours the near site; watching 5 s past the escape end, all distances "
            "score about the same.")
    s.header("The bandit's 'best distance' came from the measurement window",
             ["Run 3: how often an ambusher at 12, 18 or 25 u came within 6 u of a player who ran that route. "
              "Watched only until",
              "the escape tracker ended the escape, near sites win; watched 5 s longer, every distance works about "
              "equally."])
    s.legend(24, 108, [(C_ADA, "Bandit: watched until 5 s after the escape ends", "dot"),
                       (C_VIO, "Bandit-tail0: watched during the escape only (run 2's reward)", "dot")], gap=22)
    top, bottom, pw = 150, 330, 200
    for k, (route, (tail, esc_only)) in enumerate(data.items()):
        x0 = 76 + k * 280
        X = lambda d: x0 + 22 + (d - 12) / 13 * (pw - 44)
        Y = lambda v: bottom - v / 100 * (bottom - top)
        s.text(x0, 138, route + (" · fewer trials" if route == "Route C" else ""), 13, INK, 600)
        for t in (0, 25, 50, 75, 100):
            s.line(x0, Y(t), x0 + pw, Y(t), GRID if t else AXIS)
            if k == 0:
                s.text(x0 - 8, Y(t) + 4, f"{t}%", 11, MUTED, anchor="end")
        for d in (12, 18, 25):
            s.text(X(d), bottom + 17, f"{d} u", 11, MUTED, anchor="middle")
        for vals, color in ((esc_only, C_VIO), (tail, C_ADA)):
            pts = " ".join(f"{X(d):.1f},{Y(v):.1f}" for d, v in zip((12, 18, 25), vals))
            s.add(f'<polyline points="{pts}" fill="none" stroke="{color}" stroke-width="2" '
                  f'stroke-linejoin="round" stroke-linecap="round"/>')
            for d, v in zip((12, 18, 25), vals):
                s.dot(X(d), Y(v), color, 4.5)
        if k == 0:
            for d, v in zip((12, 18, 25), tail):
                s.text(X(d), Y(v) - 11, f"{v}%", 11, INK2, anchor="middle")
            for d, v in zip((12, 18, 25), esc_only):
                s.text(X(d), Y(v) + 19, f"{v}%", 11, INK2, anchor="middle")
    s.text(76 + 280 + pw / 2, bottom + 40, "distance of the ambush site from the base", 11.5, INK2, anchor="middle")
    s.save("contact-window.svg")


# ---------------------------------------------------------------- 7. adaptive results
def fig_adaptive():
    groups = [
        ("Layer 1 · run 3, 2 repeats", [
            ("V2 (control)", True, (42, 35, 49), (27, 23, 32), (22, 17, 28)),
            ("Thompson direction", False, (36, 30, 43), (22, 18, 27), (12, 9, 17)),
            ("Bandit", False, (40, 33, 47), (27, 22, 32), (23, 18, 30)),
            ("Thompson + Bandit", False, (38, 31, 45), (22, 18, 26), (14, 10, 19)),
            ("Bandit · joint sector × distance", False, (32, 26, 38), (28, 23, 32), (20, 15, 25)),
            ("Bandit · escape-only window", False, (40, 32, 47), (28, 23, 33), (21, 16, 28)),
            ("Bandit · redraw every escape", False, (38, 31, 45), (28, 23, 32), (21, 16, 26)),
            ("Bandit · γ = 1 (no forgetting)", False, (41, 34, 48), (22, 18, 27), (17, 12, 22)),
            ("Bandit · shared arms", False, (37, 30, 45), (25, 20, 29), (17, 13, 22)),
            ("Bandit · kept across episodes", False, (39, 32, 47), (28, 23, 32), (20, 15, 26)),
        ]),
        ("Layer 2 · run 4, 6 repeats", [
            ("V2 (control)", True, (44, 40, 48), (30, 28, 33), (23, 20, 26)),
            ("Reaction model", False, (42, 38, 46), (31, 28, 33), (21, 18, 25)),
            ("Reaction model · prior 0.8", False, (44, 40, 48), (28, 26, 31), (23, 20, 27)),
        ]),
    ]
    pitch, ghead = 25, 34
    n_rows = sum(len(g[1]) for g in groups)
    top = 150
    height = top + n_rows * pitch + len(groups) * ghead + 52
    s = SVG(880, height, "Adaptive variants versus plain V2",
            "Dot plot with 95% intervals: share of flee decisions ambushed by a holding zombie, for RunnerA, RunnerB80 "
            "and Adaptive, for every adaptive variant in runs 3 and 4. The shaded band is plain V2's interval; no "
            "variant is clearly above it, and the Thompson variants are below it for Adaptive.")
    s.header("No adaptive variant beat plain V2",
             ["Share of flee decisions ambushed by a holding zombie (the ground truth since run 3), with 95% Wilson "
              "intervals.",
              "The band is plain V2's interval in the same run. Repeats of V2 alone spread about as wide."])
    s.legend(24, 108, [(C_V2, "plain V2 (control)", "dot"), (C_ADA, "V2 with an adaptive option", "dot")])
    panels = [("RunnerA", 236), ("RunnerB80", 452), ("Adaptive", 668)]
    pw = 186
    X = lambda x0, v: x0 + v / 50 * pw
    y = top
    for _, x0 in panels:
        pass
    rows_y = []
    for gname, rows in groups:
        gy = y
        y += ghead
        ys = [y + pitch * i + pitch / 2 for i in range(len(rows))]
        rows_y.append((gname, gy, ys, rows))
        y += pitch * len(rows)
    bottom = y + 4
    for name, x0 in panels:
        s.text(x0, top - 10, name, 13, INK, 600)
        for t in (0, 25, 50):
            s.line(X(x0, t), top, X(x0, t), bottom, GRID if t else AXIS)
            if t or x0 == panels[0][1]:
                s.text(X(x0, t), bottom + 17, f"{t}%", 11, MUTED, anchor="middle")
    for gname, gy, ys, rows in rows_y:
        s.text(24, gy + 22, gname, 12, INK, 600)
        for p, (_, x0) in enumerate(panels):
            v2 = rows[0][2 + p]
            s.add(f'<rect x="{X(x0, v2[1]):.1f}" y="{gy + 28}" width="{X(x0, v2[2]) - X(x0, v2[1]):.1f}" '
                  f'height="{ys[-1] - gy - 28 + pitch / 2:.1f}" fill="{C_V2}" fill-opacity="0.10"/>')
        for ry, (label, is_v2, *vals) in zip(ys, rows):
            s.text(36, ry + 4, label, 12, INK if is_v2 else INK2, 600 if is_v2 else None)
            for p, (pname, x0) in enumerate(panels):
                v, lo, hi = vals[p]
                color = C_V2 if is_v2 else C_ADA
                s.line(X(x0, lo), ry, X(x0, hi), ry, color, 2, ' stroke-linecap="round"')
                s.dot(X(x0, v), ry, color, 4.5)
                if is_v2 or (pname == "Adaptive" and v <= 14):
                    s.text(X(x0, hi) + 7, ry + 4, f"{v}%", 11, INK2)
    s.text(X(452, 25), bottom + 38, "flee decisions ambushed by a holding zombie", 11.5, INK2, anchor="middle")
    s.save("adaptive-variants.svg")


fig_overview()
fig_arena()
fig_lineage()
fig_scatter()
fig_ground_truth()
fig_contact_window()
fig_adaptive()
print("wrote", sorted(os.listdir(OUT)))
