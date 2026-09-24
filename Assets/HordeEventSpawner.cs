using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 极简版尸潮事件生成器：
/// - 监听 GameTimer.OnHordeEventTriggered
/// - 当某天绑定了 HordeEvent（例如 DayNum==2）时，直接按资产里的参数生成尸潮
/// - 仅依赖 HordeEvent 的以下字段：
///   totalEnemyCount, maxActiveEnemies, spawnInterval, spawnRadius, minSpawnDistance, enemyTypes
/// </summary>
public class HordeEventSpawner : MonoBehaviour
{
    [Header("引用")]
    public GameTimer gameTimer;      // 关联的GameTimer（从中接收事件）
    public Transform playerTransform; // 玩家位置（作为生成中心）

    [Header("行为")]
    public bool autoStart = true;    // 收到事件后是否自动开始

    // 运行时状态
    private HordeEvent currentHordeEvent;
    private bool isSpawning = false;
    private int spawnedCount = 0;
    private float lastSpawnTime = 0f;
    private readonly List<GameObject> activeEnemies = new List<GameObject>();

    // 事件（可选回调）
    public delegate void HordeEventStartedEvent(HordeEvent hordeEvent);
    public event HordeEventStartedEvent OnHordeEventStarted;

    public delegate void HordeEventCompletedEvent(HordeEvent hordeEvent);
    public event HordeEventCompletedEvent OnHordeEventCompleted;

    public delegate void EnemySpawnedEvent(GameObject enemy);
    public event EnemySpawnedEvent OnEnemySpawned;

    private void Start()
    {
        if (gameTimer == null)
            gameTimer = GetComponent<GameTimer>();

        if (gameTimer != null)
            gameTimer.OnHordeEventTriggered += HandleHordeEventTriggered;

        if (playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }
    }

    private void OnDestroy()
    {
        if (gameTimer != null)
            gameTimer.OnHordeEventTriggered -= HandleHordeEventTriggered;
    }

    private void Update()
    {
        if (!isSpawning || currentHordeEvent == null)
            return;

        // Log for debugging horde progress
        Debug.Log($"[HordeEventSpawner.Update] Spawning: {isSpawning}, Spawned: {spawnedCount}/{currentHordeEvent.totalEnemyCount}, Active: {activeEnemies.Count}, Time since last spawn: {Time.time - lastSpawnTime:F2}s");

        // 清理已被销毁的敌人
        activeEnemies.RemoveAll(e => e == null);

        // 生成间隔判定
        if (Time.time - lastSpawnTime >= currentHordeEvent.spawnInterval)
        {
            // 只有在未达到总数且场上未超最大活跃数时才继续生成
            if (spawnedCount < currentHordeEvent.totalEnemyCount && activeEnemies.Count < currentHordeEvent.maxActiveEnemies)
            {
                SpawnEnemy();
                lastSpawnTime = Time.time;
            }
        }

        // 完成判定：已生成到总数，且场上无存活敌人
        if (spawnedCount >= currentHordeEvent.totalEnemyCount && activeEnemies.Count == 0)
        {
            CompleteHordeEvent();
        }
    }

    /// <summary>
    /// GameTimer 通知今天的 HordeEvent 被触发
    /// </summary>
    private void HandleHordeEventTriggered(HordeEvent hordeEvent)
    {
        Debug.LogError($"[HordeEventSpawner.HandleHordeEventTriggered] 接收到事件: {(hordeEvent != null ? hordeEvent.hordeName : "无")}, autoStart: {autoStart}");
        Debug.LogError($"[HordeEventSpawner.HandleHordeEventTriggered] 事件详情 - 总敌人数: {(hordeEvent != null ? hordeEvent.totalEnemyCount.ToString() : "N/A")}, 最大活跃数: {(hordeEvent != null ? hordeEvent.maxActiveEnemies.ToString() : "N/A")}");
        
        if (!autoStart || hordeEvent == null)
        {
            Debug.LogError($"[HordeEventSpawner.HandleHordeEventTriggered] 不启动事件 - autoStart: {autoStart}, hordeEvent为null: {hordeEvent == null}");
            return;
        }

        StartHordeEvent(hordeEvent);
    }

    /// <summary>
    /// 开始一个 HordeEvent（按资产字段直接驱动生成）
    /// </summary>
    public void StartHordeEvent(HordeEvent hordeEvent)
    {
        if (hordeEvent == null) 
        {
            Debug.LogError("[HordeEventSpawner.StartHordeEvent] 尝试启动一个空的 HordeEvent");
            return;
        }

        Debug.LogError($"[HordeEventSpawner.StartHordeEvent] 正在启动事件: {hordeEvent.hordeName}. 总数: {hordeEvent.totalEnemyCount}, 间隔: {hordeEvent.spawnInterval}, 半径: {hordeEvent.spawnRadius}");
        Debug.LogError($"[HordeEventSpawner.StartHordeEvent] 玩家位置: {(playerTransform != null ? playerTransform.position.ToString() : "null")}, 最小生成距离: {hordeEvent.minSpawnDistance}");

        currentHordeEvent = hordeEvent;
        isSpawning = true;
        spawnedCount = 0;
        lastSpawnTime = 0f;
        activeEnemies.Clear();

        Debug.LogError($"[HordeEventSpawner.StartHordeEvent] 事件已启动，isSpawning: {isSpawning}, spawnedCount: {spawnedCount}");

        OnHordeEventStarted?.Invoke(hordeEvent);
    }

    private void CompleteHordeEvent()
    {
        isSpawning = false;
        var finishedEvent = currentHordeEvent;
        currentHordeEvent = null;
        OnHordeEventCompleted?.Invoke(finishedEvent);
    }

    private void SpawnEnemy()
    {
        if (currentHordeEvent == null || playerTransform == null)
            return;

        // 选择敌人类型（基于权重）
        GameObject enemyPrefab = currentHordeEvent.GetRandomEnemyType();
        if (enemyPrefab == null)
            return;

        // 计算生成位置：在玩家周围的环带内 [minSpawnDistance, spawnRadius]
        Vector3 spawnPos = CalculateSpawnPosition(currentHordeEvent);

        // 实例化
        GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        if (enemy != null)
        {
            activeEnemies.Add(enemy);
            spawnedCount++;
            OnEnemySpawned?.Invoke(enemy);
        }
    }

    private static Vector3 RandomPointOnRing(float minRadius, float maxRadius)
    {
        // 均匀角度，半径在[min,max]范围内；在XZ平面上围绕玩家生成
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Random.Range(minRadius, maxRadius);
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
    }

    private Vector3 CalculateSpawnPosition(HordeEvent hordeEvent)
    {
        if (playerTransform == null) return Vector3.zero;
        float min = Mathf.Max(0f, hordeEvent.minSpawnDistance);
        float max = Mathf.Max(min + 0.01f, hordeEvent.spawnRadius);
        Vector3 offset = RandomPointOnRing(min, max);
        return playerTransform.position + offset;
    }
}