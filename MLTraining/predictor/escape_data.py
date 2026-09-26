"""Escape-prediction data from arena telemetry (format 3: escape_start carries the EscapeFeatures vector).

One sample per valid escape that moved more than 1 unit:
  features     the exact vector the game's LearnedPredictor sees at escape onset (EscapeFeatures.cs; 66
               values for 17 zones). Early format-3 data logged the first 58; the last 8 (first-escape
               directions) are replayed here with the same rules as PlayerProfile
  label        the escape's movement direction, 8 sectors (0 = east, CCW), like PlayerProfile's direction
               bins. Directions, not end zones: zones are centred on the base, but the player doesn't
               start at its centre, so escapes that end early land in a neighbouring sector's zone.
  freq         the FrequencyPredictor's direction distribution at onset, replayed from the escape
               sequence with the same update rules as PlayerProfile.AddEscape (decay; direction counts
               per start zone, decayed per start zone) and the same smoothing as FrequencyPredictor.cs,
               so offline numbers match what the commander would have predicted
Profiles are replayed per episode (the arena resets the profile every episode).
"""
import math
import glob
import json
import os

import numpy as np

TELEMETRY_ROOT = os.path.expanduser(
    "~/Library/Application Support/DefaultCompany/Waste Land 2039/EnemyAITelemetry")
SECTORS = ["E", "NE", "N", "NW", "W", "SW", "S", "SE"]  # PlayerProfile.DirectionName order (0 = east, CCW)
ESCAPE_DECAY = 0.85   # ProfileSettings.escapeDecay default
ROUTE_PRIOR = 2.0     # FrequencyPredictor defaults
DIRECTION_PRIOR = 1.0


def sector_of(zone_name):
    """'NW2' -> 3; 'Core' -> -1 (same as EscapeSectors.SectorOf for the radial map)."""
    if not zone_name or zone_name == "Core":
        return -1
    return SECTORS.index(zone_name.rstrip("0123456789"))


def direction_of(dx, dy):
    """RadialZoneMap.DirectionToSector(direction, 8): sector centred on its angle, 0 = east."""
    angle = math.degrees(math.atan2(dy, dx)) % 360.0
    return int((angle + 22.5) // 45.0) % 8


def feature_size(zone_count):
    """EscapeFeatures.Size: profile (8 + K + 8) + zone one-hot K + threat 8 + first-escape directions 8."""
    return 8 + zone_count + 8 + zone_count + 8 + 8


def threat_slice(zone_count):
    """EscapeFeatures.ThreatOffset .. +8"""
    start = 8 + zone_count + 8 + zone_count
    return slice(start, start + 8)


def batch_dirs(prefixes):
    dirs = []
    for prefix in prefixes:
        dirs += sorted(d for d in glob.glob(os.path.join(TELEMETRY_ROOT, prefix + "*")) if os.path.isdir(d))
    return dirs


class ReplayProfile:
    """Direction counts of PlayerProfile, updated exactly like AddEscape."""

    def __init__(self, zone_count):
        self.zone_count = zone_count
        self.directions = np.zeros(8)
        self.first = np.zeros(8)   # first escape of each engagement
        self.by_start = {}         # (start zone, direction) -> weight
        self.last_engagement = -1

    def add(self, start, dx, dy, engagement):
        if dx * dx + dy * dy > 1.0:
            d = direction_of(dx, dy)
            self.directions *= ESCAPE_DECAY
            self.directions[d] += 1.0
            for key in self.by_start:
                if key[0] == start:  # per-start-zone decay
                    self.by_start[key] *= ESCAPE_DECAY
            self.by_start[(start, d)] = self.by_start.get((start, d), 0.0) + 1.0
            if engagement != self.last_engagement:
                self.first *= ESCAPE_DECAY
                self.first[d] += 1.0
        self.last_engagement = engagement

    def first_distribution(self):
        total = self.first.sum()
        return self.first / total if total > 0 else np.zeros(8)

    def frequency_predict(self, current_zone):
        evidence = self.directions.sum()
        p = self.directions / evidence if evidence > 0 else np.zeros(8)
        overall = (evidence * p + DIRECTION_PRIOR / 8) / (evidence + DIRECTION_PRIOR)
        route = np.zeros(8)
        if current_zone == 0:
            route = self.first.copy()          # from the base: first escape of the next engagement
        elif 0 < current_zone < self.zone_count:
            for (start, d), weight in self.by_start.items():
                if start == current_zone:
                    route[d] += weight
        return (route + ROUTE_PRIOR * overall) / (route.sum() + ROUTE_PRIOR)

    @property
    def evidence(self):
        return float(self.directions.sum())


def load_session(path):
    with open(os.path.join(path, "session.json"), encoding="utf-8") as f:
        session = json.load(f)
    zones = session["zones"]
    index = {name: i for i, name in enumerate(zones)}

    samples = []
    persona, episode = "human", 0
    profile = ReplayProfile(len(zones))
    starts = {}
    in_episode = 0
    with open(os.path.join(path, "events.jsonl"), encoding="utf-8") as f:
        for line in f:
            e = json.loads(line)
            kind = e.get("type")
            if kind == "episode_start":
                persona = e.get("persona", persona)
                episode = e.get("episode", episode)
            elif kind == "profile_reset":
                profile = ReplayProfile(len(zones))
                in_episode = 0
            elif kind == "escape_start":
                starts[e["id"]] = e
            elif kind == "escape_end" and e.get("valid"):
                start_event = starts.pop(e["id"], {})
                start = index.get(e.get("start_zone"), -1)
                dx, dy = e.get("dx", 0.0), e.get("dy", 0.0)
                if dx * dx + dy * dy <= 1.0:
                    profile.add(start, dx, dy, e.get("engagement", -1))  # no direction, but it still ends the engagement's first escape
                    continue
                features = start_event.get("features")
                if features is not None and len(features) == feature_size(len(zones)) - 8:
                    features = list(features) + list(profile.first_distribution())  # early format-3 data
                samples.append({
                    "session": os.path.basename(path),
                    "persona": persona,
                    "episode": episode,
                    "index": in_episode,              # valid escapes earlier in this episode
                    "evidence": profile.evidence,
                    "start": start,
                    "start_zone": zones[start] if start >= 0 else "?",
                    "label": direction_of(dx, dy),
                    "features": np.asarray(features, dtype=np.float64) if features is not None else None,
                    "freq": profile.frequency_predict(start),
                })
                profile.add(start, dx, dy, e.get("engagement", -1))
                in_episode += 1
    return zones, samples


def load(prefixes):
    """All samples from the given batch prefixes; returns (zone names, samples)."""
    zones, samples = None, []
    for path in batch_dirs(prefixes):
        session_zones, session_samples = load_session(path)
        if zones is None:
            zones = session_zones
        elif session_zones != zones:
            raise ValueError(f"{path}: different zone map")
        samples += session_samples
    return zones, samples


class LearnedModel:
    """Same maths as LearnedPredictor.cs."""

    def __init__(self, weights_json):
        with open(weights_json, encoding="utf-8") as f:
            w = json.load(f)
        if w.get("version") != 2:
            raise ValueError(f"{weights_json}: version {w.get('version')}, need 2 (direction output)")
        self.k, self.f = w["classCount"], w["featureCount"]
        self.W = np.asarray(w["weights"]).reshape(self.k, self.f)
        self.b = np.asarray(w["bias"])
        self.mean = np.asarray(w.get("featureMean") or np.zeros(self.f))
        self.scale = np.asarray(w.get("featureScale") or np.ones(self.f))
        self.scale = np.where(self.scale > 1e-6, self.scale, 1.0)

    def predict(self, x):
        logits = self.W @ ((x - self.mean) / self.scale) + self.b
        logits -= logits.max()
        p = np.exp(logits)
        return p / p.sum()
