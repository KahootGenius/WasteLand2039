using UnityEngine;

/// <summary>预测时可用的信息（与指挥官看到的 HordeContext 相同来源）</summary>
public class PredictionInput
{
    public PlayerProfile Profile;
    /// <summary>玩家当前所在区域</summary>
    public int CurrentZone;
    /// <summary>EscapeFeatures 构建的特征向量（学习型预测器使用）</summary>
    public float[] Features;
}

/// <summary>
/// V2 的逃跑预测器（纯逻辑）：预测玩家下一次逃跑的终点区域分布。
/// 实现：FrequencyPredictor（按画像统计，无训练）、LearnedPredictor（离线训练的 softmax 回归）
/// </summary>
public interface IEscapePredictor
{
    string Name { get; }

    /// <summary>把各区域的概率（和为 1）写入 zoneProbabilities（长度 = 区域数）</summary>
    void Predict(PredictionInput input, float[] zoneProbabilities);
}

/// <summary>区域 → 方向扇区（8 个，0 = 东，逆时针），适用于任何 IZoneMap（按区域代表点相对中心区的方向）</summary>
public static class EscapeSectors
{
    public const int Count = 8;

    /// <summary>区域所在扇区；中心区（0）返回 -1</summary>
    public static int SectorOf(IZoneMap zones, int zone)
    {
        if (zone <= 0 || zone >= zones.ZoneCount)
            return -1;
        Vector2 direction = zones.GetZoneCenter(zone) - zones.GetZoneCenter(0);
        return direction.sqrMagnitude < 0.0001f ? -1 : RadialZoneMap.DirectionToSector(direction, Count);
    }

    /// <summary>把区域概率按扇区相加（长度 Count）；返回落在中心区的概率</summary>
    public static float Aggregate(IZoneMap zones, float[] zoneProbabilities, float[] sectorProbabilities)
    {
        System.Array.Clear(sectorProbabilities, 0, Count);
        float core = 0f;
        for (int zone = 0; zone < zoneProbabilities.Length && zone < zones.ZoneCount; zone++)
        {
            int sector = SectorOf(zones, zone);
            if (sector < 0)
                core += zoneProbabilities[zone];
            else
                sectorProbabilities[sector] += zoneProbabilities[zone];
        }
        return core;
    }

    /// <summary>概率最高的扇区（全为 0 时返回 -1）；second 为第二高</summary>
    public static int Top(float[] sectorProbabilities, out int second)
    {
        int best = -1;
        second = -1;
        for (int s = 0; s < sectorProbabilities.Length; s++)
        {
            if (sectorProbabilities[s] <= 0f)
                continue;
            if (best < 0 || sectorProbabilities[s] > sectorProbabilities[best])
            {
                second = best;
                best = s;
            }
            else if (second < 0 || sectorProbabilities[s] > sectorProbabilities[second])
            {
                second = s;
            }
        }
        return best;
    }
}
