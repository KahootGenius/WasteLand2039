using System;

/// <summary>
/// 统计预测器（V2 的对照，无训练）：按玩家画像的计数估计下一次逃跑的方向。
///   起点是中心区（基地）：下一次逃跑就是下一次交战的第一次逃跑，用每次交战第一次逃跑的方向计数
///     P(方向 | 基地) = (首次逃跑方向计数 + routePrior × P(方向)) / (首次逃跑总数 + routePrior)
///   其他起点：P(方向 | 起点区域) = (从该区域出发的方向计数 + routePrior × P(方向)) / (该区域的总数 + routePrior)
///   P(方向) = (全部逃跑的方向计数 + directionPrior × 均匀分布) / (总数 + directionPrior)
/// 即平滑回退到整体方向分布，再回退到均匀分布（狄利克雷先验）。计数来自画像，带画像的遗忘。
/// </summary>
public class FrequencyPredictor : IEscapePredictor
{
    private readonly float routePrior;
    private readonly float directionPrior;
    private readonly float[] routeBuffer = new float[EscapeSectors.Count];
    private readonly float[] overall = new float[EscapeSectors.Count];

    public string Name => "Frequency";

    /// <param name="routePrior">从当前区域出发的计数少于这个数时，更多依赖整体方向分布</param>
    /// <param name="directionPrior">方向计数少于这个数时，更多依赖均匀分布</param>
    public FrequencyPredictor(float routePrior = 2f, float directionPrior = 1f)
    {
        this.routePrior = Math.Max(0.001f, routePrior);
        this.directionPrior = Math.Max(0.001f, directionPrior);
    }

    public void Predict(PredictionInput input, float[] directionProbabilities)
    {
        PlayerProfile profile = input.Profile;
        int count = EscapeSectors.Count;

        float evidence = profile.DirectionEvidence;
        for (int d = 0; d < count; d++)
            overall[d] = (evidence * profile.EscapeDirectionProbability(d) + directionPrior / count) / (evidence + directionPrior);

        float routeTotal;
        if (input.CurrentZone == 0)
        {
            routeTotal = profile.FirstEscapeEvidence;
            for (int d = 0; d < count; d++)
                routeBuffer[d] = routeTotal * profile.FirstEscapeDirectionProbability(d);
        }
        else if (input.CurrentZone > 0 && input.CurrentZone < profile.ZoneCount)
        {
            routeTotal = profile.DirectionWeightsFrom(input.CurrentZone, routeBuffer);
        }
        else
        {
            routeTotal = Clear(routeBuffer);
        }
        for (int d = 0; d < count; d++)
            directionProbabilities[d] = (routeBuffer[d] + routePrior * overall[d]) / (routeTotal + routePrior);
    }

    private static float Clear(float[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        return 0f;
    }
}
