"""Train the V2 LearnedPredictor: multinomial logistic regression, features -> escape direction (8 sectors).

Usage (from the repo root, conda env "mlagents" or any Python with NumPy):
  python MLTraining/predictor/train_predictor.py --data 20260926_0XXX [more batch prefixes] \
      --out Assets/ML/Predictors/EscapePredictor_v1.json

Data: arena telemetry format 3 (escape_start carries EscapeFeatures), ideally from MLArena_Data
(Baseline commander, persona pool, routes shuffled per episode). Sessions are split into train /
held-out by session, so held-out numbers are on arenas the model never saw.

Output: JSON in the LearnedPredictorWeights layout, version 2 (weights direction-major, standardization
included), which PredictiveCommander loads from a TextAsset.
"""
import argparse
import datetime
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import escape_data  # noqa: E402


def softmax(z):
    z = z - z.max(axis=1, keepdims=True)
    e = np.exp(z)
    return e / e.sum(axis=1, keepdims=True)


def train(X, y, k, l2, epochs, lr, seed):
    rng = np.random.default_rng(seed)
    n, f = X.shape
    W = rng.normal(0.0, 0.01, (f, k))
    b = np.zeros(k)
    Y = np.zeros((n, k))
    Y[np.arange(n), y] = 1.0
    # Adam, full batch (a few thousand samples x 58 features: seconds)
    m_w, v_w, m_b, v_b = np.zeros_like(W), np.zeros_like(W), np.zeros_like(b), np.zeros_like(b)
    beta1, beta2, eps = 0.9, 0.999, 1e-8
    for t in range(1, epochs + 1):
        P = softmax(X @ W + b)
        grad_w = X.T @ (P - Y) / n + l2 * W
        grad_b = (P - Y).mean(axis=0)
        m_w = beta1 * m_w + (1 - beta1) * grad_w
        v_w = beta2 * v_w + (1 - beta2) * grad_w ** 2
        m_b = beta1 * m_b + (1 - beta1) * grad_b
        v_b = beta2 * v_b + (1 - beta2) * grad_b ** 2
        W -= lr * (m_w / (1 - beta1 ** t)) / (np.sqrt(v_w / (1 - beta2 ** t)) + eps)
        b -= lr * (m_b / (1 - beta1 ** t)) / (np.sqrt(v_b / (1 - beta2 ** t)) + eps)
        if t % max(1, epochs // 5) == 0:
            loss = -np.log(P[np.arange(n), y] + 1e-12).mean() + 0.5 * l2 * (W ** 2).sum()
            print(f"  epoch {t:5d}  loss {loss:.4f}")
    return W, b


def accuracy(samples, predict):
    """Top-1 / top-2 direction accuracy."""
    top1 = top2 = 0
    for s in samples:
        order = np.argsort(-predict(s))
        top1 += order[0] == s["label"]
        top2 += s["label"] in order[:2]
    n = max(1, len(samples))
    return top1 / n, top2 / n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", nargs="+", required=True, help="telemetry batch prefixes (format 3)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--holdout", type=float, default=0.25, help="share of sessions held out")
    ap.add_argument("--l2", type=float, default=1e-3)
    ap.add_argument("--epochs", type=int, default=1500)
    ap.add_argument("--lr", type=float, default=0.05)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--no-threat", action="store_true",
                    help="zero the threat-direction features: a model of the player's history only, usable before "
                         "the flee starts (the ambush is placed in advance, when the threat at onset isn't known yet)")
    args = ap.parse_args()

    zones, samples = escape_data.load(args.data)
    samples = [s for s in samples if s["features"] is not None]
    if not samples:
        sys.exit("no samples with features (telemetry format 3 needed)")
    if args.no_threat:
        threat = escape_data.threat_slice(len(zones))
        for s in samples:
            s["features"] = s["features"].copy()
            s["features"][threat] = 0.0
    k, f = 8, len(samples[0]["features"])

    sessions = sorted({s["session"] for s in samples})
    rng = np.random.default_rng(args.seed)
    held = set(rng.choice(sessions, size=max(1, int(round(len(sessions) * args.holdout))), replace=False))
    train_s = [s for s in samples if s["session"] not in held]
    test_s = [s for s in samples if s["session"] in held]
    print(f"{len(samples)} escapes from {len(sessions)} sessions; train {len(train_s)}, held-out {len(test_s)} "
          f"({len(held)} sessions); {len(zones)} zones, {f} features, {k} direction classes")

    X = np.stack([s["features"] for s in train_s])
    y = np.array([s["label"] for s in train_s])
    mean = X.mean(axis=0)
    scale = X.std(axis=0)
    scale = np.where(scale > 1e-3, scale, 1.0)
    W, b = train((X - mean) / scale, y, k, args.l2, args.epochs, args.lr, args.seed)

    weights = {
        "version": 2,
        "zoneCount": len(zones),
        "featureCount": f,
        "classCount": k,
        "weights": [round(float(v), 6) for v in W.T.reshape(-1)],   # direction-major, like LearnedPredictor.cs
        "bias": [round(float(v), 6) for v in b],
        "featureMean": [round(float(v), 6) for v in mean],
        "featureScale": [round(float(v), 6) for v in scale],
        "trainedOn": " ".join(args.data) + (" (no threat features)" if args.no_threat else ""),
        "notes": f"{datetime.date.today()} softmax regression over escape directions, l2 {args.l2}, {args.epochs} epochs, "
                 f"{len(train_s)} train escapes / {len(test_s)} held out ({len(held)} sessions)",
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(weights, fh, indent=1)
    print(f"wrote {args.out}")

    model = escape_data.LearnedModel(args.out)
    core = [s for s in test_s if s["start_zone"] == "Core"]
    print(f"\nheld-out direction accuracy   top-1    top-2    top-1 from Core ({len(core)})")
    for name, predict in (("chance", None),
                          ("frequency (profile)", lambda s: s["freq"]),
                          ("learned", lambda s: model.predict(s["features"]))):
        if predict is None:
            print(f"  {name:22s}   {1 / k:6.1%}   {2 / k:6.1%}   {1 / k:6.1%}")
            continue
        a1, a2 = accuracy(test_s, predict)
        c1, _ = accuracy(core, predict)
        print(f"  {name:22s}   {a1:6.1%}   {a2:6.1%}   {c1:6.1%}")


if __name__ == "__main__":
    main()
