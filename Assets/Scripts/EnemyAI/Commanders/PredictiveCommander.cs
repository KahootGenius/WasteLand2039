using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// V2 指挥官（"学会预测，规则来执行"）：用逃跑预测器（IEscapePredictor）估计玩家下一次逃跑的去向，
/// 按手写规则在那里提前布置伏击小队。与 BaselineCommander / RLCommander 使用相同的输入（HordeContext）和命令接口。
///
/// 规则：
/// - 预测对象（predictFrom）：默认预测玩家从基地起跑时的去向（伏击离开基地的路线）；也可按玩家当前区域预测。
/// - 压迫小队：自主行为（= 原版：发现玩家就追，否则进攻基地），保证玩家会遇到敌人、会逃跑。
/// - 伏击：画像的证据足够（minEvidence）且最可能的逃跑方向概率 ≥ minProbability 时，ambushSize 个敌人驻守该方向上的区域
///   （ambushRing：默认外圈 = 避难点一带；HoldAt，玩家进入 holdEngageRadius 即追击）。第二可能的方向（不相邻）接近时分兵两处。
///   预测不够确定时保持原来的伏击（玩家离开常见的起跑位置时预测会分散），换位置有门槛（switchMargin）。
///   证据不足（新回合 / 新画像）时全部自主 = 基准行为。
/// - 伏击成员来源：新生成的敌人直接生成在伏击点（离玩家至少 minSpawnDistanceFromPlayer，在镜头外）；
///   否则从压迫小队中调离玩家足够远（retaskMinDistance）的敌人走过去。
/// - 默认不对实际的逃跑做反应（reactToFlee 关闭）：评估的是"预测"，不是"反应"。
///
/// 自适应尸潮（默认关闭；两个开关都关 = 上面的 V2，用于 A/B 对照）：
/// - 方向（directionChoice = Thompson，A 部分）：把预测的方向分布当作狄利克雷后验均值抽一个方向（ThompsonSampling.SampleDirection），
///   不再总取最可能的方向，既探索，也让会绕开固定伏击的玩家（Adaptive 机器人 / 人类）绕不开。
/// - 位置（placement = Bandit，B 部分）：伏击老虎机（AmbushBandit），臂 = 扇区 × 离基地的距离档；
///   θ 从"玩家确实往那边跑时，伏击者是否接触到玩家"中学习。默认扇区由方向规则决定，老虎机只选距离（ChooseDistance）；
///   banditChoosesSector（-v2BanditJoint）= 第一次评估的联合选择（得分 P(扇区) × θ），保留作对照。
/// - 任一开关打开即为采样模式：不按 predictionInterval 重新预测，只在波次开始和逃跑结束时选择，
///   而且只在当前伏击"被试过"之后（见 ShouldRedecide）；redecideEveryEscape（-v2RedrawEveryEscape）= 每次逃跑后都重选（对照）。
///   minEvidence 门槛在所有模式下都保留；minProbability / switchMargin 只用于 Argmax。
/// - 试验（逃跑开始时记录各伏击点）：起跑 contactGraceSeconds 秒后，伏击小队任一成员离玩家 ≤ contactRadius 即为接触。
///   默认 6 = holdEngageRadius（玩家跑进伏击、伏击者被触发）；第一次评估用的 2.5（= 监测组件的 intercepted）
///   逃跑的机器人几乎从不进入，奖励全是 0。逃跑结束时只更新玩家实际逃跑方向扇区里的伏击点（其他扇区没有被试验）。
/// - 训练场：每回合开始时老虎机回到先验（resetBanditEachEpisode，可关闭以跨回合保留）。
/// - 跨会话保存（Bandit 模式，且玩家画像也跨会话保存时）：第一波开始时读取 AmbushBanditStore，
///   每波结束、游戏暂停和退出时写入。游戏场景用 EnemyAISettings.v2Thompson / v2Bandit 选择模式（EnemyAIBootstrap）。
///
/// 遥测（经 PlayerBehaviourMonitor）：commander_order（与 RLCommander 格式相同），每次逃跑开始时的 prediction，
/// 每次有效逃跑结束时的 ambush_trial（伏击 / 压迫小队是否接触，所有模式），老虎机每次更新的 bandit_update。
///
/// 命令行（独立运行的评估程序，覆盖 Inspector 设置）：
///   -v2Predictor Frequency|Learned   -v2From Base|PlayerZone   -v2React
///   -v2Thompson   -v2Bandit   -v2BanditShared   -v2BanditKeep（不按回合重置）   -v2Gamma 0.9   -v2Seed N
///   -v2ContactRadius 6   -v2BanditJoint   -v2RedrawEveryEscape
/// </summary>
public class PredictiveCommander : MonoBehaviour, IHordeCommander, IArenaEpisodeListener
{
    public enum PredictorChoice
    {
        /// <summary>按画像统计（无训练，对照）</summary>
        Frequency,
        /// <summary>离线训练的 softmax 回归（learnedWeights）</summary>
        Learned
    }

    public enum PredictionOrigin
    {
        /// <summary>预测玩家从基地（中心区）起跑时的去向：伏击玩家离开基地的路线，只在画像更新时才变</summary>
        Base,
        /// <summary>预测玩家从当前所在区域起跑时的去向：跟随玩家，玩家每换一个区域预测都会变</summary>
        PlayerZone
    }

    public enum AmbushRing
    {
        /// <summary>该方向最外圈的区域（避难点一带）：玩家跑到那里时迎面遇上</summary>
        Outer,
        /// <summary>该方向最内圈的区域（离开基地的路上）</summary>
        Inner
    }

    public enum DirectionChoice
    {
        /// <summary>取最可能的方向（V2）：每 predictionInterval 秒重新预测，有 minProbability / switchMargin 门槛</summary>
        Argmax,
        /// <summary>Thompson 采样（A 部分）：按预测分布的狄利克雷后验抽一个方向，只在决策点抽</summary>
        Thompson
    }

    public enum Placement
    {
        /// <summary>在该方向按 ambushRing 选的区域中心驻守（V2）</summary>
        FixedRing,
        /// <summary>伏击老虎机（B 部分）：扇区 × 距离档，从接触中学习在哪儿等；只在决策点选择</summary>
        Bandit
    }

    private const int MaxAmbushSites = 2;
    /// <summary>FrequencyPredictor 的 routePrior（默认值）：狄利克雷参数 = P × (证据 + 此值)</summary>
    private const float FrequencyRoutePrior = 2f;

    [Header("预测")]
    [SerializeField] private PredictorChoice predictor = PredictorChoice.Frequency;
    [Tooltip("学习型预测器的参数（MLTraining/predictor 训练得到的 JSON）")]
    [SerializeField] private TextAsset learnedWeights;
    [Tooltip("重新预测的间隔（秒，游戏时间）；每次逃跑结束后也立即重新预测")]
    [SerializeField] private float predictionInterval = 1f;
    [Tooltip("按哪里起跑来预测。Base（默认）：伏击玩家从基地出发的逃跑路线（位置稳定）；" +
             "PlayerZone：按玩家当前区域预测（玩家在避难点被赶出来时，伏击会跟着改到下一跳）")]
    [SerializeField] private PredictionOrigin predictFrom = PredictionOrigin.Base;
    [Tooltip("威胁方向特征统计的半径（与 EngagementSettings.threatRadius 一致）")]
    [SerializeField] private float threatRadius = 15f;

    [Header("伏击")]
    [Tooltip("用于伏击的敌人数（其余为压迫小队）")]
    [SerializeField, Min(0)] private int ambushSize = 2;
    [Tooltip("画像中（衰减后的）逃跑证据至少这么多才伏击；之前的行为与基准相同")]
    [SerializeField] private float minEvidence = 2f;
    [Tooltip("最可能的扇区概率至少这么高才伏击")]
    [SerializeField, Range(0f, 1f)] private float minProbability = 0.35f;
    [Tooltip("第二可能的扇区概率 ≥ 最可能的 × 此比例时分兵两处（ambushSize ≥ 2 时）")]
    [SerializeField, Range(0f, 1f)] private float splitRatio = 0.6f;
    [Tooltip("换伏击扇区的门槛：新的最可能扇区须比当前伏击扇区的概率高出这么多")]
    [SerializeField, Range(0f, 1f)] private float switchMargin = 0.15f;
    [Tooltip("在预测方向的哪一圈区域驻守")]
    [SerializeField] private AmbushRing ambushRing = AmbushRing.Outer;
    [SerializeField] private float holdEngageRadius = 6f;
    [Tooltip("从压迫小队调人去伏击时，只调离玩家至少这么远的敌人（近处的敌人会先追玩家）")]
    [SerializeField] private float retaskMinDistance = 9f;
    [Tooltip("伏击点生成增援时离玩家的最小距离（镜头半宽约 8.9）")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 10f;
    [SerializeField] private float spawnJitter = 2f;
    [Tooltip("玩家开始逃跑时把伏击改到实际逃跑方向（反应式变体；默认关闭，只评估预测）")]
    [SerializeField] private bool reactToFlee = false;

    [Header("自适应尸潮（默认 Argmax + FixedRing = V2）")]
    [Tooltip("伏击方向：Argmax = 最可能的方向（V2）；Thompson = 按预测分布抽样（A 部分）")]
    [SerializeField] private DirectionChoice directionChoice = DirectionChoice.Argmax;
    [Tooltip("伏击位置：FixedRing = ambushRing 选的区域中心（V2）；Bandit = 伏击老虎机学习的距离（B 部分）")]
    [SerializeField] private Placement placement = Placement.FixedRing;
    [Tooltip("老虎机的距离档（离基地中心）")]
    [SerializeField] private float[] banditDistances = { 12f, 18f, 25f };
    [Tooltip("共享模式：臂只有距离档，所有扇区共用（实验：在哪儿等是地图的属性还是每条路线的属性）")]
    [SerializeField] private bool banditShared = false;
    [Tooltip("老虎机的折扣 γ：每次更新前把该臂向先验拉回。1 = 不遗忘")]
    [SerializeField, Range(0f, 1f)] private float banditDiscount = AmbushBandit.DefaultDiscount;
    [Tooltip("训练场每回合开始时老虎机回到先验（与画像每回合重置一致）。关闭 = 跨回合保留")]
    [SerializeField] private bool resetBanditEachEpisode = true;
    [Tooltip("采样用的随机数种子（System.Random，不影响 UnityEngine.Random 和生成位置）。0 = 每次运行不同")]
    [SerializeField] private int samplingSeed = 0;
    [Tooltip("联合选择扇区和距离（得分 P(扇区) × θ，第一次评估的做法，对照用）。关闭 = 扇区由方向规则决定，老虎机只选距离")]
    [SerializeField] private bool banditChoosesSector = false;
    [Tooltip("采样模式下每次逃跑结束都重新选择（第一次评估的做法，对照用）。关闭 = 只在当前伏击被试过之后重新选择")]
    [SerializeField] private bool redecideEveryEscape = false;
    [Tooltip("接触：起跑 contactGraceSeconds 秒后伏击者离玩家 ≤ 此距离。默认 6 = holdEngageRadius（伏击者被触发）；" +
             "2.5 = PlayerBehaviourMonitor 的 intercepted（逃跑的机器人几乎从不进入）")]
    [SerializeField] private float contactRadius = 6f;
    [Tooltip("与 PlayerBehaviourMonitor.interceptGraceSeconds 一致")]
    [SerializeField] private float contactGraceSeconds = 1f;

    public string DisplayName
    {
        get
        {
            string name = "V2-" + (activePredictor != null ? activePredictor.Name : predictor.ToString());
            if (directionChoice == DirectionChoice.Thompson)
                name += "+TS";
            if (placement == Placement.Bandit)
            {
                name += "+Bandit";
                if (banditShared)
                    name += "-shared";
                if (Mathf.Abs(banditDiscount - AmbushBandit.DefaultDiscount) > 1e-4f)
                    name += "-g" + banditDiscount.ToString("0.###", CultureInfo.InvariantCulture);
                if (banditChoosesSector)
                    name += "-joint";
                if (Mathf.Abs(contactRadius - 6f) > 1e-4f)
                    name += "-r" + contactRadius.ToString("0.##", CultureInfo.InvariantCulture);
            }
            if (UsesSampling && redecideEveryEscape)
                name += "-every";
            return name;
        }
    }

    /// <summary>当前使用的预测器（学习型参数无效时退回统计预测器）</summary>
    public IEscapePredictor Predictor => activePredictor;

    /// <summary>伏击老虎机（placement = Bandit 时使用；其他模式下存在但不更新）</summary>
    public AmbushBandit Bandit => bandit;

    private class AmbushSite
    {
        public Squad Squad;
        public int Sector = -1;
        public int Zone = -1;
        /// <summary>老虎机的距离档；-1 = FixedRing（区域中心）</summary>
        public int DistanceIndex = -1;
        /// <summary>驻守点（HoldAt 的目标、伏击点生成增援的位置）</summary>
        public Vector2 Position;
        public int Quota;
        public bool Active => Sector >= 0;
    }

    /// <summary>一次逃跑开始时的伏击点快照（试验）</summary>
    private class TrialSite
    {
        public Squad Squad;
        public int Sector;
        public int DistanceIndex;
        public int Zone;
        public float Distance;
        public float MinDistance = float.PositiveInfinity;
    }

    private class Trial
    {
        public int EscapeId;
        public float StartTime;
        public readonly List<TrialSite> Sites = new List<TrialSite>();
        public float PressureMinDistance = float.PositiveInfinity;
        /// <summary>reactToFlee 在起跑时把伏击挪走了：测到的不是"等待"，不更新老虎机</summary>
        public bool Reacted;
    }

    /// <summary>一次有效试验的结果（逃跑结束时计算）</summary>
    private class TrialOutcome
    {
        public Trial Trial;
        public int RanSector;
        /// <summary>有伏击点在玩家实际逃跑的扇区（这次逃跑试过了它）</summary>
        public bool Tested;
        public bool AnyContact;
        public bool TestedContact;
        public bool PressureContact;
        public float OnRouteMinDistance = float.PositiveInfinity;
        public readonly List<string> SiteLog = new List<string>();
        public readonly List<float> SiteMinDistances = new List<float>();
    }

    private readonly AmbushSite[] sites = new AmbushSite[MaxAmbushSites];
    private Squad pressure;
    private HordeContext context;
    private IEscapePredictor activePredictor;
    private PlayerBehaviourMonitor monitor;
    private EngagementTracker subscribedTracker;
    private readonly PredictionInput input = new PredictionInput();
    private readonly List<Vector2> enemyPositions = new List<Vector2>();
    private readonly float[] sectorProbabilities = new float[EscapeSectors.Count];
    private bool hasPrediction;
    private float nextPredictionTime;
    private int pendingSpawnSite = -1;
    private bool confident;

    private System.Random samplingRandom;
    private AmbushBandit bandit;
    private readonly float[] sampledDirections = new float[EscapeSectors.Count];
    private readonly float[] routeBuffer = new float[EscapeSectors.Count];
    private int drawnDirection = -1;
    private Trial trial;
    private bool banditLoadTried;
    private bool banditPersistent;
    private bool banditSaveWarned;
    private string banditScene;
    private int banditSessions;

    /// <summary>任一自适应开关打开：只在决策点选择</summary>
    private bool UsesSampling => directionChoice == DirectionChoice.Thompson || placement == Placement.Bandit;

    private void Awake()
    {
        ApplyCommandLine();
        activePredictor = CreatePredictor();
        CreateSampling();
        monitor = GetComponent<PlayerBehaviourMonitor>();
        ResetSquads();
    }

    private void ApplyCommandLine()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : "";
            if (args[i] == "-v2Predictor" && System.Enum.TryParse(next, true, out PredictorChoice choice))
                predictor = choice;
            else if (args[i] == "-v2From" && System.Enum.TryParse(next, true, out PredictionOrigin origin))
                predictFrom = origin;
            else if (args[i] == "-v2React")
                reactToFlee = true;
            else if (args[i] == "-v2Thompson")
                directionChoice = DirectionChoice.Thompson;
            else if (args[i] == "-v2Bandit")
                placement = Placement.Bandit;
            else if (args[i] == "-v2BanditShared")
                banditShared = true;
            else if (args[i] == "-v2BanditKeep")
                resetBanditEachEpisode = false;
            else if (args[i] == "-v2Gamma" && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float gamma))
                banditDiscount = Mathf.Clamp01(gamma);
            else if (args[i] == "-v2Seed" && int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                samplingSeed = seed;
            else if (args[i] == "-v2ContactRadius" && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                contactRadius = Mathf.Max(0f, radius);
            else if (args[i] == "-v2BanditJoint")
                banditChoosesSector = true;
            else if (args[i] == "-v2RedrawEveryEscape")
                redecideEveryEscape = true;
        }
    }

    /// <summary>
    /// 采样用的随机数和老虎机。用自己的 System.Random：UnityEngine.Random 决定生成位置（HordeContext.DefaultSpawnPosition），
    /// 多抽一次就会改变之后所有的生成位置
    /// </summary>
    private void CreateSampling()
    {
        int seed = samplingSeed != 0 ? samplingSeed : unchecked(System.Environment.TickCount * 31 + GetInstanceID());
        samplingRandom = new System.Random(seed);
        bandit = new AmbushBandit(samplingRandom, banditDistances, banditShared, banditDiscount);
    }

    /// <summary>运行时切换预测器（评估脚本 / EnemyAIBootstrap 用）；须在波次开始前调用</summary>
    public void Configure(PredictorChoice choice, TextAsset weights)
    {
        predictor = choice;
        learnedWeights = weights;
        activePredictor = CreatePredictor();
    }

    /// <summary>运行时选择自适应模式（EnemyAIBootstrap 按 EnemyAISettings 调用）；须在第一波开始前调用</summary>
    public void ConfigureAdaptive(bool thompsonDirection, bool banditPlacement)
    {
        directionChoice = thompsonDirection ? DirectionChoice.Thompson : DirectionChoice.Argmax;
        placement = banditPlacement ? Placement.Bandit : Placement.FixedRing;
    }

    private IEscapePredictor CreatePredictor()
    {
        if (predictor == PredictorChoice.Learned)
        {
            if (learnedWeights == null)
            {
                Debug.LogWarning("[PredictiveCommander] 选择了学习型预测器但没有参数（learnedWeights），改用统计预测器", this);
            }
            else
            {
                LearnedPredictorWeights weights = null;
                try
                {
                    weights = JsonUtility.FromJson<LearnedPredictorWeights>(learnedWeights.text);
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"[PredictiveCommander] 无法读取 {learnedWeights.name}: {exception.Message}", this);
                }
                string error = LearnedPredictor.Validate(weights);
                if (error == null)
                    return new LearnedPredictor(weights);
                Debug.LogWarning($"[PredictiveCommander] {learnedWeights.name} 无效（{error}），改用统计预测器", this);
            }
        }
        return new FrequencyPredictor();
    }

    private void ResetSquads()
    {
        pressure = new Squad(0);
        for (int i = 0; i < sites.Length; i++)
            sites[i] = new AmbushSite { Squad = new Squad(i + 1) };
        pendingSpawnSite = -1;
        confident = false;
        drawnDirection = -1;
        trial = null; // 快照引用的是旧小队
    }

    // ---------- IHordeCommander ----------

    public void OnWaveStarted(HordeContext waveContext)
    {
        context = waveContext;
        if (monitor == null)
            monitor = GetComponent<PlayerBehaviourMonitor>(); // 监测组件可能在本组件之后才被添加
        LoadSavedBandit();
        ResetSquads();
        Subscribe(context.Engagement);
        Replan();
        nextPredictionTime = Time.time + predictionInterval;
    }

    public Vector3 ChooseSpawnPosition(HordeContext waveContext)
    {
        pendingSpawnSite = -1;
        if (waveContext.Player != null && waveContext.Zones != null)
        {
            Vector2 player = waveContext.Player.position;
            for (int i = 0; i < sites.Length; i++)
            {
                AmbushSite site = sites[i];
                if (!site.Active || site.Squad.AliveCount >= site.Quota)
                    continue;
                Vector2 point = site.Position + Random.insideUnitCircle * spawnJitter;
                if (Vector2.Distance(point, player) < minSpawnDistanceFromPlayer)
                    continue;
                pendingSpawnSite = i;
                return point;
            }
        }
        return waveContext.DefaultSpawnPosition();
    }

    public void OnEnemySpawned(HordeContext waveContext, Enemy enemy)
    {
        if (pendingSpawnSite >= 0)
            sites[pendingSpawnSite].Squad.Add(enemy);
        else
            pressure.Add(enemy);
        pendingSpawnSite = -1;
    }

    public void Tick(HordeContext waveContext)
    {
        context = waveContext;
        // 采样模式只在决策点（波次开始、逃跑结束）选择；V2 按间隔重新预测
        if (!UsesSampling && Time.time >= nextPredictionTime)
        {
            Replan();
            nextPredictionTime = Time.time + predictionInterval;
        }
        TrackTrial();
        FillSites();
    }

    public void OnWaveCompleted(HordeContext waveContext)
    {
        Unsubscribe();
        pressure.Issue(EnemyOrder.None);
        foreach (AmbushSite site in sites)
            site.Squad.Issue(EnemyOrder.None);
        SaveBandit();
    }

    // ---------- 预测与部署 ----------

    private bool Predict()
    {
        if (context == null || context.Profile == null || context.Zones == null || context.Player == null)
            return false;

        IZoneMap zones = context.Zones;
        if (input.Features == null || input.Features.Length != EscapeFeatures.Size(zones.ZoneCount))
            input.Features = new float[EscapeFeatures.Size(zones.ZoneCount)];

        // 起跑位置：基地（中心区 0）或玩家当前位置
        Vector2 origin = predictFrom == PredictionOrigin.Base ? zones.GetZoneCenter(0) : (Vector2)context.Player.position;
        CollectEnemyPositions();
        input.Profile = context.Profile;
        input.CurrentZone = predictFrom == PredictionOrigin.Base ? 0 : zones.GetZone(origin);
        // 从基地预测时还不知道起跑时的威胁（伏击是提前部署的）：威胁特征置 0
        EscapeFeatures.Build(context.Profile, input.CurrentZone, origin,
            predictFrom == PredictionOrigin.Base ? null : enemyPositions, threatRadius, input.Features);

        if (activePredictor is LearnedPredictor learned && learned.ZoneCount != zones.ZoneCount)
        {
            Debug.LogWarning($"[PredictiveCommander] 学习型预测器的区域数 {learned.ZoneCount} ≠ 场景的 {zones.ZoneCount}，改用统计预测器", this);
            activePredictor = new FrequencyPredictor();
        }
        activePredictor.Predict(input, sectorProbabilities);
        hasPrediction = true;
        return true;
    }

    private void Replan()
    {
        if (!Predict())
        {
            SetSites(-1, -1);
            return;
        }

        if (context.Profile.EscapeEvidence < minEvidence || ambushSize <= 0)
        {
            // 对这个玩家还一无所知（新回合 / 新画像）：与基准相同（所有模式都保留这个门槛）
            confident = false;
            drawnDirection = -1;
            SetSites(-1, -1);
            return;
        }

        // 方向分布：Argmax 用预测本身；Thompson 用从狄利克雷后验抽到的样本
        float[] directions = sectorProbabilities;
        int primary;
        if (directionChoice == DirectionChoice.Thompson)
        {
            drawnDirection = ThompsonSampling.SampleDirection(samplingRandom, sectorProbabilities, DirichletConcentration(), sampledDirections);
            confident = drawnDirection >= 0;
            if (!confident)
                return;
            directions = sampledDirections;
            primary = drawnDirection;
        }
        else
        {
            drawnDirection = -1;
            int top = EscapeSectors.Top(sectorProbabilities, out _);
            confident = top >= 0 && sectorProbabilities[top] >= minProbability;
            if (!confident)
                return; // 保持当前伏击：玩家不在常见的起跑位置时，从当前区域出发的预测会分散

            // 保持当前伏击扇区，除非新的最可能扇区明显更好（采样模式每次试验只选一次，不需要这个门槛）
            int current = sites[0].Sector;
            primary = !UsesSampling && current >= 0 && sectorProbabilities[top] < sectorProbabilities[current] + switchMargin
                ? current
                : top;
        }

        if (placement == Placement.Bandit && banditChoosesSector)
        {
            // 对照（第一次评估的做法）：得分 P(扇区) × θ，老虎机同时决定扇区和距离；第二个点（不相邻扇区）得分接近时分兵
            AmbushChoice first = bandit.Choose(directions, out AmbushChoice second);
            bool split = ambushSize >= 2 && second.IsValid && second.Score >= splitRatio * first.Score;
            SetSites(first, split ? second : AmbushChoice.None);
            return;
        }

        int secondary = SecondarySector(directions, primary);
        if (placement == Placement.Bandit)
        {
            // 扇区由方向规则决定（与 FixedRing 相同的分兵规则），老虎机只选距离
            SetSites(bandit.ChooseDistance(primary), secondary >= 0 ? bandit.ChooseDistance(secondary) : AmbushChoice.None);
            return;
        }
        SetSites(primary, secondary);
    }

    /// <summary>第二个伏击方向：与 primary 不相邻、概率 ≥ primary 的 splitRatio 倍中最大的；没有（或 ambushSize &lt; 2）时 -1</summary>
    private int SecondarySector(float[] directions, int primary)
    {
        int secondary = -1;
        if (ambushSize < 2)
            return secondary;
        for (int s = 0; s < EscapeSectors.Count; s++)
        {
            // 相邻扇区多半是同一条路线跨过扇区边界，不分兵
            if (SectorDistance(s, primary) < 2 || directions[s] < splitRatio * directions[primary])
                continue;
            if (secondary < 0 || directions[s] > directions[secondary])
                secondary = s;
        }
        return secondary;
    }

    /// <summary>
    /// 狄利克雷参数的总量（a_d = P_d × 此值），与 FrequencyPredictor 一致：从基地（区域 0）用每次交战第一次逃跑的证据，
    /// 从其他区域用从该区域出发的证据，再加 routePrior。学习型预测器没有"证据"，沿用同一个值（启发式）
    /// </summary>
    private float DirichletConcentration()
    {
        PlayerProfile profile = context.Profile;
        float evidence = 0f;
        if (input.CurrentZone == 0)
            evidence = profile.FirstEscapeEvidence;
        else if (input.CurrentZone > 0 && input.CurrentZone < profile.ZoneCount)
            evidence = profile.DirectionWeightsFrom(input.CurrentZone, routeBuffer);
        return evidence + FrequencyRoutePrior;
    }

    private static int SectorDistance(int a, int b)
    {
        int difference = Mathf.Abs(a - b) % EscapeSectors.Count;
        return Mathf.Min(difference, EscapeSectors.Count - difference);
    }

    /// <summary>FixedRing：在扇区里按 ambushRing 选的区域中心驻守</summary>
    private void SetSites(int primary, int secondary)
    {
        int primaryQuota = secondary >= 0 ? ambushSize - ambushSize / 2 : ambushSize;
        SetSite(0, primary, -1, primary >= 0 ? primaryQuota : 0);
        SetSite(1, secondary, -1, secondary >= 0 ? ambushSize / 2 : 0);
    }

    /// <summary>Bandit：在扇区方向上离基地中心老虎机所选距离的位置驻守</summary>
    private void SetSites(AmbushChoice primary, AmbushChoice secondary)
    {
        int primaryQuota = secondary.IsValid ? ambushSize - ambushSize / 2 : ambushSize;
        SetSite(0, primary.Sector, primary.DistanceIndex, primary.IsValid ? primaryQuota : 0);
        SetSite(1, secondary.Sector, secondary.DistanceIndex, secondary.IsValid ? ambushSize / 2 : 0);
    }

    /// <param name="distanceIndex">老虎机的距离档；-1 = FixedRing（区域中心）</param>
    private void SetSite(int index, int sector, int distanceIndex, int quota)
    {
        AmbushSite site = sites[index];
        site.Quota = quota;
        int zone = -1;
        Vector2 position = Vector2.zero;
        if (sector >= 0 && distanceIndex >= 0)
        {
            position = SectorPoint(sector, bandit.Distance(distanceIndex));
            zone = context.Zones.GetZone(position);
        }
        else if (sector >= 0)
        {
            zone = ZoneInSector(sector);
            if (zone >= 0)
                position = context.Zones.GetZoneCenter(zone);
        }
        if (zone < 0)
            sector = -1;

        if (sector < 0)
        {
            if (site.Active)
            {
                // 撤销伏击：成员回到压迫小队（自主行为）
                MoveAll(site.Squad, pressure);
                site.Squad.Issue(EnemyOrder.None);
                LogOrder(site.Squad.Id, EnemyOrder.None, -1, pressure.AliveCount);
            }
            site.Sector = -1;
            site.Zone = -1;
            site.DistanceIndex = -1;
            return;
        }

        if (site.Sector == sector && site.Zone == zone && site.DistanceIndex == distanceIndex)
            return;
        site.Sector = sector;
        site.Zone = zone;
        site.DistanceIndex = distanceIndex;
        site.Position = position;
        EnemyOrder order = EnemyOrder.HoldAt(position, holdEngageRadius);
        site.Squad.Issue(order);
        LogOrder(site.Squad.Id, order, zone, site.Squad.AliveCount);
    }

    /// <summary>扇区方向上离基地中心 distance 的点（扇区 s 的方向 = s × 45°，与 RadialZoneMap.GetZoneCenter 一致）</summary>
    private Vector2 SectorPoint(int sector, float distance)
    {
        float angle = sector * 2f * Mathf.PI / EscapeSectors.Count;
        return context.Zones.GetZoneCenter(0) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    /// <summary>该扇区中按 ambushRing 选择的区域（离中心最远 / 最近的）</summary>
    private int ZoneInSector(int sector)
    {
        IZoneMap zones = context.Zones;
        Vector2 center = zones.GetZoneCenter(0);
        int best = -1;
        float bestDistance = 0f;
        for (int zone = 1; zone < zones.ZoneCount; zone++)
        {
            if (EscapeSectors.SectorOf(zones, zone) != sector)
                continue;
            float distance = Vector2.Distance(zones.GetZoneCenter(zone), center);
            bool better = ambushRing == AmbushRing.Outer ? distance > bestDistance : distance < bestDistance;
            if (best < 0 || better)
            {
                best = zone;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>伏击点人数不足时，从压迫小队调离玩家最远的敌人过去；超额时退回压迫小队</summary>
    private void FillSites()
    {
        if (context == null || context.Player == null)
            return;
        Vector2 player = context.Player.position;

        pressure.PruneDestroyed();
        foreach (AmbushSite site in sites)
        {
            site.Squad.PruneDestroyed();
            for (int i = site.Squad.Members.Count - 1; i >= 0 && site.Squad.AliveCount > site.Quota; i--)
                Move(site.Squad.Members[i], site.Squad, pressure);
            if (!site.Active)
                continue;

            while (site.Squad.AliveCount < site.Quota)
            {
                Enemy candidate = null;
                float farthest = retaskMinDistance;
                foreach (Enemy enemy in pressure.Members)
                {
                    if (enemy == null || enemy.IsDead)
                        continue;
                    float distance = Vector2.Distance(enemy.transform.position, player);
                    if (distance >= farthest)
                    {
                        farthest = distance;
                        candidate = enemy;
                    }
                }
                if (candidate == null)
                    break;
                Move(candidate, pressure, site.Squad);
            }
        }
    }

    private static void MoveAll(Squad from, Squad to)
    {
        var members = new List<Enemy>(from.Members);
        foreach (Enemy enemy in members)
            Move(enemy, from, to);
    }

    private static void Move(Enemy enemy, Squad from, Squad to)
    {
        from.Remove(enemy);
        to.Add(enemy); // 继承目标小队的当前命令
    }

    private void CollectEnemyPositions()
    {
        enemyPositions.Clear();
        foreach (GameObject enemyObject in context.ActiveEnemies)
        {
            Enemy enemy = enemyObject != null ? enemyObject.GetComponent<Enemy>() : null;
            if (enemy != null && !enemy.IsDead)
                enemyPositions.Add(enemyObject.transform.position);
        }
    }

    // ---------- 交战事件 ----------

    private void Subscribe(EngagementTracker tracker)
    {
        if (tracker == subscribedTracker)
            return;
        Unsubscribe();
        subscribedTracker = tracker;
        if (subscribedTracker != null)
        {
            subscribedTracker.EscapeStarted += HandleEscapeStarted;
            subscribedTracker.EscapeEnded += HandleEscapeEnded;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedTracker != null)
        {
            subscribedTracker.EscapeStarted -= HandleEscapeStarted;
            subscribedTracker.EscapeEnded -= HandleEscapeEnded;
        }
        subscribedTracker = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            SaveBandit();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        SaveBandit();
    }

    private void HandleEscapeStarted(EscapeEpisode escape)
    {
        LogPrediction(escape);
        BeginTrial(escape); // 在反应式变体挪动伏击之前记录：试验的是"在哪儿等"
        if (!reactToFlee || context == null || context.Zones == null || ambushSize <= 0)
            return;

        // 反应式变体：伏击改到玩家实际逃跑的方向（按当前速度方向的扇区）
        Vector2 velocity = context.Player != null && context.Player.GetComponent<Rigidbody2D>() != null
            ? context.Player.GetComponent<Rigidbody2D>().velocity
            : Vector2.zero;
        if (velocity.sqrMagnitude < 0.01f)
            return;
        if (!hasPrediction)
            Predict();
        if (trial != null)
            trial.Reacted = true;
        SetSites(RadialZoneMap.DirectionToSector(velocity, EscapeSectors.Count), -1);
    }

    private void HandleEscapeEnded(EscapeEpisode escape)
    {
        // 先记下这次试验的结果（老虎机更新），再按已计入这次逃跑的画像决定是否重新选择
        TrialOutcome outcome = FinishTrial(escape);
        bool redecide = !UsesSampling || redecideEveryEscape || ShouldRedecide(escape, outcome);
        if (outcome != null)
            LogTrial(escape, outcome, redecide);
        if (redecide)
            Replan();
        nextPredictionTime = Time.time + predictionInterval;
    }

    /// <summary>
    /// 采样模式下是否重新选择伏击。第一次评估中每次逃跑后都重选，RunnerA 的驻守命令是 V2 的 12–25 倍，
    /// 伏击者大部分时间在两个点之间走。现在只在当前伏击"被试过"之后重新选择：
    /// - 还没有伏击点（证据刚够 / 新的一波）；
    /// - 这次逃跑跑进了某个伏击点所在的扇区（老虎机得到了一次试验）；
    /// - 这次逃跑是从预测的起点出发的（从基地出发 = 方向预测被检验了一次；PlayerZone 模式下每次逃跑都是）。
    /// 从避难点等其他地方出发、又没经过伏击点的逃跑不改变部署
    /// </summary>
    private bool ShouldRedecide(EscapeEpisode escape, TrialOutcome outcome)
    {
        if (!sites[0].Active)
            return true;
        if (outcome != null && outcome.Tested)
            return true;
        if (!escape.IsValid)
            return false;
        return predictFrom == PredictionOrigin.PlayerZone || escape.StartZone == 0;
    }

    // ---------- 试验（接触奖励） ----------

    /// <summary>逃跑开始：记录各伏击点（扇区、距离档、小队）</summary>
    private void BeginTrial(EscapeEpisode escape)
    {
        trial = null;
        if (context == null || context.Zones == null)
            return;

        trial = new Trial { EscapeId = escape.Id, StartTime = escape.StartTime };
        Vector2 center = context.Zones.GetZoneCenter(0);
        foreach (AmbushSite site in sites)
        {
            if (!site.Active)
                continue;
            trial.Sites.Add(new TrialSite
            {
                Squad = site.Squad,
                Sector = site.Sector,
                DistanceIndex = site.DistanceIndex,
                Zone = site.Zone,
                Distance = Vector2.Distance(site.Position, center)
            });
        }
    }

    /// <summary>
    /// 逃跑途中（起跑 contactGraceSeconds 秒后）记录每个伏击小队、以及压迫小队离玩家最近的距离。
    /// 成员会死亡、被调走或补充，所以每次都重新读小队成员
    /// </summary>
    private void TrackTrial()
    {
        if (trial == null || context == null || context.Player == null || context.Engagement == null)
            return;
        EscapeEpisode escape = context.Engagement.ActiveEscape;
        if (escape == null || escape.Id != trial.EscapeId || Time.time - trial.StartTime < contactGraceSeconds)
            return;

        Vector2 player = context.Player.position;
        foreach (TrialSite site in trial.Sites)
            site.MinDistance = Mathf.Min(site.MinDistance, NearestMember(site.Squad, player));
        trial.PressureMinDistance = Mathf.Min(trial.PressureMinDistance, NearestMember(pressure, player));
    }

    private static float NearestMember(Squad squad, Vector2 player)
    {
        float nearest = float.PositiveInfinity;
        foreach (Enemy enemy in squad.Members)
        {
            if (enemy == null || enemy.IsDead)
                continue;
            nearest = Mathf.Min(nearest, Vector2.Distance(enemy.transform.position, player));
        }
        return nearest;
    }

    /// <summary>
    /// 逃跑结束：与画像相同的过滤（有效、位移 &gt; 1）；只有玩家实际逃跑方向扇区里的伏击点算被试验过，
    /// Bandit 模式下用"是否接触"更新对应的臂
    /// </summary>
    private TrialOutcome FinishTrial(EscapeEpisode escape)
    {
        Trial finished = trial;
        trial = null;
        if (finished == null || finished.EscapeId != escape.Id)
            return null;
        if (!escape.IsValid || escape.Displacement.magnitude <= 1f)
            return null;

        var outcome = new TrialOutcome
        {
            Trial = finished,
            RanSector = RadialZoneMap.DirectionToSector(escape.Displacement, EscapeSectors.Count),
            PressureContact = finished.PressureMinDistance <= contactRadius
        };
        bool learn = placement == Placement.Bandit && !finished.Reacted;
        foreach (TrialSite site in finished.Sites)
        {
            bool contact = site.MinDistance <= contactRadius;
            bool tested = site.Sector == outcome.RanSector;
            outcome.AnyContact |= contact;
            if (tested)
            {
                outcome.Tested = true;
                outcome.TestedContact |= contact;
                outcome.OnRouteMinDistance = Mathf.Min(outcome.OnRouteMinDistance, site.MinDistance);
            }
            outcome.SiteLog.Add(DescribeTrialSite(site, tested));
            outcome.SiteMinDistances.Add(site.MinDistance);
            if (tested && learn && site.DistanceIndex >= 0)
            {
                bandit.Update(site.Sector, site.DistanceIndex, contact);
                LogBanditUpdate(escape, site, contact, outcome.PressureContact);
            }
        }
        return outcome;
    }

    // ---------- 跨会话保存 ----------

    /// <summary>
    /// 第一波开始时读取保存的老虎机（只在 Bandit 模式、且玩家画像也跨会话保存时；训练场的监测组件不保存画像）。
    /// 模式或距离档改过时不使用旧文件，从先验开始，下次保存时覆盖
    /// </summary>
    private void LoadSavedBandit()
    {
        if (banditLoadTried || placement != Placement.Bandit)
            return;
        if (monitor == null)
        {
            banditLoadTried = true; // 没有监测组件 = 没有跨会话画像，也不保存老虎机
            return;
        }
        if (context == null || context.Profile == null)
            return; // 监测组件还没初始化：下一波再试

        banditLoadTried = true;
        banditPersistent = monitor.ProfileIsPersistent;
        if (!banditPersistent)
            return;

        banditScene = gameObject.scene.name;
        AmbushBanditStore.SavedBandit saved = AmbushBanditStore.Load(banditScene, out string error);
        string origin;
        if (saved != null && bandit.TryImportState(saved.bandit, out error))
        {
            banditSessions = saved.sessions + 1;
            origin = "loaded";
            Debug.Log($"[PredictiveCommander] 载入保存的伏击老虎机：{bandit.Updates} 次试验，第 {banditSessions} 次会话" +
                      $"（{AmbushBanditStore.PathFor(banditScene)}）");
        }
        else
        {
            banditSessions = 1;
            origin = error == null ? "new" : "rejected";
            if (error != null)
                Debug.LogWarning($"[PredictiveCommander] 不使用保存的伏击老虎机，从先验开始：{error}", this);
        }

        monitor.LogEvent("bandit_restore")
            .Add("origin", origin)
            .Add("sessions", banditSessions)
            .Add("updates", bandit.Updates)
            .Add("error", error ?? "")
            .Write();
    }

    private void SaveBandit()
    {
        if (!banditPersistent || bandit == null)
            return;
        if (!AmbushBanditStore.Save(banditScene, banditSessions, DisplayName, bandit, out string error) && !banditSaveWarned)
        {
            banditSaveWarned = true;
            Debug.LogWarning($"[PredictiveCommander] 保存伏击老虎机失败：{error}", this);
        }
    }

    // ---------- 训练场回合 ----------

    public void OnArenaEpisodeStarting(ArenaEnvironment arena)
    {
        // 画像在此之前已被重置（ArenaEnvironment.BeginEpisode）；方向采样读取画像，自动从头开始
        trial = null;
        if (resetBanditEachEpisode)
            bandit?.Reset();
    }

    public void OnArenaEpisodeEnded(ArenaEnvironment arena)
    {
    }

    // ---------- 遥测 ----------

    private void LogOrder(int squad, EnemyOrder order, int zone, int size)
    {
        if (monitor == null || context == null || context.Zones == null)
            return;
        string playerZone = context.Player != null ? context.Zones.GetZoneName(context.Zones.GetZone(context.Player.position)) : "";
        monitor.LogEvent("commander_order")
            .Add("squad", squad)
            .Add("order", order.Type.ToString())
            .Add("zone", zone >= 0 ? context.Zones.GetZoneName(zone) : "")
            .Add("size", size)
            .Add("player_zone", playerZone)
            .Write();
    }

    private void LogPrediction(EscapeEpisode escape)
    {
        if (monitor == null || context == null || context.Zones == null || !hasPrediction)
            return;

        var top = new List<string>(3);
        var order = new List<int>();
        for (int d = 0; d < sectorProbabilities.Length; d++)
            order.Add(d);
        order.Sort((a, b) => sectorProbabilities[b].CompareTo(sectorProbabilities[a]));
        for (int i = 0; i < 3; i++)
            top.Add($"{PlayerProfile.DirectionName(order[i])}:{sectorProbabilities[order[i]]:0.###}");

        var ambush = new List<string>();
        var ambushSites = new List<string>();
        Vector2 center = context.Zones.GetZoneCenter(0);
        foreach (AmbushSite site in sites)
        {
            if (!site.Active)
                continue;
            ambush.Add($"{context.Zones.GetZoneName(site.Zone)}:{site.Squad.AliveCount}");
            ambushSites.Add(SiteName(site.Sector, Vector2.Distance(site.Position, center)));
        }

        JsonLine line = monitor.LogEvent("prediction")
            .Add("predictor", activePredictor.Name)
            .Add("escape", escape.Id)
            .Add("player_zone", context.Zones.GetZoneName(escape.StartZone))
            .Add("confident", confident)
            .Add("evidence", context.Profile != null ? context.Profile.EscapeEvidence : 0f)
            .Add("top_directions", top)
            .Add("sector_probabilities", sectorProbabilities)
            .Add("ambush", ambush)
            .Add("direction_choice", directionChoice.ToString())
            .Add("placement", placement.ToString())
            .Add("ambush_sites", ambushSites); // 方向@离基地的距离，例如 "E@25"
        if (directionChoice == DirectionChoice.Thompson)
        {
            line.Add("drawn_direction", drawnDirection >= 0 ? PlayerProfile.DirectionName(drawnDirection) : "")
                .Add("sampled_directions", sampledDirections);
        }
        line.Write();
    }

    /// <summary>每次有效逃跑：各伏击点 / 压迫小队是否接触（所有模式，包括 V2 对照）</summary>
    private void LogTrial(EscapeEpisode escape, TrialOutcome outcome, bool redecided)
    {
        if (monitor == null || context == null || context.Zones == null)
            return;
        monitor.LogEvent("ambush_trial")
            .Add("escape", escape.Id)
            .Add("ran_sector", PlayerProfile.DirectionName(outcome.RanSector))
            .Add("start_zone", context.Zones.GetZoneName(escape.StartZone))
            .Add("direction_choice", directionChoice.ToString())
            .Add("placement", placement.ToString())
            .Add("sites", outcome.SiteLog) // 方向@距离:on_route|off_route:最近距离（null = 没有成员接近过）
            .Add("site_min_distances", outcome.SiteMinDistances)
            .Add("on_route_min_distance", outcome.OnRouteMinDistance) // 玩家逃跑方向上的伏击点的最近距离（null = 没有）
            .Add("contact_radius", contactRadius)
            .Add("ambush_contact", outcome.AnyContact)
            .Add("ambush_contact_on_route", outcome.TestedContact)
            .Add("pressure_contact", outcome.PressureContact)
            .Add("pressure_min_distance", outcome.Trial.PressureMinDistance)
            .Add("reacted", outcome.Trial.Reacted)
            .Add("redecided", redecided)
            .Write();
    }

    private void LogBanditUpdate(EscapeEpisode escape, TrialSite site, bool contact, bool pressureContact)
    {
        if (monitor == null || context == null || context.Zones == null)
            return;
        monitor.LogEvent("bandit_update")
            .Add("escape", escape.Id)
            .Add("sector", PlayerProfile.DirectionName(site.Sector))
            .Add("distance_index", site.DistanceIndex)
            .Add("distance", bandit.Distance(site.DistanceIndex))
            .Add("zone", context.Zones.GetZoneName(site.Zone))
            .Add("contact", contact)
            .Add("min_distance", site.MinDistance)
            .Add("pressure_contact", pressureContact)
            .Add("alpha", bandit.Alpha(site.Sector, site.DistanceIndex))
            .Add("beta", bandit.Beta(site.Sector, site.DistanceIndex))
            .Add("shared", bandit.Shared)
            .Add("updates", bandit.Updates)
            .Write();
    }

    private static string DescribeTrialSite(TrialSite site, bool tested)
    {
        string minDistance = float.IsInfinity(site.MinDistance) ? "null" : site.MinDistance.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{SiteName(site.Sector, site.Distance)}:{(tested ? "on_route" : "off_route")}:{minDistance}";
    }

    private static string SiteName(int sector, float distance)
    {
        return PlayerProfile.DirectionName(sector) + "@" + distance.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
