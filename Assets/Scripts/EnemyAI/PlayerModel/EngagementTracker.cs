using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 行为识别参数（在 PlayerBehaviourMonitor 的 Inspector 中调整；阈值需用真人数据校准）
/// </summary>
[Serializable]
public class EngagementSettings
{
    [Tooltip("有敌人进入此半径 → 交战开始")]
    public float engageRadius = 6f;
    [Tooltip("此半径内无敌人持续 releaseDelay 秒 → 交战结束（脱离接触）")]
    public float releaseRadius = 9f;
    public float releaseDelay = 2f;
    [Tooltip("计算\"远离敌人速度\"时考虑的敌人范围（按距离倒数加权）")]
    public float threatRadius = 15f;
    [Tooltip("判断撤退的移动窗口（秒）。WASD 速度本身很干净，短窗口可减少逃跑起步时的判定延迟")]
    public float movementWindowSeconds = 0.6f;
    [Tooltip("统计开火频率的窗口（秒）。开火是离散点击，需要较长窗口")]
    public float shotWindowSeconds = 1.5f;
    [Tooltip("玩家最大移动速度（PlayerController.moveSpeed）")]
    public float playerMaxSpeed = 5f;
    [Tooltip("窗口内平均远离速度 / 最大速度 ≥ 此值 → 撤退（Flee 或 Kite）")]
    public float retreatRatio = 0.45f;
    [Tooltip("窗口内平均每秒开火次数 ≥ 此值 → 视为在射击")]
    public float shootingRate = 1f;
    [Tooltip("短于此时长的撤退不计入画像（过滤闪避）")]
    public float minEscapeSeconds = 1f;
    [Tooltip("撤退超过此时长强制结束一段逃跑记录")]
    public float maxEscapeSeconds = 10f;
    [Tooltip("风筝：撤退途中停下射击不超过此时长（秒）时仍算 Kite，逃跑记录不中断。" +
             "本游戏移动中不能开火、开火动画锁定移动约 0.42 秒，边退边打只能是\"走一段、停下射几发\"。0 = 不合并（停下射击即为 Fight）")]
    public float kiteGapSeconds = 1.5f;
}

/// <summary>
/// 交战与逃跑识别（纯逻辑，不依赖场景，可在 Unity 外测试）。
/// 每次采样调用 Step：
/// 1. 交战：有敌人进入 engageRadius 开始；releaseRadius 内无敌人持续 releaseDelay 秒结束
/// 2. 行为：滑动窗口内的"远离敌人速度"（对 threatRadius 内敌人按距离倒数加权）和开火频率
///    → Fight / Flee / Kite / Passive；逃跑途中短暂停下射击（≤ kiteGapSeconds）也算 Kite
/// 3. 逃跑：进入 Flee/Kite 开始一段 EscapeEpisode，记录经过的区域和终点区域
/// </summary>
public class EngagementTracker
{
    /// <summary>按时间长度滚动的求和窗口</summary>
    private sealed class RollingWindow
    {
        private readonly Queue<(float deltaTime, float value)> entries = new Queue<(float, float)>();
        public float Seconds { get; private set; }
        public float Total { get; private set; }

        public void Push(float deltaTime, float value, float maxSeconds)
        {
            entries.Enqueue((deltaTime, value));
            Seconds += deltaTime;
            Total += value;
            while (entries.Count > 1 && Seconds - entries.Peek().deltaTime >= maxSeconds)
            {
                var oldest = entries.Dequeue();
                Seconds -= oldest.deltaTime;
                Total -= oldest.value;
            }
        }

        public void Clear()
        {
            entries.Clear();
            Seconds = 0f;
            Total = 0f;
        }
    }

    public EngagementSettings Settings { get; }
    public IZoneMap Zones { get; }

    /// <summary>基地位置（用于记录逃跑是否朝基地方向）；未设置时不记录</summary>
    public Vector2? BasePosition { get; set; }

    public event Action<EngagementSummary> EngagementStarted;
    public event Action<EngagementSummary> EngagementEnded;
    public event Action<EscapeEpisode> EscapeStarted;
    public event Action<EscapeEpisode> EscapeEnded;
    public event Action<EngagementState, EngagementState> StateChanged;

    // 当前状态（供遥测和指挥官读取）
    public EngagementState State { get; private set; } = EngagementState.None;
    public bool IsEngaged => current != null;
    public int CurrentEngagementId => current != null ? current.Id : -1;
    public EscapeEpisode ActiveEscape => activeEscape;
    public int CurrentZone { get; private set; }
    public int EnemiesInEngageRadius { get; private set; }
    /// <summary>threatRadius 内的敌人数（为 0 时不统计行为时间）</summary>
    public int ThreatsInRange { get; private set; }
    public float NearestEnemyDistance { get; private set; } = float.PositiveInfinity;
    /// <summary>窗口内平均远离速度 / 最大速度（负数 = 在接近敌人）</summary>
    public float RetreatRatio { get; private set; }
    public float ShotRate { get; private set; }

    public int EngagementCount { get; private set; }
    public int EscapeCount { get; private set; }

    private readonly RollingWindow movementWindow = new RollingWindow(); // 值 = 远离速度 × dt
    private readonly RollingWindow shotWindow = new RollingWindow();     // 值 = 开火次数

    private EngagementSummary current;
    private EscapeEpisode activeEscape;
    private float lastThreatTime;
    private float lastRetreatTime = float.NegativeInfinity;
    private Vector2 lastPosition;

    public EngagementTracker(IZoneMap zones, EngagementSettings settings = null)
    {
        Zones = zones;
        Settings = settings ?? new EngagementSettings();
    }

    /// <param name="enemyPositions">当前存活的移动敌人位置</param>
    public void Step(PlayerSample sample, IReadOnlyList<Vector2> enemyPositions)
    {
        CurrentZone = Zones != null ? Zones.GetZone(sample.Position) : 0;
        float awaySpeed = MeasureThreats(sample, enemyPositions);

        // 滑动窗口始终滚动（交战前对逼近敌人的射击也算作战行为的一部分）
        movementWindow.Push(sample.DeltaTime, awaySpeed * sample.DeltaTime, Settings.movementWindowSeconds);
        shotWindow.Push(sample.DeltaTime, sample.ShotsFired, Settings.shotWindowSeconds);
        float meanAway = movementWindow.Seconds > 0f ? movementWindow.Total / movementWindow.Seconds : 0f;
        RetreatRatio = meanAway / Mathf.Max(0.01f, Settings.playerMaxSpeed);
        ShotRate = shotWindow.Total / Mathf.Max(shotWindow.Seconds, 0.25f);

        // 1. 交战生命周期
        if (current == null)
        {
            if (NearestEnemyDistance <= Settings.engageRadius)
                StartEngagement(sample);
        }
        else
        {
            if (NearestEnemyDistance <= Settings.releaseRadius)
                lastThreatTime = sample.Time;
            else if (sample.Time - lastThreatTime >= Settings.releaseDelay)
                EndEngagement(sample, released: true);
        }

        if (current == null)
        {
            SetState(EngagementState.None);
            lastPosition = sample.Position;
            return;
        }

        // 2. 行为分类
        bool retreating = RetreatRatio >= Settings.retreatRatio;
        bool shooting = ShotRate >= Settings.shootingRate;
        EngagementState label = retreating
            ? (shooting ? EngagementState.Kite : EngagementState.Flee)
            : (shooting ? EngagementState.Fight : EngagementState.Passive);
        if (retreating)
            lastRetreatTime = sample.Time;
        else if (shooting && activeEscape != null && Settings.kiteGapSeconds > 0f &&
                 sample.Time - lastRetreatTime <= Settings.kiteGapSeconds)
            label = EngagementState.Kite; // 撤退途中停下射击：风筝的一部分

        // 3. 累计本次交战。行为时间只在有威胁时统计：
        //    敌人全部死亡 / 被甩开后、等待交战结束的那段时间不计入（否则会被误判为 Passive）
        if (ThreatsInRange > 0)
            current.SecondsByState[(int)label] += sample.DeltaTime;
        current.Shots += sample.ShotsFired;
        current.DamageTaken += sample.DamageTaken;
        current.EndPosition = sample.Position;
        current.EndZone = CurrentZone;

        UpdateEscape(sample, label);
        SetState(label);
        lastPosition = sample.Position;
    }

    /// <summary>击杀计入当前交战</summary>
    public void NotifyKill()
    {
        if (current != null)
            current.Kills++;
    }

    /// <summary>外部结束（玩家死亡 / 波次结束 / 场景卸载）</summary>
    public void ForceEnd(PlayerSample sample)
    {
        if (current != null)
            EndEngagement(sample, released: false);
        ClearWindow();
        SetState(EngagementState.None);
    }

    private float MeasureThreats(PlayerSample sample, IReadOnlyList<Vector2> enemies)
    {
        float nearest = float.PositiveInfinity;
        int inEngage = 0;
        int inThreat = 0;
        float weightSum = 0f;
        float awaySum = 0f;

        if (enemies != null)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                Vector2 offset = sample.Position - enemies[i];
                float distance = offset.magnitude;
                if (distance < nearest)
                    nearest = distance;
                if (distance <= Settings.engageRadius)
                    inEngage++;
                if (distance <= Settings.threatRadius && distance > 0.001f)
                {
                    // 距离的变化率（忽略敌人自身移动）：正数 = 玩家在远离该敌人
                    float weight = 1f / Mathf.Max(distance, 0.5f);
                    awaySum += weight * Vector2.Dot(sample.Velocity, offset / distance);
                    weightSum += weight;
                    inThreat++;
                }
            }
        }

        NearestEnemyDistance = nearest;
        EnemiesInEngageRadius = inEngage;
        ThreatsInRange = inThreat;
        return weightSum > 0f ? awaySum / weightSum : 0f;
    }

    private void StartEngagement(PlayerSample sample)
    {
        EngagementCount++;
        current = new EngagementSummary
        {
            Id = EngagementCount,
            StartTime = sample.Time,
            EndTime = sample.Time,
            StartPosition = sample.Position,
            EndPosition = sample.Position,
            StartZone = CurrentZone,
            EndZone = CurrentZone
        };
        lastThreatTime = sample.Time;
        EngagementStarted?.Invoke(current);
    }

    private void EndEngagement(PlayerSample sample, bool released)
    {
        if (activeEscape != null)
            EndEscape(sample, released ? EscapeEndReason.GotAway : EscapeEndReason.Interrupted);

        var finished = current;
        current = null;
        finished.EndTime = sample.Time;
        finished.EndPosition = sample.Position;
        finished.EndZone = CurrentZone;
        finished.Released = released;
        EngagementEnded?.Invoke(finished);
    }

    private void UpdateEscape(PlayerSample sample, EngagementState label)
    {
        bool retreating = EngagementStateUtil.IsRetreat(label);

        if (activeEscape == null)
        {
            if (retreating)
                StartEscape(sample);
            else
                return;
        }

        if (!retreating)
        {
            EndEscape(sample, EscapeEndReason.Stopped);
            return;
        }

        var escape = activeEscape;
        escape.PathLength += Vector2.Distance(lastPosition, sample.Position);
        escape.RetreatSeconds += sample.DeltaTime;
        if (label == EngagementState.Kite)
            escape.ShootingSeconds += sample.DeltaTime;
        escape.DamageTaken += sample.DamageTaken;
        if (escape.Route[escape.Route.Count - 1] != CurrentZone)
            escape.Route.Add(CurrentZone);

        if (sample.Time - escape.StartTime >= Settings.maxEscapeSeconds)
            EndEscape(sample, EscapeEndReason.Timeout);
    }

    private void StartEscape(PlayerSample sample)
    {
        EscapeCount++;
        activeEscape = new EscapeEpisode
        {
            Id = EscapeCount,
            EngagementId = current.Id,
            StartTime = sample.Time,
            EndTime = float.NegativeInfinity, // 进行中
            StartPosition = sample.Position,
            EndPosition = sample.Position,
            StartZone = CurrentZone,
            EndZone = CurrentZone,
            StartBaseDistance = BasePosition.HasValue ? Vector2.Distance(sample.Position, BasePosition.Value) : -1f
        };
        activeEscape.Route.Add(CurrentZone);
        EscapeStarted?.Invoke(activeEscape);
    }

    private void EndEscape(PlayerSample sample, EscapeEndReason reason)
    {
        var escape = activeEscape;
        activeEscape = null;

        escape.EndTime = sample.Time;
        escape.EndPosition = sample.Position;
        escape.EndZone = CurrentZone;
        if (escape.Route[escape.Route.Count - 1] != CurrentZone)
            escape.Route.Add(CurrentZone);
        escape.EndBaseDistance = BasePosition.HasValue ? Vector2.Distance(sample.Position, BasePosition.Value) : -1f;
        escape.EndReason = reason;
        escape.IsValid = escape.Duration >= Settings.minEscapeSeconds;

        if (escape.IsValid && current != null)
            current.Escapes++;

        EscapeEnded?.Invoke(escape);
    }

    private void SetState(EngagementState state)
    {
        if (state == State)
            return;

        var previous = State;
        State = state;
        StateChanged?.Invoke(previous, state);
    }

    private void ClearWindow()
    {
        movementWindow.Clear();
        shotWindow.Clear();
        RetreatRatio = 0f;
        ShotRate = 0f;
    }
}
