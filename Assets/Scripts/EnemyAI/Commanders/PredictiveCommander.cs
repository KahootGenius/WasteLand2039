using System.Collections.Generic;
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
/// 遥测（经 PlayerBehaviourMonitor）：commander_order（与 RLCommander 格式相同），每次逃跑开始时的 prediction。
///
/// 命令行（独立运行的评估程序，覆盖 Inspector 设置）：
///   -v2Predictor Frequency|Learned   -v2From Base|PlayerZone   -v2React
/// </summary>
public class PredictiveCommander : MonoBehaviour, IHordeCommander
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

    private const int MaxAmbushSites = 2;

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

    public string DisplayName => "V2-" + (activePredictor != null ? activePredictor.Name : predictor.ToString());

    /// <summary>当前使用的预测器（学习型参数无效时退回统计预测器）</summary>
    public IEscapePredictor Predictor => activePredictor;

    private class AmbushSite
    {
        public Squad Squad;
        public int Sector = -1;
        public int Zone = -1;
        public int Quota;
        public bool Active => Sector >= 0;
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

    private void Awake()
    {
        ApplyCommandLine();
        activePredictor = CreatePredictor();
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
        }
    }

    /// <summary>运行时切换预测器（评估脚本 / EnemyAIBootstrap 用）；须在波次开始前调用</summary>
    public void Configure(PredictorChoice choice, TextAsset weights)
    {
        predictor = choice;
        learnedWeights = weights;
        activePredictor = CreatePredictor();
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
    }

    // ---------- IHordeCommander ----------

    public void OnWaveStarted(HordeContext waveContext)
    {
        context = waveContext;
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
                Vector2 point = waveContext.Zones.GetZoneCenter(site.Zone) + Random.insideUnitCircle * spawnJitter;
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
        if (Time.time >= nextPredictionTime)
        {
            Replan();
            nextPredictionTime = Time.time + predictionInterval;
        }
        FillSites();
    }

    public void OnWaveCompleted(HordeContext waveContext)
    {
        Unsubscribe();
        pressure.Issue(EnemyOrder.None);
        foreach (AmbushSite site in sites)
            site.Squad.Issue(EnemyOrder.None);
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
            // 对这个玩家还一无所知（新回合 / 新画像）：与基准相同
            confident = false;
            SetSites(-1, -1);
            return;
        }

        int top = EscapeSectors.Top(sectorProbabilities, out _);
        confident = top >= 0 && sectorProbabilities[top] >= minProbability;
        if (!confident)
            return; // 保持当前伏击：玩家不在常见的起跑位置时，从当前区域出发的预测会分散

        // 保持当前伏击扇区，除非新的最可能扇区明显更好
        int current = sites[0].Sector;
        int primary = current >= 0 && sectorProbabilities[top] < sectorProbabilities[current] + switchMargin ? current : top;

        int secondary = -1;
        if (ambushSize >= 2)
        {
            for (int s = 0; s < EscapeSectors.Count; s++)
            {
                // 相邻扇区多半是同一条路线跨过扇区边界，不分兵
                if (SectorDistance(s, primary) < 2 || sectorProbabilities[s] < splitRatio * sectorProbabilities[primary])
                    continue;
                if (secondary < 0 || sectorProbabilities[s] > sectorProbabilities[secondary])
                    secondary = s;
            }
        }
        SetSites(primary, secondary);
    }

    private static int SectorDistance(int a, int b)
    {
        int difference = Mathf.Abs(a - b) % EscapeSectors.Count;
        return Mathf.Min(difference, EscapeSectors.Count - difference);
    }

    private void SetSites(int primary, int secondary)
    {
        int primaryQuota = secondary >= 0 ? ambushSize - ambushSize / 2 : ambushSize;
        SetSite(0, primary, primary >= 0 ? primaryQuota : 0);
        SetSite(1, secondary, secondary >= 0 ? ambushSize / 2 : 0);
    }

    private void SetSite(int index, int sector, int quota)
    {
        AmbushSite site = sites[index];
        site.Quota = quota;
        int zone = sector >= 0 ? ZoneInSector(sector) : -1;
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
            return;
        }

        if (site.Sector == sector && site.Zone == zone)
            return;
        site.Sector = sector;
        site.Zone = zone;
        EnemyOrder order = EnemyOrder.HoldAt(context.Zones.GetZoneCenter(zone), holdEngageRadius);
        site.Squad.Issue(order);
        LogOrder(site.Squad.Id, order, zone, site.Squad.AliveCount);
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

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void HandleEscapeStarted(EscapeEpisode escape)
    {
        LogPrediction(escape);
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
        SetSites(RadialZoneMap.DirectionToSector(velocity, EscapeSectors.Count), -1);
    }

    private void HandleEscapeEnded(EscapeEpisode escape)
    {
        // 画像已计入这次逃跑：立即重新预测
        Replan();
        nextPredictionTime = Time.time + predictionInterval;
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
        foreach (AmbushSite site in sites)
        {
            if (site.Active)
                ambush.Add($"{context.Zones.GetZoneName(site.Zone)}:{site.Squad.AliveCount}");
        }

        monitor.LogEvent("prediction")
            .Add("predictor", activePredictor.Name)
            .Add("escape", escape.Id)
            .Add("player_zone", context.Zones.GetZoneName(escape.StartZone))
            .Add("confident", confident)
            .Add("evidence", context.Profile != null ? context.Profile.EscapeEvidence : 0f)
            .Add("top_directions", top)
            .Add("sector_probabilities", sectorProbabilities)
            .Add("ambush", ambush)
            .Write();
    }
}
