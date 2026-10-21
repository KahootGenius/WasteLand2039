using UnityEngine;

/// <summary>
/// Enemy 的截击扩展（partial）：InterceptPlayer 打开时，追击正在跑的玩家（PlayerMotion.Running：连续移动至少 1 秒）的敌人
/// 朝与玩家的相遇点走（Interception.MeetingPoint，按玩家平滑后的速度），而不是玩家当前的位置；
/// 玩家走走停停（风筝、站着打）或离玩家 attackRange + 1 以内时直接追玩家。
/// 关闭时（默认）ChasePoint 就是玩家的位置，行为与原版完全一致。
/// 由指挥官打开（PredictiveCommander 的 interceptChase / 命令行 -v2Intercept）。
/// Enemy.cs 中的调用点：DetectPlayer、PathfindingUpdate、JoinSwarmChase。
/// </summary>
public partial class Enemy
{
    private const float INTERCEPT_DIRECT_RANGE = 1f; // 离玩家 attackRange + 这么近时直接追玩家

    /// <summary>追击正在跑的玩家时朝玩家的去向截击（默认关闭）</summary>
    public bool InterceptPlayer { get; set; }

    private readonly PlayerMotion playerMotion = new PlayerMotion();
    private Transform trackedPlayer;
    private Rigidbody2D trackedPlayerBody;
    private int trackedPlayerFrame = -1;

    /// <summary>追击玩家时的目标点：截击正在跑的玩家时为相遇点，否则为玩家的位置。调用前 player 不能为 null</summary>
    private Vector2 ChasePoint()
    {
        Vector2 target = player.position;
        if (!InterceptPlayer)
            return target;

        TrackPlayerMotion(target);
        if (!playerMotion.Running || Vector2.Distance(transform.position, target) <= attackRange + INTERCEPT_DIRECT_RANGE)
            return target;
        return Interception.MeetingPoint(transform.position, moveSpeed, target, playerMotion.Velocity);
    }

    /// <summary>每帧最多更新一次玩家运动的估计：有刚体时用刚体速度，否则用位置差</summary>
    private void TrackPlayerMotion(Vector2 position)
    {
        if (Time.frameCount == trackedPlayerFrame)
            return;
        trackedPlayerFrame = Time.frameCount;

        if (trackedPlayer != player)
        {
            trackedPlayer = player;
            trackedPlayerBody = player.GetComponent<Rigidbody2D>();
            playerMotion.Reset();
        }
        playerMotion.Observe(Time.time, position, trackedPlayerBody != null ? trackedPlayerBody.velocity : (Vector2?)null);
    }
}
