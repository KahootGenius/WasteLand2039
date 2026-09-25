using UnityEngine;

/// <summary>
/// Enemy 的目标指定（partial）：训练场的同一场景中有多个玩家和基地，按标签全局查找会找错对象。
/// 生成器（HordeEventSpawner.bindEnemiesToSpawner）在实例化后、Start 之前调用 AssignTargets，
/// 指定本训练场的玩家和基地。未调用时行为与原版一致（Start 中按标签查找）。
/// </summary>
public partial class Enemy
{
    /// <summary>指定追击的玩家和进攻的基地。须在 Start 之前调用（即实例化后立即调用）</summary>
    public void AssignTargets(Transform playerTarget, Transform mainBaseTarget)
    {
        player = playerTarget;
        mainBase = mainBaseTarget;
    }

    /// <summary>该敌人追击的玩家（未找到时为 null）</summary>
    public Transform PlayerTarget => player;

    /// <summary>该敌人进攻的基地（未找到时为 null）</summary>
    public Transform MainBaseTarget => mainBase;
}
