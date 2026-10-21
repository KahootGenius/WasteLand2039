// Tests for cut-off chasers (Interception and PlayerMotion in Assets/Scripts/EnemyAI/Prediction/Interception.cs,
// 2026-10-20/21). Zombies (3 u/s) are slower than a running player (5 u/s), so a zombie that walks at the player's current
// position never catches a player who runs. With PredictiveCommander's -v2Intercept, zombies chasing a RUNNING player (moving
// for 1 s or more) head for the point where they can meet the player instead (Enemy.InterceptPlayer), assuming the player keeps
// the velocity it has now (smoothed over 0.25 s); within 2.5 they go straight at the player. Players who stop and go (the
// Kiter steps back about 0.6 s, then shoots) are chased directly: cutting them off made the zombies overshoot in the first
// real-arena trial (Kiter damage -24%). The arena checks use the logic-test arena of BotTests.cs with ArenaSim.InterceptZombies.

using System;
using System.Linq;
using UnityEngine;

static partial class LogicTests
{
    static void InterceptionTests()
    {
        Console.WriteLine("Interception (cut-off chasers)");
        InterceptionMathTests();
        PlayerMotionTests();
        InterceptionArenaTests();
    }

    static void InterceptionMathTests()
    {
        // a player standing still: walk straight at it
        float t = Interception.MeetingTime(new Vector2(6f, 0f), Vector2.zero, 3f);
        Vector2 p = Interception.MeetingPoint(Vector2.zero, 3f, new Vector2(6f, 0f), Vector2.zero);
        Check(Mathf.Abs(t - 2f) < 1e-4f && (p - new Vector2(6f, 0f)).magnitude < 1e-4f,
            "player standing still: head for the player (meet after distance / speed)", $"t = {t:0.###}, point {p}");

        // running straight at the zombie from 10 away: both cover 3.75 + 6.25 = 10 in 1.25 s
        t = Interception.MeetingTime(new Vector2(10f, 0f), new Vector2(-5f, 0f), 3f);
        p = Interception.MeetingPoint(Vector2.zero, 3f, new Vector2(10f, 0f), new Vector2(-5f, 0f));
        Check(Mathf.Abs(t - 1.25f) < 1e-4f && (p - new Vector2(3.75f, 0f)).magnitude < 1e-3f,
            "player running at the zombie: meet where both arrive together", $"t = {t:0.###}, point {p}");

        // crossing in front: from (-5, 3) running east at 5, the player meets the zombie (at the origin, walking 3 north) at
        // (0, 3) after 1 s
        t = Interception.MeetingTime(new Vector2(-5f, 3f), new Vector2(5f, 0f), 3f);
        p = Interception.MeetingPoint(Vector2.zero, 3f, new Vector2(-5f, 3f), new Vector2(5f, 0f));
        Check(Mathf.Abs(t - 1f) < 1e-4f && (p - new Vector2(0f, 3f)).magnitude < 1e-3f,
            "player crossing in front: head for the crossing point", $"t = {t:0.###}, point {p}");

        // running away faster: no meeting; aim where the player will be in 1.5 s (the most lead) instead of at the player
        t = Interception.MeetingTime(new Vector2(5f, 0f), new Vector2(5f, 0f), 3f);
        p = Interception.MeetingPoint(Vector2.zero, 3f, new Vector2(5f, 0f), new Vector2(5f, 0f));
        Check(t < 0f && (p - new Vector2(12.5f, 0f)).magnitude < 1e-3f,
            "player running away faster: no meeting, aim 1.5 s ahead of the player", $"t = {t:0.###}, point {p}");

        // the same speed, head-on: meet halfway
        t = Interception.MeetingTime(new Vector2(6f, 0f), new Vector2(-3f, 0f), 3f);
        Check(Mathf.Abs(t - 1f) < 1e-4f, "same speed, head-on: meet halfway", $"t = {t:0.###}");

        // a meeting more than 3 s away (2 × the most lead) is not used: aim 1.5 s ahead instead
        p = Interception.MeetingPoint(Vector2.zero, 3f, new Vector2(30f, 0f), new Vector2(-1f, 0f));
        Check((p - new Vector2(28.5f, 0f)).magnitude < 1e-3f, "meeting too far off (7.5 s): aim 1.5 s ahead", $"point {p}");

        // random cases: every meeting found is real (the zombie and the player reach the point at the same time)
        var rng = new System.Random(5);
        int found = 0;
        float worst = 0f;
        for (int i = 0; i < 1000; i++)
        {
            var offset = new Vector2((float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 30f - 15f);
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (float)(rng.NextDouble() * 6f);
            float meet = Interception.MeetingTime(offset, velocity, 3f);
            if (meet <= 0f || meet > 10f)
                continue;
            found++;
            worst = Mathf.Max(worst, Mathf.Abs((offset + velocity * meet).magnitude - 3f * meet));
        }
        Check(found > 300 && worst < 1e-2f, "every meeting found is real: zombie and player reach the point together",
            $"({found} of 1000 random cases meet within 10 s; worst gap {worst:0.####})");
    }

    static void PlayerMotionTests()
    {
        const float dt = 0.05f;
        // running east at 5 u/s: Running once it has moved for 1 s, velocity ≈ (5, 0)
        var motion = new PlayerMotion();
        Vector2 pos = Vector2.zero;
        bool runningAt09 = true, runningAt11 = false;
        for (int i = 0; i <= 30; i++)
        {
            float time = i * dt;
            motion.Observe(time, pos, new Vector2(5f, 0f));
            if (Mathf.Abs(time - 0.9f) < 1e-3f) runningAt09 = motion.Running;
            if (Mathf.Abs(time - 1.1f) < 1e-3f) runningAt11 = motion.Running;
            pos += new Vector2(5f, 0f) * dt;
        }
        Check(!runningAt09 && runningAt11 && (motion.Velocity - new Vector2(5f, 0f)).magnitude < 0.01f,
            "running east: counts as running after 1 s of moving, velocity (5, 0)",
            $"(running at 0.9 s {runningAt09}, at 1.1 s {runningAt11}; velocity {motion.Velocity})");

        // a kiter's step: 0.6 s back, then 1 s standing to shoot, again and again: never running, so zombies chase directly
        motion = new PlayerMotion();
        bool everRunning = false;
        for (int i = 0; i < 200; i++)
        {
            float time = i * dt;
            bool stepping = (time % 1.6f) < 0.6f;
            motion.Observe(time, pos, stepping ? new Vector2(0f, -5f) : Vector2.zero);
            everRunning |= motion.Running;
        }
        Check(!everRunning, "kiter's 0.6 s steps with stops between: never counts as running", $"(10 s of stepping)");

        // the same run measured from positions only (no rigidbody): the same answer
        motion = new PlayerMotion();
        pos = Vector2.zero;
        for (int i = 0; i <= 30; i++)
        {
            motion.Observe(i * dt, pos);
            pos += new Vector2(0f, 5f) * dt;
        }
        Check(motion.Running && (motion.Velocity - new Vector2(0f, 5f)).magnitude < 0.05f,
            "running north, velocity from positions: running, velocity (0, 5)", $"(velocity {motion.Velocity})");

        // a gap of more than 0.5 s between observations starts over (the player was out of range): last one at 1.5 s, next at 3.1 s
        motion.Observe(3.1f, pos + new Vector2(0f, 3f));
        Check(!motion.Running && motion.MovingSeconds == 0f, "an observation gap > 0.5 s starts the estimate over",
            $"(moving {motion.MovingSeconds:0.##} s)");

        motion.Reset();
        Check(!motion.Running && motion.Velocity == Vector2.zero, "Reset: not running, no velocity");
    }

    static void InterceptionArenaTests()
    {
        // The same bots and seeds with zombies chasing as now (at the player) or cutting running players off; 12 seeds × 8
        // waves. Expected from the 30-seed check (2026-10-21): fleeing bots take more damage (Adaptive 35 -> 50 per wave,
        // RunnerB80 35 -> 46); the Kiter and the Fighter don't run, so nothing changes for them.
        const int seeds = 12, waves = 8;
        (float damage, float caught) Run(Func<BotPersonaSpec> make, bool intercept)
        {
            float damage = 0f;
            int ambushes = 0, flees = 0;
            for (int s = 0; s < seeds; s++)
            {
                var sim = new ArenaSim(make(), 2000 + s, null) { InterceptZombies = intercept };
                sim.RunWaves(waves);
                damage += sim.DamageTaken;
                ambushes += sim.Ambushes;
                flees += sim.Decisions.Count(d => d.Mode == BotMode.Flee);
            }
            return (damage / (seeds * waves), flees > 0 ? (float)ambushes / flees : 0f);
        }

        var adaptive = (chase: Run(BotPersonaPresets.Adaptive, false), cut: Run(BotPersonaPresets.Adaptive, true));
        var runnerB = (chase: Run(BotPersonaPresets.RunnerB80, false), cut: Run(BotPersonaPresets.RunnerB80, true));
        var kiter = (chase: Run(BotPersonaPresets.Kiter, false), cut: Run(BotPersonaPresets.Kiter, true));
        var fighter = (chase: Run(BotPersonaPresets.Fighter, false), cut: Run(BotPersonaPresets.Fighter, true));
        Console.WriteLine($"    damage per wave, chasing at the player -> cutting running players off: Adaptive {adaptive.chase.damage:0.0} -> " +
                          $"{adaptive.cut.damage:0.0} (caught per flee {Pct(adaptive.chase.caught)} -> {Pct(adaptive.cut.caught)}), RunnerB80 " +
                          $"{runnerB.chase.damage:0.0} -> {runnerB.cut.damage:0.0}, Kiter {kiter.chase.damage:0.0} -> {kiter.cut.damage:0.0}, " +
                          $"Fighter {fighter.chase.damage:0.0} -> {fighter.cut.damage:0.0}");
        Check(adaptive.cut.damage > adaptive.chase.damage * 1.15f && adaptive.cut.caught > adaptive.chase.caught,
            "Adaptive bot: cut off, it takes more damage (> +15%) and gets caught more often",
            $"({adaptive.chase.damage:0.0} -> {adaptive.cut.damage:0.0} per wave)");
        Check(runnerB.cut.damage > runnerB.chase.damage * 1.15f, "RunnerB80: cut off, it takes more damage (> +15%)",
            $"({runnerB.chase.damage:0.0} -> {runnerB.cut.damage:0.0} per wave)");
        Check(Mathf.Abs(kiter.cut.damage - kiter.chase.damage) <= kiter.chase.damage * 0.05f,
            "Kiter (steps back, stops to shoot): about the same damage either way (within 5%)",
            $"({kiter.chase.damage:0.0} -> {kiter.cut.damage:0.0} per wave)");
        Check(Mathf.Abs(fighter.cut.damage - fighter.chase.damage) <= fighter.chase.damage * 0.05f,
            "Fighter (stands and shoots): about the same damage either way (within 5%)",
            $"({fighter.chase.damage:0.0} -> {fighter.cut.damage:0.0} per wave)");
    }
}
