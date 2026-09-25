// Behavioral tests for the pure-logic EnemyAI classes (zones, engagement tracker, player profile,
// telemetry writer; bot personas in BotTests.cs), run OUTSIDE Unity against UnityEngine.CoreModule.dll for Vector2/Mathf.
// Build + run: .claude/tools/logic-tests.sh
//
// Simulated world: 0.1 s steps, zombies walk straight at the player at 3 u/s (like the real
// baseline AI), player moves at up to 5 u/s. Scenarios mimic the Phase 2 exit criterion
// ("play fighting" vs "play fleeing east") with synthetic players.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

static partial class LogicTests
{
    static int failures;

    static int Main()
    {
        ZoneTests();
        FighterScenario();
        RunnerEastScenario();
        KiterScenario();
        AdaptationScenario();
        TelemetryFormatTest();
        BotTests();

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    static void Check(bool ok, string name, string detail = "")
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        if (!ok) failures++;
    }

    // ------------------------------------------------------------------ zones

    static void ZoneTests()
    {
        Console.WriteLine("Zones (center 0,0; core 6; band edge 20; 8 sectors)");
        var zones = new RadialZoneMap(Vector2.zero, 8, 6f, 20f);
        Check(zones.ZoneCount == 17, "zone count", $"= {zones.ZoneCount}");

        var cases = new (Vector2 p, string expected)[]
        {
            (new Vector2(1, 1), "Core"), (new Vector2(10, 0), "E1"), (new Vector2(0, 25), "N2"),
            (new Vector2(-7, -7), "SW1"), (new Vector2(30, -30), "SE2"), (new Vector2(-10, 0.1f), "W1"),
            (new Vector2(0, -12), "S1"), (new Vector2(8, 8), "NE1"),
        };
        foreach (var (p, expected) in cases)
        {
            string actual = zones.GetZoneName(zones.GetZone(p));
            Check(actual == expected, $"GetZone{p}", $"-> {actual} (expected {expected})");
        }

        bool roundTrip = true;
        for (int z = 0; z < zones.ZoneCount; z++)
            roundTrip &= zones.GetZone(zones.GetZoneCenter(z)) == z;
        Check(roundTrip, "GetZone(GetZoneCenter(z)) == z for all zones");
    }

    // ------------------------------------------------------------------ simulation

    class Sim
    {
        public const float Dt = 0.1f;
        public const float EnemySpeed = 3f;
        public readonly RadialZoneMap Zones = new RadialZoneMap(Vector2.zero, 8, 6f, 20f);
        public readonly EngagementTracker Tracker;
        public readonly PlayerProfile Profile;
        public readonly List<Vector2> Enemies = new List<Vector2>();
        public readonly System.Random Rng;
        public Vector2 Player;
        public float Time;
        public int ValidEscapes;
        public readonly List<EscapeEpisode> EscapeLog = new List<EscapeEpisode>();

        public Sim(int seed)
        {
            Rng = new System.Random(seed);
            Tracker = new EngagementTracker(Zones) { BasePosition = Vector2.zero };
            Profile = new PlayerProfile(Zones.ZoneCount);
            Tracker.EngagementEnded += Profile.AddEngagement;
            Tracker.EscapeEnded += e => { Profile.AddEscape(e); EscapeLog.Add(e); if (e.IsValid) ValidEscapes++; };
        }

        public void Spawn(int count, float radius, float centerDeg, float spreadDeg)
        {
            for (int i = 0; i < count; i++)
            {
                float deg = centerDeg + (float)(Rng.NextDouble() * 2 - 1) * spreadDeg;
                float rad = deg * Mathf.Deg2Rad;
                Enemies.Add(Player + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius);
            }
        }

        /// <param name="killRange">each shot kills the nearest enemy within this range</param>
        public void Step(Vector2 velocity, bool shoot, float killRange = 5f)
        {
            Player += velocity * Dt;
            float damage = 0f;
            for (int i = 0; i < Enemies.Count; i++)
            {
                Vector2 toPlayer = Player - Enemies[i];
                float d = toPlayer.magnitude;
                if (d > 1f) Enemies[i] += toPlayer / d * Mathf.Min(EnemySpeed * Dt, d - 1f);
                else damage += 2f;
            }

            int shots = 0;
            if (shoot && Mathf.RoundToInt(Time / Dt) % 5 == 0) // 2 shots / s
            {
                shots = 1;
                int nearest = -1;
                float best = killRange;
                for (int i = 0; i < Enemies.Count; i++)
                {
                    float d = Vector2.Distance(Player, Enemies[i]);
                    if (d <= best) { best = d; nearest = i; }
                }
                if (nearest >= 0) { Enemies.RemoveAt(nearest); Tracker.NotifyKill(); }
            }

            Tracker.Step(new PlayerSample
            {
                Time = Time, DeltaTime = Dt, Position = Player, Velocity = velocity,
                ShotsFired = shots, DamageTaken = damage
            }, Enemies);
            Time += Dt;
        }

        public void Idle(float seconds, bool shoot = false)
        {
            for (float t = 0; t < seconds; t += Dt) Step(Vector2.zero, shoot);
        }

        /// <summary>Wait (standing still) until an engagement starts, max `seconds`</summary>
        public void WaitForEngagement(float seconds = 6f)
        {
            for (float t = 0; t < seconds && !Tracker.IsEngaged; t += Dt) Step(Vector2.zero, false);
        }

        /// <summary>A "fleeing" round: wait for contact, run in a direction, lose the pursuers</summary>
        public void RunRound(float spawnCenterDeg, float runDeg, float runSeconds, bool shootWhileRunning)
        {
            Player = Vector2.zero;
            Enemies.Clear();
            Spawn(3, 10f, spawnCenterDeg, 30f);
            WaitForEngagement();
            float jitter = (float)(Rng.NextDouble() * 2 - 1) * 10f; // ±10° human imprecision
            float rad = (runDeg + jitter) * Mathf.Deg2Rad;
            Vector2 velocity = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 5f;
            for (float t = 0; t < runSeconds; t += Dt) Step(velocity, shootWhileRunning, killRange: 0f);
            Enemies.Clear(); // pursuers lose track
            Idle(4f);        // engagement releases
        }
    }

    static string Pct(float v) => v.ToString("P0", CultureInfo.InvariantCulture);

    static void FighterScenario()
    {
        Console.WriteLine("Scenario: FIGHTER (stands still, shoots 2/s, enemies from all sides)");
        var sim = new Sim(1);
        for (int round = 0; round < 10; round++)
        {
            sim.Player = Vector2.zero;
            sim.Enemies.Clear();
            sim.Spawn(4, 12f, 0f, 180f);
            for (float t = 0; t < 20f && sim.Enemies.Count > 0; t += Sim.Dt) sim.Step(Vector2.zero, shoot: true);
            sim.Idle(4f);
        }
        var p = sim.Profile;
        Console.WriteLine("    " + p.Describe(sim.Zones).Replace("\n", "\n    ").TrimEnd());
        Check(p.Engagements >= 8, "engagements detected", $"= {p.Engagements}");
        Check(p.FightShare > 0.7f, "Fight share > 70%", $"= {Pct(p.FightShare)}");
        Check(p.FleeShare < 0.1f, "Flee share < 10%", $"= {Pct(p.FleeShare)}");
        Check(sim.ValidEscapes <= 1, "no escapes recorded", $"= {sim.ValidEscapes}");
    }

    static void RunnerEastScenario()
    {
        Console.WriteLine("Scenario: RUNNER EAST (threat from the west, runs east 4 s, no shooting)");
        var sim = new Sim(2);
        for (int round = 0; round < 10; round++) sim.RunRound(180f, 0f, 4f, shootWhileRunning: false);

        var p = sim.Profile;
        Console.WriteLine("    " + p.Describe(sim.Zones).Replace("\n", "\n    ").TrimEnd());
        var top = p.TopEscapeZones(2).Select(z => sim.Zones.GetZoneName(z)).ToList();
        Check(sim.ValidEscapes >= 9, "escapes detected", $"= {sim.ValidEscapes}/10");
        Check(p.FleeShare > 0.8f, "Flee share > 80%", $"= {Pct(p.FleeShare)}");
        Check(p.PassiveShare < 0.15f, "Passive share < 15% (flee-onset lag bias)", $"= {Pct(p.PassiveShare)}");
        Check(top.Count > 0 && (top[0] == "E1" || top[0] == "E2"), "top escape zone is East", $"= {string.Join(", ", top)}");
        Check(p.EscapeDirectionProbability(0) > 0.8f, "escape direction E > 80%", $"= {Pct(p.EscapeDirectionProbability(0))}");
        Check(p.Consistency > 0.5f, "consistency > 0.5", $"= {p.Consistency:F2}");
        Check(p.TowardBaseRate < 0.2f, "not fleeing toward base", $"= {Pct(p.TowardBaseRate)}");
        Check(sim.EscapeLog.Where(e => e.IsValid).All(e => e.Type == EngagementState.Flee), "all escapes typed Flee");
        Check(p.Confidence >= 0.9f, "confidence ~1 after 10 escapes", $"= {p.Confidence:F2}");
        var obs = p.ToObservation();
        Check(obs.Length == p.ObservationSize && obs.All(v => v >= 0f && v <= 1f), "observation vector in [0,1]", $"(size {obs.Length})");
    }

    static void KiterScenario()
    {
        Console.WriteLine("Scenario: KITER (threat from the north, backs off south while shooting)");
        var sim = new Sim(3);
        for (int round = 0; round < 10; round++) sim.RunRound(90f, 270f, 4f, shootWhileRunning: true);

        var p = sim.Profile;
        Console.WriteLine("    " + p.Describe(sim.Zones).Replace("\n", "\n    ").TrimEnd());
        Check(p.KiteShare > 0.5f, "Kite share > 50%", $"= {Pct(p.KiteShare)}");
        Check(p.FleeShare < 0.2f, "Flee share < 20%", $"= {Pct(p.FleeShare)}");
        Check(p.PassiveShare < 0.15f, "Passive share < 15% (onset lag bias)", $"= {Pct(p.PassiveShare)}");
        Check(p.EscapeDirectionProbability(6) > 0.8f, "escape direction S > 80%", $"= {Pct(p.EscapeDirectionProbability(6))}");
        Check(sim.EscapeLog.Where(e => e.IsValid).All(e => e.Type == EngagementState.Kite), "all escapes typed Kite");
    }

    static void AdaptationScenario()
    {
        Console.WriteLine("Scenario: ADAPTATION (10 escapes east, then 10 escapes north)");
        var sim = new Sim(4);
        for (int round = 0; round < 10; round++) sim.RunRound(180f, 0f, 4f, false);
        string before = sim.Zones.GetZoneName(sim.Profile.TopEscapeZones(1)[0]);
        for (int round = 0; round < 10; round++) sim.RunRound(270f, 90f, 4f, false);
        string after = sim.Zones.GetZoneName(sim.Profile.TopEscapeZones(1)[0]);
        float north = sim.Profile.EscapeDirectionProbability(2);
        Check(before.StartsWith("E") && !before.StartsWith("NE"), "top zone East before switch", $"= {before}");
        Check(after.StartsWith("N") && !after.StartsWith("NE") && !after.StartsWith("NW"), "top zone North after switch", $"= {after}");
        Check(north > 0.6f, "north direction share > 60% after switch", $"= {Pct(north)}");
    }

    // ------------------------------------------------------------------ telemetry

    static void TelemetryFormatTest()
    {
        Console.WriteLine("Telemetry (under a comma-decimal culture)");
        var original = CultureInfo.CurrentCulture;
        string dir = Path.Combine(Path.GetTempPath(), "enemyai-logic-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            try { CultureInfo.CurrentCulture = new CultureInfo("de-DE"); }
            catch (CultureNotFoundException) { Console.WriteLine("    (de-DE unavailable; testing under current culture)"); }
            Console.WriteLine($"    culture = {CultureInfo.CurrentCulture.Name} (decimal '{CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator}')");

            using (var writer = new TelemetryWriter(dir))
            {
                writer.WriteSample(1.5f, 0, -2.25f, 3.125f, 0f, 5f, 87.5f, "NE1", "Flee", 3, 2, 1, float.PositiveInfinity,
                    0.75f, 2f, 1, 12.5f, 4);
                writer.Event("escape_end", 2.5f).Add("route", new List<string> { "Core", "E1", "E\"2" })
                    .Add("obs", new List<float> { 0.5f, 1f / 3f }).Add("valid", true).Add("dx", float.NaN).Write();
                writer.WriteJsonFile("session.json", JsonLine.Standalone().Add("zones", new List<string> { "Core" }).ToString());
            }

            string[] csv = File.ReadAllLines(Path.Combine(dir, "samples.csv"));
            int columns = TelemetryWriter.SampleHeader.Split(',').Length;
            Check(csv.Length == 2, "samples.csv has header + 1 row", $"({csv.Length} lines)");
            Check(csv[1].Split(',').Length == columns, "row column count matches header", $"({csv[1].Split(',').Length}/{columns})");
            Check(csv[1].StartsWith("1.5,0,-2.25,3.125,"), "floats use '.' decimals", $"row = {csv[1]}");
            Check(csv[1].Contains(",1,,0.75,"), "infinite 'nearest' written as empty cell");

            bool jsonOk = true;
            foreach (string line in File.ReadAllLines(Path.Combine(dir, "events.jsonl")))
            {
                try { JsonDocument.Parse(line); } catch (JsonException e) { jsonOk = false; Console.WriteLine("    " + e.Message); }
            }
            Check(jsonOk, "events.jsonl lines are valid JSON (incl. escaped quote, NaN -> null)");
            try { JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "session.json"))); Check(true, "session.json is valid JSON"); }
            catch (JsonException e) { Check(false, "session.json is valid JSON", e.Message); }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
