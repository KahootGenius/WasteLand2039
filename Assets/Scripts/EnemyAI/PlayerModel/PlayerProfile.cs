using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public class ProfileSettings
{
    [Tooltip("每次交战后旧数据的保留比例（越小越快适应玩家的变化）")]
    [Range(0.5f, 1f)] public float engagementDecay = 0.9f;
    [Tooltip("每次逃跑后旧的逃跑统计的保留比例")]
    [Range(0.5f, 1f)] public float escapeDecay = 0.85f;
    [Tooltip("达到此逃跑次数时置信度为 1")]
    public int escapesForFullConfidence = 10;
    [Tooltip("终点比起点离基地近这么多（单位）才算\"逃向基地\"")]
    public float towardBaseMargin = 1f;
}

/// <summary>
/// 玩家画像的可序列化状态（跨会话保存用，见 PlayerProfileStore）。只含数据，不含设置（衰减系数等取当前设置）
/// </summary>
[Serializable]
public class PlayerProfileState
{
    public int version = 1;
    public int zoneCount;
    public int engagements;
    public int escapes;
    public float[] stateSeconds;
    public float[] destinationWeights;
    public float[] directionWeights;
    public int[] routeFrom;
    public int[] routeTo;
    public float[] routeWeights;
    /// <summary>按起点区域的逃跑方向计数（起点, 方向 0..7, 权重）；旧存档没有这些字段（视为空）</summary>
    public int[] directionFrom;
    public int[] directionBin;
    public float[] directionWeight;
    /// <summary>每次交战第一次逃跑的方向计数（8 方向）；旧存档没有（视为 0）</summary>
    public float[] firstEscapeDirections;
    public float meanEscapeDistance;
    public float towardBaseRate;
    public float gotAwayRate;
}

/// <summary>
/// 玩家画像（"学到的玩家模式"）：由 EngagementTracker 的交战汇总和逃跑记录在线更新，带遗忘（衰减）。
/// 纯逻辑，可在 Unity 外测试。所有指挥官（基准 / V1 / V2）读取同一份画像。
///
/// ToObservation() 布局（长度 ObservationSize = 8 + ZoneCount + 8）：
///   [0] Fight 占比  [1] Flee 占比  [2] Kite 占比  [3] Passive 占比
///   [4] 逃跑终点一致性（1 - 归一化熵）  [5] 平均逃跑距离 / 20（截断到 1）
///   [6] 逃向基地的比例  [7] 置信度（逃跑次数 / escapesForFullConfidence）
///   [8 .. 8+K)  逃跑终点区域分布（K = ZoneCount）
///   [8+K .. 8+K+8)  逃跑方向分布（8 方向，0 = 东，逆时针）
/// </summary>
public class PlayerProfile
{
    public const int DirectionBins = 8;
    private const int ScalarCount = 8;

    public ProfileSettings Settings { get; }
    public int ZoneCount { get; }
    public int ObservationSize => ScalarCount + ZoneCount + DirectionBins;

    public event Action<PlayerProfile> Updated;

    public int Engagements { get; private set; }
    public int Escapes { get; private set; }

    private readonly float[] stateSeconds = new float[EngagementStateUtil.Count];
    private readonly float[] destinationWeights;
    private readonly float[] directionWeights = new float[DirectionBins];
    private readonly Dictionary<long, float> routeWeights = new Dictionary<long, float>();
    private readonly Dictionary<long, float> directionRoutes = new Dictionary<long, float>();
    private readonly float[] firstEscapeDirections = new float[DirectionBins];
    private int lastEscapeEngagementId = -1;

    public float MeanEscapeDistance { get; private set; }
    public float TowardBaseRate { get; private set; }
    public float GotAwayRate { get; private set; }

    public PlayerProfile(int zoneCount, ProfileSettings settings = null)
    {
        ZoneCount = Mathf.Max(1, zoneCount);
        Settings = settings ?? new ProfileSettings();
        destinationWeights = new float[ZoneCount];
    }

    // ---------- 更新 ----------

    public void AddEngagement(EngagementSummary summary)
    {
        if (summary == null || summary.Duration <= 0f)
            return;

        for (int i = 0; i < stateSeconds.Length; i++)
            stateSeconds[i] = stateSeconds[i] * Settings.engagementDecay + summary.SecondsByState[i];

        Engagements++;
        Updated?.Invoke(this);
    }

    public void AddEscape(EscapeEpisode escape)
    {
        if (escape == null || !escape.IsValid)
            return;

        float decay = Settings.escapeDecay;
        Escapes++;
        // 前几次用简单平均，之后按衰减比例做指数平均
        float alpha = Mathf.Max(1f / Escapes, 1f - decay);

        Scale(destinationWeights, decay);
        if (escape.EndZone >= 0 && escape.EndZone < ZoneCount)
            destinationWeights[escape.EndZone] += 1f;

        Vector2 displacement = escape.Displacement;
        if (displacement.sqrMagnitude > 1f)
        {
            int direction = RadialZoneMap.DirectionToSector(displacement, DirectionBins);
            Scale(directionWeights, decay);
            directionWeights[direction] += 1f;

            // 按起点的方向计数，同样只衰减同一起点的（V2 预测器：从基地出发往哪个方向跑）
            var directionKeys = new List<long>(directionRoutes.Keys);
            foreach (long key in directionKeys)
            {
                if ((int)(key >> 32) == escape.StartZone)
                    directionRoutes[key] *= decay;
            }
            long directionKey = RouteKey(escape.StartZone, direction);
            directionRoutes.TryGetValue(directionKey, out float directionWeight);
            directionRoutes[directionKey] = directionWeight + 1f;

            // 每次交战的第一次逃跑 = 玩家从所在处（通常是基地）选择的路线；之后的逃跑多是在避难点被赶出来，
            // 方向受追兵影响。V2 预测"从基地出发往哪跑"用这个分布
            if (escape.EngagementId != lastEscapeEngagementId)
            {
                Scale(firstEscapeDirections, decay);
                firstEscapeDirections[direction] += 1f;
            }
        }
        lastEscapeEngagementId = escape.EngagementId;

        // 路线计数只衰减同一起点的路线：P(终点 | 起点) 各自保留记忆，
        // 不会因为玩家在别处（例如在避难点反复被赶出来）的逃跑而遗忘从基地出发的路线（V2 预测器使用）
        var keys = new List<long>(routeWeights.Keys);
        foreach (long key in keys)
        {
            if ((int)(key >> 32) == escape.StartZone)
                routeWeights[key] *= decay;
        }
        long routeKey = RouteKey(escape.StartZone, escape.EndZone);
        routeWeights.TryGetValue(routeKey, out float routeWeight);
        routeWeights[routeKey] = routeWeight + 1f;

        MeanEscapeDistance += alpha * (displacement.magnitude - MeanEscapeDistance);
        if (escape.StartBaseDistance >= 0f && escape.EndBaseDistance >= 0f)
        {
            float towardBase = escape.EndBaseDistance < escape.StartBaseDistance - Settings.towardBaseMargin ? 1f : 0f;
            TowardBaseRate += alpha * (towardBase - TowardBaseRate);
        }
        float gotAway = escape.EndReason == EscapeEndReason.GotAway && escape.DamageTaken <= 0f ? 1f : 0f;
        GotAwayRate += alpha * (gotAway - GotAwayRate);

        Updated?.Invoke(this);
    }

    // ---------- 保存 / 恢复 ----------

    /// <summary>导出当前状态（副本，之后的更新不影响它）</summary>
    public PlayerProfileState ExportState()
    {
        var state = new PlayerProfileState
        {
            zoneCount = ZoneCount,
            engagements = Engagements,
            escapes = Escapes,
            stateSeconds = (float[])stateSeconds.Clone(),
            destinationWeights = (float[])destinationWeights.Clone(),
            directionWeights = (float[])directionWeights.Clone(),
            routeFrom = new int[routeWeights.Count],
            routeTo = new int[routeWeights.Count],
            routeWeights = new float[routeWeights.Count],
            directionFrom = new int[directionRoutes.Count],
            directionBin = new int[directionRoutes.Count],
            directionWeight = new float[directionRoutes.Count],
            firstEscapeDirections = (float[])firstEscapeDirections.Clone(),
            meanEscapeDistance = MeanEscapeDistance,
            towardBaseRate = TowardBaseRate,
            gotAwayRate = GotAwayRate
        };
        int i = 0;
        foreach (var pair in routeWeights)
        {
            state.routeFrom[i] = (int)(pair.Key >> 32);
            state.routeTo[i] = (int)(pair.Key & 0xffffffff);
            state.routeWeights[i] = pair.Value;
            i++;
        }
        i = 0;
        foreach (var pair in directionRoutes)
        {
            state.directionFrom[i] = (int)(pair.Key >> 32);
            state.directionBin[i] = (int)(pair.Key & 0xffffffff);
            state.directionWeight[i] = pair.Value;
            i++;
        }
        return state;
    }

    /// <summary>
    /// 用保存的状态替换当前数据。区域数或数组长度不符（分区设置变了）时不做任何修改并返回 false
    /// </summary>
    public bool TryImportState(PlayerProfileState state, out string error)
    {
        error = null;
        if (state == null)
            error = "没有数据";
        else if (state.version != 1)
            error = $"不支持的版本 {state.version}";
        else if (state.zoneCount != ZoneCount)
            error = $"区域数 {state.zoneCount} ≠ 当前 {ZoneCount}";
        else if (state.stateSeconds == null || state.stateSeconds.Length != stateSeconds.Length ||
                 state.destinationWeights == null || state.destinationWeights.Length != ZoneCount ||
                 state.directionWeights == null || state.directionWeights.Length != DirectionBins)
            error = "数组长度不符";
        else if (state.routeFrom == null || state.routeTo == null || state.routeWeights == null ||
                 state.routeFrom.Length != state.routeWeights.Length || state.routeTo.Length != state.routeWeights.Length)
            error = "路线数据不完整";
        else if (state.directionWeight != null &&
                 (state.directionFrom == null || state.directionBin == null ||
                  state.directionFrom.Length != state.directionWeight.Length || state.directionBin.Length != state.directionWeight.Length))
            error = "方向数据不完整";
        else if (state.firstEscapeDirections != null && state.firstEscapeDirections.Length != DirectionBins)
            error = "首次逃跑方向数据长度不符";
        if (error != null)
            return false;

        Engagements = Mathf.Max(0, state.engagements);
        Escapes = Mathf.Max(0, state.escapes);
        Array.Copy(state.stateSeconds, stateSeconds, stateSeconds.Length);
        Array.Copy(state.destinationWeights, destinationWeights, ZoneCount);
        Array.Copy(state.directionWeights, directionWeights, DirectionBins);
        routeWeights.Clear();
        for (int i = 0; i < state.routeWeights.Length; i++)
            routeWeights[RouteKey(state.routeFrom[i], state.routeTo[i])] = state.routeWeights[i];
        directionRoutes.Clear();
        if (state.directionWeight != null)
        {
            for (int i = 0; i < state.directionWeight.Length; i++)
            {
                if (state.directionBin[i] >= 0 && state.directionBin[i] < DirectionBins)
                    directionRoutes[RouteKey(state.directionFrom[i], state.directionBin[i])] = state.directionWeight[i];
            }
        }
        Array.Clear(firstEscapeDirections, 0, DirectionBins);
        if (state.firstEscapeDirections != null)
            Array.Copy(state.firstEscapeDirections, firstEscapeDirections, DirectionBins);
        lastEscapeEngagementId = -1;
        MeanEscapeDistance = state.meanEscapeDistance;
        TowardBaseRate = state.towardBaseRate;
        GotAwayRate = state.gotAwayRate;
        Updated?.Invoke(this);
        return true;
    }

    // ---------- 查询 ----------

    /// <summary>该行为状态在（衰减加权的）交战时间中的占比</summary>
    public float Share(EngagementState state)
    {
        float total = 0f;
        for (int i = 1; i < stateSeconds.Length; i++)
            total += stateSeconds[i];
        return total > 0f ? stateSeconds[(int)state] / total : 0f;
    }

    public float FightShare => Share(EngagementState.Fight);
    public float FleeShare => Share(EngagementState.Flee);
    public float KiteShare => Share(EngagementState.Kite);
    public float PassiveShare => Share(EngagementState.Passive);

    /// <summary>逃跑终点落在该区域的概率（无数据时为 0）</summary>
    public float EscapeZoneProbability(int zone)
    {
        float total = Sum(destinationWeights);
        return total > 0f && zone >= 0 && zone < ZoneCount ? destinationWeights[zone] / total : 0f;
    }

    /// <summary>逃跑终点统计的（衰减后的）总权重：约等于"最近的有效逃跑次数"，用于预测器的平滑</summary>
    public float EscapeEvidence => Sum(destinationWeights);

    /// <summary>
    /// 从 fromZone 出发的逃跑的终点权重（衰减后）写入 destinationBuffer（长度 ZoneCount），返回总权重
    /// </summary>
    public float RouteWeightsFrom(int fromZone, float[] destinationBuffer)
    {
        Array.Clear(destinationBuffer, 0, destinationBuffer.Length);
        float total = 0f;
        foreach (var pair in routeWeights)
        {
            if ((int)(pair.Key >> 32) != fromZone)
                continue;
            int toZone = (int)(pair.Key & 0xffffffff);
            if (toZone < 0 || toZone >= destinationBuffer.Length)
                continue;
            destinationBuffer[toZone] += pair.Value;
            total += pair.Value;
        }
        return total;
    }

    /// <summary>每次交战第一次逃跑的方向统计的（衰减后的）总权重</summary>
    public float FirstEscapeEvidence => Sum(firstEscapeDirections);

    /// <summary>每次交战第一次逃跑的方向分布（玩家从基地出发选的路线）；无数据时为 0</summary>
    public float FirstEscapeDirectionProbability(int direction)
    {
        float total = Sum(firstEscapeDirections);
        return total > 0f ? firstEscapeDirections[direction] / total : 0f;
    }

    /// <summary>逃跑方向统计的（衰减后的）总权重</summary>
    public float DirectionEvidence => Sum(directionWeights);

    /// <summary>
    /// 从 fromZone 出发的逃跑的方向权重（衰减后，8 方向）写入 directionBuffer，返回总权重
    /// </summary>
    public float DirectionWeightsFrom(int fromZone, float[] directionBuffer)
    {
        Array.Clear(directionBuffer, 0, directionBuffer.Length);
        float total = 0f;
        foreach (var pair in directionRoutes)
        {
            if ((int)(pair.Key >> 32) != fromZone)
                continue;
            int direction = (int)(pair.Key & 0xffffffff);
            if (direction < 0 || direction >= directionBuffer.Length)
                continue;
            directionBuffer[direction] += pair.Value;
            total += pair.Value;
        }
        return total;
    }

    public float EscapeDirectionProbability(int direction)
    {
        float total = Sum(directionWeights);
        return total > 0f ? directionWeights[direction] / total : 0f;
    }

    /// <summary>按概率从高到低的逃跑终点区域（只含概率 &gt; 0 的）</summary>
    public List<int> TopEscapeZones(int count)
    {
        var zones = new List<int>();
        for (int z = 0; z < ZoneCount; z++)
        {
            if (destinationWeights[z] > 0f)
                zones.Add(z);
        }
        zones.Sort((a, b) => destinationWeights[b].CompareTo(destinationWeights[a]));
        if (zones.Count > count)
            zones.RemoveRange(count, zones.Count - count);
        return zones;
    }

    /// <summary>最常见的（起点区域 → 终点区域）；无数据时返回 false</summary>
    public bool TryGetTopRoute(out int fromZone, out int toZone, out float share)
    {
        fromZone = toZone = -1;
        share = 0f;
        float total = 0f;
        long bestKey = -1;
        float best = 0f;
        foreach (var pair in routeWeights)
        {
            total += pair.Value;
            if (pair.Value > best)
            {
                best = pair.Value;
                bestKey = pair.Key;
            }
        }
        if (bestKey < 0)
            return false;

        fromZone = (int)(bestKey >> 32);
        toZone = (int)(bestKey & 0xffffffff);
        share = best / total;
        return true;
    }

    /// <summary>逃跑终点的一致性：1 = 总是逃到同一区域，0 = 均匀分布或无数据</summary>
    public float Consistency
    {
        get
        {
            float total = Sum(destinationWeights);
            if (total <= 0f || ZoneCount < 2)
                return 0f;

            float entropy = 0f;
            foreach (float weight in destinationWeights)
            {
                if (weight <= 0f)
                    continue;
                float p = weight / total;
                entropy -= p * Mathf.Log(p);
            }
            return 1f - entropy / Mathf.Log(ZoneCount);
        }
    }

    public float Confidence => Mathf.Clamp01(Escapes / (float)Mathf.Max(1, Settings.escapesForFullConfidence));

    public float[] ToObservation()
    {
        var buffer = new float[ObservationSize];
        WriteObservation(buffer, 0);
        return buffer;
    }

    public void WriteObservation(float[] buffer, int offset)
    {
        buffer[offset + 0] = FightShare;
        buffer[offset + 1] = FleeShare;
        buffer[offset + 2] = KiteShare;
        buffer[offset + 3] = PassiveShare;
        buffer[offset + 4] = Consistency;
        buffer[offset + 5] = Mathf.Clamp01(MeanEscapeDistance / 20f);
        buffer[offset + 6] = TowardBaseRate;
        buffer[offset + 7] = Confidence;
        for (int z = 0; z < ZoneCount; z++)
            buffer[offset + ScalarCount + z] = EscapeZoneProbability(z);
        for (int d = 0; d < DirectionBins; d++)
            buffer[offset + ScalarCount + ZoneCount + d] = EscapeDirectionProbability(d);
    }

    /// <summary>多行文字摘要（调试面板 / 日志）</summary>
    public string Describe(IZoneMap zones)
    {
        var text = new StringBuilder();
        text.AppendLine($"交战 {Engagements} 次，逃跑 {Escapes} 次，置信度 {Confidence:P0}");
        text.AppendLine($"Fight {FightShare:P0}  Flee {FleeShare:P0}  Kite {KiteShare:P0}  Passive {PassiveShare:P0}");

        var top = TopEscapeZones(3);
        if (top.Count > 0)
        {
            text.Append("逃跑终点: ");
            for (int i = 0; i < top.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append($"{ZoneName(zones, top[i])} {EscapeZoneProbability(top[i]):P0}");
            }
            text.AppendLine($"   一致性 {Consistency:F2}");

            int bestDirection = 0;
            for (int d = 1; d < DirectionBins; d++)
            {
                if (directionWeights[d] > directionWeights[bestDirection])
                    bestDirection = d;
            }
            text.AppendLine($"主要逃跑方向: {DirectionName(bestDirection)} {EscapeDirectionProbability(bestDirection):P0}");
        }

        if (TryGetTopRoute(out int from, out int to, out float share))
            text.AppendLine($"最常见路线: {ZoneName(zones, from)} → {ZoneName(zones, to)} ({share:P0})");

        if (Escapes > 0)
            text.AppendLine($"平均逃跑距离 {MeanEscapeDistance:F1}，逃向基地 {TowardBaseRate:P0}，成功脱离 {GotAwayRate:P0}");

        return text.ToString();
    }

    public static string DirectionName(int direction)
    {
        string[] names = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
        return names[((direction % DirectionBins) + DirectionBins) % DirectionBins];
    }

    private static string ZoneName(IZoneMap zones, int zone)
    {
        return zones != null ? zones.GetZoneName(zone) : zone.ToString();
    }

    private static long RouteKey(int fromZone, int toZone)
    {
        return ((long)fromZone << 32) | (uint)toZone;
    }

    private static void Scale(float[] values, float factor)
    {
        for (int i = 0; i < values.Length; i++)
            values[i] *= factor;
    }

    private static float Sum(float[] values)
    {
        float total = 0f;
        foreach (float value in values)
            total += value;
        return total;
    }
}
