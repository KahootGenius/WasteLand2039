using System;
using UnityEngine;

/// <summary>
/// 指挥官奖励（计划 §4，刻意保持简单：目标是可分析的行为，而不是最高分）。
/// 评估使用独立于奖励的指标（拦截率、预判准确率等），避免只看奖励造成的误判。
/// </summary>
[Serializable]
public class CommanderRewardSettings
{
    [Tooltip("拦截：玩家逃跑途中（起跑 interceptGraceSeconds 秒后）有敌人进入 interceptRadius。每次逃跑最多一次")]
    public float interception = 1f;
    public float interceptRadius = 2.5f;
    [Tooltip("起跑后这段时间内贴身的敌人不算拦截（多半是起跑前就在身边、正被甩开的追兵）")]
    public float interceptGraceSeconds = 1f;
    [Tooltip("迎头拦截的额外奖励：拦截时最近的敌人在玩家移动方向前方（夹角余弦 ≥ headOnCosine），" +
             "即敌人是预先等在逃跑路线上，而不是从后面追上来的。0 = 关闭（v1_ppo_01 未使用）")]
    public float headOnBonus = 0f;
    public float headOnCosine = 0.5f;
    [Tooltip("玩家每损失 1 点血")]
    public float damagePerHp = 0.01f;
    [Tooltip("玩家死亡")]
    public float playerKilled = 1f;
    [Tooltip("一波结束时玩家未受任何伤害")]
    public float unharmedWave = -0.5f;
    [Tooltip("交战中每秒（鼓励与玩家保持接触）")]
    public float engagedPerSecond = 0.002f;
}

/// <summary>
/// 奖励计算（纯逻辑，可在 Unity 外测试）。指挥官每帧调用 Step，一波结束时调用 EndWave。
/// </summary>
public class CommanderRewardTracker
{
    public CommanderRewardSettings Settings { get; }

    /// <summary>被拦截的逃跑次数（累计）</summary>
    public int Interceptions { get; private set; }
    /// <summary>其中迎头拦截的次数（累计）</summary>
    public int HeadOnInterceptions { get; private set; }
    /// <summary>观察到的逃跑次数（累计）</summary>
    public int Escapes { get; private set; }
    /// <summary>本波玩家受到的伤害</summary>
    public float WaveDamage { get; private set; }

    private int escapeId = -1;
    private bool escapeIntercepted;

    public CommanderRewardTracker(CommanderRewardSettings settings = null)
    {
        Settings = settings ?? new CommanderRewardSettings();
    }

    public void BeginWave()
    {
        WaveDamage = 0f;
        escapeId = -1;
        escapeIntercepted = false;
    }

    /// <param name="activeEscapeId">进行中的逃跑编号；没有逃跑时为 -1</param>
    /// <param name="escapeAge">进行中的逃跑已持续的秒数</param>
    /// <param name="nearestEnemyDistance">最近敌人的距离（无敌人时为无穷大）</param>
    /// <param name="nearestEnemyAheadCosine">最近敌人方向与玩家移动方向的夹角余弦（未知时为 NaN）</param>
    /// <returns>本步应加的奖励</returns>
    public float Step(float deltaTime, bool engaged, int activeEscapeId, float escapeAge,
        float nearestEnemyDistance, float damageTaken, bool playerJustDied,
        float nearestEnemyAheadCosine = float.NaN)
    {
        float reward = 0f;
        if (engaged)
            reward += Settings.engagedPerSecond * deltaTime;
        if (damageTaken > 0f)
        {
            reward += Settings.damagePerHp * damageTaken;
            WaveDamage += damageTaken;
        }
        if (playerJustDied)
            reward += Settings.playerKilled;

        if (activeEscapeId != escapeId)
        {
            escapeId = activeEscapeId;
            escapeIntercepted = false;
            if (activeEscapeId >= 0)
                Escapes++;
        }
        if (activeEscapeId >= 0 && !escapeIntercepted && escapeAge >= Settings.interceptGraceSeconds &&
            nearestEnemyDistance <= Settings.interceptRadius)
        {
            escapeIntercepted = true;
            Interceptions++;
            reward += Settings.interception;
            if (!float.IsNaN(nearestEnemyAheadCosine) && nearestEnemyAheadCosine >= Settings.headOnCosine)
            {
                HeadOnInterceptions++;
                reward += Settings.headOnBonus;
            }
        }
        return reward;
    }

    /// <summary>一波结束：玩家全程未受伤则返回惩罚</summary>
    public float EndWave()
    {
        return WaveDamage <= 0f ? Settings.unharmedWave : 0f;
    }
}
