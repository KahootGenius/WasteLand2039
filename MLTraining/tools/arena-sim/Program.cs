// The arena simulator's runs (GameArena.cs). Usage: MLTraining/tools/arena-sim.sh [MODE] [SEEDS] [EPISODES] [Option=value ...]
//   calibrate  V2 in the simulator next to the real eval (default; the Option=value settings change the simulated arena)
//   compare    V2 against V2 with the given options, on the same seeds: damage per wave and caught per flee by persona
//              (e.g. compare 32 7 Style=true for -v2Style)
//   intercept  the check against the two real interception trials (cut-off chasers without and with the 1-second rule)
//   ablate     what the game rules found during the calibration change (damage per wave by persona)
// SEEDS (default 16) runs per persona, EPISODES (default 7) episodes of 4 waves per run; options are GameArenaOptions fields,
// e.g. Intercept=true InterceptMinRun=0 BaseFriction=0 Commander=Baseline.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

static class Program
{
    // ---- the real eval: MLArena_Eval_V2 with the player-like bot, V2, 10 rounds x 40 game-minutes
    // (results/adaptive_eval/20261020_070214, telemetry batches 20261020_070218 ... 20261020_080211)
    sealed class Real
    {
        public float Dmg, Deaths, Kills, Caught = float.NaN, Flees, WaveSeconds;
        public float[] Where, Time;                           // % of the damage / of the time: home < 8, way 8-20, refuge 20-30, edge > 30
        public float Home, Out;                               // damage per wave at home (< 8) and away from it
        public float[] RouteShare, CaughtByRoute, OutPerFlee; // A, B, C; NaN = no flees on that route
        public float Touching, HomeTouching;                  // % of samples pressed against the base; % of home damage taken so
        public float LongFreezes = float.NaN, LongFreezeDamage = float.NaN; // per wave: freezes of 2.3 s+ after a shot, damage in them
    }

    const float N = float.NaN;
    static readonly Dictionary<string, Real> RealV2 = new()
    {
        ["Adaptive"] = new Real { Dmg = 15.3f, Deaths = 0.113f, Kills = 0f, Caught = 0.56f, Flees = 4.15f, WaveSeconds = 80.7f,
            Where = new[] { 47f, 2f, 0f, 50f }, Time = new[] { 23f, 40f, 20f, 17f }, Home = 7.3f, Out = 8.0f,
            RouteShare = new[] { 43f, 25f, 33f }, CaughtByRoute = new[] { 57f, 67f, 46f }, OutPerFlee = new[] { 2.6f, 1.2f, 1.6f },
            Touching = 1.9f, HomeTouching = 100f },
        ["Fighter"] = new Real { Dmg = 74.1f, Deaths = 0.562f, Kills = 10.96f, Flees = 0f, WaveSeconds = 52.5f,
            Where = new[] { 100f, 0f, 0f, 0f }, Time = new[] { 100f, 0f, 0f, 0f }, Home = 74.1f, Out = 0f,
            Touching = 8.2f, HomeTouching = 14f, LongFreezes = 0.21f, LongFreezeDamage = 2.3f },
        ["Kiter"] = new Real { Dmg = 56.6f, Deaths = 0.538f, Kills = 7.03f, Flees = 0f, WaveSeconds = 57.9f,
            Where = new[] { 49f, 36f, 13f, 2f }, Time = new[] { 49f, 39f, 10f, 2f }, Home = 27.7f, Out = 28.9f,
            Touching = 3.5f, HomeTouching = 48f, LongFreezes = 0.63f, LongFreezeDamage = 53.6f },
        ["Mixed"] = new Real { Dmg = 59.2f, Deaths = 0.446f, Kills = 6.41f, Caught = 0.53f, Flees = 2.23f, WaveSeconds = 66.5f,
            Where = new[] { 76f, 14f, 4f, 5f }, Time = new[] { 50f, 27f, 13f, 10f }, Home = 45.2f, Out = 14.0f,
            RouteShare = new[] { 0f, 71f, 29f }, CaughtByRoute = new[] { N, 58f, 41f }, OutPerFlee = new[] { N, 7.3f, 3.8f },
            Touching = 5.0f, HomeTouching = 20f, LongFreezes = 0.07f, LongFreezeDamage = 1.1f },
        ["Random"] = new Real { Dmg = 63.2f, Deaths = 0.514f, Kills = 6.63f, Caught = 1.15f, Flees = 0.97f, WaveSeconds = 57.8f,
            Where = new[] { 81f, 11f, 6f, 2f }, Time = new[] { 53f, 28f, 13f, 6f }, Home = 50.9f, Out = 12.3f,
            RouteShare = new[] { 32f, 33f, 36f }, CaughtByRoute = new[] { 104f, 143f, 99f }, OutPerFlee = new[] { 7.6f, 8.3f, 5.3f },
            Touching = 4.3f, HomeTouching = 24f, LongFreezes = 0.27f, LongFreezeDamage = 15.9f },
        ["RunnerA"] = new Real { Dmg = 3.9f, Deaths = 0.022f, Kills = 0f, Caught = 0.57f, Flees = 3.81f, WaveSeconds = 85.5f,
            Where = new[] { 45f, 0f, 0f, 55f }, Time = new[] { 20f, 32f, 19f, 29f }, Home = 1.8f, Out = 2.1f,
            RouteShare = new[] { 100f, 0f, 0f }, CaughtByRoute = new[] { 57f, N, N }, OutPerFlee = new[] { 0.6f, N, N },
            Touching = 3.0f, HomeTouching = 100f },
        ["RunnerB80"] = new Real { Dmg = 18.4f, Deaths = 0.131f, Kills = 0f, Caught = 0.54f, Flees = 4.25f, WaveSeconds = 79.0f,
            Where = new[] { 54f, 0f, 0f, 46f }, Time = new[] { 22f, 38f, 20f, 20f }, Home = 10.0f, Out = 8.4f,
            RouteShare = new[] { 10f, 80f, 11f }, CaughtByRoute = new[] { 52f, 57f, 37f }, OutPerFlee = new[] { 2.1f, 2.2f, 0.4f },
            Touching = 2.0f, HomeTouching = 99f },
    };

    // ---- the real interception trials: damage per wave per round, V2 then Intercept (RunnerB80: two arenas per round)
    //   trial 1: no 1-second rule, results/adaptive_eval/20261020_235321 (seeds 1, 104730, 209459)
    //   trial 2: with the rule (-v2Intercept as committed), results/adaptive_eval/20261021_062611 (seeds 2, 104731, 209460)
    static readonly Dictionary<string, (float[] v2, float[] cut)[]> RealTrials = new()
    {
        ["Adaptive"] = new[] { (new[] { 22.1f, 11.9f, 4.4f }, new[] { 19.3f, 24.5f, 24.1f }), (new[] { 8.9f, 20.7f, 16.6f }, new[] { 7.4f, 6.7f, 16.6f }) },
        ["Fighter"] = new[] { (new[] { 79.1f, 74.0f, 76.7f }, new[] { 71.5f, 75.0f, 72.3f }), (new[] { 65.6f, 75.6f, 70.0f }, new[] { 73.2f, 77.4f, 60.6f }) },
        ["Kiter"] = new[] { (new[] { 71.2f, 59.0f, 68.0f }, new[] { 57.6f, 53.8f, 38.8f }), (new[] { 57.4f, 57.2f, 42.2f }, new[] { 52.3f, 65.5f, 51.6f }) },
        ["Mixed"] = new[] { (new[] { 47.3f, 50.6f, 43.2f }, new[] { 38.6f, 51.4f, 53.3f }), (new[] { 69.2f, 57.0f, 57.9f }, new[] { 48.1f, 51.5f, 62.4f }) },
        ["Random"] = new[] { (new[] { 72.9f, 63.7f, 69.7f }, new[] { 62.4f, 77.4f, 62.0f }), (new[] { 53.9f, 72.9f, 61.6f }, new[] { 63.3f, 69.5f, 62.1f }) },
        ["RunnerA"] = new[] { (new[] { 3.7f, 4.4f, 7.4f }, new[] { 1.5f, 3.7f, 7.9f }), (new[] { 3.7f, 1.5f, 1.5f }, new[] { 1.5f, 10.0f, 2.2f }) },
        ["RunnerB80"] = new[] { (new[] { 17.0f, 27.1f, 15.2f, 17.2f, 7.4f, 21.9f }, new[] { 12.9f, 9.3f, 20.0f, 15.7f, 29.7f, 15.0f }),
                                (new[] { 17.3f, 11.9f, 12.6f, 22.8f, 13.8f, 17.1f }, new[] { 13.3f, 15.2f, 15.0f, 14.8f, 21.3f, 15.6f }) },
    };

    static readonly (string name, Func<BotPersonaSpec> make)[] Personas =
    {
        ("Adaptive", BotPersonaPresets.Adaptive), ("Fighter", BotPersonaPresets.Fighter), ("Kiter", BotPersonaPresets.Kiter),
        ("Mixed", BotPersonaPresets.Mixed), ("Random", BotPersonaPresets.RandomPlayer), ("RunnerA", BotPersonaPresets.RunnerA),
        ("RunnerB80", BotPersonaPresets.RunnerB80),
    };
    const int SeedBase = 1000;

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "calibrate";
        int seeds = args.Length > 1 ? int.Parse(args[1]) : 16, episodes = args.Length > 2 ? int.Parse(args[2]) : 7;
        List<(System.Reflection.FieldInfo field, object value)> options;
        try
        {
            options = Options(args.Skip(3));
        }
        catch (Exception e) when (e is ArgumentException || e is FormatException || e is InvalidCastException)
        {
            Console.WriteLine(e.Message);
            return 2;
        }
        Console.WriteLine($"arena-sim {mode}: {seeds} runs per persona x {episodes} episodes of 4 waves" +
                          (options.Count > 0 ? "; " + string.Join(" ", options.Select(o => $"{o.field.Name}={o.value}")) : ""));
        switch (mode)
        {
            case "calibrate": Calibrate(Run(Make(options), seeds, episodes)); return 0;
            case "compare": Compare(seeds, episodes, options); return 0;
            case "intercept": Intercept(seeds, episodes); return 0;
            case "ablate": Ablate(seeds, episodes); return 0;
            default:
                Console.WriteLine("modes: calibrate | compare | intercept | ablate");
                return 2;
        }
    }

    // ---------------------------------------------------------------- runs

    static List<(System.Reflection.FieldInfo field, object value)> Options(IEnumerable<string> args)
    {
        var result = new List<(System.Reflection.FieldInfo, object)>();
        foreach (string arg in args)
        {
            string[] kv = arg.Split('=', 2);
            var field = typeof(GameArenaOptions).GetField(kv[0]);
            if (field == null || kv.Length < 2)
                throw new ArgumentException($"unknown option '{arg}' (GameArenaOptions fields: " +
                                            string.Join(", ", typeof(GameArenaOptions).GetFields().Select(f => f.Name)) + ")");
            object value = field.FieldType.IsEnum ? Enum.Parse(field.FieldType, kv[1])
                : Convert.ChangeType(kv[1], field.FieldType, System.Globalization.CultureInfo.InvariantCulture);
            result.Add((field, value));
        }
        return result;
    }

    static GameArenaOptions Make(List<(System.Reflection.FieldInfo field, object value)> settings, Action<GameArenaOptions> more = null)
    {
        var o = new GameArenaOptions();
        foreach (var (field, value) in settings)
            field.SetValue(o, value);
        more?.Invoke(o);
        return o;
    }

    static GameArenaOptions Make(Action<GameArenaOptions> set) => Make(new List<(System.Reflection.FieldInfo, object)>(), set);

    /// <summary>every persona x seed, in parallel (the runs share nothing); the same seeds for every set of options</summary>
    static List<(string persona, GameArena[] runs)> Run(GameArenaOptions options, int seeds, int episodes)
    {
        var runs = new GameArena[Personas.Length, seeds];
        Parallel.For(0, Personas.Length * seeds, job =>
        {
            int p = job / seeds, s = job % seeds;
            var arena = new GameArena(Personas[p].make(), SeedBase + s, options);
            arena.RunEpisodes(episodes);
            runs[p, s] = arena;
        });
        return Enumerable.Range(0, Personas.Length)
            .Select(p => (Personas[p].name, Enumerable.Range(0, seeds).Select(s => runs[p, s]).ToArray())).ToList();
    }

    static float PerWave(GameArena[] runs, Func<GameArena, float> f) => runs.Sum(f) / runs.Sum(a => a.Waves);

    static string Pair(float sim, float real, string format) =>
        float.IsNaN(real) ? sim.ToString(format) : $"{sim.ToString(format)} ({real.ToString(format)})";

    static string Percentages(Func<int, float> part, float total) =>
        string.Join("/", Enumerable.Range(0, 4).Select(k => total > 0f ? $"{100f * part(k) / total:0}" : "-"));

    /// <summary>mean and half-width of the 95% interval (t distribution)</summary>
    static (double mean, double half) Interval(IReadOnlyList<double> d)
    {
        double m = d.Average();
        if (d.Count < 2)
            return (m, double.NaN);
        double sd = Math.Sqrt(d.Sum(x => (x - m) * (x - m)) / (d.Count - 1));
        double[] t = { 0, 12.71, 4.30, 3.18, 2.78, 2.57, 2.45, 2.36, 2.31, 2.26, 2.23, 2.20, 2.18, 2.16, 2.14, 2.13, 2.12, 2.11, 2.10, 2.09, 2.09 };
        return (m, (d.Count - 1 < t.Length ? t[d.Count - 1] : 1.96) * sd / Math.Sqrt(d.Count));
    }

    static string Change((double mean, double half) c) =>
        double.IsNaN(c.half) ? $"{c.mean:+0.0;-0.0}" : $"{c.mean:+0.0;-0.0} [{c.mean - c.half:+0.0;-0.0}, {c.mean + c.half:+0.0;-0.0}]";

    // ---------------------------------------------------------------- calibrate

    static void Calibrate(List<(string persona, GameArena[] runs)> results)
    {
        Console.WriteLine("\n== V2: simulated (real eval). Damage and time by where the player was: home < 8 / way 8-20 / refuge 20-30 / edge > 30");
        Console.WriteLine($"{"persona",-10} {"dmg/wave",13} {"deaths/wave",13} {"kills/wave",12} {"caught/flee",11} {"flees/wave",11} {"wave s",13}  {"damage where (%)",-26} time where (%)");
        foreach (var (p, runs) in results)
        {
            var real = RealV2[p];
            float caught = runs.Sum(a => a.Flees) > 0 ? 100f * runs.Sum(a => a.Catches) / runs.Sum(a => a.Flees) : float.NaN;
            float damage = runs.Sum(a => a.Damage), time = runs.Sum(a => a.TimeByRing.Sum());
            Console.WriteLine($"{p,-10} {Pair(PerWave(runs, a => a.Damage), real.Dmg, "0.0"),13} {Pair(PerWave(runs, a => a.Deaths), real.Deaths, "0.00"),13} " +
                              $"{Pair(PerWave(runs, a => a.Kills), real.Kills, "0.0"),12} {(float.IsNaN(caught) ? "-" : Pair(caught, real.Caught * 100f, "0")),11} " +
                              $"{Pair(PerWave(runs, a => a.Flees), real.Flees, "0.0"),11} {Pair(PerWave(runs, a => a.WaveSeconds), real.WaveSeconds, "0.0"),13}  " +
                              $"{Percentages(k => runs.Sum(a => a.DamageByRing[k]), damage) + " (" + string.Join("/", real.Where.Select(x => $"{x:0}")) + ")",-26} " +
                              $"{Percentages(k => runs.Sum(a => a.TimeByRing[k]), time)} ({string.Join("/", real.Time.Select(x => $"{x:0}"))})");
        }

        Console.WriteLine("profile's flee share at V2's replans (PlayerProfile.FleeShare; -v2Style treats 0.42 or more as a runner): " +
                          string.Join("  ", results.Select(x => $"{x.persona} {x.runs.Sum(a => a.FleeShareSum) / Math.Max(1, x.runs.Sum(a => a.Replans)):0.00}")));

        Console.WriteLine("\n== Flees by route A / B / C: simulated (real). Damage away from home is counted to the wave's latest flee");
        foreach (var (p, runs) in results)
        {
            var real = RealV2[p];
            int flees = runs.Sum(a => a.FleesByRoute.Sum());
            if (flees == 0 || real.RouteShare == null)
                continue;
            string ByRoute(Func<int, float> sim, float[] realValues, string format) => string.Join(" ", Enumerable.Range(0, 3).Select(k =>
                float.IsNaN(sim(k)) ? "-" : Pair(sim(k), realValues[k], format)));
            int Fl(int k) => runs.Sum(a => a.FleesByRoute[k]);
            Console.WriteLine($"{p,-10} route share % {ByRoute(k => 100f * Fl(k) / flees, real.RouteShare, "0")}   caught per flee % " +
                              $"{ByRoute(k => Fl(k) > 0 ? 100f * runs.Sum(a => a.CatchesByRoute[k]) / Fl(k) : float.NaN, real.CaughtByRoute, "0")}   " +
                              $"damage away from home per flee {ByRoute(k => Fl(k) > 0 ? runs.Sum(a => a.OutDamageByRoute[k]) / Fl(k) : float.NaN, real.OutPerFlee, "0.0")}");
        }

        Console.WriteLine("\n== Home and the base: damage per wave at home / away, % of samples pressed against the base, % of home damage taken there");
        foreach (var (p, runs) in results)
        {
            var real = RealV2[p];
            float home = runs.Sum(a => a.DamageByRing[0]);
            Console.WriteLine($"{p,-10} home {Pair(PerWave(runs, a => a.DamageByRing[0]), real.Home, "0.0"),12}   away {Pair(PerWave(runs, a => a.Damage - a.DamageByRing[0]), real.Out, "0.0"),12}   " +
                              $"against the base {Pair(100f * runs.Sum(a => a.SamplesTouchingBase) / runs.Sum(a => a.Samples), real.Touching, "0.0"),11} %   " +
                              $"home damage there {(home > 0f ? Pair(100f * runs.Sum(a => a.DamageTouchingBase) / home, real.HomeTouching, "0") : "-"),9} %");
        }

        Console.WriteLine("\n== Frozen after a shot (2.3 s or longer, a zombie within 2.5): per wave, how they ended, damage taken in them per wave");
        Console.WriteLine("   (the real counts also catch a player killed during a reload: it lies dead for 1 s before the wave resets)");
        foreach (var (p, runs) in results)
        {
            var real = RealV2[p];
            if (runs.Sum(a => a.Shots) == 0)
                continue;
            var how = new Dictionary<string, int>();
            foreach (var a in runs)
                foreach (var kv in a.LongFreezes)
                    how[kv.Key] = (how.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            Console.WriteLine($"{p,-10} {Pair((float)how.Values.Sum() / runs.Sum(a => a.Waves), real.LongFreezes, "0.00"),12} per wave   damage in them " +
                              $"{Pair(PerWave(runs, a => a.LongFreezeDamage), real.LongFreezeDamage, "0.0"),12} of {Pair(PerWave(runs, a => a.Damage), real.Dmg, "0.0")}   " +
                              $"ended by {string.Join(", ", how.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}");
        }
    }

    // ---------------------------------------------------------------- compare

    static void Compare(int seeds, int episodes, List<(System.Reflection.FieldInfo field, object value)> variant)
    {
        if (variant.Count == 0)
        {
            Console.WriteLine("compare needs at least one Option=value (the variant), e.g. Intercept=true");
            return;
        }
        var v2 = Run(new GameArenaOptions(), seeds, episodes);
        var other = Run(Make(variant), seeds, episodes);
        Console.WriteLine($"\n== V2 -> V2 with {string.Join(" ", variant.Select(o => $"{o.field.Name}={o.value}"))}: change over the same seeds [95% interval]");
        Console.WriteLine($"{"persona",-10} {"damage per wave",-36} {"caught per flee (%)",-36} deaths per wave");
        var eval = new List<double>[seeds];
        for (int s = 0; s < seeds; s++)
            eval[s] = new List<double>();
        for (int i = 0; i < v2.Count; i++)
        {
            GameArena[] a = v2[i].runs, b = other[i].runs;
            var dmg = Enumerable.Range(0, seeds).Select(s => (double)(b[s].Damage / b[s].Waves - a[s].Damage / a[s].Waves)).ToList();
            string caught = "-";
            if (a.Sum(x => x.Flees) > 0)
            {
                var c = Enumerable.Range(0, seeds).Where(s => a[s].Flees > 0 && b[s].Flees > 0)
                    .Select(s => 100.0 * ((double)b[s].Catches / b[s].Flees - (double)a[s].Catches / a[s].Flees)).ToList();
                caught = $"{100f * a.Sum(x => x.Catches) / a.Sum(x => x.Flees):0} -> {100f * b.Sum(x => x.Catches) / b.Sum(x => x.Flees):0}  {Change(Interval(c))}";
            }
            Console.WriteLine($"{v2[i].persona,-10} {$"{PerWave(a, x => x.Damage):0.0} -> {PerWave(b, x => x.Damage):0.0}  {Change(Interval(dmg))}",-36} {caught,-36} " +
                              $"{PerWave(a, x => x.Deaths):0.00} -> {PerWave(b, x => x.Deaths):0.00}");
            // the eval scene's 8 arenas: RunnerB80 twice
            for (int s = 0; s < seeds; s++)
                for (int k = 0; k < (v2[i].persona == "RunnerB80" ? 2 : 1); k++)
                    eval[s].Add(dmg[s]);
        }
        Console.WriteLine($"{"8 arenas",-10} mean change in damage per wave (RunnerB80 counted twice, as in the eval scene): {Change(Interval(eval.Select(e => e.Average()).ToList()))}");
        if (other.Any(x => x.runs.Sum(a => a.RunnerReplans) > 0))
            Console.WriteLine("treated as a runner at replans (-v2Style): " +
                              string.Join("  ", other.Select(x => $"{x.persona} {100f * x.runs.Sum(a => a.RunnerReplans) / Math.Max(1, x.runs.Sum(a => a.Replans)):0}%")));
    }

    // ---------------------------------------------------------------- intercept

    static void Intercept(int seeds, int episodes)
    {
        var v2 = Run(new GameArenaOptions(), seeds, episodes);
        var trials = new[]
        {
            ("trial 1, no 1-second rule", Run(Make(o => { o.Intercept = true; o.InterceptMinRun = 0f; }), seeds, episodes)),
            ("trial 2, with the rule", Run(Make(o => { o.Intercept = true; }), seeds, episodes)),
        };
        Console.WriteLine("\n== Cut-off chasers: change in damage per wave [95% interval], the simulator (V2 -> Intercept over the same seeds)");
        Console.WriteLine("   against the real trial (3 rounds each, paired by round; RunnerB80 by arena and round)");
        for (int t = 0; t < trials.Length; t++)
        {
            var (label, cut) = trials[t];
            Console.WriteLine($"\n{label}");
            Console.WriteLine($"{"persona",-10} {"simulator",-42} real trial");
            for (int i = 0; i < v2.Count; i++)
            {
                GameArena[] a = v2[i].runs, b = cut[i].runs;
                var sim = Enumerable.Range(0, seeds).Select(s => (double)(b[s].Damage / b[s].Waves - a[s].Damage / a[s].Waves)).ToList();
                var (realV2, realCut) = RealTrials[v2[i].persona][t];
                var real = realV2.Zip(realCut, (x, y) => (double)(y - x)).ToList();
                Console.WriteLine($"{v2[i].persona,-10} {$"{PerWave(a, x => x.Damage):0.0} -> {PerWave(b, x => x.Damage):0.0}  {Change(Interval(sim))}",-42} " +
                                  $"{realV2.Average():0.0} -> {realCut.Average():0.0}  {Change(Interval(real))}");
            }
        }
    }

    // ---------------------------------------------------------------- ablate

    static void Ablate(int seeds, int episodes)
    {
        var settings = new (string label, GameArenaOptions options)[]
        {
            ("the game's rules", new GameArenaOptions()),
            ("a fixed 0.42 s lock after a shot (no Animator)", Make(o => o.AnimatorLock = false)),
            ("no friction against the base", Make(o => o.BaseFriction = 0f)),
            ("the base does not block the player", Make(o => o.BaseBlocksPlayer = false)),
            ("zombies hit without a wind-up", Make(o => o.WindupAttack = false)),
            ("a zombie killed mid-swing misses", Make(o => o.DyingStrike = false)),
            ("bodies do not stop bullets", Make(o => o.CorpsesBlockBullets = false)),
            ("no ambushes (Baseline commander)", Make(o => o.Commander = SimCommander.Baseline)),
        };
        Console.WriteLine("\n== Damage per wave by persona under each change (real eval in the last row)");
        Console.WriteLine($"{"",-48} " + string.Join(" ", Personas.Select(p => $"{p.name,9}")));
        foreach (var (label, options) in settings)
        {
            var r = Run(options, seeds, episodes);
            Console.WriteLine($"{label,-48} " + string.Join(" ", r.Select(x => $"{PerWave(x.runs, a => a.Damage),9:0.0}")));
        }
        Console.WriteLine($"{"real eval (V2, 10 rounds)",-48} " + string.Join(" ", Personas.Select(p => $"{RealV2[p.name].Dmg,9:0.0}")));
    }
}
