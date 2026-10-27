using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 逃跑预测的输入特征（纯逻辑）。PlayerBehaviourMonitor 在每次逃跑开始时把同一向量写入遥测
/// （escape_start 的 features 字段），离线训练（MLTraining/predictor/）和游戏中的推理用的是完全相同的特征。
///
/// 布局（长度 Size(K)，K = 区域数；区域数为 17 时为 66）：
///   [0 .. 8+K+8)        玩家画像 PlayerProfile.WriteObservation（见其说明）
///   [8+K+8 .. +K)       玩家当前所在区域（one-hot）
///   [.. +8)             威胁方向：threatRadius 内的敌人相对玩家的方向分布（8 方向，0 = 东，逆时针；和为 1，无敌人时全 0）。
///                       提前部署伏击时还不知道起跑时的威胁，传入 enemies = null（全 0）
///   [.. +8)             每次交战第一次逃跑的方向分布（PlayerProfile.FirstEscapeDirectionProbability，无数据时全 0）
/// 2026-09-26 起为 66 维（遥测 format 3 的早期数据是前 58 维，训练脚本按同样规则回放补齐最后 8 维）。
/// </summary>
public static class EscapeFeatures
{
    public const int ThreatBins = 8;

    public static int Size(int zoneCount)
    {
        return ProfileSize(zoneCount) + zoneCount + ThreatBins + PlayerProfile.DirectionBins;
    }

    /// <summary>威胁方向块的起始下标</summary>
    public static int ThreatOffset(int zoneCount)
    {
        return ProfileSize(zoneCount) + zoneCount;
    }

    public static int ProfileSize(int zoneCount)
    {
        return 8 + zoneCount + PlayerProfile.DirectionBins;
    }

    public static void Build(PlayerProfile profile, int currentZone, Vector2 player, IReadOnlyList<Vector2> enemies,
        float threatRadius, float[] buffer)
    {
        int zoneCount = profile.ZoneCount;
        System.Array.Clear(buffer, 0, Size(zoneCount));

        profile.WriteObservation(buffer, 0);

        int offset = ProfileSize(zoneCount);
        if (currentZone >= 0 && currentZone < zoneCount)
            buffer[offset + currentZone] = 1f;

        offset += zoneCount;
        int firstEscapeOffset = offset + ThreatBins;
        for (int d = 0; d < PlayerProfile.DirectionBins; d++)
            buffer[firstEscapeOffset + d] = profile.FirstEscapeDirectionProbability(d);

        if (enemies == null)
            return;
        int count = 0;
        float radiusSquared = threatRadius * threatRadius;
        for (int i = 0; i < enemies.Count; i++)
        {
            Vector2 toEnemy = enemies[i] - player;
            float distanceSquared = toEnemy.sqrMagnitude;
            if (distanceSquared > radiusSquared || distanceSquared < 0.0001f)
                continue;
            buffer[offset + RadialZoneMap.DirectionToSector(toEnemy, ThreatBins)] += 1f;
            count++;
        }
        if (count > 0)
        {
            for (int d = 0; d < ThreatBins; d++)
                buffer[offset + d] /= count;
        }
    }
}
