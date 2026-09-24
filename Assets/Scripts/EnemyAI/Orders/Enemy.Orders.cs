using UnityEngine;

/// <summary>
/// Enemy 的命令扩展（partial）：让指挥官（IHordeCommander）可以指挥敌人移动、驻守伏击或追击。
/// 没有命令时（EnemyOrderType.None）所有钩子都不生效，敌人行为与原版完全一致。
/// Enemy.cs 中的调用点：DetectPlayer、DetectTargets、PathfindingUpdate、CheckIfStuck。
/// </summary>
public partial class Enemy
{
    private const float ORDER_ARRIVE_RADIUS = 0.5f; // 视为到达命令位置的距离

    private EnemyOrder currentOrder;

    /// <summary>命令完成时触发（目前只有 MoveTo 会自行完成）</summary>
    public System.Action<Enemy, EnemyOrder> OnOrderCompleted;

    public EnemyOrder CurrentOrder => currentOrder;

    /// <summary>
    /// 下达命令。立即生效：下一帧起目标选择按新命令进行
    /// </summary>
    public void SetOrder(EnemyOrder order)
    {
        if (isDead)
            return;

        currentOrder = order;
        VerboseLog.Log($"{gameObject.name} 收到命令: {order}");
    }

    public void ClearOrder()
    {
        SetOrder(EnemyOrder.None);
    }

    /// <summary>当前命令下的玩家检测半径</summary>
    private float EffectiveDetectionRadius()
    {
        switch (currentOrder.Type)
        {
            case EnemyOrderType.Chase:
                return float.PositiveInfinity;
            case EnemyOrderType.HoldAt:
                return currentOrder.EngageRadius > 0f ? currentOrder.EngageRadius : detectionRadius;
            default:
                return detectionRadius;
        }
    }

    /// <summary>
    /// 玩家不在范围内时由 DetectTargets 调用：有位置命令则以命令位置为目标（代替基地）。
    /// 返回 false 表示没有位置命令，由原有的基地逻辑接管。
    /// </summary>
    private bool UpdateOrderTarget()
    {
        if (!currentOrder.HasPosition)
            return false;

        // MoveTo 到达即完成，恢复自主行为（同一帧由基地逻辑接管）
        if (currentOrder.Type == EnemyOrderType.MoveTo &&
            Vector2.Distance(transform.position, currentOrder.Position) <= ORDER_ARRIVE_RADIUS)
        {
            var completed = currentOrder;
            currentOrder = EnemyOrder.None;
            OnOrderCompleted?.Invoke(this, completed);
            return false;
        }

        currentTarget = currentOrder.Position;
        hasTarget = true;
        targetingMainBase = false;
        isMainBaseInRange = false;
        return true;
    }

    /// <summary>
    /// 玩家离开范围时由 DetectPlayer 调用：有位置命令则返回命令位置，而不是转向基地
    /// </summary>
    private bool TryResumeOrder()
    {
        if (!currentOrder.HasPosition)
            return false;

        currentTarget = currentOrder.Position;
        hasTarget = true;
        targetingMainBase = false;
        isMainBaseInRange = false;
        return true;
    }

    /// <summary>寻路协程使用：当前命令的目的地</summary>
    private bool TryGetOrderDestination(out Vector2 destination)
    {
        destination = currentOrder.Position;
        return currentOrder.HasPosition;
    }

    /// <summary>正在前往命令位置（尚未到达）：用于卡住检测</summary>
    private bool IsTravelingToOrder
    {
        get
        {
            return currentOrder.HasPosition &&
                   Vector2.Distance(transform.position, currentOrder.Position) > ORDER_ARRIVE_RADIUS;
        }
    }

    private void OnDrawGizmos()
    {
        if (!currentOrder.HasPosition || isDead)
            return;

        // 命令可视化：黄色 = MoveTo，品红 = HoldAt（圆圈为交战半径）
        Gizmos.color = currentOrder.Type == EnemyOrderType.HoldAt ? Color.magenta : Color.yellow;
        Gizmos.DrawLine(transform.position, currentOrder.Position);
        Gizmos.DrawWireSphere(currentOrder.Position, ORDER_ARRIVE_RADIUS);
        if (currentOrder.Type == EnemyOrderType.HoldAt)
        {
            Gizmos.DrawWireSphere(currentOrder.Position, EffectiveDetectionRadius());
        }
    }
}
