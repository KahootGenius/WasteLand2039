using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 指挥官可读取的尸潮状态（由 HordeEventSpawner 维护）。
/// 所有指挥官看到的都是同一份信息。
/// </summary>
public class HordeContext
{
    public HordeContext(IReadOnlyList<GameObject> activeEnemies)
    {
        ActiveEnemies = activeEnemies;
    }

    /// <summary>当前尸潮配置；两波之间为 null</summary>
    public HordeEvent Wave { get; internal set; }
    public Transform Player { get; internal set; }
    public Transform MainBase { get; internal set; }

    /// <summary>本次运行中的波次序号（从 0 开始）</summary>
    public int WaveIndex { get; internal set; }
    public float WaveStartTime { get; internal set; }
    public float WaveTime => Time.time - WaveStartTime;

    public int SpawnedCount { get; internal set; }
    public int TotalEnemyCount => Wave != null ? Wave.totalEnemyCount : 0;

    /// <summary>本波存活的敌人（只读视图）</summary>
    public IReadOnlyList<GameObject> ActiveEnemies { get; }

    /// <summary>
    /// 原版生成规则：玩家周围 [minSpawnDistance, spawnRadius] 环带内的随机点（XY平面）。
    /// 消耗两次 Random 调用，与原版顺序一致。
    /// </summary>
    public Vector3 DefaultSpawnPosition()
    {
        if (Player == null || Wave == null)
            return Vector3.zero;

        float min = Mathf.Max(0f, Wave.minSpawnDistance);
        float max = Mathf.Max(min + 0.01f, Wave.spawnRadius);
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Random.Range(min, max);
        return Player.position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
    }
}
