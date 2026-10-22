// The eval arena (MLArena_Eval_V2) as plain C#, fast enough to try horde ideas before spending real eval runs (2026-10-21).
// It runs the game's own BotBrain (player-like senses), EngagementTracker, PlayerProfile and FrequencyPredictor, and
// re-implements the rest from the game's code and prefabs:
//   - zombies (Enemy.cs, Zombie_Arena / 001Z): speed 3, sees the player within 5 (6 while holding a post), attack range 1.5,
//     a 0.5 s wind-up that hits only if the player is still within 1.5, cooldown 2 s, 20 damage, 100 HP. A zombie killed
//     mid-swing still lands it (Enemy.Die leaves PerformAttack running); the body stays for the 1 s death animation and still
//     stops bullets. Without the player in range a zombie walks to the base centre and attacks it from 1.5. Its trigger
//     collider passes through everything
//   - the player (ArenaPlayer): speed 5, 100 HP; the base's 5 x 6 box blocks it (zombies and bullets pass), with Unity's
//     default friction of 0.4 when it slides along the box; the weapon fires only while the player stands still; after a shot
//     it cannot move until the attack animation ends (the attack lock, below)
//   - the pistol: 20 damage, 5 shots/s, range 10, 30 rounds, 2 s reload, bullets fly at 20 u/s and hit the zombie's box
//   - waves (HordeEventSpawner + ArenaWave, Day 3): 20 zombies, at most 5 at once, one every 2 s, 5-15 from the player
//   - episodes (ArenaEnvironment): 4 waves, 90 s limit per wave, 2 s pause, the player back home with full health and a
//     new persona draw and player profile each episode
//   - V2 (PredictiveCommander's defaults): ambush posts on the outer ring (radius 25.5) of the predicted escape sectors,
//     quotas, retasking, spawning at the posts; and its -v2Intercept option (cut-off chasers)
// One step is one frame of eval.sh's lockstep (0.02 s of game time), in Unity's order (see Step). Nothing is fitted: every
// number comes from the game. MLTraining/tools/arena-sim.sh calibrate compares the simulated V2 with the real eval (its
// numbers are in Program.cs); damage per wave lands within 10% for six of the seven personas.
//
// Two of the game's quirks decide much of the damage:
//   - pinned against the base: movement is 8-way (BotBrain.QuantizeEightWay), so a bot heading around the base often pushes
//     straight into it and stays put; all of the runners' damage at home is taken there
//   - frozen after a shot (PlayerController.isAttacking and Resources/Characters/PlayerAnimation.controller). Every shot sets
//     isAttacking and the Animator's Attack trigger; FixedUpdate skips Move while isAttacking is set, and only the attack
//     clip's OnAttackComplete event (at its end, 0.4167 s) clears it. Any State -> Attack needs the trigger and IsMoving false
//     (IsMoving is the move input, not the velocity); Attack -> Idle (exit time 0.4, 0.25 s) and Walk -> Idle (0.05 s)
//     cannot be interrupted. The weapon fires only once the player is still, so the first shot after walking comes one frame
//     after the stop, inside the Walk -> Idle transition: its trigger waits. If the move input comes back before the
//     transition ends (a Kiter backing off from a zombie that has just come within 2), the attack animation never starts,
//     nothing clears isAttacking, and the player stays frozen until the move input stops. The real Kiter takes 54 of its 57
//     damage per wave in such freezes (2.3 s or longer after a shot, most ending in death); this model gives 48 of 51
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum SimCommander { Baseline, V2 }

/// <summary>What the simulated arena does; the defaults are the game's rules. The switches reproduce the calibration steps</summary>
public class GameArenaOptions
{
    public SimCommander Commander = SimCommander.V2;
    public bool Intercept;                   // PredictiveCommander -v2Intercept
    public float InterceptMinRun = PlayerMotion.DefaultMinRunSeconds; // the 1-second rule; 0 = the first real trial (no rule)
    public int WavesPerEpisode = 4;
    public bool BaseBlocksPlayer = true;
    public float BaseFriction = 0.4f;        // Unity's default 2D material (no PhysicsMaterial2D in the project)
    public bool WindupAttack = true;         // false: zombies hit the moment they are in range
    public bool WindupNeedsRange = true;     // false: the hit lands even if the player left during the wind-up
    public float ZombieBaseStop = 1.5f;      // Enemy.MoveTowardsTarget: stops within attackRange of the base centre
    public bool BulletTravel = true;         // false: bullets hit at once
    public bool BoxHitbox = true;            // false: bullets hit within HitHalfWidth of the zombie's position
    public float HitHalfWidth = 1.4f;
    public bool DyingStrike = true;
    public float DeathAnimation = 1f;
    public bool CorpsesBlockBullets = true;
    public bool AnimatorLock = true;         // false: a fixed lock of AttackLock after every shot
    public float AttackLock = 0.42f;
    public float ZombieSpeed = 3f, ChaseStop = 1.5f, SpawnMin = 5f, SpawnMax = 15f;
}

public class GameArena
{
    // ---- the game's numbers (Prefabs/001Z.prefab, ArenaPlayer.prefab, Day 3.asset, RangedWeapon, ArenaEnvironment)
    // PlayerBehaviourMonitor samples once its accumulator reaches 1 / 10 s; five float steps of 0.02 add up to just under 0.1,
    // so the game samples every 0.12 s
    const float Dt = 0.02f, SampleRate = 10f;
    const float PlayerSpeed = 5f, Detection = 5f, HoldEngage = 6f;
    const float AttackRange = 1.5f, AttackCooldown = 2f, AttackWindup = 0.5f, ZombieDamage = 20f, ZombieHp = 100f;
    const float BulletDamage = 20f, BulletSpeed = 20f, WeaponRange = 10f, FireCooldown = 0.2f, ReloadTime = 2f;
    const float SpreadDegrees = 0.25f;       // RangedWeapon: (1 - accuracy 0.95) × maxSpread 5
    const int Magazine = 30, WaveTotal = 20, MaxActive = 5;
    const float SpawnInterval = 2f, WaveTimeLimit = 90f, DelayBeforeWave = 2f;
    static readonly Vector2 BaseHalf = new Vector2(2.5f, 3f);             // BoxCollider2D 5 x 6 at the arena centre
    static readonly Vector2 PlayerHalf = new Vector2(0.44f, 0.62f);       // BoxCollider2D 0.22 x 0.31 x scale 4
    static readonly Vector2 PlayerOffset = new Vector2(0f, 0.62f);
    static readonly Vector2 ZombieBoxHalf = new Vector2(1.52f, 1.73f);    // trigger BoxCollider2D 0.38 x 0.43 x scale 8
    static readonly Vector2 ZombieBoxOffset = new Vector2(0f, 0.75f);
    const float BulletRadius = 0.15f;
    // the attack animation (see the header)
    const float AttackClip = 0.4166667f, AttackExitStart = 0.4f * AttackClip, WalkToIdle = 0.05f;
    // V2 (PredictiveCommander's defaults)
    const int AmbushSize = 2;
    const float MinEvidence = 2f, MinProbability = 0.35f, SplitRatio = 0.6f, SwitchMargin = 0.15f, RetaskMinDistance = 9f;
    const float MinSpawnFromPlayer = 10f, SpawnJitter = 2f, PredictionInterval = 1f, RingRadius = 25.5f;

    class Zombie
    {
        public Vector2 Pos;
        public float Hp = ZombieHp;
        public float LastAttack = -99f, AttackEnd = -1f;
        public bool AttackOnPlayer;
        public int Site = -1;          // -1 = no order (walks to the base, chases the player in range)
        public float DeadUntil = -1f;  // dying: removed at this time
        public bool Dead => DeadUntil >= 0f;
        public PlayerMotion Motion;    // its own estimate of the player's motion (Enemy.Interception.cs)
        public Vector2 Target;         // Enemy.currentTarget, set in Update and walked to in the physics step
        public float Stop;             //   stops once within this distance of it
    }

    class Site
    {
        public int Sector = -1;
        public Vector2 Post;
        public int Quota;
        public bool Active => Sector >= 0;
    }

    class Bullet
    {
        public Vector2 Pos, Dir;
        public float Travelled;
    }

    public readonly GameArenaOptions Options;
    public readonly BotArenaLayout Layout = new BotArenaLayout { Base = Vector2.zero, Home = new Vector2(0f, -4.5f), BoundsRadius = 38f };
    readonly RadialZoneMap zones = new RadialZoneMap(Vector2.zero, 8, 6f, 20f);
    readonly BotPersonaSpec spec;
    readonly System.Random rng, brainRng;
    readonly List<Zombie> zombies = new List<Zombie>();
    readonly List<Bullet> bullets = new List<Bullet>();
    readonly List<Vector2> enemyPositions = new List<Vector2>();
    readonly Site[] sites = { new Site(), new Site() };
    readonly float[] sectorProbabilities = new float[EscapeSectors.Count];
    readonly FrequencyPredictor predictor = new FrequencyPredictor();
    readonly PredictionInput input = new PredictionInput();

    BotBrain brain;
    EngagementTracker tracker;
    PlayerProfile profile;
    Vector2 player, velocity;
    float health, time, reloadEnd = -1f, nextFire, lockUntil, sampleAccumulator, damageSinceSample, nextReplan, nextSpawn;
    int ammo, shotsSinceSample, spawned, lastFleeRoute = -1;
    bool replanRequested;
    // the attack lock: PlayerController.isAttacking, the Attack trigger, the attack clip playing, IsMoving, Walk -> Idle
    bool attacking, attackTrigger, attackPlaying, animMoving;
    float attackStart = -99f, settleUntil = -1f;

    // ---- results
    public int Waves, Deaths, Kills, Flees, Catches, Cleared, Timeouts, Shots;
    public float Damage, WaveSeconds;
    public readonly float[] DamageByRing = new float[4], TimeByRing = new float[4]; // home < 8, way 8-20, refuge 20-30, edge > 30
    // by route (A, B, C): flee decisions, catches (BotBrain.Ambushed) and their causes (AmbushCause), and damage away from home
    // (r >= 8) by the route of the wave's latest flee ([3] = no flee yet this wave)
    public readonly int[] FleesByRoute = new int[3], CatchesByRoute = new int[3], CatchCauses = new int[3];
    public readonly float[] OutDamageByRoute = new float[4];
    // pressed against the base: samples (every 0.12 s) and damage taken
    public int Samples, SamplesTouchingBase;
    public float DamageTouchingBase;
    // freezes after a shot (until the player moves, shoots again, dies or the wave ends) during which a zombie came within 2.5:
    // shorter than 0.6 s, 0.6-2.3 s, and 2.3 s or longer by how they ended, with the damage taken in those
    public int ShortFreezes, MidFreezes;
    public readonly Dictionary<string, int> LongFreezes = new Dictionary<string, int>();
    public float LongFreezeDamage;
    float freezeStart = -1f, freezeDamage;
    bool freezeNear;

    public GameArena(BotPersonaSpec spec, int seed, GameArenaOptions options)
    {
        this.spec = spec;
        Options = options ?? new GameArenaOptions();
        rng = new System.Random(seed);
        brainRng = new System.Random(seed * 31 + 7);
        // the eval arena's routes: A = east, B = north-west, C = south-west, refuges 26 from the centre
        foreach (var (name, degrees) in new[] { ("A", 0f), ("B", 135f), ("C", 225f) })
        {
            float rad = degrees * Mathf.Deg2Rad;
            Layout.Routes.Add(new BotRoute { Name = name, Refuge = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 26f });
        }
        input.Features = new float[EscapeFeatures.Size(zones.ZoneCount)];
    }

    public void RunEpisodes(int episodes)
    {
        for (int e = 0; e < episodes; e++)
        {
            BeginEpisode();
            for (int w = 0; w < Options.WavesPerEpisode; w++)
                RunWave();
        }
    }

    void BeginEpisode()
    {
        brain = new BotBrain(spec.Sample(rng), Layout, brainRng);
        brain.Decided += d =>
        {
            if (d.Mode != BotMode.Flee)
                return;
            Flees++;
            if (d.Route >= 0 && d.Route < 3) FleesByRoute[d.Route]++;
            lastFleeRoute = d.Route;
        };
        brain.Ambushed += route =>
        {
            Catches++;
            if (route >= 0 && route < 3) CatchesByRoute[route]++;
            CatchCauses[(int)brain.LastAmbushCause]++;
        };
        tracker = new EngagementTracker(zones) { BasePosition = Vector2.zero };
        profile = new PlayerProfile(zones.ZoneCount);
        tracker.EngagementEnded += profile.AddEngagement;
        tracker.EscapeEnded += e => { profile.AddEscape(e); replanRequested = true; };
    }

    void RunWave()
    {
        Waves++;
        player = Layout.Home;
        velocity = Vector2.zero;
        health = 100f;
        ammo = Magazine;
        reloadEnd = -1f;
        lockUntil = 0f;
        // ArenaEnvironment.ResetPlayerAndBase: OnAttackComplete and Animator.Rebind at every wave start
        attacking = attackTrigger = attackPlaying = animMoving = false;
        settleUntil = -1f;
        zombies.Clear();
        bullets.Clear();
        brain.ResetForWave();
        lastFleeRoute = -1;
        time += DelayBeforeWave;

        foreach (Site site in sites)
        {
            site.Sector = -1;
            site.Quota = 0;
        }
        spawned = 0;
        nextSpawn = time;
        float start = time;
        Replan();
        nextReplan = time + PredictionInterval;

        while (true)
        {
            if (health <= 0f) { Deaths++; CloseFreeze("died"); break; }
            if (spawned >= WaveTotal && zombies.Count == 0) { Cleared++; CloseFreeze("wave end"); break; }
            if (time - start >= WaveTimeLimit) { Timeouts++; CloseFreeze("wave end"); break; }
            if (spawned < WaveTotal && zombies.Count < MaxActive && time >= nextSpawn)
            {
                Spawn();
                nextSpawn = time + SpawnInterval;
            }
            Step();
        }
        WaveSeconds += time - start;
        tracker.ForceEnd(new PlayerSample { Time = time, Position = player });
    }

    // ---------------------------------------------------------------- spawning (HordeEventSpawner + PredictiveCommander)

    void Spawn()
    {
        int site = -1;
        Vector2 point = Vector2.zero;
        if (Options.Commander == SimCommander.V2)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                if (!sites[i].Active || Members(i) >= sites[i].Quota)
                    continue;
                Vector2 candidate = sites[i].Post + InsideUnitCircle() * SpawnJitter;
                if (Vector2.Distance(candidate, player) < MinSpawnFromPlayer)
                    continue;
                site = i;
                point = candidate;
                break;
            }
        }
        if (site < 0)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float radius = Options.SpawnMin + (float)rng.NextDouble() * (Options.SpawnMax - Options.SpawnMin);
            point = player + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        zombies.Add(new Zombie { Pos = point, Target = point, Site = site, Motion = new PlayerMotion(PlayerMotion.DefaultSmoothing, Options.InterceptMinRun) });
        spawned++;
    }

    Vector2 InsideUnitCircle()
    {
        while (true)
        {
            var v = new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
            if (v.sqrMagnitude <= 1f)
                return v;
        }
    }

    // ---------------------------------------------------------------- V2 (PredictiveCommander.Replan / SetSite / FillSites)

    int Members(int site) => zombies.Count(z => z.Site == site && !z.Dead);

    void Replan()
    {
        if (Options.Commander != SimCommander.V2)
            return;
        if (profile.EscapeEvidence < MinEvidence)
        {
            SetSite(0, -1, 0);
            SetSite(1, -1, 0);
            return;
        }
        input.Profile = profile;
        input.CurrentZone = 0;
        predictor.Predict(input, sectorProbabilities);
        int top = EscapeSectors.Top(sectorProbabilities, out _);
        if (top < 0 || sectorProbabilities[top] < MinProbability)
            return; // keep the current ambush
        int current = sites[0].Sector;
        int primary = current >= 0 && sectorProbabilities[top] < sectorProbabilities[current] + SwitchMargin ? current : top;
        int secondary = -1;
        for (int s = 0; s < EscapeSectors.Count; s++)
        {
            if (SectorDistance(s, primary) < 2 || sectorProbabilities[s] < SplitRatio * sectorProbabilities[primary])
                continue;
            if (secondary < 0 || sectorProbabilities[s] > sectorProbabilities[secondary])
                secondary = s;
        }
        SetSite(0, primary, secondary >= 0 ? AmbushSize - AmbushSize / 2 : AmbushSize);
        SetSite(1, secondary, secondary >= 0 ? AmbushSize / 2 : 0);
    }

    static int SectorDistance(int a, int b)
    {
        int d = Math.Abs(a - b) % EscapeSectors.Count;
        return Math.Min(d, EscapeSectors.Count - d);
    }

    void SetSite(int index, int sector, int quota)
    {
        Site site = sites[index];
        site.Quota = quota;
        if (sector < 0)
        {
            if (site.Active)
                foreach (Zombie z in zombies)
                    if (z.Site == index)
                        z.Site = -1;
            site.Sector = -1;
            return;
        }
        if (site.Sector == sector)
            return;
        site.Sector = sector;
        float angle = sector * 2f * Mathf.PI / EscapeSectors.Count;
        site.Post = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RingRadius;
    }

    void FillSites()
    {
        if (Options.Commander != SimCommander.V2)
            return;
        for (int i = 0; i < sites.Length; i++)
        {
            int members = Members(i);
            for (int k = zombies.Count - 1; k >= 0 && members > sites[i].Quota; k--)
                if (zombies[k].Site == i) { zombies[k].Site = -1; members--; }
            if (!sites[i].Active)
                continue;
            while (members < sites[i].Quota)
            {
                Zombie candidate = null;
                float farthest = RetaskMinDistance;
                foreach (Zombie z in zombies)
                {
                    if (z.Site != -1 || z.Dead)
                        continue;
                    float d = Vector2.Distance(z.Pos, player);
                    if (d >= farthest) { farthest = d; candidate = z; }
                }
                if (candidate == null)
                    break;
                candidate.Site = i;
                members++;
            }
        }
    }

    // ---------------------------------------------------------------- one frame
    // A Unity frame runs FixedUpdate and the physics step (everything moves), then Update (the bot first, the weapon, the
    // commander, each zombie's target and attack; the zombie's wind-up coroutine resumes after Update), then the Animator.
    // A step here is one Update and the physics step after it: a zombie walks toward where the player was at the end of the
    // previous frame, and it attacks from where it stands after moving

    void Step()
    {
        if (reloadEnd >= 0f && time >= reloadEnd) { ammo = Magazine; reloadEnd = -1f; }
        zombies.RemoveAll(z => z.Dead && time >= z.DeadUntil && z.AttackEnd < 0f);

        // ---- Update: the bot, then PlayerController (IsMoving) and WeaponManager (fires only while the player is still)
        enemyPositions.Clear();
        foreach (Zombie z in zombies)
            if (!z.Dead)
                enemyPositions.Add(z.Pos);
        bool ready = reloadEnd < 0f && ammo > 0 && time >= nextFire;
        BotCommand command = brain.Step(new BotObservation
        {
            Time = time, Position = player, Velocity = velocity, Health = health, WeaponReady = ready,
            IsReloading = reloadEnd >= 0f, Ammo = ammo, MagazineSize = Magazine, Enemies = enemyPositions
        });
        if (command.Fire && velocity.magnitude <= 0.1f && ready)
            Fire(command.Aim);
        if (command.Reload && reloadEnd < 0f && ammo < Magazine)
            reloadEnd = time + ReloadTime;

        // the commander (V2) and the zombies
        if (Options.Commander == SimCommander.V2)
        {
            if (replanRequested || time >= nextReplan)
            {
                Replan();
                replanRequested = false;
                if (time >= nextReplan)
                    nextReplan = time + PredictionInterval;
            }
            FillSites();
        }
        int ring = Ring(player);
        foreach (Zombie z in zombies)
            UpdateZombie(z, ring);

        // the Animator
        bool moveInput = command.Move.sqrMagnitude > 0.01f; // IsMoving = moveDirection.magnitude > 0.1
        if (Options.AnimatorLock)
            UpdateAnimator(moveInput);

        // ---- the physics step. PlayerController.FixedUpdate moves the player only while not attacking (otherwise the body
        // keeps its velocity: it fired standing still); zombies that are not attacking walk toward their targets
        if (Options.AnimatorLock)
        {
            if (!attacking)
                velocity = moveInput ? command.Move.normalized * PlayerSpeed : Vector2.zero;
        }
        else
        {
            velocity = time < lockUntil || !moveInput ? Vector2.zero : command.Move.normalized * PlayerSpeed;
        }
        Vector2 before = player;
        player += velocity * Dt;
        if (Options.BaseBlocksPlayer)
            PushOutOfBase(before);
        velocity = (player - before) / Dt; // what the rigidbody ends up doing after the contact
        if (freezeStart >= 0f && (player - before).sqrMagnitude > 1e-8f)
            CloseFreeze("moved");
        foreach (Zombie z in zombies)
            MoveZombie(z);
        StepBullets();

        TimeByRing[Ring(player)] += Dt;
        if (freezeStart >= 0f && !freezeNear)
            foreach (Zombie z in zombies)
                if (!z.Dead && Vector2.Distance(z.Pos, player) <= 2.5f) { freezeNear = true; break; }

        // PlayerBehaviourMonitor's sample: the tracker and profile V2 learns from
        sampleAccumulator += Dt;
        if (sampleAccumulator >= 1f / SampleRate)
        {
            enemyPositions.Clear();
            foreach (Zombie z in zombies)
                if (!z.Dead)
                    enemyPositions.Add(z.Pos);
            tracker.Step(new PlayerSample
            {
                Time = time, DeltaTime = sampleAccumulator, Position = player, Velocity = velocity,
                ShotsFired = shotsSinceSample, DamageTaken = damageSinceSample
            }, enemyPositions);
            Samples++;
            if (TouchingBase())
                SamplesTouchingBase++;
            sampleAccumulator = 0f;
            shotsSinceSample = 0;
            damageSinceSample = 0f;
        }
        time += Dt;
    }

    /// <summary>where the player is: home < 8, way 8-20, refuge 20-30, edge > 30</summary>
    static int Ring(Vector2 position)
    {
        float r = position.magnitude;
        return r < 8f ? 0 : r < 20f ? 1 : r < 30f ? 2 : 3;
    }

    /// <summary>the Animator's update after this frame's scripts (see the header)</summary>
    void UpdateAnimator(bool moveInput)
    {
        if (attackPlaying && time >= attackStart + AttackClip)
        {
            attackPlaying = false;
            attacking = false; // OnAttackComplete
        }
        if (!attackPlaying && animMoving && !moveInput)
            settleUntil = time + WalkToIdle; // Walk -> Idle starts
        bool inExit = attackPlaying && time >= attackStart + AttackExitStart;
        if (attackTrigger && !moveInput && time >= settleUntil && !inExit)
        {
            attackTrigger = false; // Any State -> Attack (also restarts an attack still before its exit)
            attackPlaying = true;
            attackStart = time;
        }
        animMoving = moveInput;
    }

    /// <summary>
    /// the player's box against the base's box (Box2D): pushed back out along the shallower axis, and friction takes up to
    /// BaseFriction x the blocked movement off the slide along the wall. In the real arena a runner pushing diagonally into
    /// the base slides at 2.1 u/s, 0.6 x the 3.5 a frictionless wall would leave; one pushing straight into it stays put
    /// (8-way movement, BotBrain.QuantizeEightWay) and takes all of the runners' damage at home
    /// </summary>
    void PushOutOfBase(Vector2 before)
    {
        Vector2 d = player + PlayerOffset - Layout.Base;
        float ox = BaseHalf.x + PlayerHalf.x - Mathf.Abs(d.x);
        float oy = BaseHalf.y + PlayerHalf.y - Mathf.Abs(d.y);
        if (ox <= 0f || oy <= 0f)
            return;
        if (ox < oy)
        {
            player.x += (d.x >= 0f ? 1f : -1f) * ox;
            float slide = player.y - before.y;
            player.y -= Mathf.Sign(slide) * Mathf.Min(Mathf.Abs(slide), Options.BaseFriction * ox);
        }
        else
        {
            player.y += (d.y >= 0f ? 1f : -1f) * oy;
            float slide = player.x - before.x;
            player.x -= Mathf.Sign(slide) * Mathf.Min(Mathf.Abs(slide), Options.BaseFriction * oy);
        }
    }

    /// <summary>the player's box touches the base (within 0.08, as in the telemetry check)</summary>
    bool TouchingBase()
    {
        Vector2 d = player + PlayerOffset - Layout.Base;
        float gx = Mathf.Abs(d.x) - (BaseHalf.x + PlayerHalf.x), gy = Mathf.Abs(d.y) - (BaseHalf.y + PlayerHalf.y);
        return gx <= 0.08f && gy <= 0.08f && Mathf.Max(gx, gy) >= -0.08f;
    }

    /// <summary>Enemy.Update: the target (DetectTargets), then the attack (HandleAttack and the PerformAttack coroutine)</summary>
    void UpdateZombie(Zombie z, int playerRing)
    {
        float toPlayer = Vector2.Distance(z.Pos, player);
        if (z.Dead)
        {
            // killed mid-swing: the swing still lands if the player is in range
            if (z.AttackEnd >= 0f && time >= z.AttackEnd)
            {
                z.AttackEnd = -1f;
                if (z.AttackOnPlayer && toPlayer <= AttackRange)
                    Hit(playerRing);
            }
            return;
        }

        // the player within range (5, or 6 for a zombie with a HoldAt order) first, then the order's post, then the base
        bool inRange = toPlayer <= (z.Site >= 0 ? HoldEngage : Detection);
        if (inRange)
        {
            if (Options.Intercept)
                z.Motion.Observe(time, player, velocity); // ChasePoint, every frame the player is in range
            z.Target = player;
            if (Options.Intercept && z.Motion.Running && toPlayer > AttackRange + 1f)
                z.Target = Interception.MeetingPoint(z.Pos, Options.ZombieSpeed, player, z.Motion.Velocity);
            z.Stop = Options.ChaseStop;
        }
        else if (z.Site >= 0)
        {
            z.Target = sites[z.Site].Post;
            z.Stop = 0.1f;
        }
        else
        {
            z.Target = Layout.Base;
            z.Stop = Options.ZombieBaseStop;
        }

        if (z.AttackEnd < 0f)
        {
            if (time < z.LastAttack + AttackCooldown)
                return;
            // HandleAttack: the player in range first, otherwise the base. PerformAttack stands still for the 0.5 s wind-up
            if (inRange && toPlayer <= AttackRange)
            {
                z.LastAttack = time;
                if (Options.WindupAttack)
                {
                    z.AttackEnd = time + AttackWindup;
                    z.AttackOnPlayer = true;
                }
                else
                {
                    Hit(playerRing);
                }
            }
            else if (!inRange && z.Site < 0 && Options.WindupAttack && z.Pos.magnitude <= AttackRange)
            {
                z.LastAttack = time;
                z.AttackEnd = time + AttackWindup;
                z.AttackOnPlayer = false;
            }
        }
        else if (time >= z.AttackEnd)
        {
            // the wind-up is over: the hit lands if the player is still in range
            z.AttackEnd = -1f;
            if (z.AttackOnPlayer && (toPlayer <= AttackRange || !Options.WindupNeedsRange))
                Hit(playerRing);
        }
    }

    /// <summary>Enemy.FixedUpdate / MoveTowardsTarget: a full step at moveSpeed until within the stop distance (dead or
    /// attacking zombies stand still)</summary>
    void MoveZombie(Zombie z)
    {
        if (z.Dead || z.AttackEnd >= 0f)
            return;
        Vector2 offset = z.Target - z.Pos;
        float distance = offset.magnitude;
        if (distance > z.Stop)
            z.Pos += offset / distance * Options.ZombieSpeed * Dt;
    }

    void Hit(int ring)
    {
        if (freezeStart >= 0f)
            freezeDamage += ZombieDamage;
        health -= ZombieDamage;
        Damage += ZombieDamage;
        damageSinceSample += ZombieDamage;
        DamageByRing[ring] += ZombieDamage;
        if (ring > 0)
            OutDamageByRoute[lastFleeRoute >= 0 && lastFleeRoute < 3 ? lastFleeRoute : 3] += ZombieDamage;
        if (TouchingBase())
            DamageTouchingBase += ZombieDamage;
    }

    // ---------------------------------------------------------------- shooting

    void Fire(Vector2 aim)
    {
        ammo--;
        Shots++;
        shotsSinceSample++;
        nextFire = time + FireCooldown;
        CloseFreeze("shot");
        freezeStart = time;
        freezeNear = false;
        freezeDamage = 0f;
        attacking = true;     // PlayerController.TriggerAttackAnimation
        attackTrigger = true;
        lockUntil = time + Options.AttackLock;
        if (ammo <= 0)
            reloadEnd = time + ReloadTime;
        float spread = ((float)rng.NextDouble() * 2f - 1f) * SpreadDegrees * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(aim.x * Mathf.Cos(spread) - aim.y * Mathf.Sin(spread), aim.x * Mathf.Sin(spread) + aim.y * Mathf.Cos(spread)).normalized;
        if (Options.BulletTravel)
        {
            bullets.Add(new Bullet { Pos = player, Dir = dir });
            return;
        }
        HitScan(player, dir, WeaponRange);
    }

    void StepBullets()
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = bullets[i];
            float stepLength = BulletSpeed * Dt;
            if (HitScan(b.Pos, b.Dir, stepLength) || (b.Travelled += stepLength) >= WeaponRange)
            {
                bullets.RemoveAt(i);
                continue;
            }
            b.Pos += b.Dir * stepLength;
        }
    }

    /// <summary>the first zombie (or body) on the bullet's path within length takes it</summary>
    bool HitScan(Vector2 from, Vector2 dir, float length)
    {
        Zombie hit = null;
        float best = length;
        foreach (Zombie z in zombies)
        {
            if (z.Dead && !Options.CorpsesBlockBullets)
                continue;
            float along;
            if (Options.BoxHitbox)
            {
                if (!SegmentHitsBox(from, dir, length, z.Pos + ZombieBoxOffset, ZombieBoxHalf, out along) || along > best)
                    continue;
            }
            else
            {
                Vector2 offset = z.Pos - from;
                along = Vector2.Dot(offset, dir);
                if (along < -Options.HitHalfWidth || along > best || (offset - dir * along).magnitude > Options.HitHalfWidth)
                    continue;
            }
            hit = z;
            best = along;
        }
        if (hit == null)
            return false;
        if (hit.Dead)
            return true; // the body takes the bullet
        hit.Hp -= BulletDamage;
        if (hit.Hp <= 0f)
        {
            hit.DeadUntil = time + Options.DeathAnimation;
            if (!Options.DyingStrike)
                hit.AttackEnd = -1f;
            Kills++;
            tracker.NotifyKill();
        }
        return true;
    }

    /// <summary>a bullet (radius BulletRadius) moving from p along d for at most length touches the box: distance along d at entry</summary>
    static bool SegmentHitsBox(Vector2 p, Vector2 d, float length, Vector2 center, Vector2 half, out float along)
    {
        along = 0f;
        Vector2 h = half + new Vector2(BulletRadius, BulletRadius);
        float tmin = -1e9f, tmax = 1e9f;
        for (int axis = 0; axis < 2; axis++)
        {
            float pa = axis == 0 ? p.x - center.x : p.y - center.y, da = axis == 0 ? d.x : d.y, ha = axis == 0 ? h.x : h.y;
            if (Mathf.Abs(da) < 1e-6f)
            {
                if (Mathf.Abs(pa) > ha) return false;
                continue;
            }
            float t1 = (-ha - pa) / da, t2 = (ha - pa) / da;
            if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
            tmin = Mathf.Max(tmin, t1);
            tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        if (tmax < 0f || tmin > length) return false;
        along = Mathf.Max(0f, tmin);
        return true;
    }

    // ---------------------------------------------------------------- freezes after a shot (compared with the telemetry)

    void CloseFreeze(string how)
    {
        if (freezeStart < 0f)
            return;
        float length = time - freezeStart;
        if (freezeNear)
        {
            if (length < 0.6f) ShortFreezes++;
            else if (length < 2.3f) MidFreezes++;
            else
            {
                LongFreezes[how] = (LongFreezes.TryGetValue(how, out int n) ? n : 0) + 1;
                LongFreezeDamage += freezeDamage;
            }
        }
        freezeStart = -1f;
    }
}
