using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>机器人当前在做什么</summary>
public enum BotMode
{
    /// <summary>在基地附近待命</summary>
    Calm,
    /// <summary>原地作战</summary>
    Fight,
    /// <summary>沿路线逃向避难点，不射击</summary>
    Flee,
    /// <summary>边退边打：停下射击，敌人逼近就背离敌人（偏向路线方向）退一小段，再停下射击</summary>
    Kite,
    /// <summary>逃脱后原地停留</summary>
    Linger,
    /// <summary>返回基地</summary>
    Return
}

/// <summary>逃跑途中被拦截的原因（BotBrain.LastAmbushCause）</summary>
public enum AmbushCause
{
    /// <summary>受到伤害</summary>
    Damage,
    /// <summary>路线前方近处出现敌人</summary>
    AheadOnRoute,
    /// <summary>到达避难点时附近已有敌人</summary>
    AtRefuge
}

/// <summary>机器人每一步看到的信息（与真人从屏幕上能得到的信息相当）</summary>
public struct BotObservation
{
    public float Time;
    public Vector2 Position;
    public Vector2 Velocity;
    public float Health;
    /// <summary>此刻按下开火能否射出（有弹、未换弹、冷却结束）</summary>
    public bool WeaponReady;
    public bool IsReloading;
    public int Ammo;
    public int MagazineSize;
    /// <summary>存活的移动敌人位置</summary>
    public IReadOnlyList<Vector2> Enemies;
}

/// <summary>机器人每一步的输入（交给 IPlayerInput）</summary>
public struct BotCommand
{
    /// <summary>移动方向（单位向量或零）</summary>
    public Vector2 Move;
    /// <summary>瞄准方向（单位向量）</summary>
    public Vector2 Aim;
    public bool Fire;
    public bool Reload;
}

/// <summary>一条逃跑路线：从基地附近到避难点</summary>
public class BotRoute
{
    public string Name;
    public Vector2 Refuge;
}

/// <summary>机器人对训练场的了解（世界坐标）</summary>
public class BotArenaLayout
{
    public Vector2 Base;
    /// <summary>待命位置（基地旁）</summary>
    public Vector2 Home;
    public float HomeRadius = 1.5f;
    /// <summary>离基地超过此距离时往回走（训练场边界）</summary>
    public float BoundsRadius = 38f;
    public readonly List<BotRoute> Routes = new List<BotRoute>();
}

/// <summary>一次交战决定（写入遥测，作为评估玩家画像的"真实标签"）</summary>
public struct BotDecision
{
    public float Time;
    /// <summary>Fight / Flee / Kite</summary>
    public BotMode Mode;
    /// <summary>路线序号；作战时为 -1</summary>
    public int Route;
    public string RouteName;
    public float NearestEnemyDistance;
    public Vector2 Position;
}

/// <summary>
/// 脚本机器人玩家的决策（纯逻辑，不依赖场景，可在 Unity 外测试）。
/// 每次交战前按性格参数选定方式（作战 / 逃跑 / 风筝）和路线；敌人靠近后经过反应时间执行；
/// 安全后停留一会再回基地待命。与真人一样：移动中不能开火，开火需要先停下。
/// </summary>
public class BotBrain
{
    private const float StationarySpeed = 0.1f;     // WeaponManager：速度 > 0.1 视为移动，不能开火
    private const float RefugeArriveRadius = 2f;
    private const float AvoidRadius = 3.5f;         // 逃跑途中绕开这个距离内的敌人
    private const float EvadeRadius = 6f;           // 到达避难点后躲开这个距离内的敌人
    private const float BoundsMargin = 3f;
    private const float AmbushGraceSeconds = 1f;    // 逃跑开始后这段时间内的情况不算被拦截（起跑时身边本来就有敌人）
    private const float AmbushDistance = 4f;        // 路线前方这个距离内出现敌人 = 被拦截
    private const float KiteRouteBias = 0.4f;       // 风筝后退方向中路线方向的权重（其余为背离敌人）
    private const float KiteEmergencyDistance = 2f; // 敌人贴身时不等射够发数就后退
    private const float IdleReloadCooldown = 3f;
    private const float FightChaseExtra = 6f;       // 作战时，射程外但在 safeDistance + 此值内的敌人会主动靠近

    public BotParams Params { get; }
    public BotArenaLayout Layout { get; }

    public BotMode Mode { get; private set; } = BotMode.Calm;
    /// <summary>下一次交战打算采用的方式（Fight / Flee / Kite）</summary>
    public BotMode Intent { get; private set; }
    /// <summary>当前逃跑 / 风筝路线（-1 = 无）</summary>
    public int Route { get; private set; } = -1;
    /// <summary>当前路线偏好（适应型性格会随被拦截而变化）</summary>
    public IReadOnlyList<float> RouteWeights => routeWeights;
    public float NearestEnemyDistance { get; private set; } = float.PositiveInfinity;
    public int Decisions { get; private set; }
    public int Ambushes { get; private set; }
    /// <summary>最近一次被拦截的原因（Ambushed 触发时有效）</summary>
    public AmbushCause LastAmbushCause { get; private set; }
    /// <summary>最近一次被拦截时触发它的敌人在本步观测 Enemies 中的下标；受到伤害时为 -1（不知道是谁）</summary>
    public int LastAmbushEnemyIndex { get; private set; } = -1;

    /// <summary>做出一次交战决定</summary>
    public event Action<BotDecision> Decided;
    /// <summary>逃跑路线上被拦截（参数：路线序号）</summary>
    public event Action<int> Ambushed;

    private readonly System.Random rng;
    private readonly float[] preferredRouteWeights;
    private readonly float[] routeWeights;

    private float pendingSince = -1f;   // 触发条件首次满足的时间（反应计时）
    private float safeSince = -1f;      // 最近敌人超出 safeDistance 的起始时间
    private float lingerUntil;
    private bool kiteRetreating;
    private bool kiteMoveStarted;
    private float kitePhaseEnd;
    private int kiteShotsSinceRetreat;
    private float nextClickTime;
    private float nextIdleReload;
    private float lastHealth = -1f;
    private float escapeStartTime;
    private bool escapeAmbushed;
    private Vector2 lastAim = Vector2.right;

    public BotBrain(BotParams parameters, BotArenaLayout layout, System.Random random)
    {
        Params = parameters ?? new BotParams();
        Layout = layout ?? new BotArenaLayout();
        rng = random ?? new System.Random();

        int count = Layout.Routes.Count;
        preferredRouteWeights = new float[count];
        for (int i = 0; i < count; i++)
            preferredRouteWeights[i] = i < Params.RouteWeights.Length ? Mathf.Max(0f, Params.RouteWeights[i]) : 0f;
        float total = 0f;
        foreach (float weight in preferredRouteWeights)
            total += weight;
        for (int i = 0; i < count; i++)
            preferredRouteWeights[i] = total > 0f ? preferredRouteWeights[i] / total : 1f / count;
        routeWeights = (float[])preferredRouteWeights.Clone();

        Intent = ChooseIntent();
    }

    /// <summary>新一波开始（玩家已回到待命位置）：回到待命状态。路线偏好（适应结果）保留</summary>
    public void ResetForWave()
    {
        Mode = BotMode.Calm;
        Route = -1;
        pendingSince = -1f;
        safeSince = -1f;
        kiteRetreating = false;
        lastHealth = -1f;
        NearestEnemyDistance = float.PositiveInfinity;
        Intent = ChooseIntent();
    }

    public BotCommand Step(BotObservation o)
    {
        float time = o.Time;
        bool damaged = lastHealth >= 0f && o.Health < lastHealth - 0.01f;
        lastHealth = o.Health;

        int nearest = FindNearest(o, out float nearestDistance);
        NearestEnemyDistance = nearestDistance;

        if (nearestDistance > Params.SafeDistance)
        {
            if (safeSince < 0f)
                safeSince = time;
        }
        else
        {
            safeSince = -1f;
        }
        bool safe = safeSince >= 0f && time - safeSince >= Params.SafeTime;

        UpdateMode(o, time, damaged, safe, nearestDistance);

        // ---------- 行动 ----------
        Vector2 aim = nearest >= 0 ? (o.Enemies[nearest] - o.Position).normalized : lastAim;
        if (aim.sqrMagnitude < 0.0001f)
            aim = lastAim;
        lastAim = aim;

        Vector2 move = Vector2.zero;
        bool wantShoot = false;
        bool inFireRange = nearest >= 0 && nearestDistance <= Params.FireRange;

        switch (Mode)
        {
            case BotMode.Calm:
                if (Vector2.Distance(o.Position, Layout.Home) > Layout.HomeRadius * 2f)
                    move = Steer(o, Layout.Home, AvoidRadius);
                break;

            case BotMode.Return:
                move = Steer(o, Layout.Home, AvoidRadius);
                break;

            case BotMode.Linger:
                break;

            case BotMode.Fight:
                if (inFireRange)
                    wantShoot = true;
                else if (nearest >= 0 && nearestDistance <= Params.SafeDistance + FightChaseExtra)
                    move = Steer(o, o.Enemies[nearest], 0f);
                break;

            case BotMode.Flee:
                move = EscapeDirection(o);
                break;

            case BotMode.Kite:
                // 像作战一样停下射击；射够 KiteShots 发后敌人仍逼近到 KiteNearDistance 内就后退一小段
                // （背离敌人，偏向路线），再停下射击。
                // 敌人离玩家超过 5 就不再追击，所以每段只退 2～4 个单位
                if (kiteRetreating)
                {
                    if (!kiteMoveStarted && o.Velocity.magnitude > StationarySpeed)
                    {
                        kiteMoveStarted = true; // 开火动画会先锁住移动，从真正开始移动时计时
                        kitePhaseEnd = time + Params.KiteMoveTime;
                    }
                    if (kiteMoveStarted && time >= kitePhaseEnd)
                        kiteRetreating = false;
                }
                else if (nearest >= 0 && nearestDistance <= Params.KiteNearDistance &&
                         (kiteShotsSinceRetreat >= Params.KiteShots || nearestDistance <= KiteEmergencyDistance))
                {
                    kiteRetreating = true;
                    kiteMoveStarted = false;
                    kiteShotsSinceRetreat = 0;
                }

                if (kiteRetreating)
                    move = KiteDirection(o);
                else if (inFireRange)
                    wantShoot = true;
                else if (nearest >= 0 && nearestDistance <= Params.SafeDistance + FightChaseExtra)
                    move = Steer(o, o.Enemies[nearest], 0f);
                break;
        }

        var command = new BotCommand();
        if (wantShoot)
        {
            move = Vector2.zero;
            if (o.Velocity.magnitude <= StationarySpeed && o.WeaponReady && time >= nextClickTime)
            {
                command.Fire = true;
                nextClickTime = time + 1f / Params.ClickRate;
                kiteShotsSinceRetreat++;
                aim = Rotate(aim, ((float)rng.NextDouble() * 2f - 1f) * Params.AimError);
            }
        }

        if (!o.IsReloading && o.MagazineSize > 0 && o.Ammo < o.MagazineSize / 2 &&
            nearestDistance > Params.SafeDistance && time >= nextIdleReload)
        {
            command.Reload = true;
            nextIdleReload = time + IdleReloadCooldown;
        }

        command.Move = Params.EightWayMovement ? QuantizeEightWay(move) : move;
        command.Aim = aim;
        return command;
    }

    /// <summary>当前状态的单行描述（调试面板）</summary>
    public string Describe()
    {
        string route = Route >= 0 ? " → " + RouteName(Route) : "";
        string weights = "";
        for (int i = 0; i < routeWeights.Length; i++)
            weights += (i > 0 ? " " : "") + RouteName(i) + " " + routeWeights[i].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        return $"{Mode}{route}   下次: {Intent}   决定 {Decisions} 次，被拦截 {Ambushes} 次   路线偏好 {weights}";
    }

    public string RouteName(int route)
    {
        if (route < 0 || route >= Layout.Routes.Count)
            return "-";
        string name = Layout.Routes[route].Name;
        return string.IsNullOrEmpty(name) ? ((char)('A' + route)).ToString() : name;
    }

    // ---------- 状态切换 ----------

    private void UpdateMode(BotObservation o, float time, bool damaged, bool safe, float nearestDistance)
    {
        switch (Mode)
        {
            case BotMode.Calm:
            case BotMode.Linger:
            case BotMode.Return:
                float trigger = Intent == BotMode.Fight
                    ? Mathf.Max(Params.FireRange, Params.DecisionDistance)
                    : Params.DecisionDistance;
                if (nearestDistance <= trigger)
                {
                    if (pendingSince < 0f)
                        pendingSince = time;
                    if (time - pendingSince >= Params.ReactionTime)
                    {
                        Commit(o, time, nearestDistance);
                        return;
                    }
                }
                else
                {
                    pendingSince = -1f;
                }

                if (Mode == BotMode.Linger && time >= lingerUntil)
                    Mode = BotMode.Return;
                if (Mode == BotMode.Return && Vector2.Distance(o.Position, Layout.Home) <= Layout.HomeRadius)
                    Mode = BotMode.Calm;
                break;

            case BotMode.Fight:
                if (safe)
                    Mode = BotMode.Return;
                break;

            case BotMode.Flee:
            case BotMode.Kite:
                if (!escapeAmbushed && IsAmbushed(o, time, damaged))
                    RegisterAmbush();
                if (safe)
                {
                    FinishEscape();
                    Mode = BotMode.Linger;
                    lingerUntil = time + Params.LingerTime;
                }
                break;
        }
    }

    private void Commit(BotObservation o, float time, float nearestDistance)
    {
        pendingSince = -1f;
        safeSince = -1f;
        Mode = Intent;

        if (Mode == BotMode.Flee || Mode == BotMode.Kite)
        {
            Route = ChooseRoute();
            escapeStartTime = time;
            escapeAmbushed = false;
            kiteRetreating = Mode == BotMode.Kite; // 风筝从后退开始（敌人已在决定距离内）
            kiteMoveStarted = false;
        }
        else
        {
            Route = -1;
        }

        Decisions++;
        Decided?.Invoke(new BotDecision
        {
            Time = time,
            Mode = Mode,
            Route = Route,
            RouteName = Route >= 0 ? RouteName(Route) : "",
            NearestEnemyDistance = nearestDistance,
            Position = o.Position
        });

        Intent = ChooseIntent();
    }

    /// <summary>
    /// 逃跑途中被拦截（起跑 AmbushGraceSeconds 秒之后）：受到伤害，或路线前方近处出现敌人，
    /// 或到达避难点时附近已有敌人
    /// </summary>
    private bool IsAmbushed(BotObservation o, float time, bool damaged)
    {
        if (time - escapeStartTime <= AmbushGraceSeconds)
            return false;
        if (damaged)
        {
            LastAmbushCause = AmbushCause.Damage;
            LastAmbushEnemyIndex = -1;
            return true;
        }
        if (Route < 0 || o.Enemies == null)
            return false;

        Vector2 refuge = Layout.Routes[Route].Refuge;
        Vector2 toRefuge = refuge - o.Position;
        bool arrived = toRefuge.magnitude <= RefugeArriveRadius;
        for (int i = 0; i < o.Enemies.Count; i++)
        {
            Vector2 toEnemy = o.Enemies[i] - o.Position;
            if (arrived ? Vector2.Distance(o.Enemies[i], refuge) <= Params.DecisionDistance
                        : toEnemy.magnitude <= AmbushDistance && Vector2.Dot(toEnemy, toRefuge) > 0f)
            {
                LastAmbushCause = arrived ? AmbushCause.AtRefuge : AmbushCause.AheadOnRoute;
                LastAmbushEnemyIndex = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>被拦截立即生效（即使本次逃跑以死亡告终，下次也会避开）</summary>
    private void RegisterAmbush()
    {
        escapeAmbushed = true;
        Ambushes++;
        if (Params.AdaptRoutes)
        {
            routeWeights[Route] *= Params.AmbushPenalty;
            NormalizeRouteWeights();
        }
        Ambushed?.Invoke(Route);
    }

    private void FinishEscape()
    {
        if (Route < 0)
            return;

        if (!escapeAmbushed && Params.AdaptRoutes)
        {
            for (int i = 0; i < routeWeights.Length; i++)
                routeWeights[i] += Params.AmbushRecovery * (preferredRouteWeights[i] - routeWeights[i]);
            NormalizeRouteWeights();
        }
    }

    private BotMode ChooseIntent()
    {
        float fight = Params.FightWeight;
        float flee = Params.FleeWeight;
        float kite = Params.KiteWeight; // 没有路线时逃跑 / 风筝改为背离敌人（见 EscapeDirection）
        float total = fight + flee + kite;
        if (total <= 0f)
            return BotMode.Fight;

        float roll = (float)rng.NextDouble() * total;
        if (roll < fight)
            return BotMode.Fight;
        if (roll < fight + flee)
            return BotMode.Flee;
        return BotMode.Kite;
    }

    private int ChooseRoute()
    {
        if (routeWeights.Length == 0)
            return -1;

        float roll = (float)rng.NextDouble();
        float cumulative = 0f;
        for (int i = 0; i < routeWeights.Length; i++)
        {
            cumulative += routeWeights[i];
            if (roll < cumulative)
                return i;
        }
        return routeWeights.Length - 1;
    }

    private void NormalizeRouteWeights()
    {
        float total = 0f;
        foreach (float weight in routeWeights)
            total += weight;
        for (int i = 0; i < routeWeights.Length; i++)
            routeWeights[i] = total > 0f ? routeWeights[i] / total : 1f / routeWeights.Length;
    }

    // ---------- 移动 ----------

    /// <summary>
    /// 逃跑方向：沿路线跑向避难点，绕开途中的敌人；前方被堵住或已到达时躲开附近的敌人
    /// </summary>
    private Vector2 EscapeDirection(BotObservation o)
    {
        if (Route < 0)
            return Away(o, EvadeRadius * 2f);

        Vector2 refuge = Layout.Routes[Route].Refuge;
        Vector2 toRefuge = refuge - o.Position;
        if (toRefuge.magnitude <= RefugeArriveRadius || IsBlockedAhead(o, toRefuge))
            return Away(o, EvadeRadius);
        return Steer(o, refuge, AvoidRadius);
    }

    /// <summary>风筝后退方向：主要背离射程内的敌人，并偏向路线方向</summary>
    private Vector2 KiteDirection(BotObservation o)
    {
        Vector2 away = Away(o, Params.FireRange + 1f);
        if (away.sqrMagnitude < 0.0001f || Route < 0)
            return EscapeDirection(o);

        Vector2 toRefuge = Layout.Routes[Route].Refuge - o.Position;
        Vector2 routeDirection = toRefuge.magnitude > RefugeArriveRadius ? toRefuge.normalized : Vector2.zero;
        Vector2 direction = away * (1f - KiteRouteBias) + routeDirection * KiteRouteBias + BoundsPush(o.Position);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : away;
    }

    /// <summary>前方（朝避难点方向）近处有敌人</summary>
    private static bool IsBlockedAhead(BotObservation o, Vector2 toRefuge)
    {
        if (o.Enemies == null)
            return false;
        Vector2 forward = toRefuge.normalized;
        for (int i = 0; i < o.Enemies.Count; i++)
        {
            Vector2 toEnemy = o.Enemies[i] - o.Position;
            float distance = toEnemy.magnitude;
            if (distance <= AvoidRadius + 1f && distance > 0.001f && Vector2.Dot(toEnemy / distance, forward) > 0.5f)
                return true;
        }
        return false;
    }

    /// <summary>朝目标移动，绕开 avoidRadius 内的敌人，不越出训练场边界</summary>
    private Vector2 Steer(BotObservation o, Vector2 target, float avoidRadius)
    {
        Vector2 toTarget = target - o.Position;
        float distance = toTarget.magnitude;
        if (distance < 0.3f)
            return Vector2.zero;
        Vector2 desired = toTarget / distance;

        if (avoidRadius > 0f && o.Enemies != null)
        {
            Vector2 avoid = Vector2.zero;
            for (int i = 0; i < o.Enemies.Count; i++)
            {
                Vector2 offset = o.Position - o.Enemies[i];
                float d = offset.magnitude;
                if (d < avoidRadius && d > 0.001f)
                    avoid += offset / d * ((avoidRadius - d) / avoidRadius);
            }

            Vector2 steered = desired + avoid * 1.5f;
            if (steered.magnitude < 0.3f)
            {
                // 敌人正挡在前方：从侧面绕过（选远离敌人的一侧）
                Vector2 side = new Vector2(-desired.y, desired.x);
                if (Vector2.Dot(side, avoid) < 0f)
                    side = -side;
                steered = desired * 0.3f + side;
            }
            desired = steered;
        }

        desired += BoundsPush(o.Position);
        return desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector2.zero;
    }

    /// <summary>背离 radius 内的敌人（没有则原地不动）</summary>
    private Vector2 Away(BotObservation o, float radius)
    {
        Vector2 away = Vector2.zero;
        if (o.Enemies != null)
        {
            for (int i = 0; i < o.Enemies.Count; i++)
            {
                Vector2 offset = o.Position - o.Enemies[i];
                float d = offset.magnitude;
                if (d < radius && d > 0.001f)
                    away += offset / d * ((radius - d) / radius);
            }
        }
        if (away.sqrMagnitude < 0.0001f)
            return Vector2.zero;

        away = away.normalized + BoundsPush(o.Position) * 2f;
        return away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.zero;
    }

    private Vector2 BoundsPush(Vector2 position)
    {
        Vector2 fromBase = position - Layout.Base;
        float r = fromBase.magnitude;
        float limit = Layout.BoundsRadius - BoundsMargin;
        if (r <= limit || r < 0.001f)
            return Vector2.zero;
        return -fromBase / r * Mathf.Clamp01((r - limit) / BoundsMargin) * 2f;
    }

    // ---------- 工具 ----------

    private static int FindNearest(BotObservation o, out float distance)
    {
        distance = float.PositiveInfinity;
        int best = -1;
        if (o.Enemies == null)
            return best;
        for (int i = 0; i < o.Enemies.Count; i++)
        {
            float d = Vector2.Distance(o.Position, o.Enemies[i]);
            if (d < distance)
            {
                distance = d;
                best = i;
            }
        }
        return best;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    /// <summary>吸附到最近的 8 个方向之一（WASD 能按出的方向）</summary>
    public static Vector2 QuantizeEightWay(Vector2 v)
    {
        if (v.sqrMagnitude < 0.0001f)
            return Vector2.zero;
        const float step = Mathf.PI / 4f;
        float angle = Mathf.Round(Mathf.Atan2(v.y, v.x) / step) * step;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
}
