using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 小队：一组共享同一命令的敌人。指挥官按小队下达命令，小队转发给所有存活成员；
/// 后加入的成员自动继承小队当前命令。成员死亡时自动移除。
/// </summary>
public class Squad
{
    public int Id { get; }
    public EnemyOrder CurrentOrder { get; private set; }

    private readonly List<Enemy> members = new List<Enemy>();
    public IReadOnlyList<Enemy> Members => members;
    public int AliveCount => members.Count;

    public Squad(int id)
    {
        Id = id;
    }

    public void Add(Enemy enemy)
    {
        if (enemy == null || enemy.IsDead || members.Contains(enemy))
            return;

        members.Add(enemy);
        enemy.OnDeath += HandleMemberDeath;
        enemy.SetOrder(CurrentOrder);
    }

    public void Remove(Enemy enemy)
    {
        if (enemy == null || !members.Remove(enemy))
            return;

        enemy.OnDeath -= HandleMemberDeath;
    }

    /// <summary>向全体存活成员下达命令</summary>
    public void Issue(EnemyOrder order)
    {
        CurrentOrder = order;
        PruneDestroyed();
        foreach (var member in members)
        {
            member.SetOrder(order);
        }
    }

    /// <summary>存活成员的中心位置；没有成员时返回 false</summary>
    public bool TryGetCentroid(out Vector2 centroid)
    {
        PruneDestroyed();
        centroid = Vector2.zero;
        if (members.Count == 0)
            return false;

        foreach (var member in members)
        {
            centroid += (Vector2)member.transform.position;
        }
        centroid /= members.Count;
        return true;
    }

    private void HandleMemberDeath(Enemy enemy)
    {
        Remove(enemy);
    }

    // 防御：成员可能未经 OnDeath 直接被销毁（例如 ForceDestroy / 场景卸载）
    private void PruneDestroyed()
    {
        members.RemoveAll(m => m == null);
    }

    public override string ToString()
    {
        return $"Squad {Id} [{members.Count}] {CurrentOrder}";
    }
}
