// Bot persona tests (Phase 3): the pure BotBrain drives a simulated arena with the same geometry as
// MLArena (base at 0,0; home 0,-4.5; refuges A=E, B=NW, C=SW at 26 units) through the REAL
// EngagementTracker + PlayerProfile. Checks the Phase 3 exit criterion ("each persona produces the
// expected profile") before it is confirmed in Unity with real physics.
//
// Simulated rules mirror the game: player 5 u/s, can't fire while moving (|v| > 0.1), 0.42 s attack
// animation locks movement, 5 shots/s, 30-round magazine, 2 s reload, 20 dmg/shot; zombies 3 u/s,
// 100 HP, chase within 5 units, otherwise walk to the base; waves of 20 zombies, max 5 alive, one
// every 2 s in a 5-15 ring around the player, 90 s limit.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

static partial class LogicTests
{
    static void BotTests()
    {
        BotParamTests();

        Console.WriteLine("Bot personas in a simulated arena (8 waves each, Mixed 12)");
        var fighter = RunPersona("Fighter", BotPersonaPresets.Fighter(), 11);
        var runner = RunPersona("RunnerA", BotPersonaPresets.RunnerA(), 12);
        var mixed = RunPersona("Mixed", BotPersonaPresets.Mixed(), 13, waves: 12);
        var kiter = RunPersona("Kiter", BotPersonaPresets.Kiter(), 14);
        var random = RunPersona("Random", BotPersonaPresets.RandomPlayer(), 15);
        var runnerAmbushed = RunPersona("RunnerA, ambushed at A", BotPersonaPresets.RunnerA(), 16, ambushRoute: 0);
        var adaptiveAmbushed = RunPersona("Adaptive, ambushed at A", BotPersonaPresets.Adaptive(), 16, ambushRoute: 0);
        Console.WriteLine("    label breakdown, RunnerA:");
        PrintLabelBreakdown(runner);
        Console.WriteLine("    label breakdown, Kiter:");
        PrintLabelBreakdown(kiter);

        Console.WriteLine("Checks: Fighter");
        Check(fighter.Decisions.Count > 0 && fighter.Decisions.All(d => d.Mode == BotMode.Fight), "all decisions Fight", $"({fighter.Decisions.Count})");
        Check(fighter.Profile.FightShare > 0.6f, "Fight share > 60%", $"= {Pct(fighter.Profile.FightShare)}");
        Check(fighter.Profile.FleeShare < 0.15f, "Flee share < 15%", $"= {Pct(fighter.Profile.FleeShare)}");
        Check(fighter.Kills >= 20, "kills zombies", $"= {fighter.Kills}");

        Console.WriteLine("Checks: RunnerA");
        var runnerFlees = runner.Decisions.Where(d => d.Mode == BotMode.Flee).ToList();
        Check(runnerFlees.Count > 0 && runnerFlees.Count == runner.Decisions.Count && runnerFlees.All(d => d.RouteName == "A"),
            "all decisions Flee via A", $"({runnerFlees.Count})");
        Check(runner.Profile.FleeShare > 0.6f, "Flee share > 60%", $"= {Pct(runner.Profile.FleeShare)}");
        Check(runner.ValidEscapes >= 6, "escapes recorded", $"= {runner.ValidEscapes}");
        Check(runner.TopZone.StartsWith("E") && !runner.TopZone.StartsWith("E ") , "top escape zone is East", $"= {runner.TopZone}");
        Check(runner.Profile.EscapeDirectionProbability(0) > 0.5f, "escape direction E > 50%", $"= {Pct(runner.Profile.EscapeDirectionProbability(0))}");

        Console.WriteLine("Checks: Mixed (60% flee, B > C)");
        float mixedFleeDecisions = mixed.Decisions.Count(d => d.Mode == BotMode.Flee) / (float)Math.Max(1, mixed.Decisions.Count);
        int viaB = mixed.Decisions.Count(d => d.Mode == BotMode.Flee && d.RouteName == "B");
        int viaC = mixed.Decisions.Count(d => d.Mode == BotMode.Flee && d.RouteName == "C");
        int viaA = mixed.Decisions.Count(d => d.Mode == BotMode.Flee && d.RouteName == "A");
        Check(mixedFleeDecisions > 0.35f && mixedFleeDecisions < 0.85f, "flee decisions ~60%", $"= {Pct(mixedFleeDecisions)} of {mixed.Decisions.Count}");
        Check(viaA == 0 && viaB > viaC, "flee routes B > C, never A", $"(A {viaA}, B {viaB}, C {viaC})");
        Check(mixed.Profile.FightShare > 0.15f && mixed.Profile.FleeShare > 0.15f, "profile shows both Fight and Flee",
            $"(Fight {Pct(mixed.Profile.FightShare)}, Flee {Pct(mixed.Profile.FleeShare)})");
        float nw = mixed.Profile.EscapeDirectionProbability(3), sw = mixed.Profile.EscapeDirectionProbability(5);
        Check(nw > sw, "escape direction NW > SW", $"(NW {Pct(nw)}, SW {Pct(sw)})");

        Console.WriteLine("Checks: Kiter");
        // A kiter stands and shoots until a zombie gets close, then steps back (it can't fire while moving and
        // each shot locks movement ~0.42 s), so about half of its time really is Fight. What sets it apart:
        // Kite time that the other personas don't have, Kite-typed escapes, and a retreat direction.
        float otherKite = new[] { fighter, runner, mixed }.Max(sim => sim.Profile.KiteShare);
        Check(kiter.Profile.KiteShare >= 0.15f && kiter.Profile.KiteShare > kiter.Profile.FleeShare && kiter.Profile.KiteShare > 3f * otherKite,
            "Kite share >= 15%, above Flee, >3x any non-kiting persona",
            $"(Kite {Pct(kiter.Profile.KiteShare)}, Flee {Pct(kiter.Profile.FleeShare)}, others max {Pct(otherKite)})");
        Check(kiter.TopZone.StartsWith("SW") || kiter.TopZone.TrimEnd('1', '2') == "S" || kiter.TopZone.TrimEnd('1', '2') == "W",
            "top escape zone toward route C (SW)", $"= {kiter.TopZone}");
        var kiteEscapes = kiter.Escapes.Where(e => e.IsValid).ToList();
        Check(kiteEscapes.Count > 0 && kiteEscapes.Count(e => e.Type == EngagementState.Kite) * 2 > kiteEscapes.Count,
            "most escapes typed Kite", $"({kiteEscapes.Count(e => e.Type == EngagementState.Kite)}/{kiteEscapes.Count})");
        Check(kiter.Kills > 0, "kiter still kills", $"= {kiter.Kills}");
        // Same kiter, same seed, with the stop-to-shoot merge off (the tracker as of Phase 2)
        var kiterNoGap = new ArenaSim(BotPersonaPresets.Kiter(), 14, null, new EngagementSettings { kiteGapSeconds = 0f });
        kiterNoGap.RunWaves(8);
        Console.WriteLine($"    for comparison, kiteGapSeconds = 0: Kite {Pct(kiterNoGap.Profile.KiteShare)}, Fight {Pct(kiterNoGap.Profile.FightShare)}, " +
                          $"escapes typed Kite {kiterNoGap.Escapes.Count(e => e.IsValid && e.Type == EngagementState.Kite)}/{kiterNoGap.Escapes.Count(e => e.IsValid)}");

        Console.WriteLine("Checks: Adaptive vs RunnerA when route A is ambushed");
        Check(runnerAmbushed.Ambushes >= 2, "ambushes happen on A", $"= {runnerAmbushed.Ambushes}");
        var causes = runnerAmbushed.AmbushCauses.Concat(adaptiveAmbushed.AmbushCauses).ToList();
        Check(causes.Count > 0 && causes.All(c => (c.cause == AmbushCause.Damage) == (c.index < 0)) &&
              causes.Any(c => c.cause != AmbushCause.Damage),
            "each ambush records its cause, and the zombie for 'ahead' / 'at the refuge'",
            "(" + string.Join(", ", causes.GroupBy(c => c.cause).Select(g => $"{g.Key} {g.Count()}")) + ")");
        Check(runnerAmbushed.Decisions.All(d => d.RouteName == "A"), "RunnerA keeps using A (no adaptation)");
        var adaptiveFlees = adaptiveAmbushed.Decisions.Where(d => d.Mode == BotMode.Flee).ToList();
        var laterHalf = adaptiveFlees.Skip(adaptiveFlees.Count / 2).ToList();
        float laterA = laterHalf.Count(d => d.RouteName == "A") / (float)Math.Max(1, laterHalf.Count);
        Check(adaptiveAmbushed.Ambushes >= 1, "Adaptive gets ambushed", $"= {adaptiveAmbushed.Ambushes}");
        Check(laterA < 0.5f, "Adaptive mostly avoids A later", $"(A = {Pct(laterA)} of the last {laterHalf.Count} flees)");
        Check(adaptiveAmbushed.MinWeightA < 0.3f, "Adaptive's weight on A dropped after ambushes",
            $"(min {adaptiveAmbushed.MinWeightA:F2}, started {adaptiveAmbushed.Brain.Params.RouteWeights[0]:F2}, end {adaptiveAmbushed.Brain.RouteWeights[0]:F2})");
        float runnerEast = EastSideEscapeShare(runnerAmbushed), adaptiveEast = EastSideEscapeShare(adaptiveAmbushed);
        Check(adaptiveEast < runnerEast, "profile: Adaptive's escapes end on the east side less often than RunnerA's",
            $"({Pct(adaptiveEast)} vs {Pct(runnerEast)})");

        Console.WriteLine("Checks: Random");
        int modes = random.Decisions.Select(d => d.Mode).Distinct().Count();
        int routes = random.Decisions.Where(d => d.Route >= 0).Select(d => d.Route).Distinct().Count();
        Check(modes >= 2 && routes >= 2, "uses several modes and routes", $"({modes} modes, {routes} routes, {random.Decisions.Count} decisions)");
        Check(random.Profile.Consistency < runner.Profile.Consistency, "less consistent than RunnerA",
            $"({random.Profile.Consistency:F2} vs {runner.Profile.Consistency:F2})");
    }

    static void BotParamTests()
    {
        Console.WriteLine("Bot parameters");
        var rng = new System.Random(5);

        var runner = BotPersonaPresets.RunnerA().Sample(rng, "RunnerA");
        Check(runner.FleeWeight == 1f && runner.FightWeight == 0f && runner.RouteWeights.SequenceEqual(new[] { 1f, 0f, 0f }),
            "zero weights stay zero under jitter (RunnerA = 100% flee via A)");

        var spec = BotPersonaPresets.RandomPlayer();
        bool inRange = true, normalized = true;
        for (int i = 0; i < 200; i++)
        {
            var p = spec.Sample(rng);
            inRange &= p.DecisionDistance >= spec.decisionDistance.min && p.DecisionDistance <= spec.decisionDistance.max;
            inRange &= p.AimError >= spec.aimError.min && p.AimError <= spec.aimError.max;
            normalized &= Mathf.Abs(p.FightWeight + p.FleeWeight + p.KiteWeight - 1f) < 1e-4f;
            normalized &= Mathf.Abs(p.RouteWeights.Sum() - 1f) < 1e-4f;
        }
        Check(inRange, "sampled values stay inside their ranges (200 samples)");
        Check(normalized, "mode and route weights are normalized");

        Vector2 q1 = BotBrain.QuantizeEightWay(new Vector2(1f, 0.3f));
        Vector2 q2 = BotBrain.QuantizeEightWay(new Vector2(-1f, -1.1f));
        Check((q1 - Vector2.right).magnitude < 1e-4f, "8-way: (1, 0.3) -> E", $"= {q1}");
        Check((q2 - new Vector2(-0.7071f, -0.7071f)).magnitude < 1e-3f, "8-way: (-1, -1.1) -> SW", $"= {q2}");
    }

    /// <summary>profile's escape-destination probability summed over the E / NE / SE zones</summary>
    static float EastSideEscapeShare(ArenaSim sim)
    {
        float share = 0f;
        for (int z = 0; z < sim.Zones.ZoneCount; z++)
        {
            string name = sim.Zones.GetZoneName(z);
            if (name.StartsWith("E") || name.StartsWith("NE") || name.StartsWith("SE"))
                share += sim.Profile.EscapeZoneProbability(z);
        }
        return share;
    }

    static void PrintLabelBreakdown(ArenaSim sim)
    {
        foreach (var group in sim.LabelByMode.GroupBy(kv => kv.Key.Item1).OrderByDescending(g => g.Sum(kv => kv.Value)))
        {
            float total = group.Sum(kv => kv.Value);
            string parts = string.Join("  ", group.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.Item2} {kv.Value / total:P0}"));
            Console.WriteLine($"      bot {group.Key,-7} {total,6:F1}s -> tracker {parts}");
        }
    }

    static ArenaSim RunPersona(string label, BotPersonaSpec spec, int seed, int waves = 8, int? ambushRoute = null)
    {
        var sim = new ArenaSim(spec, seed, ambushRoute);
        sim.RunWaves(waves);
        var p = sim.Profile;
        int fight = sim.Decisions.Count(d => d.Mode == BotMode.Fight);
        int flee = sim.Decisions.Count(d => d.Mode == BotMode.Flee);
        int kite = sim.Decisions.Count(d => d.Mode == BotMode.Kite);
        Console.WriteLine($"  {label,-24} decisions F/Fl/K {fight}/{flee}/{kite}  kills {sim.Kills,3}  deaths {sim.Deaths}  " +
                          $"profile Fight {Pct(p.FightShare)} Flee {Pct(p.FleeShare)} Kite {Pct(p.KiteShare)} Passive {Pct(p.PassiveShare)}  " +
                          $"escapes {sim.ValidEscapes,2} top {sim.TopZone} cons {p.Consistency:F2}");
        return sim;
    }

    class ArenaSim
    {
        const float Dt = 0.05f;
        const float SampleDt = 0.1f;
        const float PlayerSpeed = 5f, ZombieSpeed = 3f, Detection = 5f, Contact = 2f, BaseStop = 3.2f;
        const float ZombieHp = 100f, BulletDamage = 20f, WeaponRange = 10f, HitHalfWidth = 1.4f;
        const float FireCooldown = 0.2f, ReloadTime = 2f, AttackLock = 0.42f;
        const float ZombieAttackCooldown = 2f, ZombieDamage = 20f;
        const int Magazine = 30, WaveTotal = 20, MaxActive = 5;
        const float SpawnInterval = 2f, WaveTimeLimit = 90f;

        class Zombie
        {
            public Vector2 Pos;
            public float Hp = ZombieHp;
            public float NextAttack;
            public Vector2? Post; // sentinel: holds this spot, chases only within detection radius
        }

        public readonly RadialZoneMap Zones = new RadialZoneMap(Vector2.zero, 8, 6f, 20f);
        public readonly BotArenaLayout Layout = new BotArenaLayout { Base = Vector2.zero, Home = new Vector2(0f, -4.5f), BoundsRadius = 38f };
        public readonly EngagementTracker Tracker;
        public readonly PlayerProfile Profile;
        public readonly BotBrain Brain;
        public readonly List<BotDecision> Decisions = new List<BotDecision>();
        public readonly List<EscapeEpisode> Escapes = new List<EscapeEpisode>();
        public int Kills, Deaths, Ambushes, ValidEscapes;
        public readonly List<(AmbushCause cause, int index)> AmbushCauses = new List<(AmbushCause, int)>();
        public float MinWeightA = 1f;
        /// <summary>engaged seconds by (what the bot was doing, what the tracker labeled it)</summary>
        public readonly Dictionary<(BotMode, EngagementState), float> LabelByMode = new Dictionary<(BotMode, EngagementState), float>();
        public string TopZone => Profile.TopEscapeZones(1).Select(z => Zones.GetZoneName(z)).FirstOrDefault() ?? "-";

        readonly System.Random rng;
        readonly int? ambushRoute;
        readonly List<Zombie> zombies = new List<Zombie>();
        readonly List<Vector2> enemyPositions = new List<Vector2>();
        Vector2 player, velocity;
        float health, time, reloadEnd = -1f, nextFire, lockUntil, sampleAccumulator, damageSinceSample;
        int ammo, shotsSinceSample;

        public ArenaSim(BotPersonaSpec spec, int seed, int? ambushRoute, EngagementSettings settings = null)
        {
            rng = new System.Random(seed);
            this.ambushRoute = ambushRoute;
            foreach (var (name, degrees) in new[] { ("A", 0f), ("B", 135f), ("C", 225f) })
            {
                float rad = degrees * Mathf.Deg2Rad;
                Layout.Routes.Add(new BotRoute { Name = name, Refuge = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 26f });
            }

            Tracker = new EngagementTracker(Zones, settings) { BasePosition = Vector2.zero };
            Profile = new PlayerProfile(Zones.ZoneCount);
            Tracker.EngagementEnded += Profile.AddEngagement;
            Tracker.EscapeEnded += e => { Profile.AddEscape(e); Escapes.Add(e); if (e.IsValid) ValidEscapes++; };

            Brain = new BotBrain(spec.Sample(rng), Layout, new System.Random(seed * 31 + 7));
            Brain.Decided += Decisions.Add;
            Brain.Ambushed += _ =>
            {
                Ambushes++;
                MinWeightA = Math.Min(MinWeightA, Brain.RouteWeights[0]);
                AmbushCauses.Add((Brain.LastAmbushCause, Brain.LastAmbushEnemyIndex));
            };
        }

        public void RunWaves(int waves)
        {
            for (int w = 0; w < waves; w++)
                RunWave();
        }

        void RunWave()
        {
            player = Layout.Home;
            velocity = Vector2.zero;
            health = 100f;
            ammo = Magazine;
            reloadEnd = -1f;
            lockUntil = 0f;
            zombies.Clear();
            Brain.ResetForWave();

            if (ambushRoute.HasValue)
            {
                Vector2 refuge = Layout.Routes[ambushRoute.Value].Refuge;
                Vector2 side = new Vector2(-refuge.normalized.y, refuge.normalized.x) * 2.5f;
                foreach (Vector2 post in new[] { refuge + side, refuge - side })
                    zombies.Add(new Zombie { Pos = post, Post = post });
            }

            int spawned = 0;
            float nextSpawn = time;
            float end = time + WaveTimeLimit;
            while (time < end && health > 0f && (spawned < WaveTotal || zombies.Any(z => z.Post == null)))
            {
                if (spawned < WaveTotal && zombies.Count(z => z.Post == null) < MaxActive && time >= nextSpawn)
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2);
                    float radius = 5f + (float)rng.NextDouble() * 10f;
                    zombies.Add(new Zombie { Pos = player + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius });
                    spawned++;
                    nextSpawn = time + SpawnInterval;
                }
                Step();
            }
            if (health <= 0f)
                Deaths++;
            Tracker.ForceEnd(new PlayerSample { Time = time, Position = player });
        }

        void Step()
        {
            if (reloadEnd >= 0f && time >= reloadEnd)
            {
                ammo = Magazine;
                reloadEnd = -1f;
            }

            RefreshEnemyPositions();
            bool ready = reloadEnd < 0f && ammo > 0 && time >= nextFire;
            BotCommand command = Brain.Step(new BotObservation
            {
                Time = time, Position = player, Velocity = velocity, Health = health,
                WeaponReady = ready, IsReloading = reloadEnd >= 0f, Ammo = ammo, MagazineSize = Magazine,
                Enemies = enemyPositions
            });

            if (command.Fire && velocity.magnitude <= 0.1f && ready)
                Fire(command.Aim);
            if (command.Reload && reloadEnd < 0f && ammo < Magazine)
                reloadEnd = time + ReloadTime;

            velocity = time < lockUntil || command.Move.sqrMagnitude < 0.0001f ? Vector2.zero : command.Move.normalized * PlayerSpeed;
            player += velocity * Dt;

            foreach (Zombie z in zombies)
            {
                float toPlayer = Vector2.Distance(z.Pos, player);
                Vector2 target;
                float stop;
                if (toPlayer <= Detection) { target = player; stop = Contact; }
                else if (z.Post.HasValue) { target = z.Post.Value; stop = 0.2f; }
                else { target = Vector2.zero; stop = BaseStop; }

                Vector2 offset = target - z.Pos;
                float distance = offset.magnitude;
                if (distance > stop)
                    z.Pos += offset / distance * Mathf.Min(ZombieSpeed * Dt, distance - stop);

                if (Vector2.Distance(z.Pos, player) <= Contact + 0.05f && time >= z.NextAttack)
                {
                    health -= ZombieDamage;
                    damageSinceSample += ZombieDamage;
                    z.NextAttack = time + ZombieAttackCooldown;
                }
            }

            sampleAccumulator += Dt;
            if (sampleAccumulator >= SampleDt - 1e-4f)
            {
                RefreshEnemyPositions();
                Tracker.Step(new PlayerSample
                {
                    Time = time, DeltaTime = sampleAccumulator, Position = player, Velocity = velocity,
                    ShotsFired = shotsSinceSample, DamageTaken = damageSinceSample
                }, enemyPositions);
                if (Tracker.IsEngaged && Tracker.ThreatsInRange > 0)
                {
                    var key = (Brain.Mode, Tracker.State);
                    LabelByMode.TryGetValue(key, out float seconds);
                    LabelByMode[key] = seconds + sampleAccumulator;
                }
                sampleAccumulator = 0f;
                shotsSinceSample = 0;
                damageSinceSample = 0f;
            }
            time += Dt;
        }

        void Fire(Vector2 aim)
        {
            ammo--;
            nextFire = time + FireCooldown;
            lockUntil = time + AttackLock;
            shotsSinceSample++;
            if (ammo <= 0)
                reloadEnd = time + ReloadTime;

            float spread = ((float)rng.NextDouble() * 2f - 1f) * 0.25f * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(aim.x * Mathf.Cos(spread) - aim.y * Mathf.Sin(spread), aim.x * Mathf.Sin(spread) + aim.y * Mathf.Cos(spread)).normalized;
            Zombie hit = null;
            float best = WeaponRange;
            foreach (Zombie z in zombies)
            {
                Vector2 offset = z.Pos - player;
                float along = Vector2.Dot(offset, dir);
                if (along <= 0f || along > best)
                    continue;
                if ((offset - dir * along).magnitude <= HitHalfWidth)
                {
                    hit = z;
                    best = along;
                }
            }
            if (hit == null)
                return;
            hit.Hp -= BulletDamage;
            if (hit.Hp <= 0f)
            {
                zombies.Remove(hit);
                Kills++;
                Tracker.NotifyKill();
            }
        }

        void RefreshEnemyPositions()
        {
            enemyPositions.Clear();
            foreach (Zombie z in zombies)
                enemyPositions.Add(z.Pos);
        }
    }
}
