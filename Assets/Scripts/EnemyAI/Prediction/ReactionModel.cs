using System;
using System.Collections.Generic;

/// <summary>
/// 玩家反应模型（自适应尸潮的第二层，纯逻辑）：V2 的预测器估计玩家往哪跑；这个模型学习玩家怎样对尸潮作出反应——
/// 在某个方向上遇到僵尸之后，下次是否会避开那个方向（认知科学中的"输则换"学习，lose-shift）。
///
/// 假设：玩家在扇区 s 遇到僵尸后，下次选 s 的倾向乘以避开强度 c；没遇到僵尸时各扇区的倾向以 recovery 的比例恢复到 1。
/// c 不是给定的，而是从玩家的实际选择中在线学习：候选 c ∈ strengths（默认 1 = 不反应、0.6、0.35、0.2、0.1），
/// 先验 P(c = 1) = priorNoReaction，其余候选平分。每次观察（玩家这次往哪跑、有没有遇到僵尸）先按各候选对这次选择的
/// 预测概率做贝叶斯更新，再更新各候选的倾向。
/// 预测 = Σ_c P(c | 历史) × 预测_c；预测_c 只在玩家跑过的扇区（"路线"）之间按 基础预测 × 倾向_c 重新分配这些扇区的总概率，
/// 没跑过的方向保持基础预测（否则玩家在每条路线上都遇到过僵尸时，概率会流向他从来不走的方向）。
/// 对不反应的玩家（c = 1 的后验趋近 1）预测就是基础预测。
///
/// 用法：每次"出行"（玩家从基地出发、又回到基地）结束时 Observe(出发前的基础预测, 出行的扇区, 是否遇到僵尸)；
/// 需要预测时 Apply(基础预测, 输出)。
/// </summary>
public class ReactionModel
{
    /// <summary>候选的避开强度：1 = 不反应</summary>
    public static readonly float[] DefaultStrengths = { 1f, 0.6f, 0.35f, 0.2f, 0.1f };
    public const float DefaultPriorNoReaction = 0.5f;
    public const float DefaultRecovery = 0.1f;
    private const double MinLikelihood = 1e-6;

    private readonly float[] strengths;
    private readonly double[] logPrior;
    private readonly double[] logPosterior;
    private readonly float[][] factors;
    /// <summary>玩家跑过的扇区（Observe 过的）：倾向只在这些扇区之间重新分配概率</summary>
    private readonly bool[] routes;
    private readonly float recovery;
    private readonly int sectorCount;
    private readonly double[] mixed;

    public int SectorCount => sectorCount;
    public IReadOnlyList<float> Strengths => strengths;
    public float Recovery => recovery;
    public float PriorNoReaction { get; }
    /// <summary>已观察的出行数（Reset 清零）</summary>
    public int Observations { get; private set; }

    /// <param name="priorNoReaction">先验 P(不反应)；strengths 中没有 1 时忽略，所有候选平分</param>
    /// <param name="recovery">没遇到僵尸的出行之后，倾向向 1 恢复的比例</param>
    public ReactionModel(int sectorCount = EscapeSectors.Count, float priorNoReaction = DefaultPriorNoReaction,
                         float recovery = DefaultRecovery, float[] strengths = null)
    {
        if (sectorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sectorCount), sectorCount, "扇区数须 > 0");
        this.sectorCount = sectorCount;
        this.strengths = (float[])(strengths ?? DefaultStrengths).Clone();
        if (this.strengths.Length == 0)
            throw new ArgumentException("至少需要一个候选强度", nameof(strengths));
        for (int h = 0; h < this.strengths.Length; h++)
            this.strengths[h] = Math.Max(0f, this.strengths[h]);
        this.recovery = Math.Min(1f, Math.Max(0f, recovery));
        PriorNoReaction = Math.Min(0.999f, Math.Max(0.001f, priorNoReaction));

        int none = NoReactionIndex();
        logPrior = new double[this.strengths.Length];
        for (int h = 0; h < logPrior.Length; h++)
        {
            double prior = none < 0 ? 1.0 / logPrior.Length
                : h == none ? PriorNoReaction
                : (1.0 - PriorNoReaction) / (logPrior.Length - 1);
            logPrior[h] = Math.Log(Math.Max(prior, 1e-12));
        }
        logPosterior = new double[logPrior.Length];
        mixed = new double[sectorCount];
        routes = new bool[sectorCount];
        factors = new float[logPrior.Length][];
        for (int h = 0; h < factors.Length; h++)
            factors[h] = new float[sectorCount];
        Reset();
    }

    /// <summary>回到先验：不知道玩家会不会反应，所有倾向为 1</summary>
    public void Reset()
    {
        Array.Copy(logPrior, logPosterior, logPrior.Length);
        foreach (float[] f in factors)
        {
            for (int s = 0; s < f.Length; s++)
                f[s] = 1f;
        }
        Array.Clear(routes, 0, routes.Length);
        Observations = 0;
    }

    /// <summary>候选 h 的后验概率</summary>
    public float Posterior(int candidate)
    {
        double max = double.NegativeInfinity;
        foreach (double v in logPosterior)
            max = Math.Max(max, v);
        double sum = 0;
        foreach (double v in logPosterior)
            sum += Math.Exp(v - max);
        return (float)(Math.Exp(logPosterior[candidate] - max) / sum);
    }

    /// <summary>后验 P(不反应)；没有强度为 1 的候选时为 0</summary>
    public float NoReactionProbability
    {
        get
        {
            int none = NoReactionIndex();
            return none >= 0 ? Posterior(none) : 0f;
        }
    }

    /// <summary>避开强度的后验均值（1 = 不反应）</summary>
    public float MeanStrength
    {
        get
        {
            float mean = 0f;
            for (int h = 0; h < strengths.Length; h++)
                mean += Posterior(h) * strengths[h];
            return mean;
        }
    }

    /// <summary>候选 h 下扇区 s 的倾向（1 = 没有避开）</summary>
    public float Factor(int candidate, int sector) => factors[candidate][sector];

    /// <summary>后验加权的倾向（遥测用：玩家对各扇区的"避开程度"）</summary>
    public float MeanFactor(int sector)
    {
        float mean = 0f;
        for (int h = 0; h < factors.Length; h++)
            mean += Posterior(h) * factors[h][sector];
        return mean;
    }

    /// <summary>玩家跑过这个扇区（倾向在跑过的扇区之间重新分配概率）</summary>
    public bool IsRoute(int sector) => routes[sector];

    /// <summary>考虑玩家反应后的预测：Σ_c P(c) × 预测_c。output 可以就是 baseProbabilities</summary>
    public void Apply(float[] baseProbabilities, float[] output)
    {
        CheckLength(baseProbabilities, nameof(baseProbabilities));
        CheckLength(output, nameof(output));
        Array.Clear(mixed, 0, sectorCount);
        for (int h = 0; h < factors.Length; h++)
        {
            double weight = Posterior(h);
            for (int s = 0; s < sectorCount; s++)
                mixed[s] += weight * CandidateProbability(h, baseProbabilities, s);
        }
        for (int s = 0; s < sectorCount; s++)
            output[s] = (float)mixed[s];
    }

    /// <summary>候选 h 下这次往扇区 sector 跑的概率</summary>
    public float Probability(int candidate, float[] baseProbabilities, int sector)
    {
        CheckLength(baseProbabilities, nameof(baseProbabilities));
        return (float)CandidateProbability(candidate, baseProbabilities, sector);
    }

    /// <summary>
    /// 预测_h(s)：跑过的扇区之间按 基础 × 倾向 分配它们的基础概率总和，其余扇区 = 基础预测
    /// （总和与基础预测相同；跑过的扇区倾向全为 0 或基础全为 0 时保持基础预测）
    /// </summary>
    private double CandidateProbability(int candidate, float[] baseProbabilities, int sector)
    {
        if (!routes[sector])
            return baseProbabilities[sector];
        float[] f = factors[candidate];
        double mass = 0, weighted = 0;
        for (int s = 0; s < sectorCount; s++)
        {
            if (!routes[s])
                continue;
            mass += baseProbabilities[s];
            weighted += baseProbabilities[s] * f[s];
        }
        return weighted > 0 ? mass * baseProbabilities[sector] * f[sector] / weighted : baseProbabilities[sector];
    }

    /// <summary>
    /// 一次出行结束：basePrediction = 出发前的基础预测，sector = 玩家这次跑去的扇区，met = 途中是否遇到僵尸。
    /// 先用各候选对这次选择的预测概率更新后验，再更新各候选的倾向
    /// </summary>
    public void Observe(float[] basePrediction, int sector, bool met)
    {
        CheckLength(basePrediction, nameof(basePrediction));
        if (sector < 0 || sector >= sectorCount)
            throw new ArgumentOutOfRangeException(nameof(sector), sector, $"扇区须在 0..{sectorCount - 1}");

        double max = double.NegativeInfinity;
        for (int h = 0; h < factors.Length; h++)
        {
            // 用出发前的预测（这次的扇区若是第一次跑，各候选的预测相同，不提供信息）
            logPosterior[h] += Math.Log(Math.Max(CandidateProbability(h, basePrediction, sector), MinLikelihood));
            max = Math.Max(max, logPosterior[h]);
        }
        for (int h = 0; h < logPosterior.Length; h++)
            logPosterior[h] -= max; // 防止长期累积后下溢

        for (int h = 0; h < factors.Length; h++)
        {
            float[] f = factors[h];
            if (met)
            {
                f[sector] *= strengths[h];
            }
            else
            {
                for (int s = 0; s < sectorCount; s++)
                    f[s] += recovery * (1f - f[s]);
            }
        }
        routes[sector] = true;
        Observations++;
    }

    private int NoReactionIndex()
    {
        for (int h = 0; h < strengths.Length; h++)
        {
            if (Math.Abs(strengths[h] - 1f) < 1e-6f)
                return h;
        }
        return -1;
    }

    private void CheckLength(float[] array, string name)
    {
        if (array == null || array.Length != sectorCount)
            throw new ArgumentException($"长度须为 {sectorCount}", name);
    }
}
