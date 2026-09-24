using UnityEngine;

/// <summary>
/// 基准指挥官（对照组）：完全复现原版行为。
/// 在玩家周围环带内生成敌人，不下达任何命令，敌人保持自主行为
/// （发现玩家则追击，否则进攻基地）。
/// HordeEventSpawner 未指定指挥官时会自动添加此组件。
/// </summary>
public class BaselineCommander : MonoBehaviour, IHordeCommander
{
    public string DisplayName => "Baseline";

    public void OnWaveStarted(HordeContext context) { }

    public Vector3 ChooseSpawnPosition(HordeContext context)
    {
        return context.DefaultSpawnPosition();
    }

    public void OnEnemySpawned(HordeContext context, Enemy enemy) { }

    public void Tick(HordeContext context) { }

    public void OnWaveCompleted(HordeContext context) { }
}
