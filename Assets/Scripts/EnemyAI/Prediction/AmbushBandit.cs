using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 伏击老虎机的可序列化状态（跨会话保存用，模仿 PlayerProfileState）。只含数据，不含设置（折扣 γ 取当前设置）
/// </summary>
[Serializable]
public class AmbushBanditState
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    /// <summary>共享模式（臂只有距离档，所有扇区共用）；须与当前设置一致</summary>
    public bool shared;
    /// <summary>距离档（离基地中心的距离）；须与当前设置一致</summary>
    public float[] distances;
    /// <summary>每个臂的 Beta 参数，按 AmbushBandit.ArmIndex 排列（扇区 × 距离档数 + 距离下标；共享模式只有距离下标）</summary>
    public float[] alpha;
    public float[] beta;
    /// <summary>累计更新（试验）次数</summary>
    public int updates;
}

/// <summary>AmbushBandit.Choose 的结果：在哪个扇区、哪个距离档等待</summary>
public readonly struct AmbushChoice
{
    public static readonly AmbushChoice None = new AmbushChoice(-1, -1, 0f, 0f);

    /// <summary>扇区（0 = 东，逆时针，与 EscapeSectors 一致）；-1 = 没有</summary>
    public readonly int Sector;
    public readonly int DistanceIndex;
    /// <summary>这个臂抽到的 θ ~ Beta(α, β)：P(接触 | 玩家往这个扇区跑, 在这个距离等)</summary>
    public readonly float Theta;
    /// <summary>得分 = P(扇区) × θ</summary>
    public readonly float Score;

    public bool IsValid => Sector >= 0;

    public AmbushChoice(int sector, int distanceIndex, float theta, float score)
    {
        Sector = sector;
        DistanceIndex = distanceIndex;
        Theta = theta;
        Score = score;
    }
}

/// <summary>
/// 伏击位置的老虎机（自适应尸潮 B 部分，纯逻辑）：学习在预测的逃跑方向上离基地多远等待才真正接触得到玩家。
///
/// 臂：扇区（8 个，与 EscapeSectors 一致）× 距离档（默认离基地中心 12 / 18 / 25）。
///   共享模式（shared）下臂只有距离档，所有扇区共用。这是一个实验："在哪儿等"是地图的属性，还是每条路线的属性？
/// 状态：每个臂一个 Beta(α, β) 后验，先验 (1, 1)。θ 的含义是 P(接触 | 玩家确实往这个扇区跑, 我们在这个距离等)，
///   所以下面的得分 P(扇区) × θ 不会重复计算方向的概率。
/// 选择（Thompson 采样），两种用法：
///   ChooseDistance(扇区)：扇区由方向规则决定（Argmax / A 部分的 Thompson），老虎机只在该扇区的距离档中选（默认）。
///   Choose(P)：联合选择，每个臂抽 θ ~ Beta(α, β)，得分 = P(扇区) × θ，返回得分最高的臂和不相邻扇区中得分最高的臂。
///     第一次评估（2026-10-06）发现：奖励稀疏时，被试验的（预测路线上的）臂累积失败，没被试验的扇区保持乐观的先验，
///     联合选择会把伏击拉离预测路线；所以默认只选距离，联合选择保留为对照（-v2BanditJoint）。
/// 更新：先向先验折扣 α ← 1 + γ(α − 1)、β ← 1 + γ(β − 1)，再按是否接触加 1。只改被试验的臂。
///   向先验（而不是向 0）衰减，形状参数始终 ≥ 1，后验不会塌缩；每个臂的有效样本数最多约 1 / (1 − γ)，
///   玩家改变习惯时能跟上。γ = 1 = 不遗忘。
/// 随机数：用调用方给的 System.Random（带种子），不用 UnityEngine.Random。生成位置用的是后者
///   （HordeContext.DefaultSpawnPosition），多抽一次就会改变之后所有的生成位置，给与 V2 的对照增加噪声。
/// 方向的 Thompson 采样（A 部分）见 ThompsonSampling.SampleDirection。
/// </summary>
public class AmbushBandit
{
    public const int SectorCount = EscapeSectors.Count;
    public const float DefaultDiscount = 0.9f;
    private const float DistanceTolerance = 1e-4f;

    /// <summary>默认距离档：内圈中段、两圈交界附近、外圈（避难点一带）</summary>
    public static float[] DefaultDistances => new[] { 12f, 18f, 25f };

    private readonly Random random;
    private readonly float[] distances;
    private readonly float[] alpha;
    private readonly float[] beta;
    private readonly float[] theta;
    private readonly int[] sectorBestDistance = new int[SectorCount];
    private readonly float[] sectorBestScore = new float[SectorCount];
    private float discount;

    /// <param name="random">随机数（带种子，例如 new System.Random(seed)；可与方向采样共用）</param>
    /// <param name="distances">距离档（离基地中心）；null 或空 = DefaultDistances</param>
    /// <param name="shared">true = 臂只有距离档，所有扇区共用</param>
    /// <param name="discount">折扣 γ（0..1），每次更新前把该臂向先验拉回</param>
    public AmbushBandit(Random random, float[] distances = null, bool shared = false, float discount = DefaultDiscount)
    {
        this.random = random ?? throw new ArgumentNullException(nameof(random));
        this.distances = distances != null && distances.Length > 0 ? (float[])distances.Clone() : DefaultDistances;
        Shared = shared;
        Discount = discount;
        alpha = new float[ArmCount];
        beta = new float[ArmCount];
        theta = new float[ArmCount];
        Reset();
    }

    public bool Shared { get; }
    public int DistanceCount => distances.Length;
    public int ArmCount => Shared ? distances.Length : SectorCount * distances.Length;
    public IReadOnlyList<float> Distances => distances;
    /// <summary>累计更新（试验）次数</summary>
    public int Updates { get; private set; }

    public float Discount
    {
        get => discount;
        set => discount = float.IsNaN(value) ? DefaultDiscount : Math.Min(1f, Math.Max(0f, value));
    }

    /// <summary>所有臂回到先验 (1, 1)（训练场每回合重置时用）</summary>
    public void Reset()
    {
        for (int arm = 0; arm < alpha.Length; arm++)
        {
            alpha[arm] = 1f;
            beta[arm] = 1f;
        }
        Updates = 0;
    }

    /// <summary>臂的下标；共享模式下忽略 sector</summary>
    public int ArmIndex(int sector, int distanceIndex)
    {
        if (distanceIndex < 0 || distanceIndex >= distances.Length)
            throw new ArgumentOutOfRangeException(nameof(distanceIndex), distanceIndex, $"距离档须在 0..{distances.Length - 1}");
        if (Shared)
            return distanceIndex;
        if (sector < 0 || sector >= SectorCount)
            throw new ArgumentOutOfRangeException(nameof(sector), sector, $"扇区须在 0..{SectorCount - 1}");
        return sector * distances.Length + distanceIndex;
    }

    public float Distance(int distanceIndex) => distances[distanceIndex];
    public float Alpha(int sector, int distanceIndex) => alpha[ArmIndex(sector, distanceIndex)];
    public float Beta(int sector, int distanceIndex) => beta[ArmIndex(sector, distanceIndex)];

    /// <summary>后验均值 α / (α + β)：目前估计的接触概率</summary>
    public float Mean(int sector, int distanceIndex)
    {
        int arm = ArmIndex(sector, distanceIndex);
        return alpha[arm] / (alpha[arm] + beta[arm]);
    }

    /// <summary>两个扇区之间隔几个扇区（0..4）；&lt; 2 = 相同或相邻</summary>
    public static int SectorGap(int a, int b)
    {
        int difference = Math.Abs(a - b) % SectorCount;
        return Math.Min(difference, SectorCount - difference);
    }

    // ---------- 选择 ----------

    public AmbushChoice Choose(float[] sectorProbabilities)
    {
        return Choose(sectorProbabilities, out _);
    }

    /// <summary>
    /// Thompson 采样：每个臂抽一次 θ ~ Beta(α, β)，得分 = P(扇区) × θ，返回得分最高的臂；
    /// second 为与它不相邻（隔 ≥ 2 个扇区）的扇区中得分最高的臂，没有时为 AmbushChoice.None。
    /// 概率 ≤ 0 的扇区不参与。所有扇区概率都 ≤ 0 时返回 None
    /// </summary>
    /// <param name="sectorProbabilities">各扇区的逃跑概率（长度 SectorCount，不必归一化）</param>
    public AmbushChoice Choose(float[] sectorProbabilities, out AmbushChoice second)
    {
        if (sectorProbabilities == null || sectorProbabilities.Length != SectorCount)
            throw new ArgumentException($"需要 {SectorCount} 个扇区的概率", nameof(sectorProbabilities));

        // 每次都按固定顺序给所有臂各抽一次：随机数的消耗只取决于调用次数，相同种子可复现
        for (int arm = 0; arm < theta.Length; arm++)
            theta[arm] = (float)ThompsonSampling.Beta(random, alpha[arm], beta[arm]);

        int bestSector = -1;
        for (int sector = 0; sector < SectorCount; sector++)
        {
            sectorBestDistance[sector] = -1;
            sectorBestScore[sector] = 0f;
            float probability = sectorProbabilities[sector];
            if (!(probability > 0f))
                continue;
            for (int d = 0; d < distances.Length; d++)
            {
                float score = probability * theta[ArmIndex(sector, d)];
                if (score > sectorBestScore[sector])
                {
                    sectorBestScore[sector] = score;
                    sectorBestDistance[sector] = d;
                }
            }
            if (sectorBestDistance[sector] >= 0 && (bestSector < 0 || sectorBestScore[sector] > sectorBestScore[bestSector]))
                bestSector = sector;
        }

        second = AmbushChoice.None;
        if (bestSector < 0)
            return AmbushChoice.None;

        int secondSector = -1;
        for (int sector = 0; sector < SectorCount; sector++)
        {
            // 相邻扇区多半是同一条路线跨过扇区边界，不分兵（与 PredictiveCommander 一致）
            if (sectorBestDistance[sector] < 0 || SectorGap(sector, bestSector) < 2)
                continue;
            if (secondSector < 0 || sectorBestScore[sector] > sectorBestScore[secondSector])
                secondSector = sector;
        }
        if (secondSector >= 0)
            second = MakeChoice(secondSector);
        return MakeChoice(bestSector);
    }

    /// <summary>
    /// 只选距离：扇区已由方向规则决定，只给该扇区的各距离档抽 θ ~ Beta(α, β)，返回 θ 最大的距离档。
    /// 得分 = θ（扇区概率不参与）。共享模式下各扇区用同一组臂
    /// </summary>
    public AmbushChoice ChooseDistance(int sector)
    {
        if (sector < 0 || sector >= SectorCount)
            throw new ArgumentOutOfRangeException(nameof(sector), sector, $"扇区须在 0..{SectorCount - 1}");

        int best = -1;
        float bestTheta = 0f;
        for (int d = 0; d < distances.Length; d++)
        {
            int arm = ArmIndex(sector, d);
            theta[arm] = (float)ThompsonSampling.Beta(random, alpha[arm], beta[arm]);
            if (best < 0 || theta[arm] > bestTheta)
            {
                best = d;
                bestTheta = theta[arm];
            }
        }
        return new AmbushChoice(sector, best, bestTheta, bestTheta);
    }

    private AmbushChoice MakeChoice(int sector)
    {
        int d = sectorBestDistance[sector];
        return new AmbushChoice(sector, d, theta[ArmIndex(sector, d)], sectorBestScore[sector]);
    }

    // ---------- 更新 ----------

    /// <summary>
    /// 记录一次试验：玩家确实往 sector 跑了，我们在 distanceIndex 等着，是否接触到（contact）。
    /// 玩家往别的扇区跑时，这个臂没有被试验，不要调用
    /// </summary>
    public void Update(int sector, int distanceIndex, bool contact)
    {
        int arm = ArmIndex(sector, distanceIndex);
        alpha[arm] = 1f + discount * (alpha[arm] - 1f);
        beta[arm] = 1f + discount * (beta[arm] - 1f);
        if (contact)
            alpha[arm] += 1f;
        else
            beta[arm] += 1f;
        Updates++;
    }

    // ---------- 保存 / 恢复 ----------

    /// <summary>导出当前状态（副本，之后的更新不影响它）</summary>
    public AmbushBanditState ExportState()
    {
        return new AmbushBanditState
        {
            shared = Shared,
            distances = (float[])distances.Clone(),
            alpha = (float[])alpha.Clone(),
            beta = (float[])beta.Clone(),
            updates = Updates
        };
    }

    /// <summary>
    /// 用保存的状态替换当前数据。模式、距离档或数组长度不符（设置变了）、参数无效时不做任何修改并返回 false
    /// </summary>
    public bool TryImportState(AmbushBanditState state, out string error)
    {
        error = null;
        if (state == null)
            error = "没有数据";
        else if (state.version != AmbushBanditState.CurrentVersion)
            error = $"不支持的版本 {state.version}";
        else if (state.shared != Shared)
            error = state.shared ? "存档是共享模式（臂只有距离档），当前按扇区" : "存档按扇区，当前是共享模式";
        else if (!SameDistances(state.distances))
            error = $"距离档 {FormatDistances(state.distances)} ≠ 当前 {FormatDistances(distances)}";
        else if (state.alpha == null || state.beta == null || state.alpha.Length != ArmCount || state.beta.Length != ArmCount)
            error = $"参数长度不符（应为 {ArmCount}）";
        else if (!ValidShapes(state.alpha) || !ValidShapes(state.beta))
            error = "参数无效（α、β 须为 ≥ 1 的有限数）";
        if (error != null)
            return false;

        Array.Copy(state.alpha, alpha, ArmCount);
        Array.Copy(state.beta, beta, ArmCount);
        Updates = Math.Max(0, state.updates);
        return true;
    }

    private bool SameDistances(float[] other)
    {
        if (other == null || other.Length != distances.Length)
            return false;
        for (int d = 0; d < distances.Length; d++)
        {
            if (!(Math.Abs(other[d] - distances[d]) <= DistanceTolerance))
                return false;
        }
        return true;
    }

    private static bool ValidShapes(float[] values)
    {
        foreach (float value in values)
        {
            // 折扣向先验衰减，正常状态下 α、β 始终 ≥ 1
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 1f - 1e-5f)
                return false;
        }
        return true;
    }

    private static string FormatDistances(float[] values)
    {
        if (values == null)
            return "（无）";
        return string.Join("/", Array.ConvertAll(values, value => value.ToString("0.##", CultureInfo.InvariantCulture)));
    }
}

/// <summary>
/// Thompson 采样用的随机分布（纯 C#，用调用方的 System.Random）：
///   Gamma(k)：Marsaglia–Tsang（形状 ≥ 1）；形状 &lt; 1 时用 Gamma(k) = Gamma(k + 1) · U^(1/k)（A 部分需要）
///   Beta(a, b) = X / (X + Y)，X ~ Gamma(a)，Y ~ Gamma(b)
///   SampleDirection：逃跑方向的狄利克雷采样（A 部分）
/// </summary>
public static class ThompsonSampling
{
    /// <summary>标准正态（Box–Muller）</summary>
    public static double StandardNormal(Random random)
    {
        double u1 = 1.0 - random.NextDouble(); // (0, 1]，避免 log(0)
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Gamma(shape, 1)；shape ≤ 0（或 NaN）时返回 0</summary>
    public static double Gamma(Random random, double shape)
    {
        if (!(shape > 0.0))
            return 0.0;
        if (shape < 1.0)
        {
            double u = 1.0 - random.NextDouble();
            return Gamma(random, shape + 1.0) * Math.Pow(u, 1.0 / shape);
        }

        // Marsaglia & Tsang (2000), "A simple method for generating gamma variables"
        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x, v;
            do
            {
                x = StandardNormal(random);
                v = 1.0 + c * x;
            }
            while (v <= 0.0);
            v = v * v * v;
            double u = 1.0 - random.NextDouble();
            double x2 = x * x;
            if (u < 1.0 - 0.0331 * x2 * x2)
                return d * v;
            if (Math.Log(u) < 0.5 * x2 + d * (1.0 - v + Math.Log(v)))
                return d * v;
        }
    }

    /// <summary>Beta(a, b) = X / (X + Y)</summary>
    public static double Beta(Random random, double a, double b)
    {
        double x = Gamma(random, a);
        double y = Gamma(random, b);
        double sum = x + y;
        if (sum > 0.0)
            return x / sum;
        // 两个都下溢为 0（形状极小）：返回均值
        return a + b > 0.0 ? a / (a + b) : 0.5;
    }

    /// <summary>
    /// A 部分：逃跑方向的 Thompson 采样。把预测器的方向分布 P 当作狄利克雷后验的均值，
    /// 参数 a_d = P_d × concentration；抽 g_d ~ Gamma(a_d)，归一化后写入 sample，返回最大的方向。
    ///
    /// FrequencyPredictor 从基地预测时正好是狄利克雷后验均值，concentration = profile.FirstEscapeEvidence + routePrior（默认 2），
    /// 所以不需要计数，只需要 PredictiveCommander 已有的概率。画像的计数每次逃跑 ×0.85，证据最多约 6.7，
    /// 探索永远不会完全停止（对会改变习惯的玩家正合适）。concentration 越大越接近 argmax；
    /// ≤ 0 时退化为按 P 抽一个方向（狄利克雷在 concentration → 0 时的极限）。
    /// </summary>
    /// <param name="probabilities">各方向的概率（不必归一化；≤ 0 的方向不会被选中）</param>
    /// <param name="sample">可为 null；否则长度 ≥ probabilities.Length，写入归一化的样本（和为 1）</param>
    /// <returns>被选中的方向；概率全 ≤ 0 时返回 -1（sample 全 0）</returns>
    public static int SampleDirection(Random random, float[] probabilities, float concentration, float[] sample = null)
    {
        if (random == null)
            throw new ArgumentNullException(nameof(random));
        if (probabilities == null)
            throw new ArgumentNullException(nameof(probabilities));
        int count = probabilities.Length;
        if (sample != null && sample.Length < count)
            throw new ArgumentException("sample 长度不足", nameof(sample));

        double total = 0.0;
        for (int d = 0; d < count; d++)
        {
            if (probabilities[d] > 0f)
                total += probabilities[d];
        }
        if (sample != null)
            Array.Clear(sample, 0, sample.Length);
        if (!(total > 0.0))
            return -1;

        double scale = concentration > 0f ? concentration / total : 0.0;
        double[] draws = new double[count];
        double sum = 0.0;
        int best = -1;
        for (int d = 0; d < count; d++)
        {
            double shape = probabilities[d] > 0f ? probabilities[d] * scale : 0.0;
            draws[d] = shape > 0.0 ? Gamma(random, shape) : 0.0;
            sum += draws[d];
            if (draws[d] > 0.0 && (best < 0 || draws[d] > draws[best]))
                best = d;
        }

        if (!(sum > 0.0) || best < 0)
        {
            // concentration ≤ 0，或所有样本都下溢：按 P 抽一个方向
            double pick = random.NextDouble() * total;
            best = -1;
            for (int d = 0; d < count; d++)
            {
                if (!(probabilities[d] > 0f))
                    continue;
                best = d;
                pick -= probabilities[d];
                if (pick < 0.0)
                    break;
            }
            if (sample != null)
                sample[best] = 1f;
            return best;
        }

        if (sample != null)
        {
            for (int d = 0; d < count; d++)
                sample[d] = (float)(draws[d] / sum);
        }
        return best;
    }
}
