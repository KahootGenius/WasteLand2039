// Tests for the V2 escape predictors (EnemyAI/Prediction): feature layout, the frequency (Markov)
// predictor on simulated runners, route conditioning, sector aggregation, and the learned
// (softmax-regression) predictor's maths and validation.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static partial class LogicTests
{
    static int ZoneIndex(IZoneMap zones, string name) =>
        Enumerable.Range(0, zones.ZoneCount).First(z => zones.GetZoneName(z) == name);

    static void PredictionTests()
    {
        Console.WriteLine("Escape prediction (V2)");
        var zones = new RadialZoneMap(Vector2.zero, 8, 6f, 20f);
        int core = 0, e1 = ZoneIndex(zones, "E1"), e2 = ZoneIndex(zones, "E2"), n2 = ZoneIndex(zones, "N2"),
            nw2 = ZoneIndex(zones, "NW2"), sw2 = ZoneIndex(zones, "SW2");

        // ---- features
        var fresh = new PlayerProfile(zones.ZoneCount);
        var features = new float[EscapeFeatures.Size(zones.ZoneCount)];
        var enemies = new List<Vector2> { new Vector2(5f, 0.5f), new Vector2(6f, -0.5f), new Vector2(0f, 7f), new Vector2(40f, 0f) };
        EscapeFeatures.Build(fresh, e1, new Vector2(1f, 0f), enemies, 15f, features);
        int oneHot = EscapeFeatures.ProfileSize(zones.ZoneCount);
        int threat = oneHot + zones.ZoneCount;
        Check(features.Length == 58, "feature vector size 58 for 17 zones", $"= {features.Length}");
        Check(features[oneHot + e1] == 1f && features.Skip(oneHot).Take(zones.ZoneCount).Sum() == 1f, "current zone one-hot");
        Check(Math.Abs(features[threat + 0] - 2f / 3f) < 1e-5 && Math.Abs(features[threat + 2] - 1f / 3f) < 1e-5 &&
              Math.Abs(features.Skip(threat).Take(8).Sum() - 1f) < 1e-5,
            "threat directions: share per direction within the radius (far enemy ignored)",
            $"= E {features[threat]:F2}, N {features[threat + 2]:F2}");

        // ---- sectors
        Check(EscapeSectors.SectorOf(zones, e2) == 0 && EscapeSectors.SectorOf(zones, nw2) == 3 &&
              EscapeSectors.SectorOf(zones, sw2) == 5 && EscapeSectors.SectorOf(zones, core) == -1,
            "sector of a zone (E2 = 0, NW2 = 3, SW2 = 5, Core = none)");

        // ---- frequency predictor
        var frequency = new FrequencyPredictor();
        var probabilities = new float[zones.ZoneCount];
        var sectors = new float[EscapeSectors.Count];
        frequency.Predict(new PredictionInput { Profile = fresh, CurrentZone = core }, probabilities);
        Check(Math.Abs(probabilities.Sum() - 1f) < 1e-5 && probabilities.Max() - probabilities.Min() < 1e-6,
            "no data: uniform over zones");

        var sim = new Sim(21);
        var afterRounds = new List<float>();
        for (int round = 0; round < 10; round++)
        {
            sim.RunRound(180f, 0f, 4f, shootWhileRunning: false);
            frequency.Predict(new PredictionInput { Profile = sim.Profile, CurrentZone = core }, probabilities);
            float coreShare = EscapeSectors.Aggregate(sim.Zones, probabilities, sectors);
            afterRounds.Add(sectors[0]);
            if (round == 0)
                Check(Math.Abs(sectors.Sum() + coreShare - 1f) < 1e-5, "sector probabilities + core sum to 1");
        }
        Check(afterRounds[2] > 0.5f, "runner east: P(east) > 50% after 3 escapes", $"= {Pct(afterRounds[2])}");
        Check(afterRounds[9] > 0.8f, "runner east: P(east) > 80% after 10 escapes",
            $"= {string.Join(" ", afterRounds.Select(p => Pct(p)))}");

        // Markov: the route depends on where the escape starts (Core → E2, then E2 → N2)
        var routes = new PlayerProfile(zones.ZoneCount);
        var state = routes.ExportState();
        state.escapes = 6;
        state.destinationWeights[e2] = 3f;
        state.destinationWeights[n2] = 3f;
        state.routeFrom = new[] { core, e2 };
        state.routeTo = new[] { e2, n2 };
        state.routeWeights = new[] { 3f, 3f };
        routes.TryImportState(state, out _);
        frequency.Predict(new PredictionInput { Profile = routes, CurrentZone = core }, probabilities);
        int fromCore = Array.IndexOf(probabilities, probabilities.Max());
        float pFromCore = probabilities.Max();
        frequency.Predict(new PredictionInput { Profile = routes, CurrentZone = e2 }, probabilities);
        int fromE2 = Array.IndexOf(probabilities, probabilities.Max());
        Check(fromCore == e2 && fromE2 == n2, "route conditioning: from Core → E2, from E2 → N2",
            $"= {zones.GetZoneName(fromCore)} ({Pct(pFromCore)}), {zones.GetZoneName(fromE2)} ({Pct(probabilities.Max())})");

        // ---- learned predictor
        int featureCount = EscapeFeatures.Size(zones.ZoneCount);
        var weights = new LearnedPredictorWeights
        {
            zoneCount = zones.ZoneCount,
            featureCount = featureCount,
            weights = new float[zones.ZoneCount * featureCount],
            bias = new float[zones.ZoneCount]
        };
        weights.bias[e2] = (float)Math.Log(3.0); // softmax(bias): E2 three times as likely as each other zone
        var learned = new LearnedPredictor(weights);
        var input = new PredictionInput { Features = new float[featureCount] };
        learned.Predict(input, probabilities);
        Check(Math.Abs(probabilities.Sum() - 1f) < 1e-5 && Math.Abs(probabilities[e2] / probabilities[n2] - 3f) < 1e-3,
            "learned: softmax of bias when features are zero", $"= E2 {Pct(probabilities[e2])}");

        weights.weights[nw2 * featureCount + oneHot + core] = 5f; // "from Core, the player goes NW"
        input.Features[oneHot + core] = 1f;
        learned.Predict(input, probabilities);
        Check(Array.IndexOf(probabilities, probabilities.Max()) == nw2, "learned: a feature weight moves the prediction",
            $"= NW2 {Pct(probabilities[nw2])}");

        weights.featureMean = Enumerable.Repeat(0.5f, featureCount).ToArray();
        weights.featureScale = Enumerable.Repeat(0.5f, featureCount).ToArray();
        learned = new LearnedPredictor(weights);
        learned.Predict(input, probabilities);
        float expected = 5f * (1f - 0.5f) / 0.5f; // standardized one-hot: (1 - mean) / scale
        Check(Array.IndexOf(probabilities, probabilities.Max()) == nw2 &&
              Math.Abs(Math.Log(probabilities[nw2] / probabilities[n2]) - expected) < 1e-3,
            "learned: features standardized with the stored mean / scale");

        bool rejected = false;
        try { new LearnedPredictor(new LearnedPredictorWeights { zoneCount = 17, featureCount = 50, weights = new float[850], bias = new float[17] }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "learned: wrong feature count rejected", $"({LearnedPredictor.Validate(new LearnedPredictorWeights { zoneCount = 17, featureCount = 50 })})");
    }
}
