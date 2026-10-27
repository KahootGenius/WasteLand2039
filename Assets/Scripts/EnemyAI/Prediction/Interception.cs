using UnityEngine;

/// <summary>
/// 截击点：追击者以固定速度直线前往、目标保持当前速度时，两者相遇的位置。
/// 僵尸（3 u/s）比奔跑的玩家（5 u/s）慢，朝玩家当前的位置追永远追不上；朝相遇点走，
/// 在玩家前方或侧面的僵尸就能截住他（Enemy.InterceptPlayer）。
/// 纯计算（只用 Vector2 / Mathf），供 Enemy 和逻辑测试（MLTraining/tools/logic-tests/InterceptionTests.cs）使用。
/// </summary>
public static class Interception
{
    /// <summary>默认最长提前量（秒）：没有相遇点时最多瞄准玩家 1.5 秒后的位置；相遇时间最多取它的两倍</summary>
    public const float DefaultMaxLead = 1.5f;

    /// <summary>
    /// 相遇时间：最小的 t &gt; 0，使 |offset + velocity·t| = speed·t（offset = 目标 − 追击者）；没有相遇时返回 -1
    /// </summary>
    public static float MeetingTime(Vector2 offset, Vector2 velocity, float speed)
    {
        float a = Vector2.Dot(velocity, velocity) - speed * speed;
        float b = 2f * Vector2.Dot(offset, velocity);
        float c = Vector2.Dot(offset, offset);
        if (Mathf.Abs(a) < 1e-4f)
            return b < 0f ? -c / b : -1f; // 两者一样快：只有目标迎面而来才会相遇

        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
            return -1f;
        float root = Mathf.Sqrt(discriminant);
        float t1 = (-b - root) / (2f * a);
        float t2 = (-b + root) / (2f * a);
        if (t1 > t2)
        {
            float swap = t1;
            t1 = t2;
            t2 = swap;
        }
        if (t1 > 0f)
            return t1;
        return t2 > 0f ? t2 : -1f;
    }

    /// <summary>
    /// 追击者应该前往的点：相遇时间不超过 2 × maxLead 时为相遇点；否则（追不上，或要很久才相遇）
    /// 为目标 min(maxLead, 距离 / 速度) 秒后的位置，至少朝玩家的去向切过去
    /// </summary>
    public static Vector2 MeetingPoint(Vector2 pursuer, float speed, Vector2 target, Vector2 targetVelocity,
        float maxLead = DefaultMaxLead)
    {
        Vector2 offset = target - pursuer;
        float lead = speed > 0f ? Mathf.Min(maxLead, offset.magnitude / speed) : 0f;
        float t = MeetingTime(offset, targetVelocity, speed);
        if (t > 0f && t <= 2f * maxLead)
            lead = t;
        return target + targetVelocity * lead;
    }
}

/// <summary>
/// 截击用的玩家运动估计：平滑后的速度，以及玩家已经连续移动了多久。
/// 只截击正在跑的玩家（Running：连续移动至少 minRunSeconds 秒）。走走停停的玩家（风筝：后退约 0.6 秒就停下射击；
/// 站着打的玩家）如果也按速度提前截，僵尸会冲过头、反而离玩家更远：2026-10-21 的试运行中 Kiter 受到的伤害少了 24%，
/// 僵尸在它 2 以内的时间从 17% 降到 12%。逃跑的玩家一跑就是几秒（Adaptive 机器人 89% 的移动持续 1 秒以上）。
/// 纯计算，供 Enemy（Enemy.Interception.cs）和逻辑测试使用。
/// </summary>
public class PlayerMotion
{
    /// <summary>速度的平滑时间（秒）</summary>
    public const float DefaultSmoothing = 0.25f;
    /// <summary>连续移动这么久才算在跑（秒）</summary>
    public const float DefaultMinRunSeconds = 1f;
    /// <summary>速度超过这个值（u/s）算在移动</summary>
    public const float MovingSpeed = 1f;
    /// <summary>两次观测相隔超过这么久（秒）就重新开始估计</summary>
    public const float StaleSeconds = 0.5f;

    private readonly float smoothing;
    private readonly float minRunSeconds;
    private Vector2 lastPosition;
    private float lastTime = -1f;

    public PlayerMotion(float smoothing = DefaultSmoothing, float minRunSeconds = DefaultMinRunSeconds)
    {
        this.smoothing = Mathf.Max(1e-4f, smoothing);
        this.minRunSeconds = Mathf.Max(0f, minRunSeconds);
    }

    /// <summary>平滑后的速度</summary>
    public Vector2 Velocity { get; private set; }
    /// <summary>已经连续移动了多久（秒）</summary>
    public float MovingSeconds { get; private set; }
    /// <summary>正在跑：连续移动至少 minRunSeconds 秒</summary>
    public bool Running => lastTime >= 0f && MovingSeconds >= minRunSeconds;

    public void Reset()
    {
        Velocity = Vector2.zero;
        MovingSeconds = 0f;
        lastTime = -1f;
    }

    /// <summary>
    /// 新的观测：时间、玩家位置，以及测得的速度（例如刚体速度；null 时用两次位置之差）
    /// </summary>
    public void Observe(float time, Vector2 position, Vector2? measuredVelocity = null)
    {
        bool fresh = lastTime >= 0f && time >= lastTime && time - lastTime <= StaleSeconds;
        float dt = fresh ? time - lastTime : 0f;
        Vector2 measured = measuredVelocity ?? (fresh && dt > 0f ? (position - lastPosition) / dt : Vector2.zero);

        Velocity = fresh ? Velocity + (measured - Velocity) * Mathf.Min(1f, dt / smoothing) : measured;
        MovingSeconds = measured.magnitude > MovingSpeed ? (fresh ? MovingSeconds + dt : 0f) : 0f;
        lastPosition = position;
        lastTime = time;
    }
}
