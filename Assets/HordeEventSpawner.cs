using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 极简版尸潮事件生成器：
/// - 监听 GameTimer.OnHordeEventTriggered
/// - 当某天绑定了 HordeEvent（例如 DayNum==2）时，直接按资产里的参数生成尸潮
/// - 仅依赖 HordeEvent 的以下字段：
///   totalEnemyCount, maxActiveEnemies, spawnInterval, spawnRadius, minSpawnDistance, enemyTypes
/// - 生成位置和敌人命令由指挥官（IHordeCommander）决定；默认 BaselineCommander = 原版行为
/// </summary>
public class HordeEventSpawner : MonoBehaviour
{
    [Header("引用")]
    public GameTimer gameTimer;      // 关联的GameTimer（从中接收事件）
    public Transform playerTransform; // 玩家位置（作为生成中心）
    [Tooltip("主基地。为空时按 MainBase 标签 / 组件查找（原版行为）")]
    public Transform mainBaseTransform;

    [Header("行为")]
    public bool autoStart = true;    // 收到事件后是否自动开始

    [Header("测试 / 训练")]
    [Tooltip("尸潮结束后自动重新开始同一尸潮（用于测试、数据采集和ML训练）。默认关闭，不影响正常游戏")]
    public bool endlessWaves = false;
    [Tooltip("无尽模式下两波之间的间隔（秒，受 Time.timeScale 影响）")]
    public float endlessWaveDelay = 3f;
    [Tooltip("把本生成器的玩家和基地直接指定给生成的敌人，代替敌人按标签全局查找。" +
             "同一场景有多个玩家 / 基地（ML 训练场）时必须开启。默认关闭，不影响正常游戏")]
    public bool bindEnemiesToSpawner = false;

    [Header("指挥官（敌人AI）")]
    [Tooltip("实现 IHordeCommander 的组件。为空时自动使用本物体上的指挥官组件；都没有则添加 BaselineCommander（原版行为）")]
    [SerializeField] private MonoBehaviour commanderComponent;

    private IHordeCommander commander;
    private HordeContext context;

    // 玩家模型（由 PlayerBehaviourMonitor 通过 AttachPlayerModel 提供）
    private IZoneMap zones;
    private PlayerProfile profile;
    private EngagementTracker engagement;

    public IHordeCommander Commander => commander;
    public HordeContext Context => context;

    // 运行时状态
    private HordeEvent currentHordeEvent;
    private bool isSpawning = false;
    private int spawnedCount = 0;
    private float lastSpawnTime = 0f;
    private readonly List<GameObject> activeEnemies = new List<GameObject>();
    private int completedWaves = 0;

    /// <summary>已结束的尸潮波数（含被中止的；无尽模式下持续累加）</summary>
    public int CompletedWaves => completedWaves;

    /// <summary>当前是否有尸潮在进行（生成中或还有存活敌人）</summary>
    public bool IsWaveActive => isSpawning;

    // 事件（可选回调）
    public delegate void HordeEventStartedEvent(HordeEvent hordeEvent);
    public event HordeEventStartedEvent OnHordeEventStarted;

    public delegate void HordeEventCompletedEvent(HordeEvent hordeEvent);
    public event HordeEventCompletedEvent OnHordeEventCompleted;

    /// <summary>尸潮被 AbortHordeEvent 中止（敌人被直接移除，而不是被消灭）</summary>
    public event HordeEventCompletedEvent OnHordeEventAborted;

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

        EnsureCommander();
    }

    private void OnValidate()
    {
        // 拖入物体时 Unity 会选中其第一个 MonoBehaviour，这里改为该物体上的指挥官组件
        if (commanderComponent != null && !(commanderComponent is IHordeCommander))
        {
            var found = commanderComponent.GetComponent<IHordeCommander>() as MonoBehaviour;
            if (found == null)
                Debug.LogWarning($"[HordeEventSpawner] {commanderComponent.name} 上没有实现 IHordeCommander 的组件", this);
            commanderComponent = found;
        }
    }

    private void EnsureCommander()
    {
        if (commander != null)
            return;

        context = new HordeContext(activeEnemies)
        {
            Player = playerTransform,
            MainBase = mainBaseTransform != null ? mainBaseTransform : FindMainBase(),
            Zones = zones,
            Profile = profile,
            Engagement = engagement
        };

        if (commanderComponent is IHordeCommander assigned)
        {
            commander = assigned;
        }
        else
        {
            // 不用 ??：Unity 对象的空值判断需要走 UnityEngine.Object 的 == 重载
            var onThisObject = GetComponent<IHordeCommander>();
            commander = (onThisObject as Object) != null
                ? onThisObject
                : gameObject.AddComponent<BaselineCommander>();
        }

        VerboseLog.Log($"[HordeEventSpawner] 指挥官: {commander.DisplayName}");
    }

    /// <summary>
    /// 接入玩家模型，使指挥官可以通过 HordeContext 读取分区、画像和交战状态
    /// </summary>
    public void AttachPlayerModel(IZoneMap zoneMap, PlayerProfile playerProfile, EngagementTracker tracker)
    {
        zones = zoneMap;
        profile = playerProfile;
        engagement = tracker;

        if (context != null)
        {
            context.Zones = zoneMap;
            context.Profile = playerProfile;
            context.Engagement = tracker;
        }
    }

    private static Transform FindMainBase()
    {
        // 与 Enemy.FindMainBase 相同的查找顺序
        GameObject baseObj = GameObject.FindGameObjectWithTag("MainBase");
        if (baseObj != null)
            return baseObj.transform;

        MainBase mainBaseComponent = FindObjectOfType<MainBase>();
        return mainBaseComponent != null ? mainBaseComponent.transform : null;
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
        VerboseLog.Log($"[HordeEventSpawner.Update] Spawning: {isSpawning}, Spawned: {spawnedCount}/{currentHordeEvent.totalEnemyCount}, Active: {activeEnemies.Count}, Time since last spawn: {Time.time - lastSpawnTime:F2}s");

        // 清理已被销毁的敌人
        activeEnemies.RemoveAll(e => e == null);

        // 指挥官决策（频率由指挥官自行控制）
        commander.Tick(context);

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
        VerboseLog.Log($"[HordeEventSpawner.HandleHordeEventTriggered] 接收到事件: {(hordeEvent != null ? hordeEvent.hordeName : "无")}, autoStart: {autoStart}");
        VerboseLog.Log($"[HordeEventSpawner.HandleHordeEventTriggered] 事件详情 - 总敌人数: {(hordeEvent != null ? hordeEvent.totalEnemyCount.ToString() : "N/A")}, 最大活跃数: {(hordeEvent != null ? hordeEvent.maxActiveEnemies.ToString() : "N/A")}");
        
        if (!autoStart || hordeEvent == null)
        {
            VerboseLog.Log($"[HordeEventSpawner.HandleHordeEventTriggered] 不启动事件 - autoStart: {autoStart}, hordeEvent为null: {hordeEvent == null}");
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

        VerboseLog.Log($"[HordeEventSpawner.StartHordeEvent] 正在启动事件: {hordeEvent.hordeName}. 总数: {hordeEvent.totalEnemyCount}, 间隔: {hordeEvent.spawnInterval}, 半径: {hordeEvent.spawnRadius}");
        VerboseLog.Log($"[HordeEventSpawner.StartHordeEvent] 玩家位置: {(playerTransform != null ? playerTransform.position.ToString() : "null")}, 最小生成距离: {hordeEvent.minSpawnDistance}");

        currentHordeEvent = hordeEvent;
        isSpawning = true;
        spawnedCount = 0;
        lastSpawnTime = 0f;
        activeEnemies.Clear();

        EnsureCommander();
        context.Wave = hordeEvent;
        context.Player = playerTransform;
        context.WaveIndex = completedWaves;
        context.WaveStartTime = Time.time;
        context.SpawnedCount = 0;
        commander.OnWaveStarted(context);

        VerboseLog.Log($"[HordeEventSpawner.StartHordeEvent] 事件已启动，isSpawning: {isSpawning}, spawnedCount: {spawnedCount}, 指挥官: {commander.DisplayName}");

        OnHordeEventStarted?.Invoke(hordeEvent);
    }

    private void CompleteHordeEvent()
    {
        isSpawning = false;
        var finishedEvent = currentHordeEvent;
        currentHordeEvent = null;
        completedWaves++;
        commander.OnWaveCompleted(context);
        context.Wave = null;
        OnHordeEventCompleted?.Invoke(finishedEvent);

        // 无尽模式：延迟后重新开始同一尸潮
        if (endlessWaves && finishedEvent != null)
        {
            StartCoroutine(RestartWaveAfterDelay(finishedEvent));
        }
    }

    /// <summary>
    /// 立即结束当前尸潮并移除场上敌人（ML 训练场的回合超时 / 玩家死亡重置用）。
    /// 与正常结束的区别：触发 OnHordeEventAborted 而不是 OnHordeEventCompleted，且不会触发无尽模式的自动重开
    /// </summary>
    public void AbortHordeEvent()
    {
        if (!isSpawning)
            return;

        foreach (var enemy in activeEnemies)
        {
            if (enemy != null)
                Destroy(enemy);
        }
        activeEnemies.Clear();

        isSpawning = false;
        var abortedEvent = currentHordeEvent;
        currentHordeEvent = null;
        completedWaves++;
        commander.OnWaveCompleted(context);
        context.Wave = null;
        OnHordeEventAborted?.Invoke(abortedEvent);
    }

    private IEnumerator RestartWaveAfterDelay(HordeEvent hordeEvent)
    {
        yield return new WaitForSeconds(endlessWaveDelay);

        // 期间可能已由其他逻辑启动了新尸潮，或关闭了无尽模式
        if (endlessWaves && !isSpawning)
        {
            VerboseLog.Log($"[HordeEventSpawner] 无尽模式：开始第 {completedWaves + 1} 波");
            StartHordeEvent(hordeEvent);
        }
    }

    private void SpawnEnemy()
    {
        if (currentHordeEvent == null || playerTransform == null)
            return;

        // 选择敌人类型（基于权重）
        GameObject enemyPrefab = currentHordeEvent.GetRandomEnemyType();
        if (enemyPrefab == null)
            return;

        // 生成位置由指挥官决定（BaselineCommander = 原版规则：玩家周围 [minSpawnDistance, spawnRadius] 环带）
        Vector3 spawnPos = commander.ChooseSpawnPosition(context);

        // 实例化
        GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        if (enemy != null)
        {
            activeEnemies.Add(enemy);
            spawnedCount++;
            context.SpawnedCount = spawnedCount;

            var enemyComponent = enemy.GetComponent<Enemy>();
            if (enemyComponent != null)
            {
                if (bindEnemiesToSpawner)
                    enemyComponent.AssignTargets(playerTransform, context.MainBase);
                commander.OnEnemySpawned(context, enemyComponent);
            }

            OnEnemySpawned?.Invoke(enemy);
        }
    }
}