// Tests for the adaptive-horde ambush bandit (EnemyAI/Prediction/AmbushBandit.cs): the Gamma / Beta samplers,
// Thompson sampling of the ambush arm against simulated contact tables (stationary, per-route vs shared arms,
// and a switch of the best arm halfway), the discounted update, state export / import, and Thompson sampling of
// the escape direction (Dirichlet, part A).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

static partial class LogicTests
{
    static void BanditTests()
    {
        Console.WriteLine("Ambush bandit (adaptive horde)");
        BanditSamplerTests();
        BanditChoiceTests();
        BanditRouteTableTests();
        BanditNonStationaryTests();
        BanditUpdateAndStateTests();
        BanditDirectionTests();
    }

    static (double mean, double variance) Moments(Func<double> draw, int n)
    {
        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double x = draw();
            sum += x;
            sumSq += x * x;
        }
        double mean = sum / n;
        return (mean, sumSq / n - mean * mean);
    }

    static string F(double v, string format = "0.###") => v.ToString(format, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ samplers

    static void BanditSamplerTests()
    {
        var random = new Random(7);
        const int n = 50000;

        bool gammaOk = true;
        var gammaDetail = new List<string>();
        foreach (double k in new[] { 0.3, 1.0, 2.5, 9.0 })
        {
            bool positive = true;
            var (mean, variance) = Moments(() =>
            {
                double x = ThompsonSampling.Gamma(random, k);
                positive &= x > 0 && !double.IsInfinity(x);
                return x;
            }, n);
            gammaOk &= positive && Math.Abs(mean - k) < 0.03 * k && Math.Abs(variance - k) < 0.1 * k;
            gammaDetail.Add($"k={F(k)}: {F(mean)}/{F(variance)}");
        }
        Check(gammaOk, "Gamma(k): mean ≈ k, variance ≈ k, all draws > 0 (k = 0.3 uses the U^(1/k) boost)",
            "(mean/var " + string.Join(", ", gammaDetail) + ")");
        Check(ThompsonSampling.Gamma(random, 0) == 0 && ThompsonSampling.Gamma(random, -1) == 0 &&
              ThompsonSampling.Gamma(random, double.NaN) == 0, "Gamma(shape ≤ 0 or NaN) = 0");

        bool betaOk = true;
        var betaDetail = new List<string>();
        foreach (var (a, b) in new[] { (1.0, 1.0), (2.0, 5.0), (8.0, 3.0), (0.5, 0.5) })
        {
            bool inRange = true;
            var (mean, variance) = Moments(() =>
            {
                double x = ThompsonSampling.Beta(random, a, b);
                inRange &= x >= 0 && x <= 1;
                return x;
            }, n);
            double expectedMean = a / (a + b);
            double expectedVariance = a * b / ((a + b) * (a + b) * (a + b + 1));
            betaOk &= inRange && Math.Abs(mean - expectedMean) < 0.01 && Math.Abs(variance - expectedVariance) < 0.1 * expectedVariance;
            betaDetail.Add($"({F(a)},{F(b)}): {F(mean)}/{F(variance, "0.####")}");
        }
        Check(betaOk, "Beta(a, b): mean a/(a+b), variance ab/((a+b)²(a+b+1)), draws in [0, 1]",
            "(mean/var " + string.Join(", ", betaDetail) + ")");
    }

    // ------------------------------------------------------------------ choosing

    static float[] OneHot(int sector)
    {
        var p = new float[AmbushBandit.SectorCount];
        p[sector] = 1f;
        return p;
    }

    static void BanditChoiceTests()
    {
        // One predicted direction (east), three distances; the middle one makes contact most often
        var random = new Random(3);
        var world = new Random(4);
        var bandit = new AmbushBandit(random);
        float[] truth = { 0.2f, 0.7f, 0.35f };
        float[] east = OneHot(0);
        var picks = new List<int>();
        for (int trial = 0; trial < 300; trial++)
        {
            AmbushChoice choice = bandit.Choose(east);
            picks.Add(choice.DistanceIndex);
            bandit.Update(choice.Sector, choice.DistanceIndex, world.NextDouble() < truth[choice.DistanceIndex]);
        }
        float shareBest = picks.Skip(200).Count(d => d == 1) / 100f;
        float shareFirst = picks.Take(30).Count(d => d == 1) / 30f;
        Check(bandit.Choose(east).Sector == 0 && picks.All(d => d >= 0 && d < 3), "one-hot direction: always that sector");
        Check(shareBest > 0.7f, "stationary: picks the best distance (18) > 70% of the last 100 trials",
            $"= {Pct(shareBest)} (first 30 trials {Pct(shareFirst)}; posterior means {F(bandit.Mean(0, 0), "0.00")} / " +
            $"{F(bandit.Mean(0, 1), "0.00")} / {F(bandit.Mean(0, 2), "0.00")})");

        // Second site: never the same or an adjacent sector, never a sector with P = 0, score not above the first
        var split = new AmbushBandit(new Random(5));
        float[] p = { 0.4f, 0.35f, 0f, 0f, 0.25f, 0f, 0f, 0f };
        bool secondOk = true, firstOk = true;
        int withSecond = 0;
        for (int i = 0; i < 500; i++)
        {
            AmbushChoice first = split.Choose(p, out AmbushChoice second);
            firstOk &= first.IsValid && p[first.Sector] > 0f && first.Score > 0f;
            if (second.IsValid)
            {
                withSecond++;
                secondOk &= AmbushBandit.SectorGap(first.Sector, second.Sector) >= 2 && p[second.Sector] > 0f &&
                            second.Score <= first.Score;
            }
            if (i % 2 == 0)
                split.Update(first.Sector, first.DistanceIndex, i % 3 == 0);
        }
        Check(firstOk && secondOk && withSecond > 0,
            "second site: non-adjacent sector with P > 0, score ≤ first", $"({withSecond}/500 choices had one)");
        Check(!split.Choose(new float[AmbushBandit.SectorCount], out AmbushChoice none).IsValid && !none.IsValid,
            "all probabilities 0: no site");

        // Distance only (the default in the commander): the sector is given, the bandit picks the distance
        var near = new AmbushBandit(new Random(13));
        var nearWorld = new Random(14);
        float[] nearTruth = { 0.7f, 0.3f, 0.1f };
        var nearPicks = new List<int>();
        bool sameSector = true;
        for (int trial = 0; trial < 300; trial++)
        {
            AmbushChoice choice = near.ChooseDistance(3);
            sameSector &= choice.Sector == 3 && choice.Score == choice.Theta && choice.Theta > 0f;
            nearPicks.Add(choice.DistanceIndex);
            near.Update(3, choice.DistanceIndex, nearWorld.NextDouble() < nearTruth[choice.DistanceIndex]);
        }
        float nearShare = nearPicks.Skip(200).Count(d => d == 0) / 100f;
        bool untouched = Enumerable.Range(0, AmbushBandit.SectorCount).Where(s => s != 3)
            .All(s => Enumerable.Range(0, 3).All(d => near.Alpha(s, d) == 1f && near.Beta(s, d) == 1f));
        bool threwSector = false;
        try { near.ChooseDistance(-1); } catch (ArgumentOutOfRangeException) { threwSector = true; }
        Check(sameSector && untouched && threwSector && nearShare > 0.7f,
            "distance only: stays in the given sector, picks its best distance (12) > 70% of the last 100 trials",
            $"= {Pct(nearShare)}");

        // Same seed, same history → same choices (own System.Random, nothing else consumed)
        var a = new AmbushBandit(new Random(11));
        var b = new AmbushBandit(new Random(11));
        bool same = true;
        for (int i = 0; i < 50; i++)
        {
            AmbushChoice ca = a.Choose(p, out AmbushChoice sa), cb = b.Choose(p, out AmbushChoice sb);
            same &= ca.Sector == cb.Sector && ca.DistanceIndex == cb.DistanceIndex && sa.Sector == sb.Sector && ca.Theta == cb.Theta;
            a.Update(ca.Sector, ca.DistanceIndex, i % 4 == 0);
            b.Update(cb.Sector, cb.DistanceIndex, i % 4 == 0);
        }
        Check(same, "same seed and history → identical choices");
    }

    // ------------------------------------------------------------------ per-route vs shared arms

    /// <summary>
    /// The player escapes along a direction drawn from P. Sites go on the bandit's best arm and its second
    /// (non-adjacent) arm; a site is a trial only when the player ran into its sector, as in the commander.
    /// Returns the contact rate per escape over the last <paramref name="measure"/> escapes, and how often each
    /// (sector, distance) held a site in that window.
    /// </summary>
    static float RunRouteTable(AmbushBandit bandit, float[] p, float[,] truth, int trials, int measure, int seed, out int[,] held)
    {
        var world = new Random(seed);
        held = new int[AmbushBandit.SectorCount, bandit.DistanceCount];
        int contacts = 0;
        for (int trial = 0; trial < trials; trial++)
        {
            int ran = SampleIndex(world, p);
            AmbushChoice first = bandit.Choose(p, out AmbushChoice second);
            bool contact = false;
            foreach (AmbushChoice site in new[] { first, second })
            {
                if (site.IsValid && trial >= trials - measure)
                    held[site.Sector, site.DistanceIndex]++;
                if (!site.IsValid || site.Sector != ran)
                    continue;
                bool hit = world.NextDouble() < truth[ran, site.DistanceIndex];
                bandit.Update(site.Sector, site.DistanceIndex, hit);
                contact |= hit;
            }
            if (trial >= trials - measure && contact)
                contacts++;
        }
        return contacts / (float)measure;
    }

    static int SampleIndex(Random random, float[] p)
    {
        double pick = random.NextDouble() * p.Sum();
        for (int i = 0; i < p.Length; i++)
        {
            pick -= p[i];
            if (pick < 0)
                return i;
        }
        return p.Length - 1;
    }

    static void BanditRouteTableTests()
    {
        // East (route A) is caught best on the outer ring (25), north-west (route B) close to the base (12)
        float[] p = { 0.55f, 0.025f, 0.025f, 0.30f, 0.025f, 0.025f, 0.025f, 0.025f };
        var truth = new float[AmbushBandit.SectorCount, 3];
        for (int s = 0; s < AmbushBandit.SectorCount; s++)
            for (int d = 0; d < 3; d++)
                truth[s, d] = 0.3f;
        truth[0, 0] = 0.1f; truth[0, 1] = 0.3f; truth[0, 2] = 0.8f;
        truth[3, 0] = 0.7f; truth[3, 1] = 0.3f; truth[3, 2] = 0.1f;

        var perRoute = new AmbushBandit(new Random(21));
        var shared = new AmbushBandit(new Random(21), shared: true);
        float perRouteRate = RunRouteTable(perRoute, p, truth, 800, 300, 22, out int[,] perRouteHeld);
        float sharedRate = RunRouteTable(shared, p, truth, 800, 300, 22, out int[,] sharedHeld);

        // Judged by where the sites were held in the last 300 escapes (the posterior means are noisy with γ = 0.9,
        // about 10 effective trials per arm)
        string Held(int[,] held, int sector) => $"{held[sector, 0]}/{held[sector, 1]}/{held[sector, 2]}";
        int MostHeld(int[,] held, params int[] sectors) => Enumerable.Range(0, 3)
            .OrderByDescending(d => sectors.Sum(s => held[s, d])).First();
        Check(MostHeld(perRouteHeld, 0) == 2 && MostHeld(perRouteHeld, 3) == 0,
            "per-route arms: waits at 25 on the east route and at 12 on the north-west route",
            $"(sites held at 12/18/25: E {Held(perRouteHeld, 0)}, NW {Held(perRouteHeld, 3)})");
        Check(shared.ArmCount == 3 && MostHeld(sharedHeld, Enumerable.Range(0, 8).ToArray()) == 2 && shared.Mean(0, 2) == shared.Mean(5, 2),
            "shared arms: 3 arms for all sectors, settles on the distance that suits the main route (25)",
            $"(E {Held(sharedHeld, 0)}, NW {Held(sharedHeld, 3)})");
        Check(perRouteRate > sharedRate + 0.05f, "contact rate: per-route arms beat shared arms when routes differ",
            $"({Pct(perRouteRate)} vs {Pct(sharedRate)} of the last 300 escapes)");
    }

    // ------------------------------------------------------------------ non-stationary

    /// <summary>
    /// One sector, three distances, the best arm swaps after <paramref name="before"/> trials.
    /// Returns the number of trials after the swap until the new best arm's posterior mean overtakes the old
    /// one (<paramref name="after"/> = never), and the new best arm's share of the last 50 picks.
    /// </summary>
    static (int switchAfter, float lateShare) RunSwitch(float discount, int seed, int before = 150, int after = 150)
    {
        var bandit = new AmbushBandit(new Random(seed), discount: discount);
        var world = new Random(seed + 1000);
        float[] east = OneHot(0);
        int switchAfter = after;
        int lateNew = 0;
        for (int trial = 0; trial < before + after; trial++)
        {
            bool swapped = trial >= before;
            AmbushChoice choice = bandit.Choose(east);
            float truth = choice.DistanceIndex == 1 ? 0.2f : (choice.DistanceIndex == 0) != swapped ? 0.8f : 0.2f;
            bandit.Update(0, choice.DistanceIndex, world.NextDouble() < truth);
            if (swapped && switchAfter == after && bandit.Mean(0, 2) > bandit.Mean(0, 0))
                switchAfter = trial - before + 1;
            if (trial >= before + after - 50 && choice.DistanceIndex == 2)
                lateNew++;
        }
        return (switchAfter, lateNew / 50f);
    }

    static void BanditNonStationaryTests()
    {
        const int runs = 31;
        var forgetting = Enumerable.Range(0, runs).Select(seed => RunSwitch(0.9f, 100 + seed)).ToList();
        var remembering = Enumerable.Range(0, runs).Select(seed => RunSwitch(1f, 100 + seed)).ToList();
        int Median(IEnumerable<int> values) => values.OrderBy(v => v).ElementAt(runs / 2);
        int medianForgetting = Median(forgetting.Select(r => r.switchAfter));
        int medianRemembering = Median(remembering.Select(r => r.switchAfter));
        float lateForgetting = forgetting.Average(r => r.lateShare);
        float lateRemembering = remembering.Average(r => r.lateShare);
        Check(medianForgetting <= 25, "best arm swaps: γ = 0.9 switches within ~15 trials (median of 31 runs)",
            $"= {medianForgetting} (quartiles {forgetting.Select(r => r.switchAfter).OrderBy(v => v).ElementAt(runs / 4)}–" +
            $"{forgetting.Select(r => r.switchAfter).OrderBy(v => v).ElementAt(3 * runs / 4)})");
        Check(medianRemembering > 3 * medianForgetting && lateRemembering < lateForgetting,
            "γ = 1 (no forgetting) is much slower to switch",
            $"(median {(medianRemembering >= 150 ? "not within 150" : medianRemembering.ToString())} trials; " +
            $"new best arm in the last 50 picks: {Pct(lateForgetting)} with γ = 0.9, {Pct(lateRemembering)} with γ = 1)");
    }

    // ------------------------------------------------------------------ update + state

    static void BanditUpdateAndStateTests()
    {
        var bandit = new AmbushBandit(new Random(1));
        AmbushBanditState fresh = bandit.ExportState();
        Check(bandit.ArmCount == 24 && fresh.alpha.All(v => v == 1f) && fresh.beta.All(v => v == 1f) &&
              bandit.Distances.SequenceEqual(new[] { 12f, 18f, 25f }),
            "8 sectors × 3 distances (12/18/25), all at the prior (1, 1)");

        bandit.Update(3, 1, true);
        AmbushBanditState afterOne = bandit.ExportState();
        int arm = bandit.ArmIndex(3, 1);
        bool othersUnchanged = Enumerable.Range(0, bandit.ArmCount).Where(i => i != arm)
            .All(i => afterOne.alpha[i] == 1f && afterOne.beta[i] == 1f);
        Check(othersUnchanged && bandit.Alpha(3, 1) == 2f && bandit.Beta(3, 1) == 1f && bandit.Updates == 1,
            "Update changes only the updated arm (contact: α 1 → 2)");
        bandit.Update(3, 1, false);
        Check(Math.Abs(bandit.Alpha(3, 1) - 1.9f) < 1e-6f && Math.Abs(bandit.Beta(3, 1) - 2f) < 1e-6f,
            "discount toward the prior, then add: (2, 1) + miss → (1.9, 2.0) with γ = 0.9",
            $"= ({F(bandit.Alpha(3, 1))}, {F(bandit.Beta(3, 1))})");

        var lastOnly = new AmbushBandit(new Random(1), discount: 0f);
        lastOnly.Update(0, 0, true);
        lastOnly.Update(0, 0, false);
        var churn = new AmbushBandit(new Random(1), discount: 0.5f);
        var coin = new Random(2);
        for (int i = 0; i < 2000; i++)
            churn.Update(coin.Next(8), coin.Next(3), coin.NextDouble() < 0.5);
        AmbushBanditState churned = churn.ExportState();
        Check(lastOnly.Alpha(0, 0) == 1f && lastOnly.Beta(0, 0) == 2f && churned.alpha.Min() >= 1f && churned.beta.Min() >= 1f &&
              churned.alpha.Zip(churned.beta, (x, y) => x + y).Max() <= 2f + 1f / (1f - 0.5f) + 1e-4f,
            "shapes stay ≥ 1, and α + β ≤ 2 + 1/(1 − γ) (γ = 0 keeps only the last trial)");

        var shared = new AmbushBandit(new Random(1), shared: true);
        shared.Update(5, 2, true);
        Check(shared.ArmCount == 3 && shared.Alpha(0, 2) == 2f && shared.Alpha(7, 2) == 2f && shared.Alpha(5, 0) == 1f,
            "shared mode: one arm per distance, the same for every sector");

        bool threw = false;
        try { bandit.Update(8, 0, true); } catch (ArgumentOutOfRangeException) { threw = true; }
        bool threwDistance = false;
        try { bandit.Update(0, 3, true); } catch (ArgumentOutOfRangeException) { threwDistance = true; }
        Check(threw && threwDistance, "out-of-range sector / distance rejected");

        // ---- export → JSON → import
        var trained = new AmbushBandit(new Random(31));
        var world = new Random(32);
        float[] p = { 0.5f, 0.1f, 0.05f, 0.2f, 0.05f, 0.05f, 0.03f, 0.02f };
        for (int i = 0; i < 60; i++)
        {
            AmbushChoice c = trained.Choose(p);
            trained.Update(c.Sector, c.DistanceIndex, world.NextDouble() < 0.4);
        }
        AmbushBanditState state = trained.ExportState();
        var json = new JsonSerializerOptions { IncludeFields = true };
        AmbushBanditState fromFile = JsonSerializer.Deserialize<AmbushBanditState>(JsonSerializer.Serialize(state, json), json);
        var restored = new AmbushBandit(new Random(33));
        bool imported = restored.TryImportState(fromFile, out string error);
        AmbushBanditState again = restored.ExportState();
        Check(imported && again.alpha.SequenceEqual(state.alpha) && again.beta.SequenceEqual(state.beta) &&
              again.updates == state.updates && again.updates == 60,
            "state survives a JSON round trip: identical α, β and update count", error ?? "");

        var original = new AmbushBandit(new Random(44));
        original.TryImportState(state, out _);
        var copy = new AmbushBandit(new Random(44));
        copy.TryImportState(fromFile, out _);
        bool sameChoices = true;
        for (int i = 0; i < 30; i++)
        {
            AmbushChoice x = original.Choose(p), y = copy.Choose(p);
            sameChoices &= x.Sector == y.Sector && x.DistanceIndex == y.DistanceIndex;
            original.Update(x.Sector, x.DistanceIndex, i % 3 == 0);
            copy.Update(y.Sector, y.DistanceIndex, i % 3 == 0);
        }
        Check(sameChoices, "restored bandit keeps choosing and learning like the original (same seed)");

        trained.Update(0, 2, true);
        Check(trained.ExportState().updates == 61 && state.updates == 60 && !trained.ExportState().alpha.SequenceEqual(state.alpha),
            "exported state is a copy (later updates don't change it)");

        // ---- rejections leave the bandit untouched
        AmbushBanditState Modified(Action<AmbushBanditState> change)
        {
            AmbushBanditState s = trained.ExportState();
            change(s);
            return s;
        }
        var target = new AmbushBandit(new Random(1));
        var rejections = new (string name, AmbushBanditState state)[]
        {
            ("missing", null),
            ("other version", Modified(s => s.version = 2)),
            ("other distances", Modified(s => s.distances = new[] { 10f, 20f, 30f })),
            ("shared save into per-sector bandit", Modified(s => s.shared = true)),
            ("short α", Modified(s => s.alpha = s.alpha.Take(10).ToArray())),
            ("α < 1", Modified(s => s.alpha[4] = 0.5f)),
            ("NaN β", Modified(s => s.beta[7] = float.NaN)),
        };
        var reasons = new List<string>();
        bool allRejected = true;
        foreach (var (name, bad) in rejections)
        {
            bool ok = target.TryImportState(bad, out string reason);
            allRejected &= !ok && reason != null;
            reasons.Add(reason);
        }
        AmbushBanditState untouched = target.ExportState();
        Check(allRejected && untouched.updates == 0 && untouched.alpha.All(v => v == 1f) && untouched.beta.All(v => v == 1f),
            "mismatched / broken states rejected, bandit untouched", $"({string.Join("; ", reasons.Take(4))} …)");

        trained.Reset();
        AmbushBanditState reset = trained.ExportState();
        Check(reset.updates == 0 && reset.alpha.All(v => v == 1f) && reset.beta.All(v => v == 1f), "Reset: back to the prior");
    }

    // ------------------------------------------------------------------ direction (part A)

    static void BanditDirectionTests()
    {
        float[] p = { 0.5f, 0.2f, 0.1f, 0.1f, 0.05f, 0.05f, 0f, 0f };
        const int n = 20000;
        var random = new Random(9);
        var sample = new float[8];

        var rows = new List<(float c, double mean0, double var0, double expectedVar0, float topShare)>();
        bool meansOk = true, shapeOk = true, zeroNeverChosen = true;
        foreach (float c in new[] { 2f, 8.67f, 50f })
        {
            var sums = new double[8];
            double sum0 = 0, sumSq0 = 0;
            int top = 0;
            for (int i = 0; i < n; i++)
            {
                int chosen = ThompsonSampling.SampleDirection(random, p, c, sample);
                shapeOk &= Math.Abs(sample.Sum() - 1f) < 1e-4f && sample[chosen] == sample.Max();
                zeroNeverChosen &= p[chosen] > 0f && sample[6] == 0f && sample[7] == 0f;
                for (int d = 0; d < 8; d++)
                    sums[d] += sample[d];
                sum0 += sample[0];
                sumSq0 += sample[0] * sample[0];
                if (chosen == 0)
                    top++;
            }
            for (int d = 0; d < 8; d++)
                meansOk &= Math.Abs(sums[d] / n - p[d]) < 0.01;
            double mean0 = sum0 / n;
            rows.Add((c, mean0, sumSq0 / n - mean0 * mean0, p[0] * (1 - p[0]) / (c + 1), top / (float)n));
        }
        Check(meansOk, "Dirichlet: mean of the samples ≈ P (concentration 2, 6.67 + 2, 50)");
        Check(shapeOk && zeroNeverChosen, "sample sums to 1, the returned direction is its largest, P = 0 never chosen");
        bool varianceOk = rows.All(r => Math.Abs(r.var0 - r.expectedVar0) < 0.1 * r.expectedVar0);
        bool shrinks = rows[0].var0 > rows[1].var0 && rows[1].var0 > rows[2].var0;
        Check(varianceOk && shrinks, "spread shrinks as evidence grows: Var = P(1 − P)/(c + 1)",
            "(" + string.Join(", ", rows.Select(r => $"c={F(r.c, "0.##")}: {F(r.var0, "0.0000")} vs {F(r.expectedVar0, "0.0000")}")) + ")");
        bool exploresLess = rows[0].topShare < rows[1].topShare && rows[1].topShare < rows[2].topShare;
        Check(exploresLess && rows[1].topShare < 0.95f && rows[2].topShare > 0.9f,
            "exploration: picks the top direction more as evidence grows, but never always at saturated evidence (≈ 6.7)",
            "(top picked " + string.Join(", ", rows.Select(r => $"{Pct(r.topShare)} at c={F(r.c, "0.##")}")) + ")");

        var counts = new int[8];
        for (int i = 0; i < n; i++)
            counts[ThompsonSampling.SampleDirection(random, p, 0f, sample)]++;
        Check(counts.Select((count, d) => Math.Abs(count / (double)n - p[d])).Max() < 0.015 && sample.Sum() == 1f,
            "concentration 0: a direction drawn with probability P (one-hot sample)");
        Check(ThompsonSampling.SampleDirection(random, new float[8], 5f, sample) == -1 && sample.All(v => v == 0f),
            "all probabilities 0: no direction");

        // With the frequency predictor from the base: a_d = P_d × (FirstEscapeEvidence + routePrior 2)
        var sim = new Sim(21);
        for (int round = 0; round < 6; round++)
            sim.RunRound(180f, 0f, 4f, shootWhileRunning: false);
        var directions = new float[EscapeSectors.Count];
        new FrequencyPredictor().Predict(new PredictionInput { Profile = sim.Profile, CurrentZone = 0 }, directions);
        float concentration = sim.Profile.FirstEscapeEvidence + 2f;
        int east = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (ThompsonSampling.SampleDirection(random, directions, concentration) == 0)
                east++;
        }
        Check(east / 2000f > 0.9f && directions[0] > 0.8f,
            "runner east, from the base (concentration = first-escape evidence + 2): the draw picks east",
            $"(P(E) {Pct(directions[0])}, evidence {F(sim.Profile.FirstEscapeEvidence, "0.00")}, " +
            $"east in {F(100.0 * east / 2000, "0.0")} % of draws)");
    }
}
