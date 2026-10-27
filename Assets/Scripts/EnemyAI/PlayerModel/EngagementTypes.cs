using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家在交战中的行为状态（每个采样根据滑动窗口判定）
/// </summary>
public enum EngagementState
{
    /// <summary>未交战</summary>
    None = 0,
    /// <summary>正面作战：不撤退，持续射击</summary>
    Fight = 1,
    /// <summary>逃跑：快速远离敌人，几乎不射击</summary>
    Flee = 2,
    /// <summary>边打边退（风筝）：远离敌人同时射击</summary>
    Kite = 3,
    /// <summary>消极：既不撤退也不射击（例如站桩、被围、操作背包）</summary>
    Passive = 4
}

public static class EngagementStateUtil
{
    public const int Count = 5;

    public static bool IsRetreat(EngagementState state)
    {
        return state == EngagementState.Flee || state == EngagementState.Kite;
    }
}

/// <summary>
/// 一次玩家采样（由 PlayerBehaviourMonitor 以固定频率生成）
/// </summary>
public struct PlayerSample
{
    public float Time;
    public float DeltaTime;
    public Vector2 Position;
    public Vector2 Velocity;
    /// <summary>本采样间隔内的开火次数</summary>
    public int ShotsFired;
    /// <summary>本采样间隔内受到的伤害</summary>
    public float DamageTaken;
}

/// <summary>
/// 一次交战的汇总（交战结束时生成）
/// </summary>
public class EngagementSummary
{
    public int Id;
    public float StartTime;
    public float EndTime;
    public float Duration => EndTime - StartTime;
    public Vector2 StartPosition;
    public Vector2 EndPosition;
    public int StartZone;
    public int EndZone;

    /// <summary>各状态累计秒数，按 (int)EngagementState 索引</summary>
    public readonly float[] SecondsByState = new float[EngagementStateUtil.Count];

    public int Shots;
    public float DamageTaken;
    public int Kills;
    public int Escapes;
    /// <summary>true = 玩家脱离接触（敌人全部远离）；false = 被外部结束（死亡 / 波次结束）</summary>
    public bool Released;

    public float Seconds(EngagementState state)
    {
        return SecondsByState[(int)state];
    }

    /// <summary>用时最长的行为状态</summary>
    public EngagementState Dominant
    {
        get
        {
            var best = EngagementState.Fight;
            for (int i = 1; i < EngagementStateUtil.Count; i++)
            {
                if (SecondsByState[i] > SecondsByState[(int)best])
                    best = (EngagementState)i;
            }
            return best;
        }
    }
}

public enum EscapeEndReason
{
    /// <summary>玩家停止撤退（转为作战 / 消极）</summary>
    Stopped,
    /// <summary>成功脱离：交战因敌人远离而结束</summary>
    GotAway,
    /// <summary>撤退持续超过上限</summary>
    Timeout,
    /// <summary>被外部结束（死亡 / 波次结束）</summary>
    Interrupted
}

/// <summary>
/// 一次逃跑（撤退）过程：从进入 Flee/Kite 开始，到停止撤退或脱离接触为止
/// </summary>
public class EscapeEpisode
{
    public int Id;
    public int EngagementId;
    public float StartTime;
    public float EndTime;
    public float Duration => EndTime - StartTime;

    public Vector2 StartPosition;
    public Vector2 EndPosition;
    public int StartZone;
    public int EndZone;
    /// <summary>经过的区域序列（相邻去重，包含起点和终点区域）</summary>
    public readonly List<int> Route = new List<int>();
    public float PathLength;

    public float RetreatSeconds;   // 其中 Flee + Kite 的时间
    public float ShootingSeconds;  // 其中 Kite 的时间
    public float DamageTaken;

    /// <summary>到基地的距离（无基地时为 -1）</summary>
    public float StartBaseDistance = -1f;
    public float EndBaseDistance = -1f;

    public EscapeEndReason EndReason;
    /// <summary>持续时间达到最小值，计入玩家画像</summary>
    public bool IsValid;

    public bool IsOpen => EndTime < StartTime;

    /// <summary>以边打边退为主则为 Kite，否则 Flee</summary>
    public EngagementState Type =>
        ShootingSeconds > RetreatSeconds * 0.5f ? EngagementState.Kite : EngagementState.Flee;

    public Vector2 Displacement => EndPosition - StartPosition;
}
