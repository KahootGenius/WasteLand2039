using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections;
using System.Collections.Generic;

public class EnemySpawner : MonoBehaviour
{
    [Header("生成设置")]
    [SerializeField] protected Transform player; // 玩家对象
    [SerializeField] public float spawnRadius = 15f; // 生成半径
    [SerializeField] private float minDistanceFromPlayer = 8f; // 距离玩家的最小距离
    [SerializeField] private int maxEnemyCount = 10; // 最大敌人数量
    [SerializeField] public float spawnInterval = 5f; // 生成间隔时间
    
    [Header("先决条件设置")]
    [SerializeField] private bool usePrerequisites = true; // 是否使用先决条件
    [SerializeField] private string[] requiredConditions = {"enemy_spawning_enabled"}; // 必需的条件
    [SerializeField] private bool waitForConditions = true; // 是否等待条件满足
    [SerializeField] private float conditionCheckInterval = 1f; // 条件检查间隔
    
    [Header("敌人预制体")]
    [SerializeField] private GameObject[] enemyPrefabs; // 敌人预制体数组
    [SerializeField] private float[] spawnWeights; // 生成权重
    
    [Header("生成限制")]
    [SerializeField] private LayerMask obstacleLayerMask = -1; // 障碍物图层
    [SerializeField] private float obstacleCheckRadius = 1f; // 障碍物检测半径
    [SerializeField] private int maxSpawnAttempts = 20; // 最大生成尝试次数
    
    [Header("Tilemap限制")]
    [SerializeField] private Tilemap spawnTilemap; // 生成限制Tilemap
    [SerializeField] private bool requireTilemapArea = true; // 是否必须在Tilemap区域内生成
    [SerializeField] private Vector3 tilemapCheckOffset = Vector3.zero; // Tilemap检测偏移
    
    [Header("调试设置")]
    [SerializeField] private bool enableDebugMode = false; // 启用调试模式
    [SerializeField] protected bool autoStart = true; // 自动开始生成
    
    // 受保护变量
    protected List<GameObject> spawnedEnemies = new List<GameObject>();
    private Coroutine spawnCoroutine;
    private Camera playerCamera;
    private Bounds tilemapBounds; // Tilemap边界
    
    // 属性
    public int CurrentEnemyCount => spawnedEnemies.Count;
    public bool IsSpawning => spawnCoroutine != null;
    
    private void Start()
    {
        // 自动查找玩家
        if (player == null)
        {
            FindPlayer();
        }
        
        // 获取玩家摄像机
        if (player != null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindObjectOfType<Camera>();
            }
        }
        
        // 初始化Tilemap
        InitializeTilemap();
        
        // 验证设置
        ValidateSettings();
        
        // 自动开始生成或等待条件
        if (player != null)
        {
            if (autoStart)
            {
                StartSpawning();
            }
            else if (usePrerequisites && waitForConditions)
            {
                // 延迟启动，确保PrerequisiteManager已经初始化
                StartCoroutine(DelayedStartWaitingForConditions());
            }
        }
    }
    
    private void Update()
    {
        // 清理已销毁的敌人引用
        CleanupDestroyedEnemies();
    }
    
    /// <summary>
    /// 查找玩家对象
    /// </summary>
    private void FindPlayer()
    {
        // 首先尝试通过标签查找
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            return;
        }
        
        // 尝试查找PlayerController组件
        PlayerController playerController = FindObjectOfType<PlayerController>();
        if (playerController != null)
        {
            player = playerController.transform;
            return;
        }
        
        Debug.LogWarning("[EnemySpawner] 未找到玩家对象，请手动设置或确保玩家有'Player'标签");
    }
    
    /// <summary>
    /// 初始化Tilemap
    /// </summary>
    private void InitializeTilemap()
    {
        if (spawnTilemap == null)
        {
            // 尝试自动查找Tilemap
            spawnTilemap = FindObjectOfType<Tilemap>();
            if (spawnTilemap != null && enableDebugMode)
            {
                Debug.Log($"[EnemySpawner] 自动找到Tilemap: {spawnTilemap.name}");
            }
        }
        
        if (spawnTilemap != null)
        {
            // 计算Tilemap边界
            tilemapBounds = spawnTilemap.localBounds;
            tilemapBounds.center = spawnTilemap.transform.position + tilemapBounds.center;
            
            if (enableDebugMode)
            {
                Debug.Log($"[EnemySpawner] Tilemap边界: {tilemapBounds}");
            }
        }
        else if (requireTilemapArea)
        {
            Debug.LogWarning("[EnemySpawner] 未找到Tilemap，但requireTilemapArea为true，生成可能受限");
        }
    }
    
    /// <summary>
    /// 验证设置
    /// </summary>
    private void ValidateSettings()
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
        {
            Debug.LogError("[EnemySpawner] 未设置敌人预制体！");
            return;
        }
        
        // 验证权重数组
        if (spawnWeights == null || spawnWeights.Length != enemyPrefabs.Length)
        {
            Debug.LogWarning("[EnemySpawner] 生成权重数组长度不匹配，使用默认权重");
            spawnWeights = new float[enemyPrefabs.Length];
            for (int i = 0; i < spawnWeights.Length; i++)
            {
                spawnWeights[i] = 1f;
            }
        }
        
        // 验证距离设置
        if (minDistanceFromPlayer >= spawnRadius)
        {
            Debug.LogWarning("[EnemySpawner] 最小距离大于等于生成半径，自动调整");
            minDistanceFromPlayer = spawnRadius * 0.6f;
        }
        
        // 验证Tilemap设置
        if (requireTilemapArea && spawnTilemap == null)
        {
            Debug.LogWarning("[EnemySpawner] 启用了Tilemap区域限制但未设置Tilemap");
        }
    }
    
    /// <summary>
    /// 开始生成敌人
    /// </summary>
    public void StartSpawning()
    {
        if (spawnCoroutine != null)
        {
            Debug.LogWarning("[EnemySpawner] 生成器已经在运行中");
            return;
        }
        
        if (player == null)
        {
            Debug.LogError("[EnemySpawner] 玩家对象未设置，无法开始生成");
            return;
        }
        
        // 检查先决条件
        if (usePrerequisites && !CheckPrerequisites())
        {
            if (waitForConditions)
            {
                Debug.Log("[EnemySpawner] 先决条件未满足，等待条件满足后开始生成");
                spawnCoroutine = StartCoroutine(WaitForConditionsCoroutine());
                return;
            }
            else
            {
                Debug.LogWarning("[EnemySpawner] 先决条件未满足，无法开始生成敌人");
                return;
            }
        }
        
        spawnCoroutine = StartCoroutine(SpawnCoroutine());
        Debug.Log("[EnemySpawner] 开始生成敌人");
    }
    
    /// <summary>
    /// 停止生成敌人
    /// </summary>
    public void StopSpawning()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
            Debug.Log("[EnemySpawner] 停止生成敌人");
        }
    }
    
    /// <summary>
    /// 延迟开始等待条件满足，确保PrerequisiteManager已初始化
    /// </summary>
    private IEnumerator DelayedStartWaitingForConditions()
    {
        // 等待PrerequisiteManager初始化
        while (PrerequisiteManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log("[EnemySpawner] PrerequisiteManager已初始化，开始监听先决条件变化");
        StartWaitingForConditions();
    }
    
    /// <summary>
    /// 开始等待条件满足（不依赖autoStart）
    /// </summary>
    public void StartWaitingForConditions()
    {
        if (spawnCoroutine != null)
        {
            Debug.LogWarning("[EnemySpawner] 生成器已经在运行中");
            return;
        }
        
        if (player == null)
        {
            Debug.LogError("[EnemySpawner] 玩家对象未设置，无法开始等待条件");
            return;
        }
        
        Debug.Log("[EnemySpawner] 开始等待先决条件满足");
        spawnCoroutine = StartCoroutine(WaitForConditionsCoroutine());
    }
    
    /// <summary>
    /// 等待条件满足的协程
    /// </summary>
    private IEnumerator WaitForConditionsCoroutine()
    {
        while (!CheckPrerequisites())
        {
            yield return new WaitForSeconds(conditionCheckInterval);
        }
        
        Debug.Log("[EnemySpawner] 先决条件已满足，开始生成敌人");
        spawnCoroutine = StartCoroutine(SpawnCoroutine());
    }
    
    /// <summary>
    /// 检查先决条件是否满足
    /// </summary>
    /// <returns>是否满足所有先决条件</returns>
    private bool CheckPrerequisites()
    {
        if (!usePrerequisites || requiredConditions == null || requiredConditions.Length == 0)
        {
            return true;
        }
        
        if (PrerequisiteManager.Instance == null)
        {
            Debug.LogWarning("[EnemySpawner] PrerequisiteManager未找到，条件检查失败");
            return false; // 修复：如果PrerequisiteManager未找到，应该返回false而不是true
        }
        
        bool result = PrerequisiteManager.Instance.CheckAllConditions(requiredConditions);
        
        if (enableDebugMode)
        {
            Debug.Log($"[EnemySpawner] 先决条件检查结果: {result}, 需要条件: [{string.Join(", ", requiredConditions)}]");
        }
        
        return result;
    }
    
    /// <summary>
    /// 生成协程
    /// </summary>
    private IEnumerator SpawnCoroutine()
    {
        while (true)
        {
            // 检查是否需要生成新敌人
            if (spawnedEnemies.Count < maxEnemyCount)
            {
                TrySpawnEnemy();
            }
            
            yield return new WaitForSeconds(spawnInterval);
        }
    }
    
    /// <summary>
    /// 尝试生成敌人
    /// </summary>
    private void TrySpawnEnemy()
    {
        if (player == null)
            return;
            
        Vector3 spawnPosition;
        bool foundValidPosition = false;
        
        // 尝试多次寻找合适的生成位置
        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            spawnPosition = GetRandomSpawnPosition();
            
            if (IsValidSpawnPosition(spawnPosition))
            {
                SpawnEnemyAtPosition(spawnPosition);
                foundValidPosition = true;
                break;
            }
        }
        
        if (!foundValidPosition && enableDebugMode)
        {
            Debug.LogWarning("[EnemySpawner] 无法找到合适的生成位置");
        }
    }
    
    /// <summary>
    /// 获取随机生成位置
    /// </summary>
    private Vector3 GetRandomSpawnPosition()
    {
        // 在环形区域内生成（避免在玩家附近生成）
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        float randomDistance = Random.Range(minDistanceFromPlayer, spawnRadius);
        
        Vector3 spawnPosition = player.position + new Vector3(randomDirection.x, randomDirection.y, 0) * randomDistance;
        return spawnPosition;
    }
    
    /// <summary>
    /// 检查生成位置是否有效
    /// </summary>
    protected bool IsValidSpawnPosition(Vector3 position)
    {
        // 检查是否在Tilemap区域内
        if (requireTilemapArea && spawnTilemap != null)
        {
            if (!IsPositionInTilemapArea(position))
            {
                if (enableDebugMode)
                {
                    Debug.Log($"[EnemySpawner] 位置 {position} 不在Tilemap区域内");
                }
                return false;
            }
        }
        
        // 检查是否在障碍物上
        Collider2D obstacle = Physics2D.OverlapCircle(position, obstacleCheckRadius, obstacleLayerMask);
        if (obstacle != null)
        {
            return false;
        }
        
        // 检查是否在玩家视野内（可选）
        if (playerCamera != null && IsPositionInCameraView(position))
        {
            return false;
        }
        
        // 检查与其他敌人的距离
        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy != null && Vector3.Distance(position, enemy.transform.position) < 2f)
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// 检查位置是否在Tilemap区域内
    /// </summary>
    private bool IsPositionInTilemapArea(Vector3 position)
    {
        if (spawnTilemap == null)
            return true; // 如果没有设置Tilemap，允许生成
            
        // 将世界坐标转换为Tilemap的本地坐标
        Vector3 localPosition = spawnTilemap.transform.InverseTransformPoint(position + tilemapCheckOffset);
        
        // 检查是否在Tilemap边界内
        if (!tilemapBounds.Contains(localPosition))
        {
            return false;
        }
        
        // 检查该位置是否有Tile（更精确的检查）
        Vector3Int cellPosition = spawnTilemap.WorldToCell(position + tilemapCheckOffset);
        return spawnTilemap.HasTile(cellPosition);
    }
    
    /// <summary>
    /// 检查位置是否在摄像机视野内
    /// </summary>
    private bool IsPositionInCameraView(Vector3 position)
    {
        if (playerCamera == null)
            return false;
            
        Vector3 viewportPoint = playerCamera.WorldToViewportPoint(position);
        return viewportPoint.x >= 0 && viewportPoint.x <= 1 && 
               viewportPoint.y >= 0 && viewportPoint.y <= 1 && 
               viewportPoint.z > 0;
    }
    
    /// <summary>
    /// 在指定位置生成敌人
    /// </summary>
    private void SpawnEnemyAtPosition(Vector3 position)
    {
        GameObject enemyPrefab = SelectRandomEnemyPrefab();
        if (enemyPrefab == null)
        {
            Debug.LogError("[EnemySpawner] 无法选择敌人预制体");
            return;
        }
        
        GameObject spawnedEnemy = Instantiate(enemyPrefab, position, Quaternion.identity);
        spawnedEnemies.Add(spawnedEnemy);
        
        // 设置敌人的父对象（可选）
        spawnedEnemy.transform.SetParent(transform);
        
        if (enableDebugMode)
        {
            Debug.Log($"[EnemySpawner] 在位置 {position} 生成了敌人 {enemyPrefab.name}");
        }
    }
    
    /// <summary>
    /// 根据权重选择随机敌人预制体
    /// </summary>
    private GameObject SelectRandomEnemyPrefab()
    {
        if (enemyPrefabs.Length == 0)
            return null;
            
        if (enemyPrefabs.Length == 1)
            return enemyPrefabs[0];
            
        // 计算总权重
        float totalWeight = 0f;
        for (int i = 0; i < spawnWeights.Length; i++)
        {
            totalWeight += spawnWeights[i];
        }
        
        if (totalWeight <= 0f)
        {
            return enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
        }
        
        // 根据权重选择
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            currentWeight += spawnWeights[i];
            if (randomValue <= currentWeight)
            {
                return enemyPrefabs[i];
            }
        }
        
        return enemyPrefabs[enemyPrefabs.Length - 1];
    }
    
    /// <summary>
    /// 清理已销毁的敌人引用
    /// </summary>
    private void CleanupDestroyedEnemies()
    {
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            if (spawnedEnemies[i] == null)
            {
                spawnedEnemies.RemoveAt(i);
            }
        }
    }
    
    /// <summary>
    /// 立即生成一个敌人
    /// </summary>
    public void SpawnEnemyNow()
    {
        if (spawnedEnemies.Count >= maxEnemyCount)
        {
            Debug.LogWarning("[EnemySpawner] 已达到最大敌人数量限制");
            return;
        }
        
        TrySpawnEnemy();
    }
    
    /// <summary>
    /// 清除所有生成的敌人
    /// </summary>
    public void ClearAllEnemies()
    {
        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy != null)
            {
                Destroy(enemy);
            }
        }
        
        spawnedEnemies.Clear();
        Debug.Log("[EnemySpawner] 清除了所有生成的敌人");
    }
    
    /// <summary>
    /// 设置生成参数
    /// </summary>
    public void SetSpawnParameters(float radius, int maxCount, float interval)
    {
        spawnRadius = radius;
        maxEnemyCount = maxCount;
        spawnInterval = interval;
        
        Debug.Log($"[EnemySpawner] 更新生成参数: 半径={radius}, 最大数量={maxCount}, 间隔={interval}");
    }
    
    /// <summary>
    /// 获取生成统计信息
    /// </summary>
    public string GetSpawnStats()
    {
        string prerequisiteStatus = usePrerequisites ? (CheckPrerequisites() ? "满足" : "未满足") : "未启用";
        return $"当前敌人数量: {spawnedEnemies.Count}/{maxEnemyCount}, 生成状态: {(IsSpawning ? "运行中" : "已停止")}, 先决条件: {prerequisiteStatus}";
    }
    
    /// <summary>
    /// 强制开始生成（忽略先决条件）
    /// </summary>
    public void ForceStartSpawning()
    {
        bool originalUsePrerequisites = usePrerequisites;
        usePrerequisites = false;
        StartSpawning();
        usePrerequisites = originalUsePrerequisites;
        
        Debug.Log("[EnemySpawner] 强制开始生成敌人（忽略先决条件）");
    }
    
    /// <summary>
    /// 设置先决条件
    /// </summary>
    /// <param name="conditions">条件数组</param>
    public void SetRequiredConditions(string[] conditions)
    {
        requiredConditions = conditions;
        Debug.Log($"[EnemySpawner] 设置先决条件: {string.Join(", ", conditions)}");
    }
    
    /// <summary>
    /// 启用/禁用先决条件检查
    /// </summary>
    /// <param name="enabled">是否启用</param>
    public void SetPrerequisitesEnabled(bool enabled)
    {
        usePrerequisites = enabled;
        Debug.Log($"[EnemySpawner] 先决条件检查: {(enabled ? "启用" : "禁用")}");
    }
    
    private void OnDrawGizmosSelected()
    {
        if (player == null)
            return;
            
        // 绘制生成范围
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(player.position, spawnRadius);
        
        // 绘制最小距离
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(player.position, minDistanceFromPlayer);
        
        // 绘制已生成的敌人位置
        Gizmos.color = Color.green;
        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy != null)
            {
                Gizmos.DrawWireCube(enemy.transform.position, Vector3.one * 0.5f);
            }
        }
    }
    
    private void OnDestroy()
    {
        // 停止生成协程
        StopSpawning();
    }
}