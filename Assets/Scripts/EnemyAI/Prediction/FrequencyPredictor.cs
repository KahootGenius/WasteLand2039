using System;

/// <summary>
/// 统计预测器（V2 的对照，无训练）：按玩家画像的计数估计下一次逃跑的终点。
///   P(终点 | 当前区域) = (从当前区域出发的路线计数 + routePrior × P(终点)) / (路线总数 + routePrior)
///   P(终点)           = (终点计数 + destinationPrior × 均匀分布) / (终点总数 + destinationPrior)
/// 即一阶马尔可夫，平滑回退到终点分布，再回退到均匀分布（狄利克雷先验）。计数来自画像，带画像的遗忘（衰减）。
/// </summary>
public class FrequencyPredictor : IEscapePredictor
{
    private readonly float routePrior;
    private readonly float destinationPrior;
    private float[] routeBuffer = new float[0];
    private float[] destinationBuffer = new float[0];

    public string Name => "Frequency";

    /// <param name="routePrior">路线计数少于这个数时，更多依赖整体终点分布</param>
    /// <param name="destinationPrior">终点计数少于这个数时，更多依赖均匀分布</param>
    public FrequencyPredictor(float routePrior = 2f, float destinationPrior = 1f)
    {
        this.routePrior = Math.Max(0.001f, routePrior);
        this.destinationPrior = Math.Max(0.001f, destinationPrior);
    }

    public void Predict(PredictionInput input, float[] zoneProbabilities)
    {
        PlayerProfile profile = input.Profile;
        int zoneCount = profile.ZoneCount;
        if (routeBuffer.Length != zoneCount)
        {
            routeBuffer = new float[zoneCount];
            destinationBuffer = new float[zoneCount];
        }

        float evidence = profile.EscapeEvidence;
        for (int zone = 0; zone < zoneCount; zone++)
            destinationBuffer[zone] = (evidence * profile.EscapeZoneProbability(zone) + destinationPrior / zoneCount) /
                                      (evidence + destinationPrior);

        float routeTotal = input.CurrentZone >= 0 && input.CurrentZone < zoneCount
            ? profile.RouteWeightsFrom(input.CurrentZone, routeBuffer)
            : ClearAndReturnZero(routeBuffer);
        for (int zone = 0; zone < zoneCount; zone++)
            zoneProbabilities[zone] = (routeBuffer[zone] + routePrior * destinationBuffer[zone]) / (routeTotal + routePrior);
    }

    private static float ClearAndReturnZero(float[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        return 0f;
    }
}
