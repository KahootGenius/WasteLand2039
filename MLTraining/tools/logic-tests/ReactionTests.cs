// Tests for the player reaction model (EnemyAI/Prediction/ReactionModel.cs, the second layer of the adaptive horde):
// the prior leaves the base prediction alone, contact lowers that direction and no contact lets it recover, a player
// who doesn't react is recognized (the prediction stays the base one), and against a player who follows the Adaptive
// bot's rule (BotBrain: route weight × 0.2 after an ambush, slow recovery) the model predicts the route better than
// the base prediction it adjusts.

using System;
using System.Collections.Generic;
using System.Linq;

static partial class LogicTests
{
    // routes A / B / C of the arena = sectors E (0), NW (3), SW (5)
    static readonly int[] ReactionRouteSectors = { 0, 3, 5 };

    static void ReactionTests()
    {
        Console.WriteLine("Player reaction model (adaptive horde, layer 2)");
        ReactionMechanicsTests();
        ReactionNonReactingTests();
        ReactionAdaptiveBotTests();
    }

    /// <summary>Base prediction from decayed route counts (like the profile's first-escape counts), over 8 sectors</summary>
    static float[] ReactionBase(double[] counts)
    {
        var p = new float[EscapeSectors.Count];
        double total = 0;
        for (int s = 0; s < p.Length; s++)
        {
            p[s] = (float)(counts[s] + (ReactionRouteSectors.Contains(s) ? 0.5 : 0.02));
            total += p[s];
        }
        for (int s = 0; s < p.Length; s++)
            p[s] = (float)(p[s] / total);
        return p;
    }

    static int ReactionTop(float[] p)
    {
        int best = 0;
        for (int s = 1; s < p.Length; s++)
            if (p[s] > p[best]) best = s;
        return best;
    }

    static void ReactionMechanicsTests()
    {
        var model = new ReactionModel();
        float[] base0 = { 0.5f, 0.05f, 0.05f, 0.2f, 0.05f, 0.1f, 0.025f, 0.025f };
        var output = new float[8];
        model.Apply(base0, output);
        Check(output.Zip(base0, (a, b) => Math.Abs(a - b)).Max() < 1e-6f && Math.Abs(model.NoReactionProbability - 0.5f) < 1e-6f,
            "prior: P(no reaction) = 0.5 and the prediction is the base prediction");

        // a model that is sure the player reacts: after outings east and north-west, contact in the east lowers the east;
        // its probability moves to the other route (NW), directions the player never took keep theirs
        var reacting = new ReactionModel(priorNoReaction: 0.001f);
        reacting.Observe(base0, 3, false);
        reacting.Observe(base0, 0, true);
        reacting.Apply(base0, output);
        bool lowered = output[0] < base0[0] * 0.75f && output[3] > base0[3] && Math.Abs(output[1] - base0[1]) < 1e-6f &&
                       Math.Abs(output.Sum() - 1f) < 1e-4f;
        Check(lowered, "contact on a route lowers it next time, the player's other route gains, unused directions don't",
            $"= E {F(base0[0])} → {F(output[0])}, NW {F(base0[3])} → {F(output[3])}, NE {F(base0[1])} → {F(output[1])}");
        float afterContact = output[0];

        var single = new ReactionModel(priorNoReaction: 0.001f);
        single.Observe(base0, 0, true);
        single.Apply(base0, output);
        Check(output.Zip(base0, (a, b) => Math.Abs(a - b)).Max() < 1e-6f, "with one route seen there is nothing to shift: base prediction");

        for (int i = 0; i < 30; i++)
            reacting.Observe(base0, 3, false);
        reacting.Apply(base0, output);
        Check(reacting.MeanFactor(0) > 0.9f && output[0] > afterContact,
            "outings without contact let it recover", $"(east factor {F(reacting.MeanFactor(0))}, E {F(afterContact)} → {F(output[0])})");

        reacting.Reset();
        reacting.Apply(base0, output);
        Check(reacting.Observations == 0 && output.Zip(base0, (a, b) => Math.Abs(a - b)).Max() < 1e-6f,
            "Reset returns to the prior");

        var zeros = new float[8];
        reacting.Observe(base0, 0, true);
        reacting.Apply(zeros, output);
        bool threw = false;
        try { reacting.Observe(base0, 8, true); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(output.All(v => v == 0f) && threw, "empty base prediction stays empty; a sector out of range is rejected");
    }

    /// <summary>
    /// A player who picks routes from fixed weights whatever happens; contact on 60% of outings. With a clear favourite
    /// (80% B) the model settles on "no reaction" and leaves the prediction alone. A uniform player doesn't repeat
    /// routes as often as the recency-weighted base expects, which a reaction partly explains, so there the test is
    /// only that the model predicts no worse (log score and top-1 accuracy).
    /// </summary>
    static void ReactionNonReactingTests()
    {
        foreach (var (name, weights) in new[] { ("80% B", new[] { 0.1, 0.8, 0.1 }), ("uniform", new[] { 1 / 3.0, 1 / 3.0, 1 / 3.0 }) })
        {
            double noReaction = 0, maxShift = 0, baseLog = 0, modelLog = 0;
            int baseHits = 0, modelHits = 0, n = 0;
            const int seeds = 20;
            for (int seed = 0; seed < seeds; seed++)
            {
                var rnd = new Random(100 + seed);
                var model = new ReactionModel();
                var counts = new double[8];
                var output = new float[8];
                for (int k = 0; k < 300; k++)
                {
                    float[] basePrediction = ReactionBase(counts);
                    model.Apply(basePrediction, output);
                    int route = Pick(rnd, weights);
                    int sector = ReactionRouteSectors[route];
                    baseLog += Math.Log(basePrediction[sector]);
                    modelLog += Math.Log(output[sector]);
                    baseHits += ReactionTop(basePrediction) == sector ? 1 : 0;
                    modelHits += ReactionTop(output) == sector ? 1 : 0;
                    n++;
                    model.Observe(basePrediction, sector, rnd.NextDouble() < 0.6);
                    for (int s = 0; s < 8; s++) counts[s] *= 0.917; // half-life 8 outings
                    counts[sector] += 1;
                }
                noReaction += model.NoReactionProbability / seeds;
                float[] last = ReactionBase(counts);
                model.Apply(last, output);
                maxShift = Math.Max(maxShift, output.Zip(last, (a, b) => (double)Math.Abs(a - b)).Max());
            }
            string detail = $"(P(no reaction) after 300 outings {F(noReaction)}, log score {F(baseLog / n)} → {F(modelLog / n)}, " +
                            $"top-1 {(double)baseHits / n:P0} → {(double)modelHits / n:P0})";
            if (name == "80% B")
                Check(noReaction > 0.9 && maxShift < 0.05, $"a player who doesn't react ({name}): P(no reaction) → 1, prediction ≈ base", detail);
            else
                Check(modelLog >= baseLog - 0.01 * n && modelHits >= baseHits - 0.02 * n,
                    $"a player who doesn't react ({name}): no worse than the base prediction", detail);
        }
    }

    /// <summary>
    /// The Adaptive bot's rule: prefers A (0.8 / 0.1 / 0.1), a route's weight × 0.2 after it met zombies there, else 5%
    /// recovery toward the preference. The commander ambushes the predicted top route (contact 55%), chasers meet the
    /// player on 37% of outings anyway. Episodes of 15 outings, everything resets each episode (as in the arena).
    /// </summary>
    static void ReactionAdaptiveBotTests()
    {
        int baseHits = 0, modelHits = 0, total = 0;
        double noReaction = 0;
        const int episodes = 400;
        var rnd = new Random(7);
        for (int e = 0; e < episodes; e++)
        {
            double[] pref = { 0.8, 0.1, 0.1 };
            double[] w = (double[])pref.Clone();
            var counts = new double[8];
            var model = new ReactionModel();
            var adjusted = new float[8];
            for (int k = 0; k < 15; k++)
            {
                float[] basePrediction = ReactionBase(counts);
                model.Apply(basePrediction, adjusted);
                int route = Pick(rnd, w);
                int sector = ReactionRouteSectors[route];
                int ambushed = ReactionTop(adjusted);
                bool met = (ambushed == sector && rnd.NextDouble() < 0.55) || rnd.NextDouble() < 0.37;
                if (k >= 2)
                {
                    baseHits += ReactionTop(basePrediction) == sector ? 1 : 0;
                    modelHits += ambushed == sector ? 1 : 0;
                    total++;
                }
                model.Observe(basePrediction, sector, met);
                if (met) w[route] *= 0.2;
                else for (int i = 0; i < 3; i++) w[i] += 0.05 * (pref[i] - w[i]);
                double sum = w.Sum();
                for (int i = 0; i < 3; i++) w[i] /= sum;
                for (int s = 0; s < 8; s++) counts[s] *= 0.917;
                counts[sector] += 1;
            }
            noReaction += model.NoReactionProbability / episodes;
        }
        double baseAccuracy = (double)baseHits / total, modelAccuracy = (double)modelHits / total;
        Check(noReaction < 0.3, "a player who avoids where they met zombies is recognized within an episode",
            $"(mean P(no reaction) after 15 outings {F(noReaction)})");
        Check(modelAccuracy >= baseAccuracy + 0.05, "it predicts that player's next route better than the base prediction",
            $"(top-1 {baseAccuracy:P0} → {modelAccuracy:P0}, outings 3–15 of {episodes} episodes)");
    }

    static int Pick(Random rnd, double[] weights)
    {
        double roll = rnd.NextDouble() * weights.Sum(), cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) return i;
        }
        return weights.Length - 1;
    }
}
