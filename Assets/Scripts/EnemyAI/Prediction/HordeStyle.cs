using UnityEngine;

/// <summary>
/// 按玩家风格分配伏击（PredictiveCommander 的 styleAware / -v2Style）：尸潮从画像判断玩家是"多半在跑"还是"多半在打"，
/// 再决定把多少敌人派去等。逃跑占（衰减加权的）交战时间至少 runnerFleeShare 的玩家多半在跑：在预测的避难点一带
/// 伏击 runnerAmbushSize 个（比 V2 的 2 个多），不设关口。其余玩家多半在打（站着打、风筝、有时才跑）：不在远处伏击
/// （V2 的两个伏击者在避难点干等，基地附近的战斗少了两个人），只派 gateSize 个守在预测的逃跑方向上、离基地
/// gateDistance 的关口（离开基地的路上），其余都在基地附近施压。
/// 机器人的画像中逃跑占比：Adaptive / RunnerA / RunnerB80 约 0.52-0.55，Mixed 0.31，Random 0.21，Kiter 0.05，Fighter 0；
/// 0.42 在两组之间。在模拟训练场（MLTraining/tools/arena-sim）中，按风格分配让 8 个训练场的平均伤害比 V2 每波
/// 多约 5（2026-10-21，两组各 32 个种子：+4.7、+6.0），没有哪个性格变少。纯计算，供 PredictiveCommander、模拟训练场和逻辑测试使用。
/// </summary>
public static class HordeStyle
{
    /// <summary>逃跑占交战时间至少这么多 = 多半在跑</summary>
    public const float DefaultRunnerFleeShare = 0.42f;
    /// <summary>多半在跑的玩家：避难点一带的伏击人数</summary>
    public const int DefaultRunnerAmbushSize = 3;
    /// <summary>多半在打的玩家：关口人数</summary>
    public const int DefaultGateSize = 1;
    /// <summary>关口离基地中心的距离（玩家的待命位置离基地中心 4.5，避难点 26）</summary>
    public const float DefaultGateDistance = 9f;

    /// <summary>多半在跑：逃跑占（衰减加权的）交战时间至少 runnerFleeShare（PlayerProfile.FleeShare）</summary>
    public static bool IsRunner(float fleeShare, float runnerFleeShare)
    {
        return fleeShare >= runnerFleeShare;
    }

    /// <summary>伏击人数（避难点一带）和关口人数（离开基地的路上）</summary>
    public static (int ambush, int gate) Allocate(float fleeShare, float runnerFleeShare, int runnerAmbushSize, int gateSize)
    {
        return IsRunner(fleeShare, runnerFleeShare)
            ? (Mathf.Max(0, runnerAmbushSize), 0)
            : (0, Mathf.Max(0, gateSize));
    }
}
