// Tests for style-aware allocation (HordeStyle in Assets/Scripts/EnemyAI/Prediction/HordeStyle.cs, 2026-10-21). With
// PredictiveCommander's -v2Style, players who mostly run (fleeing for at least 0.42 of their engaged time, PlayerProfile.
// FleeShare) get a bigger ambush at the refuge (3 zombies instead of V2's 2) and no gate; the others get no ambush at the
// refuge and 1 zombie holding a gate on the predicted way out, 9 from the base, while the rest press them at home.
// 0.42 sits between the eval bots' flee shares (Adaptive / RunnerA / RunnerB80 0.52-0.55, Mixed 0.31, Random 0.21,
// Kiter 0.05, Fighter 0; MLTraining/tools/arena-sim.sh calibrate). The synthetic players of LogicTests.cs (Sim) check
// that the rule reads their profiles the right way.

using System;
using UnityEngine;

static partial class LogicTests
{
    static void StyleTests()
    {
        Console.WriteLine("Style-aware allocation (HordeStyle)");
        float share = HordeStyle.DefaultRunnerFleeShare;
        int ambush = HordeStyle.DefaultRunnerAmbushSize, gate = HordeStyle.DefaultGateSize;

        // the rule
        var runner = HordeStyle.Allocate(0.55f, share, ambush, gate);
        Check(runner == (3, 0), "flee share 0.55 (the runner bots): ambush of 3 at the refuge, no gate", $"-> {runner}");
        var mixed = HordeStyle.Allocate(0.31f, share, ambush, gate);
        Check(mixed == (0, 1), "flee share 0.31 (the Mixed bot): no ambush at the refuge, a gate of 1", $"-> {mixed}");
        var edge = HordeStyle.Allocate(share, share, ambush, gate);
        Check(edge == (3, 0), "exactly at the threshold: counts as a runner", $"-> {edge}");
        var negative = (HordeStyle.Allocate(0.9f, share, -1, gate), HordeStyle.Allocate(0.1f, share, ambush, -2));
        Check(negative == ((0, 0), (0, 0)), "negative sizes count as 0", $"-> {negative}");

        // profiles of synthetic players (the scenarios of LogicTests.cs): a runner is a runner, a fighter and a kiter are not
        var fleeing = new Sim(21);
        for (int round = 0; round < 10; round++) fleeing.RunRound(180f, 0f, 4f, shootWhileRunning: false);
        var kiting = new Sim(22);
        for (int round = 0; round < 10; round++) kiting.RunRound(90f, 270f, 4f, shootWhileRunning: true);
        var fighting = new Sim(23);
        for (int round = 0; round < 10; round++)
        {
            fighting.Player = Vector2.zero;
            fighting.Enemies.Clear();
            fighting.Spawn(4, 12f, 0f, 180f);
            for (float t = 0; t < 20f && fighting.Enemies.Count > 0; t += Sim.Dt) fighting.Step(Vector2.zero, shoot: true);
            fighting.Idle(4f);
        }
        Check(HordeStyle.IsRunner(fleeing.Profile.FleeShare, share), "player who runs east every time: runner",
            $"(flee share {Pct(fleeing.Profile.FleeShare)})");
        Check(!HordeStyle.IsRunner(kiting.Profile.FleeShare, share), "player who backs off shooting: not a runner (gets the gate)",
            $"(flee share {Pct(kiting.Profile.FleeShare)}, kite {Pct(kiting.Profile.KiteShare)})");
        Check(!HordeStyle.IsRunner(fighting.Profile.FleeShare, share), "player who stands and fights: not a runner",
            $"(flee share {Pct(fighting.Profile.FleeShare)})");
    }
}
