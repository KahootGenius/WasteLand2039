using System;
using System.Globalization;
using UnityEngine;

/// <summary>闭区间 [min, max] 的随机范围（每回合采样一次）</summary>
[Serializable]
public struct FloatRange
{
    public float min;
    public float max;

    public FloatRange(float min, float max)
    {
        this.min = min;
        this.max = max;
    }

    public float Sample(System.Random rng)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }
}

/// <summary>
/// 机器人玩家的"性格"（BotPersona 资产的内容；纯数据，可在 Unity 外测试）。
/// 数值参数都是范围：每个回合 Sample() 一次得到一组具体参数（BotParams），
/// 模拟"同一类型的不同玩家"，避免指挥官只学会对付某一组固定数值。
/// </summary>
[Serializable]
public class BotPersonaSpec
{
    [Header("交战方式：每次交战按权重选一种（自动归一化）")]
    public float fightWeight = 1f;
    public float fleeWeight = 0f;
    public float kiteWeight = 0f;

    [Header("逃跑 / 风筝路线偏好（按训练场路线顺序 A, B, C…）")]
    public float[] routeWeights = { 1f, 1f, 1f };

    [Tooltip("每回合对上述权重的随机扰动：每项乘以 1 ± 此值。0 = 不扰动；为 0 的权重始终为 0")]
    [Range(0f, 1f)] public float weightJitter = 0.15f;

    [Header("适应：逃跑途中被拦截后改走其他路线")]
    public bool adaptRoutes = false;
    [Tooltip("被拦截后该路线的权重乘以此值")]
    [Range(0f, 1f)] public float ambushPenalty = 0.25f;
    [Tooltip("每次未被拦截的逃跑后，各路线权重向本回合初始偏好恢复的比例")]
    [Range(0f, 1f)] public float ambushRecovery = 0.1f;

    [Header("反应（每回合在范围内随机）")]
    [Tooltip("最近的敌人进入此距离时执行逃跑 / 风筝（交战识别半径为 6，敌人发现玩家的半径为 5）")]
    public FloatRange decisionDistance = new FloatRange(4f, 5.5f);
    [Tooltip("从察觉到行动的反应时间（秒）")]
    public FloatRange reactionTime = new FloatRange(0.15f, 0.35f);
    [Tooltip("离最近的敌人超过此距离才算安全")]
    public FloatRange safeDistance = new FloatRange(8f, 10f);
    [Tooltip("安全状态持续多久才结束本次交战（秒）")]
    public FloatRange safeTime = new FloatRange(1.5f, 2.5f);
    [Tooltip("逃跑结束后在原地停留多久再返回基地（秒）")]
    public FloatRange lingerTime = new FloatRange(1f, 4f);

    [Header("射击")]
    [Tooltip("作战 / 风筝时，敌人进入此距离开始射击（武器射程 10）")]
    public FloatRange fireRange = new FloatRange(6f, 9f);
    [Tooltip("瞄准误差（度）：每发在 ±此值内随机偏移")]
    public FloatRange aimError = new FloatRange(2f, 6f);
    [Tooltip("最高点击频率（次/秒；武器上限 5）")]
    public FloatRange clickRate = new FloatRange(3f, 5f);

    [Header("风筝（移动中不能开火、开火锁定移动约 0.42 秒，所以是\"停下射击，敌人逼近就退一小段\"）")]
    [Tooltip("最近的敌人进入此距离时后退一小段")]
    public FloatRange kiteNearDistance = new FloatRange(3f, 4.5f);
    [Tooltip("每段后退的时长（秒）。敌人离玩家超过 5 就不再追击，退太远等于逃跑")]
    public FloatRange kiteMoveTime = new FloatRange(0.4f, 0.8f);
    [Tooltip("两次后退之间至少射击的发数（四舍五入；敌人贴身时例外）")]
    public FloatRange kiteShots = new FloatRange(1.5f, 3.4f);

    [Header("移动")]
    [Tooltip("只朝 8 个方向移动（模拟 WASD 键盘操作）")]
    public bool eightWayMovement = true;

    /// <summary>采样一组本回合使用的具体参数</summary>
    public BotParams Sample(System.Random rng, string personaName = "")
    {
        var p = new BotParams { PersonaName = personaName ?? "" };

        float fight = Jitter(fightWeight, rng);
        float flee = Jitter(fleeWeight, rng);
        float kite = Jitter(kiteWeight, rng);
        float total = fight + flee + kite;
        if (total <= 0f)
        {
            fight = 1f;
            total = 1f;
        }
        p.FightWeight = fight / total;
        p.FleeWeight = flee / total;
        p.KiteWeight = kite / total;

        float[] routes = routeWeights ?? new float[0];
        p.RouteWeights = new float[routes.Length];
        for (int i = 0; i < routes.Length; i++)
            p.RouteWeights[i] = Jitter(routes[i], rng);
        BotParams.Normalize(p.RouteWeights);

        p.AdaptRoutes = adaptRoutes;
        p.AmbushPenalty = ambushPenalty;
        p.AmbushRecovery = ambushRecovery;

        p.DecisionDistance = decisionDistance.Sample(rng);
        p.ReactionTime = Mathf.Max(0f, reactionTime.Sample(rng));
        p.SafeDistance = safeDistance.Sample(rng);
        p.SafeTime = Mathf.Max(0f, safeTime.Sample(rng));
        p.LingerTime = Mathf.Max(0f, lingerTime.Sample(rng));
        p.FireRange = fireRange.Sample(rng);
        p.AimError = Mathf.Max(0f, aimError.Sample(rng));
        p.ClickRate = Mathf.Max(0.5f, clickRate.Sample(rng));
        p.KiteMoveTime = Mathf.Max(0.1f, kiteMoveTime.Sample(rng));
        p.KiteNearDistance = kiteNearDistance.Sample(rng);
        p.KiteShots = Mathf.Max(1, Mathf.RoundToInt(kiteShots.Sample(rng)));
        p.EightWayMovement = eightWayMovement;
        return p;
    }

    private float Jitter(float weight, System.Random rng)
    {
        if (weight <= 0f)
            return 0f;
        return weight * Mathf.Max(0f, 1f + weightJitter * (float)(rng.NextDouble() * 2.0 - 1.0));
    }
}

/// <summary>一个回合内机器人使用的具体参数（BotPersonaSpec.Sample 的结果）</summary>
public class BotParams
{
    public string PersonaName = "";

    // 交战方式概率（和为 1）
    public float FightWeight = 1f;
    public float FleeWeight;
    public float KiteWeight;

    /// <summary>路线偏好（和为 1；全为 0 时由 BotBrain 视为均匀分布）</summary>
    public float[] RouteWeights = new float[0];

    public bool AdaptRoutes;
    public float AmbushPenalty = 0.25f;
    public float AmbushRecovery = 0.1f;

    public float DecisionDistance = 5f;
    public float ReactionTime = 0.25f;
    public float SafeDistance = 9f;
    public float SafeTime = 2f;
    public float LingerTime = 2f;

    public float FireRange = 7f;
    public float AimError = 4f;
    public float ClickRate = 4f;
    public float KiteNearDistance = 4f;
    public float KiteMoveTime = 0.6f;
    public int KiteShots = 2;

    public bool EightWayMovement = true;

    /// <summary>把参数写入遥测事件</summary>
    public JsonLine AddTo(JsonLine line)
    {
        return line
            .Add("persona", PersonaName)
            .Add("p_fight", FightWeight).Add("p_flee", FleeWeight).Add("p_kite", KiteWeight)
            .Add("route_weights", RouteWeights)
            .Add("adapt_routes", AdaptRoutes)
            .Add("decision_distance", DecisionDistance)
            .Add("reaction_time", ReactionTime)
            .Add("safe_distance", SafeDistance)
            .Add("safe_time", SafeTime)
            .Add("linger_time", LingerTime)
            .Add("fire_range", FireRange)
            .Add("aim_error", AimError)
            .Add("click_rate", ClickRate)
            .Add("kite_move_time", KiteMoveTime)
            .Add("kite_near_distance", KiteNearDistance)
            .Add("kite_shots", KiteShots)
            .Add("eight_way", EightWayMovement);
    }

    /// <summary>单行摘要（调试面板）</summary>
    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        string routes = "";
        for (int i = 0; i < RouteWeights.Length; i++)
            routes += (i > 0 ? " " : "") + (char)('A' + i) + " " + RouteWeights[i].ToString("0.00", inv);
        return string.Format(inv, "作战 {0:P0} 逃跑 {1:P0} 风筝 {2:P0} | 路线 {3}{4} | 决定距离 {5:0.0} 射程 {6:0.0} 误差 {7:0}°",
            FightWeight, FleeWeight, KiteWeight, routes, AdaptRoutes ? "（会适应）" : "",
            DecisionDistance, FireRange, AimError);
    }

    public static void Normalize(float[] weights)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = Mathf.Max(0f, weights[i]);
            total += weights[i];
        }
        if (total <= 0f)
            return;
        for (int i = 0; i < weights.Length; i++)
            weights[i] /= total;
    }
}
