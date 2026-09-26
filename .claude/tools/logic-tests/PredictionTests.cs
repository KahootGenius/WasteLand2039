// Tests for the V2 escape predictors (EnemyAI/Prediction): feature layout, sectors, the frequency (Markov)
// predictor over escape directions on simulated runners, start-zone conditioning and per-start memory, and
// the learned (softmax-regression) predictor's maths and validation.

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
        Check(features.Length == 66 && EscapeFeatures.ThreatOffset(zones.ZoneCount) == oneHot + zones.ZoneCount,
            "feature vector size 66 for 17 zones", $"= {features.Length}");
        Check(features[oneHot + e1] == 1f && features.Skip(oneHot).Take(zones.ZoneCount).Sum() == 1f, "current zone one-hot");
        Check(Math.Abs(features[threat + 0] - 2f / 3f) < 1e-5 && Math.Abs(features[threat + 2] - 1f / 3f) < 1e-5 &&
              Math.Abs(features.Skip(threat).Take(8).Sum() - 1f) < 1e-5,
            "threat directions: share per direction within the radius (far enemy ignored)",
            $"= E {features[threat]:F2}, N {features[threat + 2]:F2}");

        // ---- sectors
        Check(EscapeSectors.SectorOf(zones, e2) == 0 && EscapeSectors.SectorOf(zones, nw2) == 3 &&
              EscapeSectors.SectorOf(zones, sw2) == 5 && EscapeSectors.SectorOf(zones, core) == -1,
            "sector of a zone (E2 = 0, NW2 = 3, SW2 = 5, Core = none)");

        // ---- frequency predictor (8 escape directions)
        var frequency = new FrequencyPredictor();
        var directions = new float[EscapeSectors.Count];
        frequency.Predict(new PredictionInput { Profile = fresh, CurrentZone = core }, directions);
        Check(Math.Abs(directions.Sum() - 1f) < 1e-5 && directions.Max() - directions.Min() < 1e-6,
            "no data: uniform over directions");

        var sim = new Sim(21);
        var afterRounds = new List<float>();
        for (int round = 0; round < 10; round++)
        {
            sim.RunRound(180f, 0f, 4f, shootWhileRunning: false);
            frequency.Predict(new PredictionInput { Profile = sim.Profile, CurrentZone = core }, directions);
            afterRounds.Add(directions[0]);
            if (round == 0)
                Check(Math.Abs(directions.Sum() - 1f) < 1e-5, "direction probabilities sum to 1");
        }
        Check(afterRounds[2] > 0.5f, "runner east: P(east) > 50% after 3 escapes", $"= {Pct(afterRounds[2])}");
        Check(afterRounds[9] > 0.8f, "runner east: P(east) > 80% after 10 escapes",
            $"= {string.Join(" ", afterRounds.Select(p => Pct(p)))}");

        // Markov: the direction depends on where the escape starts, and counts from one start zone are not
        // forgotten because of escapes elsewhere (per-start decay)
        var routes = new PlayerProfile(zones.ZoneCount);
        var state = routes.ExportState();
        state.escapes = 12;
        state.directionWeights[3] = 2f;   // overall: NW and N equally often...
        state.directionWeights[2] = 2f;
        state.firstEscapeDirections[3] = 2f;   // ...but the first escape of an engagement (from home) goes NW,
        state.directionFrom = new[] { e2 };     // and escapes that start in E2 go north
        state.directionBin = new[] { 2 };
        state.directionWeight = new[] { 2f };
        routes.TryImportState(state, out _);
        frequency.Predict(new PredictionInput { Profile = routes, CurrentZone = core }, directions);
        int fromCore = Array.IndexOf(directions, directions.Max());
        float pFromCore = directions.Max();
        frequency.Predict(new PredictionInput { Profile = routes, CurrentZone = e2 }, directions);
        int fromE2 = Array.IndexOf(directions, directions.Max());
        Check(fromCore == 3 && fromE2 == 2, "from the base: first-escape directions (NW); from E2: its own counts (N)",
            $"= {PlayerProfile.DirectionName(fromCore)} ({Pct(pFromCore)}), {PlayerProfile.DirectionName(fromE2)} ({Pct(directions.Max())})");

        var memory = new Sim(22);
        memory.RunRound(180f, 0f, 4f, false);              // one escape east from Core...
        for (int i = 0; i < 12; i++)                        // ...then many escapes that start elsewhere
            memory.Profile.AddEscape(new EscapeEpisode { StartZone = e2, EndZone = n2, IsValid = true, StartPosition = new Vector2(20f, 0f), EndPosition = new Vector2(20f, 8f) });
        var fromCoreBuffer = new float[EscapeSectors.Count];
        float kept = memory.Profile.DirectionWeightsFrom(core, fromCoreBuffer);
        Check(kept > 0.99f && fromCoreBuffer[0] > 0.99f, "per-start decay: escapes elsewhere don't erase the route from Core",
            $"(Core → E weight {fromCoreBuffer[0]:F2} after 12 escapes from E2)");

        // first escape of an engagement = the route chosen from home; later escapes in it don't count
        var engagements = new PlayerProfile(zones.ZoneCount);
        Vector2 home = Vector2.zero;
        engagements.AddEscape(new EscapeEpisode { EngagementId = 1, IsValid = true, StartZone = core, EndZone = nw2, StartPosition = home, EndPosition = new Vector2(-10f, 10f) });
        engagements.AddEscape(new EscapeEpisode { EngagementId = 1, IsValid = true, StartZone = nw2, EndZone = e2, StartPosition = new Vector2(-10f, 10f), EndPosition = new Vector2(10f, 10f) });
        engagements.AddEscape(new EscapeEpisode { EngagementId = 1, IsValid = true, StartZone = e2, EndZone = e2, StartPosition = new Vector2(10f, 10f), EndPosition = new Vector2(20f, 10f) });
        engagements.AddEscape(new EscapeEpisode { EngagementId = 2, IsValid = true, StartZone = core, EndZone = nw2, StartPosition = home, EndPosition = new Vector2(-10f, 10f) });
        Check(Math.Abs(engagements.FirstEscapeEvidence - (0.85f + 1f)) < 1e-4 && Math.Abs(engagements.FirstEscapeDirectionProbability(3) - 1f) < 1e-5,
            "first escape per engagement: 2 of 4 escapes count, both NW", $"(evidence {engagements.FirstEscapeEvidence:F2})");
        EscapeFeatures.Build(engagements, core, home, null, 15f, features);
        Check(features[features.Length - 8 + 3] == 1f && features.Skip(EscapeFeatures.ThreatOffset(zones.ZoneCount)).Take(8).All(v => v == 0f),
            "features: first-escape directions at the end; no threat when enemies are unknown");

        // ---- learned predictor (softmax over 8 directions)
        int featureCount = EscapeFeatures.Size(zones.ZoneCount);
        var weights = new LearnedPredictorWeights
        {
            zoneCount = zones.ZoneCount,
            featureCount = featureCount,
            classCount = EscapeSectors.Count,
            weights = new float[EscapeSectors.Count * featureCount],
            bias = new float[EscapeSectors.Count]
        };
        weights.bias[0] = (float)Math.Log(3.0); // softmax(bias): east three times as likely as each other direction
        var learned = new LearnedPredictor(weights);
        var input = new PredictionInput { Features = new float[featureCount] };
        learned.Predict(input, directions);
        Check(Math.Abs(directions.Sum() - 1f) < 1e-5 && Math.Abs(directions[0] / directions[2] - 3f) < 1e-3,
            "learned: softmax of bias when features are zero", $"= E {Pct(directions[0])}");

        weights.weights[3 * featureCount + oneHot + core] = 5f; // "from Core, the player goes NW"
        input.Features[oneHot + core] = 1f;
        learned.Predict(input, directions);
        Check(Array.IndexOf(directions, directions.Max()) == 3, "learned: a feature weight moves the prediction",
            $"= NW {Pct(directions[3])}");

        weights.featureMean = Enumerable.Repeat(0.5f, featureCount).ToArray();
        weights.featureScale = Enumerable.Repeat(0.5f, featureCount).ToArray();
        learned = new LearnedPredictor(weights);
        learned.Predict(input, directions);
        float expected = 5f * (1f - 0.5f) / 0.5f; // standardized one-hot: (1 - mean) / scale
        Check(Array.IndexOf(directions, directions.Max()) == 3 &&
              Math.Abs(Math.Log(directions[3] / directions[2]) - expected) < 1e-3,
            "learned: features standardized with the stored mean / scale");

        bool rejected = false;
        try { new LearnedPredictor(new LearnedPredictorWeights { zoneCount = 17, featureCount = 50, classCount = 8, weights = new float[400], bias = new float[8] }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "learned: wrong feature count rejected", $"({LearnedPredictor.Validate(new LearnedPredictorWeights { zoneCount = 17, featureCount = 50, classCount = 8 })})");
        Check(LearnedPredictor.Validate(new LearnedPredictorWeights { version = 1, zoneCount = 17, featureCount = 58, classCount = 17 }) != null,
            "learned: old zone-output weights (version 1) rejected");
    }
}
