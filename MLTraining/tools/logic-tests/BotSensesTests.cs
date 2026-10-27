// Tests for the bot's player-like senses (BotSenses in BotBrain.cs, 2026-10-20). A real player only sees what the
// camera shows, so the bot only sees zombies inside MainGame's camera rectangle (orthographic size 5 at 1920x1080),
// which trails the player the way CameraController does. And a catch only teaches the bot to avoid a route while it is
// really running that route (moving toward the route's refuge, or standing at it): a zombie that catches up from the
// route's side while the bot dodges somewhere else no longer counts against that route. BotSenses.Legacy is the bot
// as it was before, kept to reproduce the earlier evaluations.

using System;
using System.Collections.Generic;
using UnityEngine;

static partial class LogicTests
{
    static void BotSensesTests()
    {
        Console.WriteLine("Bot senses: player-like view and catches (legacy for comparison)");
        BotViewTests();
        BotCatchTests();
    }

    /// <summary>A flee-only bot that always takes route A (east), reacts at 5.4 with no delay</summary>
    static BotParams SensesBotParams()
    {
        return new BotParams
        {
            FightWeight = 0f, FleeWeight = 1f, KiteWeight = 0f, RouteWeights = new[] { 1f, 0f, 0f },
            AdaptRoutes = true, AmbushPenalty = 0.2f, AmbushRecovery = 0.05f,
            DecisionDistance = 5.4f, ReactionTime = 0f, SafeDistance = 9f, SafeTime = 2f, LingerTime = 1f
        };
    }

    /// <summary>The MLArena layout (base 0,0; home 0,-4.5; refuges A = E, B = NW, C = SW at 26)</summary>
    static BotArenaLayout SensesLayout(BotSenses senses)
    {
        var layout = new BotArenaLayout { Base = Vector2.zero, Home = new Vector2(0f, -4.5f), BoundsRadius = 38f, Senses = senses };
        foreach (float degrees in new[] { 0f, 135f, 225f })
        {
            float rad = degrees * Mathf.Deg2Rad;
            layout.Routes.Add(new BotRoute { Refuge = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 26f });
        }
        return layout;
    }

    /// <summary>Drives one BotBrain: 0.02 s steps, the player moves at 5 u/s wherever the brain says</summary>
    class SensesRun
    {
        public const float Dt = 0.02f, Speed = 5f;
        public readonly BotBrain Brain;
        public readonly List<(AmbushCause cause, int index)> Catches = new List<(AmbushCause, int)>();
        public Vector2 Player;
        public float Time, Health = 100f;
        Vector2 velocity;

        public SensesRun(BotSenses senses)
        {
            Brain = new BotBrain(SensesBotParams(), SensesLayout(senses), new System.Random(1));
            Player = Brain.Layout.Home;
            Brain.Ambushed += _ => Catches.Add((Brain.LastAmbushCause, Brain.LastAmbushEnemyIndex));
        }

        public void Step(params Vector2[] enemies)
        {
            BotCommand command = Brain.Step(new BotObservation
            {
                Time = Time, Position = Player, Velocity = velocity, Health = Health,
                Ammo = 30, MagazineSize = 30, Enemies = enemies
            });
            velocity = command.Move * Speed;
            Player += velocity * Dt;
            Time += Dt;
        }

        public string Describe()
        {
            return Catches.Count == 0 ? "no catch" : string.Join(", ", Catches.ConvertAll(c => $"{c.cause} (zombie {c.index})"));
        }
    }

    /// <summary>Moves a chasing zombie toward the player, keeping it `gap` away (at most 6 u/s)</summary>
    static Vector2 Chase(Vector2 zombie, Vector2 player, float gap)
    {
        Vector2 toPlayer = player - zombie;
        float excess = toPlayer.magnitude - gap;
        return excess > 0f ? zombie + toPlayer.normalized * Mathf.Min(excess, 6f * SensesRun.Dt) : zombie;
    }

    static void BotViewTests()
    {
        BotSenses playerLike = BotSenses.PlayerLike, legacy = BotSenses.Legacy;
        Check(Mathf.Abs(playerLike.ViewHalfExtents.x - 8.889f) < 0.01f && playerLike.ViewHalfExtents.y == 5f &&
              playerLike.CameraSmoothSpeed == 5f && playerLike.CatchOnlyOnRoute && playerLike.Name == "player_like",
            "player-like senses: MainGame's camera (half 8.89 x 5, smoothing 5), catches only on the route",
            $"= {playerLike.ViewHalfExtents}");
        Check(!legacy.LimitedView && !legacy.CatchOnlyOnRoute && legacy.Name == "legacy" && new BotArenaLayout().Senses.Name == "player_like",
            "legacy senses see everything; a new layout is player-like by default");

        // a zombie 5.3 above the bot is inside its reaction distance (5.4) but off screen (half-height 5);
        // 5.3 to the side it is on screen (half-width 8.89)
        foreach (var (where, offset, playerReacts) in new[] { ("above", new Vector2(0f, 5.3f), false), ("beside", new Vector2(-5.3f, 0f), true) })
        {
            var seeAll = new SensesRun(legacy);
            var seeScreen = new SensesRun(playerLike);
            Vector2 zombie = seeAll.Player + offset;
            for (int i = 0; i < 10; i++)
            {
                seeAll.Step(zombie);
                seeScreen.Step(zombie);
            }
            bool legacyFled = seeAll.Brain.Mode == BotMode.Flee, playerFled = seeScreen.Brain.Mode == BotMode.Flee;
            Check(legacyFled && playerFled == playerReacts,
                $"a zombie 5.3 {where} the bot: {(playerReacts ? "on screen, it flees" : "off screen, it doesn't react")} (legacy flees either way)",
                $"(player-like {seeScreen.Brain.Mode}, legacy {seeAll.Brain.Mode})");
        }

        // the camera trails a running player like CameraController (smoothSpeed 5): v (1 - k dt) / k = 0.9 behind at 5 u/s
        var trail = new SensesRun(playerLike);
        for (int i = 0; i < 100; i++)
            trail.Brain.Step(new BotObservation { Time = i * 0.02f, Position = new Vector2(i * 0.1f, 0f), Health = 100f, Enemies = new Vector2[0] });
        float lag = 9.9f - trail.Brain.ViewCenter.x;
        trail.Brain.ResetForWave();
        trail.Brain.Step(new BotObservation { Time = 2f, Position = new Vector2(3f, 3f), Health = 100f, Enemies = new Vector2[0] });
        Check(lag > 0.8f && lag < 1f && (trail.Brain.ViewCenter - new Vector2(3f, 3f)).magnitude < 1e-4f,
            "the bot's camera trails a running player like the game's camera, and a new wave starts on the player",
            $"(lag {lag:F2} at 5 u/s)");
    }

    static void BotCatchTests()
    {
        var results = new Dictionary<string, SensesRun>();
        foreach (var (senses, label) in new[] { (BotSenses.PlayerLike, "player-like"), (BotSenses.Legacy, "legacy") })
        {
            // 1. running its route east with a zombie chasing 3 behind (no catch), then a zombie 3.5 ahead (a catch);
            //    zombie 0 is far off screen, so the catching zombie's index must point into the full list
            var ahead = new SensesRun(senses);
            Vector2 offscreen = new Vector2(0f, 30f), chaser = ahead.Player + new Vector2(-4f, 0f);
            for (int i = 0; i < 100; i++)
            {
                ahead.Step(offscreen, chaser);
                chaser = Chase(chaser, ahead.Player, 3f);
            }
            int beforeAhead = ahead.Catches.Count;
            Vector2 forward = (ahead.Brain.Layout.Routes[0].Refuge - ahead.Player).normalized;
            ahead.Step(offscreen, chaser, ahead.Player + forward * 3.5f);
            Check(beforeAhead == 0 && ahead.Catches.Count == 1 && ahead.Catches[0].cause == AmbushCause.AheadOnRoute && ahead.Catches[0].index == 2,
                $"{label}: running its route, a chaser behind is no catch, a zombie close ahead is", $"({ahead.Describe()})");

            // 2. route A is blocked at the start: the bot dodges west while a zombie chases 3 behind it, on route A's side
            var dodge = new SensesRun(senses);
            Vector2 blocker = dodge.Player + new Vector2(3.8f, 0f);
            for (int i = 0; i < 150; i++)
            {
                dodge.Step(blocker);
                blocker = Chase(blocker, dodge.Player, 3f);
            }
            results[label + " dodge"] = dodge;

            // 3. the same dodge with the chaser 4.4 behind (not close enough to count), and the bot gets hit at 1.5 s
            var hit = new SensesRun(senses);
            Vector2 hitter = hit.Player + new Vector2(4.4f, 0f);
            for (int i = 0; i < 150; i++)
            {
                if (i == 75)
                    hit.Health -= 20f;
                hit.Step(hitter);
                hitter = Chase(hitter, hit.Player, 4.4f);
            }
            results[label + " hit while dodging"] = hit;

            // 4. running its route and hit at 1.5 s: a catch either way
            var hitOnRoute = new SensesRun(senses);
            Vector2 behind = hitOnRoute.Player + new Vector2(-4f, 0f);
            for (int i = 0; i < 100; i++)
            {
                if (i == 75)
                    hitOnRoute.Health -= 20f;
                hitOnRoute.Step(behind);
            }
            Check(hitOnRoute.Catches.Count == 1 && hitOnRoute.Catches[0].cause == AmbushCause.Damage,
                $"{label}: hit while running its route is a catch", $"({hitOnRoute.Describe()})");

            // 5. runs all the way to refuge A (a zombie chasing 5 behind keeps it from feeling safe), where a zombie waits
            //    4.5 beyond the refuge: a catch at the refuge
            var refuge = new SensesRun(senses);
            Vector2 pursuer = refuge.Player + new Vector2(-4f, 0f), waiting = refuge.Brain.Layout.Routes[0].Refuge + new Vector2(4.5f, 0f);
            for (int i = 0; i < 500 && refuge.Catches.Count == 0; i++)
            {
                refuge.Step(pursuer, waiting);
                pursuer = Chase(pursuer, refuge.Player, 5f);
            }
            Check(refuge.Catches.Count == 1 && refuge.Catches[0].cause == AmbushCause.AtRefuge && refuge.Catches[0].index == 1,
                $"{label}: a zombie waiting at the refuge it reaches is a catch", $"({refuge.Describe()})");
        }

        SensesRun dodgePlayer = results["player-like dodge"], dodgeLegacy = results["legacy dodge"];
        Check(dodgePlayer.Catches.Count == 0 && dodgeLegacy.Catches.Count == 1 && dodgeLegacy.Catches[0].cause == AmbushCause.AheadOnRoute &&
              dodgePlayer.Player.x < -5f && dodgePlayer.Brain.Mode == BotMode.Flee,
            "a zombie chasing from the route's side while the bot dodges elsewhere: legacy counts it against the route, player-like doesn't",
            $"(player-like: {dodgePlayer.Describe()}, ended at x = {dodgePlayer.Player.x:F1}; legacy: {dodgeLegacy.Describe()})");
        SensesRun hitPlayer = results["player-like hit while dodging"], hitLegacy = results["legacy hit while dodging"];
        Check(hitPlayer.Catches.Count == 0 && hitLegacy.Catches.Count == 1 && hitLegacy.Catches[0].cause == AmbushCause.Damage,
            "hit while dodging off its route: legacy counts it against the route, player-like doesn't",
            $"(player-like: {hitPlayer.Describe()}; legacy: {hitLegacy.Describe()})");
    }
}
