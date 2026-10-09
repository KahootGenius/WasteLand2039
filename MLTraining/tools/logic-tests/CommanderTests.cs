// Tests for the pure parts of the learning commanders (Phase 4): action decoding and the reward tracker.

using System;
using System.Linq;

static partial class LogicTests
{
    static void CommanderLearningTests()
    {
        Console.WriteLine("Commander actions");
        int[] sizes = CommanderActions.BranchSizes(17, CommanderActions.Scheme.PerSquad);
        Check(sizes.SequenceEqual(new[] { 20, 20, 20, 3 }), "branch sizes for 17 zones", $"= [{string.Join(", ", sizes)}]");
        Check(CommanderActions.DecodeSquad(0, 17, out int z0) == CommanderActions.Kind.Keep && z0 == -1, "0 = keep");
        Check(CommanderActions.DecodeSquad(1, 17, out _) == CommanderActions.Kind.Autonomous, "1 = autonomous");
        Check(CommanderActions.DecodeSquad(2, 17, out _) == CommanderActions.Kind.Chase, "2 = chase");
        bool holds = Enumerable.Range(0, 17).All(z =>
            CommanderActions.DecodeSquad(CommanderActions.EncodeHold(z), 17, out int decoded) == CommanderActions.Kind.Hold && decoded == z);
        Check(holds, "3 + k = hold zone k (all 17 zones round-trip)");
        Check(CommanderActions.DecodeSquad(20, 17, out _) == CommanderActions.Kind.Keep, "out of range = keep");

        int[] retask = CommanderActions.BranchSizes(17, CommanderActions.Scheme.RetaskOne);
        Check(retask.SequenceEqual(new[] { 4, 19, 3 }), "RetaskOne branch sizes", $"= [{string.Join(", ", retask)}]");
        Check(CommanderActions.DecodeRetaskSquad(0) == -1 && CommanderActions.DecodeRetaskSquad(1) == 0 && CommanderActions.DecodeRetaskSquad(3) == 2,
            "RetaskOne: 0 = no change, 1 + i = squad i");
        bool retaskHolds = Enumerable.Range(0, 17).All(z =>
            CommanderActions.DecodeRetaskOrder(2 + z, 17, out int decoded) == CommanderActions.Kind.Hold && decoded == z);
        Check(CommanderActions.DecodeRetaskOrder(0, 17, out _) == CommanderActions.Kind.Autonomous &&
              CommanderActions.DecodeRetaskOrder(1, 17, out _) == CommanderActions.Kind.Chase && retaskHolds,
            "RetaskOne orders: 0 autonomous, 1 chase, 2 + k hold zone k");
        Check(CommanderActions.SchemeFromMaskSize(63, 17) == CommanderActions.Scheme.PerSquad &&
              CommanderActions.SchemeFromMaskSize(26, 17) == CommanderActions.Scheme.RetaskOne &&
              CommanderActions.SchemeFromMaskSize(10, 17) == null, "scheme detected from action-mask size (63 / 26)");

        Console.WriteLine("Commander rewards");
        var settings = new CommanderRewardSettings();
        var r = new CommanderRewardTracker(settings);
        r.BeginWave();
        float engaged = r.Step(1f, true, -1, 0f, 10f, 0f, false);
        Check(Math.Abs(engaged - settings.engagedPerSecond) < 1e-6, "engaged time reward", $"= {engaged}");
        float early = r.Step(0.1f, true, 1, 0.5f, 1f, 0f, false);
        Check(early < settings.interception * 0.5f, "enemy close within the grace period is not an interception", $"= {early:F3}");
        float intercept = r.Step(0.1f, true, 1, 1.5f, 2f, 0f, false);
        Check(intercept >= settings.interception, "interception after grace", $"= {intercept:F3}");
        float again = r.Step(0.1f, true, 1, 2f, 1f, 0f, false);
        Check(again < settings.interception * 0.5f, "only one interception per escape", $"= {again:F3}");
        float second = r.Step(0.1f, true, 2, 1.5f, 2f, 0f, false);
        Check(second >= settings.interception && r.Interceptions == 2 && r.Escapes == 2, "a new escape can be intercepted again",
            $"(interceptions {r.Interceptions}, escapes {r.Escapes})");
        float hit = r.Step(0.1f, true, -1, 0f, 1f, 20f, false);
        Check(Math.Abs(hit - (20f * settings.damagePerHp + 0.1f * settings.engagedPerSecond)) < 1e-5, "damage reward", $"= {hit:F3}");
        float kill = r.Step(0.1f, false, -1, 0f, 1f, 0f, true);
        Check(Math.Abs(kill - settings.playerKilled) < 1e-6, "kill reward", $"= {kill}");
        Check(r.EndWave() == 0f, "no unharmed penalty after damage");
        r.BeginWave();
        r.Step(1f, false, -1, 0f, 20f, 0f, false);
        Check(Math.Abs(r.EndWave() - settings.unharmedWave) < 1e-6, "unharmed wave penalty", $"= {settings.unharmedWave}");

        var off = new CommanderRewardTracker(new CommanderRewardSettings());
        off.BeginWave();
        float plain = off.Step(0.1f, false, 1, 1.5f, 2f, 0f, false, 1f);
        Check(Math.Abs(plain - off.Settings.interception) < 1e-6 && off.HeadOnInterceptions == 1,
            "head-on bonus off by default (still counted)", $"= {plain:F3}");
        var headOn = new CommanderRewardTracker(new CommanderRewardSettings { headOnBonus = 2f });
        headOn.BeginWave();
        float behind = headOn.Step(0.1f, false, 1, 1.5f, 2f, 0f, false, -0.9f);
        float ahead = headOn.Step(0.1f, false, 2, 1.5f, 2f, 0f, false, 0.8f);
        float unknown = headOn.Step(0.1f, false, 3, 1.5f, 2f, 0f, false);
        Check(Math.Abs(behind - 1f) < 1e-6 && Math.Abs(ahead - 3f) < 1e-6 && Math.Abs(unknown - 1f) < 1e-6 &&
              headOn.HeadOnInterceptions == 1 && headOn.Interceptions == 3,
            "head-on bonus only when the enemy is ahead of the escape", $"= behind {behind}, ahead {ahead}, unknown {unknown}");
    }
}
