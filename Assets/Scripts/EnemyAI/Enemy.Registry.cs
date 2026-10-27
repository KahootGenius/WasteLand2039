using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy 的全局登记（partial）：场景中所有启用的敌人，以及任意敌人死亡的全局事件。
/// 供 PlayerBehaviourMonitor 等系统使用，避免每帧 FindObjectsOfType。
/// </summary>
public partial class Enemy
{
    private static readonly List<Enemy> activeEnemies = new List<Enemy>();

    /// <summary>当前启用的敌人（包含正在播放死亡动画的，使用时请检查 IsDead）</summary>
    public static IReadOnlyList<Enemy> AllActive => activeEnemies;

    /// <summary>任意敌人死亡</summary>
    public static event System.Action<Enemy> AnyDied;

    private void OnEnable()
    {
        if (!activeEnemies.Contains(this))
            activeEnemies.Add(this);
        OnDeath += RaiseAnyDied;
    }

    private void OnDisable()
    {
        activeEnemies.Remove(this);
        OnDeath -= RaiseAnyDied;
    }

    private static void RaiseAnyDied(Enemy enemy)
    {
        AnyDied?.Invoke(enemy);
    }

    // 关闭"域重载"（Enter Play Mode Options）时静态数据不会自动清空
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeEnemies.Clear();
        AnyDied = null;
    }
}
