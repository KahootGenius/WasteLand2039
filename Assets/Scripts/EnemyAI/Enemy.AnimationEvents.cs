/// <summary>
/// Enemy 的动画事件接收（partial）。001Z_Attack 动画带有 OnAttackHit / OnAttackComplete 事件，
/// 但 Enemy 没有对应方法，每次攻击都会在控制台报两条 "has no receiver" 错误（main 分支同样如此）。
/// 攻击的命中和结束由 Enemy.PerformAttack 协程处理，这两个事件本来就不起作用；
/// 这里提供空的接收方法，行为不变，只是避免训练时（高速、多个训练场）大量错误日志拖慢运行。
/// </summary>
public partial class Enemy
{
    /// <summary>动画事件：攻击命中帧（伤害由 PerformAttack 协程结算）</summary>
    private void OnAttackHit() { }

    /// <summary>动画事件：攻击动画结束（攻击状态由 PerformAttack 协程管理）</summary>
    private void OnAttackComplete() { }
}
