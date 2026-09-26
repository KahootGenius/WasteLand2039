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
/// V2 的逃跑预测器（纯逻辑）：预测玩家下一次逃跑的方向（8 个方向扇区，0 = 东，逆时针，与 EscapeSectors 一致）。
/// 预测方向而不是终点区域：区域以基地为中心划分，而玩家不一定从基地正中起跑，提前结束的逃跑会落在相邻扇区的区域里；
/// 逃跑的位移方向更接近玩家选择的路线。
/// 实现：FrequencyPredictor（按画像统计，无训练）、LearnedPredictor（离线训练的 softmax 回归）
/// </summary>
public interface IEscapePredictor
{
    string Name { get; }

    /// <summary>把各方向的概率（和为 1）写入 directionProbabilities（长度 EscapeSectors.Count）</summary>
    void Predict(PredictionInput input, float[] directionProbabilities);
}

/// <summary>方向扇区（8 个，0 = 东，逆时针）；区域 → 扇区适用于任何 IZoneMap（按区域代表点相对中心区的方向）</summary>
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
