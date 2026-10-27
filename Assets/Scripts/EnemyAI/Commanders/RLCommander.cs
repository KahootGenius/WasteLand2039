using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// V1 强化学习指挥官（ML-Agents PPO）。与 BaselineCommander 使用相同的输入（HordeContext）和命令接口。
///
/// - 3 个小队，命令为 自主 / 追击 / 驻守区域 k；另选增援加入哪个小队。动作方案见 CommanderActions：
///   RetaskOne（默认，每次决策最多改派一个小队）或 PerSquad（v1_ppo_01，每次决策给每个小队一个命令）。增援加入驻守小队时生成在其驻守区域，且离玩家至少
///   minSpawnDistanceFromPlayer（镜头外，不会凭空出现在玩家眼前）；否则按原版规则生成在玩家周围。
/// - 决策：每 decisionInterval 秒（游戏时间），以及玩家开始逃跑、每波开始时立即决策。
/// - 观察：玩家画像（学到的偏好）+ 玩家状态 + 交战状态 + 波次进度 + 各小队状态 + 各区域敌人数，
///   长度 ObservationSize（区域数为 17 时为 111）。
/// - 奖励见 CommanderRewardSettings。
/// - 训练场回合（ArenaEnvironment）即训练回合：通过 IArenaEpisodeListener 开始 / 结束，并读取课程等级。
///
/// 没有连接训练器、也没有模型时使用 Heuristic：所有小队自主、增援轮流分配 ≈ 原版行为。
/// </summary>
[RequireComponent(typeof(BehaviorParameters))]
public class RLCommander : Agent, IHordeCommander, IArenaEpisodeListener
{
    public const string DefaultBehaviorName = "HordeCommander";
    public const int ExpectedZoneCount = 17;
    public const string HeadOnBonusParameter = "head_on_bonus";
    private const int ProfileSize = 8 + ExpectedZoneCount + PlayerProfile.DirectionBins;
    private const int SquadFeatures = 9;
    public const int ObservationSize =
        ProfileSize + ExpectedZoneCount + 5 + 8 + 4 + CommanderActions.Squads * SquadFeatures + ExpectedZoneCount;

    private const float PositionScale = 40f;
    private const float WaveTimeScale = 90f;
    private const int SquadSizeScale = 5;

    [Header("决策")]
    [Tooltip("动作方案（须与 BehaviorParameters 的动作分支和模型一致；安装模型时自动设置）")]
    [SerializeField] private CommanderActions.Scheme actionScheme = CommanderActions.Scheme.RetaskOne;
    [Tooltip("定期决策间隔（秒，游戏时间）")]
    [SerializeField] private float decisionInterval = 2f;
    [Tooltip("驻守小队的交战半径（玩家进入此范围即追击）")]
    [SerializeField] private float holdEngageRadius = 6f;
    [Tooltip("驻守小队的增援生成点离玩家的最小距离（镜头半宽约 8.9）")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 10f;
    [Tooltip("增援生成点在驻守区域代表点附近的随机偏移")]
    [SerializeField] private float spawnJitter = 3f;

    [Header("奖励")]
    [SerializeField] private CommanderRewardSettings rewards = new CommanderRewardSettings();

    [Header("课程")]
    [Tooltip("训练器环境参数名：性格池的课程等级（见 MLTraining/config）")]
    [SerializeField] private string curriculumParameter = "persona_level";

    public string DisplayName => "RL-V1";

    private readonly Squad[] squads = new Squad[CommanderActions.Squads];
    private readonly int[] squadZones = new int[CommanderActions.Squads];
    private readonly int[] zoneCounts = new int[ExpectedZoneCount];
    private readonly float[] profileBuffer = new float[ProfileSize];

    private HordeContext context;
    private CommanderRewardTracker rewardTracker;
    private EngagementTracker subscribedTracker;
    private PlayerController playerController;
    private Rigidbody2D playerBody;
    private PlayerBehaviourMonitor monitor;
    private System.Random rng;
    private int reinforceSquad;
    private int heuristicReinforce;
    private float nextDecisionTime;
    private float lastHealth = -1f;
    private bool playerWasDead;
    private bool warnedZones;

    /// <summary>本训练回合的拦截次数 / 观察到的逃跑次数（统计用）</summary>
    public int Interceptions => rewardTracker != null ? rewardTracker.Interceptions : 0;
    public int EscapesSeen => rewardTracker != null ? rewardTracker.Escapes : 0;

    // ---------- Agent ----------

    public override void Initialize()
    {
        MaxStep = 0; // 回合由训练场控制
        rng = new System.Random(GetInstanceID());
        rewardTracker = new CommanderRewardTracker(rewards);
        for (int i = 0; i < squads.Length; i++)
        {
            squads[i] = new Squad(i);
            squadZones[i] = -1;
        }
    }

    public override void OnEpisodeBegin()
    {
        rewardTracker = new CommanderRewardTracker(rewards);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (context == null || context.Zones == null || context.Zones.ZoneCount != ExpectedZoneCount || context.Player == null)
        {
            if (context != null && context.Zones != null && context.Zones.ZoneCount != ExpectedZoneCount && !warnedZones)
            {
                warnedZones = true;
                Debug.LogWarning($"[RLCommander] 区域数 {context.Zones.ZoneCount} ≠ {ExpectedZoneCount}，观察全部为 0", this);
            }
            for (int i = 0; i < ObservationSize; i++)
                sensor.AddObservation(0f);
            return;
        }

        IZoneMap zones = context.Zones;
        Vector2 player = context.Player.position;
        Vector2 basePosition = context.MainBase != null ? (Vector2)context.MainBase.position : player;

        // 1. 玩家画像（33）
        if (context.Profile != null && context.Profile.ObservationSize == ProfileSize)
            context.Profile.WriteObservation(profileBuffer, 0);
        else
            System.Array.Clear(profileBuffer, 0, profileBuffer.Length);
        sensor.AddObservation(profileBuffer);

        // 2. 玩家所在区域（17）
        sensor.AddOneHotObservation(zones.GetZone(player), ExpectedZoneCount);

        // 3. 玩家状态（5）
        Vector2 velocity = playerBody != null ? playerBody.velocity : Vector2.zero;
        sensor.AddObservation(Clamp((player - basePosition) / PositionScale));
        sensor.AddObservation(Clamp(velocity / 5f));
        sensor.AddObservation(playerController != null ? playerController.CurrentHealth / Mathf.Max(1f, playerController.MaxHealth) : 1f);

        // 4. 交战状态（8）
        EngagementTracker tracker = context.Engagement;
        EngagementState state = tracker != null ? tracker.State : EngagementState.None;
        sensor.AddOneHotObservation((int)state, EngagementStateUtil.Count);
        EscapeEpisode escape = tracker != null ? tracker.ActiveEscape : null;
        sensor.AddObservation(escape != null ? 1f : 0f);
        sensor.AddObservation(tracker != null && tracker.IsEngaged ? 1f : 0f);
        sensor.AddObservation(escape != null ? Mathf.Clamp01((Time.time - escape.StartTime) / 10f) : 0f);

        // 5. 波次进度（4）
        int total = Mathf.Max(1, context.TotalEnemyCount);
        int maxActive = context.Wave != null ? Mathf.Max(1, context.Wave.maxActiveEnemies) : 1;
        sensor.AddObservation(Mathf.Clamp01(context.SpawnedCount / (float)total));
        sensor.AddObservation(Mathf.Clamp01(CountAlive() / (float)maxActive));
        sensor.AddObservation(Mathf.Clamp01(context.WaveTime / WaveTimeScale));
        sensor.AddObservation(context.SpawnedCount >= total ? 1f : 0f);

        // 6. 小队（3 × 9）
        for (int i = 0; i < squads.Length; i++)
        {
            Squad squad = squads[i];
            sensor.AddObservation(Mathf.Clamp01(squad.AliveCount / (float)SquadSizeScale));
            EnemyOrderType order = squad.CurrentOrder.Type;
            sensor.AddObservation(order == EnemyOrderType.None ? 1f : 0f);
            sensor.AddObservation(order == EnemyOrderType.Chase ? 1f : 0f);
            sensor.AddObservation(order == EnemyOrderType.HoldAt ? 1f : 0f);
            sensor.AddObservation(order == EnemyOrderType.HoldAt
                ? Clamp((squad.CurrentOrder.Position - player) / PositionScale)
                : Vector2.zero);
            bool hasMembers = squad.TryGetCentroid(out Vector2 centroid);
            sensor.AddObservation(hasMembers ? Clamp((centroid - player) / PositionScale) : Vector2.zero);
            sensor.AddObservation(hasMembers ? 1f : 0f);
        }

        // 7. 各区域的敌人数（17）
        System.Array.Clear(zoneCounts, 0, zoneCounts.Length);
        foreach (GameObject enemyObject in context.ActiveEnemies)
        {
            Enemy enemy = enemyObject != null ? enemyObject.GetComponent<Enemy>() : null;
            if (enemy != null && !enemy.IsDead)
                zoneCounts[zones.GetZone(enemy.transform.position)]++;
        }
        for (int z = 0; z < ExpectedZoneCount; z++)
            sensor.AddObservation(Mathf.Clamp01(zoneCounts[z] / (float)SquadSizeScale));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        ActionSegment<int> discrete = actions.DiscreteActions;
        if (context == null || context.Zones == null)
            return;

        int zoneCount = context.Zones.ZoneCount;
        if (actionScheme == CommanderActions.Scheme.RetaskOne)
        {
            if (discrete.Length < 3)
                return;
            int squad = CommanderActions.DecodeRetaskSquad(discrete[0]);
            if (squad >= 0)
                Apply(squad, CommanderActions.DecodeRetaskOrder(discrete[1], zoneCount, out int zone), zone);
            reinforceSquad = Mathf.Clamp(discrete[2], 0, CommanderActions.Squads - 1);
            return;
        }

        if (discrete.Length < CommanderActions.Squads + 1)
            return;
        for (int i = 0; i < CommanderActions.Squads; i++)
            Apply(i, CommanderActions.DecodeSquad(discrete[i], zoneCount, out int zone), zone);
        reinforceSquad = Mathf.Clamp(discrete[CommanderActions.Squads], 0, CommanderActions.Squads - 1);
    }

    private void Apply(int squad, CommanderActions.Kind kind, int zone)
    {
        switch (kind)
        {
            case CommanderActions.Kind.Autonomous:
                IssueIfChanged(squad, EnemyOrder.None, -1);
                break;
            case CommanderActions.Kind.Chase:
                IssueIfChanged(squad, EnemyOrder.Chase(), -1);
                break;
            case CommanderActions.Kind.Hold:
                IssueIfChanged(squad, EnemyOrder.HoldAt(context.Zones.GetZoneCenter(zone), holdEngageRadius), zone);
                break;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // 近似原版：小队保持自主（不改派），增援轮流加入
        ActionSegment<int> discrete = actionsOut.DiscreteActions;
        if (actionScheme == CommanderActions.Scheme.RetaskOne)
        {
            discrete[0] = 0;
            discrete[1] = CommanderActions.RetaskAutonomous;
            discrete[2] = heuristicReinforce;
        }
        else
        {
            for (int i = 0; i < CommanderActions.Squads; i++)
                discrete[i] = CommanderActions.Autonomous;
            discrete[CommanderActions.Squads] = heuristicReinforce;
        }
        heuristicReinforce = (heuristicReinforce + 1) % CommanderActions.Squads;
    }

    // ---------- IHordeCommander ----------

    public void OnWaveStarted(HordeContext waveContext)
    {
        context = waveContext;
        CachePlayer();
        SubscribeTracker(context.Engagement);

        for (int i = 0; i < squads.Length; i++)
        {
            squads[i] = new Squad(i);
            squadZones[i] = -1;
        }
        reinforceSquad = 0;
        rewardTracker.BeginWave();
        lastHealth = playerController != null ? playerController.CurrentHealth : -1f;
        playerWasDead = playerController != null && playerController.IsDead;

        RequestDecision();
        nextDecisionTime = Time.time + decisionInterval;
    }

    public Vector3 ChooseSpawnPosition(HordeContext waveContext)
    {
        int zone = squadZones[reinforceSquad];
        if (zone >= 0 && waveContext.Zones != null && waveContext.Player != null)
        {
            Vector2 player = waveContext.Player.position;
            Vector2 center = waveContext.Zones.GetZoneCenter(zone);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float radius = (float)rng.NextDouble() * spawnJitter;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (Vector2.Distance(point, player) >= minSpawnDistanceFromPlayer)
                    return point;
            }
        }
        return waveContext.DefaultSpawnPosition();
    }

    public void OnEnemySpawned(HordeContext waveContext, Enemy enemy)
    {
        squads[reinforceSquad].Add(enemy);
    }

    public void Tick(HordeContext waveContext)
    {
        context = waveContext;
        StepRewards();

        if (Time.time >= nextDecisionTime)
        {
            RequestDecision();
            nextDecisionTime = Time.time + decisionInterval;
        }
    }

    public void OnWaveCompleted(HordeContext waveContext)
    {
        StepRewards();
        AddReward(rewardTracker.EndWave());
        UnsubscribeTracker();
        foreach (Squad squad in squads)
            squad.Issue(EnemyOrder.None);
    }

    // ---------- IArenaEpisodeListener ----------

    public void OnArenaEpisodeStarting(ArenaEnvironment arena)
    {
        if (Academy.IsInitialized)
        {
            EnvironmentParameters parameters = Academy.Instance.EnvironmentParameters;
            float level = parameters.GetWithDefault(curriculumParameter, ArenaCurriculum.Level);
            ArenaCurriculum.Level = Mathf.RoundToInt(level);
            // 训练配置可覆盖迎头拦截奖励（MLTraining/config 的 environment_parameters）
            rewards.headOnBonus = parameters.GetWithDefault(HeadOnBonusParameter, rewards.headOnBonus);
        }
    }

    public void OnArenaEpisodeEnded(ArenaEnvironment arena)
    {
        EndEpisode();
    }

    // ---------- 内部 ----------

    private void StepRewards()
    {
        if (context == null)
            return;

        float health = playerController != null ? playerController.CurrentHealth : 0f;
        float damage = lastHealth >= 0f ? Mathf.Max(0f, lastHealth - health) : 0f;
        lastHealth = health;
        bool dead = playerController != null && playerController.IsDead;
        bool justDied = dead && !playerWasDead;
        playerWasDead = dead;

        EngagementTracker tracker = context.Engagement;
        EscapeEpisode escape = tracker != null ? tracker.ActiveEscape : null;
        float nearest = tracker != null ? tracker.NearestEnemyDistance : float.PositiveInfinity;
        float reward = rewardTracker.Step(
            Time.deltaTime,
            tracker != null && tracker.IsEngaged,
            escape != null ? escape.Id : -1,
            escape != null ? Time.time - escape.StartTime : 0f,
            nearest,
            damage,
            justDied,
            escape != null && nearest <= rewards.interceptRadius ? NearestEnemyAheadCosine() : float.NaN);
        if (reward != 0f)
            AddReward(reward);
    }

    private void IssueIfChanged(int squadIndex, EnemyOrder order, int zone)
    {
        EnemyOrder current = squads[squadIndex].CurrentOrder;
        if (current.Type == order.Type && squadZones[squadIndex] == zone)
            return;
        squads[squadIndex].Issue(order);
        squadZones[squadIndex] = zone;

        // 记录改派（评估用：策略把小队派去了哪里）
        if (monitor == null)
            monitor = GetComponent<PlayerBehaviourMonitor>();
        if (monitor != null && context != null && context.Zones != null)
        {
            string playerZone = context.Player != null ? context.Zones.GetZoneName(context.Zones.GetZone(context.Player.position)) : "";
            monitor.LogEvent("commander_order")
                .Add("squad", squadIndex)
                .Add("order", order.Type.ToString())
                .Add("zone", zone >= 0 ? context.Zones.GetZoneName(zone) : "")
                .Add("size", squads[squadIndex].AliveCount)
                .Add("player_zone", playerZone)
                .Write();
        }
    }

    /// <summary>最近的敌人方向与玩家移动方向的夹角余弦（玩家几乎静止或没有敌人时为 NaN）</summary>
    private float NearestEnemyAheadCosine()
    {
        if (context.Player == null || playerBody == null || playerBody.velocity.sqrMagnitude < 0.01f)
            return float.NaN;
        Vector2 player = context.Player.position;
        Vector2 best = Vector2.zero;
        float bestDistance = float.PositiveInfinity;
        foreach (GameObject enemyObject in context.ActiveEnemies)
        {
            Enemy enemy = enemyObject != null ? enemyObject.GetComponent<Enemy>() : null;
            if (enemy == null || enemy.IsDead)
                continue;
            Vector2 offset = (Vector2)enemyObject.transform.position - player;
            float distance = offset.sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = offset;
            }
        }
        if (float.IsPositiveInfinity(bestDistance) || best.sqrMagnitude < 0.0001f)
            return float.NaN;
        return Vector2.Dot(best.normalized, playerBody.velocity.normalized);
    }

    private int CountAlive()
    {
        int alive = 0;
        foreach (GameObject enemyObject in context.ActiveEnemies)
        {
            Enemy enemy = enemyObject != null ? enemyObject.GetComponent<Enemy>() : null;
            if (enemy != null && !enemy.IsDead)
                alive++;
        }
        return alive;
    }

    private void CachePlayer()
    {
        if (context.Player == null)
            return;
        if (playerController == null || playerController.transform != context.Player)
        {
            playerController = context.Player.GetComponent<PlayerController>();
            playerBody = context.Player.GetComponent<Rigidbody2D>();
        }
    }

    private void SubscribeTracker(EngagementTracker tracker)
    {
        if (tracker == subscribedTracker)
            return;
        UnsubscribeTracker();
        subscribedTracker = tracker;
        if (subscribedTracker != null)
            subscribedTracker.EscapeStarted += HandleEscapeStarted;
    }

    private void UnsubscribeTracker()
    {
        if (subscribedTracker != null)
            subscribedTracker.EscapeStarted -= HandleEscapeStarted;
        subscribedTracker = null;
    }

    private void HandleEscapeStarted(EscapeEpisode escape)
    {
        // 玩家开始逃跑：立即决策（事件驱动）
        RequestDecision();
        nextDecisionTime = Time.time + decisionInterval;
    }

    protected override void OnDisable()
    {
        UnsubscribeTracker();
        base.OnDisable();
    }

    private static Vector2 Clamp(Vector2 value)
    {
        return new Vector2(Mathf.Clamp(value.x, -1f, 1f), Mathf.Clamp(value.y, -1f, 1f));
    }

    public CommanderActions.Scheme ActionScheme
    {
        get => actionScheme;
        set => actionScheme = value;
    }

    public float DecisionInterval
    {
        get => decisionInterval;
        set => decisionInterval = Mathf.Max(0.1f, value);
    }

    /// <summary>各方案训练时使用的决策间隔（推理时应保持一致）：PerSquad 1 秒，RetaskOne 2 秒</summary>
    public static float TrainedDecisionInterval(CommanderActions.Scheme scheme)
    {
        return scheme == CommanderActions.Scheme.PerSquad ? 1f : 2f;
    }

    /// <summary>由模型的动作屏蔽输入判断它使用的动作方案；无法判断时返回 null</summary>
    public static CommanderActions.Scheme? DetectScheme(Unity.Sentis.ModelAsset modelAsset)
    {
        if (modelAsset == null)
            return null;
        Unity.Sentis.Model model = Unity.Sentis.ModelLoader.Load(modelAsset);
        foreach (Unity.Sentis.Model.Input input in model.inputs)
        {
            if (input.name == "action_masks" && input.shape.rank == 2 && input.shape[1].isValue)
                return CommanderActions.SchemeFromMaskSize(input.shape[1].value, ExpectedZoneCount);
        }
        return null;
    }

    /// <summary>配置 BehaviorParameters（编辑器生成训练场、安装模型、运行时添加指挥官时调用）</summary>
    public static void ConfigureBehavior(BehaviorParameters behavior,
        CommanderActions.Scheme scheme = CommanderActions.Scheme.RetaskOne)
    {
        behavior.BehaviorName = DefaultBehaviorName;
        behavior.BrainParameters.VectorObservationSize = ObservationSize;
        behavior.BrainParameters.NumStackedVectorObservations = 1;
        behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(CommanderActions.BranchSizes(ExpectedZoneCount, scheme));
    }
}
