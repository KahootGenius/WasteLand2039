using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 物品随机生成器
/// 在玩家周围定时生成随机物品
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    [Header("生成设置")]
    [SerializeField] private float spawnInterval = 10f; // 生成间隔（秒）
    [SerializeField] private int maxItemsInWorld = 20; // 世界中最大物品数量
    [SerializeField] private bool autoStart = true; // 是否自动开始生成
    
    [Header("生成范围")]
    [SerializeField] private float minSpawnDistance = 5f; // 最小生成距离
    [SerializeField] private float maxSpawnDistance = 15f; // 最大生成距离
    [SerializeField] private LayerMask obstacleLayer = 1; // 障碍物图层
    
    [Header("物品设置")]
    [SerializeField] private List<Item> availableItems = new List<Item>(); // 可生成的物品列表
    [SerializeField] private bool useAllItemsInProject = true; // 是否使用项目中的所有物品
    [SerializeField] private int minAmount = 1; // 最小生成数量
    [SerializeField] private int maxAmount = 3; // 最大生成数量
    
    [Header("稀有度权重")]
    [SerializeField] private float commonWeight = 50f; // 普通物品权重
    [SerializeField] private float rareWeight = 25f; // 稀有物品权重
    [SerializeField] private float epicWeight = 15f; // 史诗物品权重
    [SerializeField] private float legendaryWeight = 10f; // 传说物品权重
    
    [Header("调试设置")]
    [SerializeField] private bool showDebugInfo = true; // 显示调试信息
    [SerializeField] private bool showSpawnRange = true; // 显示生成范围
    
    // 私有变量
    private Transform playerTransform;
    private List<GameObject> spawnedItems = new List<GameObject>();
    private Coroutine spawnCoroutine;
    private bool isSpawning = false;
    
    // 公共属性用于外部访问
    public List<Item> AvailableItems => availableItems;
    public int MaxItemsInWorld => maxItemsInWorld;
    public float SpawnInterval => spawnInterval;
    public int CurrentItemCount => spawnedItems.Count;
    public bool IsSpawning => isSpawning;
    
    private void Start()
    {
        // 查找玩家
        FindPlayer();
        
        // 加载所有可用物品
        if (useAllItemsInProject)
        {
            LoadAllItems();
        }
        
        // 自动开始生成
        if (autoStart)
        {
            StartSpawning();
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"ItemSpawner initialized with {availableItems.Count} available items");
        }
    }
    
    private void FindPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
        else
        {
            Debug.LogWarning("ItemSpawner: Player not found! Make sure player has 'Player' tag.");
        }
    }
    
    private void LoadAllItems()
    {
        // 从Resources文件夹加载所有Item资源
        Item[] allItems = Resources.LoadAll<Item>("");
        
        foreach (Item item in allItems)
        {
            if (!availableItems.Contains(item))
            {
                availableItems.Add(item);
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"Loaded {allItems.Length} items from Resources");
        }
    }
    
    /// <summary>
    /// 开始生成物品
    /// </summary>
    public void StartSpawning()
    {
        if (!isSpawning && availableItems.Count > 0)
        {
            isSpawning = true;
            spawnCoroutine = StartCoroutine(SpawnRoutine());
            
            if (showDebugInfo)
            {
                Debug.Log("ItemSpawner: Started spawning items");
            }
        }
    }
    
    /// <summary>
    /// 停止生成物品
    /// </summary>
    public void StopSpawning()
    {
        if (isSpawning)
        {
            isSpawning = false;
            
            if (spawnCoroutine != null)
            {
                StopCoroutine(spawnCoroutine);
                spawnCoroutine = null;
            }
            
            if (showDebugInfo)
            {
                Debug.Log("ItemSpawner: Stopped spawning items");
            }
        }
    }
    
    /// <summary>
    /// 生成协程
    /// </summary>
    private IEnumerator SpawnRoutine()
    {
        while (isSpawning)
        {
            yield return new WaitForSeconds(spawnInterval);
            
            // 检查是否需要生成新物品
            if (ShouldSpawnItem())
            {
                SpawnRandomItem();
            }
        }
    }
    
    /// <summary>
    /// 检查是否应该生成物品
    /// </summary>
    private bool ShouldSpawnItem()
    {
        // 清理已销毁的物品引用
        CleanupDestroyedItems();
        
        // 检查世界中物品数量
        return spawnedItems.Count < maxItemsInWorld && playerTransform != null;
    }
    
    /// <summary>
    /// 清理已销毁的物品引用
    /// </summary>
    private void CleanupDestroyedItems()
    {
        spawnedItems.RemoveAll(item => item == null);
    }
    
    /// <summary>
    /// 生成随机物品
    /// </summary>
    public void SpawnRandomItem()
    {
        if (availableItems.Count == 0)
        {
            Debug.LogWarning("ItemSpawner: No available items to spawn!");
            return;
        }
        
        // 选择随机物品
        Item selectedItem = SelectRandomItem();
        if (selectedItem == null) return;
        
        // 选择生成位置
        Vector3 spawnPosition = GetRandomSpawnPosition();
        if (spawnPosition == Vector3.zero) return;
        
        // 确定生成数量
        int amount = Random.Range(minAmount, maxAmount + 1);
        if (selectedItem.isStackable)
        {
            amount = Mathf.Min(amount, selectedItem.maxStackSize);
        }
        else
        {
            amount = 1;
        }
        
        // 生成物品
        GameObject spawnedItem = ItemPickup.CreateItemPickup(selectedItem, spawnPosition, amount);
        
        if (spawnedItem != null)
        {
            spawnedItems.Add(spawnedItem);
            
            if (showDebugInfo)
            {
                Debug.Log($"Spawned {amount}x {selectedItem.itemName} at {spawnPosition}");
            }
        }
    }
    
    /// <summary>
    /// 根据稀有度权重选择随机物品
    /// </summary>
    private Item SelectRandomItem()
    {
        if (availableItems.Count == 0) return null;
        
        // 创建权重列表
        List<float> weights = new List<float>();
        
        foreach (Item item in availableItems)
        {
            float weight = GetItemWeight(item);
            weights.Add(weight);
        }
        
        // 根据权重选择物品
        int selectedIndex = GetWeightedRandomIndex(weights);
        return availableItems[selectedIndex];
    }
    
    /// <summary>
    /// 获取物品权重
    /// </summary>
    private float GetItemWeight(Item item)
    {
        // 根据物品等级确定稀有度
        if (item.itemLevel >= 50)
            return legendaryWeight;
        else if (item.itemLevel >= 30)
            return epicWeight;
        else if (item.itemLevel >= 20)
            return rareWeight;
        else
            return commonWeight;
    }
    
    /// <summary>
    /// 根据权重获取随机索引
    /// </summary>
    private int GetWeightedRandomIndex(List<float> weights)
    {
        float totalWeight = 0f;
        foreach (float weight in weights)
        {
            totalWeight += weight;
        }
        
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        
        for (int i = 0; i < weights.Count; i++)
        {
            currentWeight += weights[i];
            if (randomValue <= currentWeight)
            {
                return i;
            }
        }
        
        return weights.Count - 1; // fallback
    }
    
    /// <summary>
    /// 获取随机生成位置
    /// </summary>
    private Vector3 GetRandomSpawnPosition()
    {
        if (playerTransform == null) return Vector3.zero;
        
        int maxAttempts = 30; // 最大尝试次数
        
        for (int i = 0; i < maxAttempts; i++)
        {
            // 在玩家周围生成随机角度和距离
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(minSpawnDistance, maxSpawnDistance);
            
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * distance,
                Mathf.Sin(angle) * distance,
                0f
            );
            
            Vector3 candidatePosition = playerTransform.position + offset;
            
            // 检查位置是否有障碍物
            if (!Physics2D.OverlapCircle(candidatePosition, 0.5f, obstacleLayer))
            {
                return candidatePosition;
            }
        }
        
        Debug.LogWarning("ItemSpawner: Could not find valid spawn position after maximum attempts");
        return Vector3.zero;
    }
    
    /// <summary>
    /// 立即生成一个物品（用于测试）
    /// </summary>
    [ContextMenu("Spawn Item Now")]
    public void SpawnItemNow()
    {
        if (ShouldSpawnItem())
        {
            SpawnRandomItem();
        }
        else
        {
            Debug.Log("Cannot spawn item: conditions not met");
            DiagnoseSpawnConditions();
        }
    }
    
    /// <summary>
    /// 诊断生成条件问题
    /// </summary>
    [ContextMenu("Diagnose Spawn Conditions")]
    public void DiagnoseSpawnConditions()
    {
        Debug.Log("=== ItemSpawner Diagnosis ===");
        
        // 检查玩家
        if (playerTransform == null)
        {
            Debug.LogError("❌ Player not found! Make sure:");
            Debug.LogError("   1. Player GameObject exists in scene");
            Debug.LogError("   2. Player has 'Player' tag");
            return;
        }
        else
        {
            Debug.Log($"✅ Player found: {playerTransform.name}");
        }
        
        // 检查物品列表
        CleanupDestroyedItems();
        if (availableItems.Count == 0)
        {
            Debug.LogError("❌ No available items to spawn! Check:");
            Debug.LogError("   1. availableItems list is not empty");
            Debug.LogError("   2. useAllItemsInProject is enabled and Items exist in Resources folder");
            Debug.LogError("   3. Item ScriptableObjects are properly created");
            return;
        }
        else
        {
            Debug.Log($"✅ Available items: {availableItems.Count}");
            foreach (Item item in availableItems)
            {
                if (item != null)
                    Debug.Log($"   - {item.itemName} (Level: {item.itemLevel})");
                else
                    Debug.LogWarning("   - NULL item found in list!");
            }
        }
        
        // 检查世界中物品数量
        if (spawnedItems.Count >= maxItemsInWorld)
        {
            Debug.LogWarning($"⚠️ Maximum items reached: {spawnedItems.Count}/{maxItemsInWorld}");
            Debug.LogWarning("   Consider increasing maxItemsInWorld or clearing existing items");
            return;
        }
        else
        {
            Debug.Log($"✅ Items in world: {spawnedItems.Count}/{maxItemsInWorld}");
        }
        
        // 检查生成位置
        Vector3 testPosition = GetRandomSpawnPosition();
        if (testPosition == Vector3.zero)
        {
            Debug.LogWarning("⚠️ Cannot find valid spawn position! Check:");
            Debug.LogWarning("   1. minSpawnDistance and maxSpawnDistance settings");
            Debug.LogWarning("   2. obstacleLayer mask settings");
            Debug.LogWarning("   3. Too many obstacles around player");
            Debug.LogWarning($"   Current settings: min={minSpawnDistance}, max={maxSpawnDistance}");
        }
        else
        {
            Debug.Log($"✅ Valid spawn position found: {testPosition}");
        }
        
        Debug.Log("=== End Diagnosis ===");
    }
    
    /// <summary>
    /// 清理所有生成的物品
    /// </summary>
    [ContextMenu("Clear All Spawned Items")]
    public void ClearAllSpawnedItems()
    {
        foreach (GameObject item in spawnedItems)
        {
            if (item != null)
            {
                DestroyImmediate(item);
            }
        }
        
        spawnedItems.Clear();
        
        if (showDebugInfo)
        {
            Debug.Log("Cleared all spawned items");
        }
    }
    
    /// <summary>
    /// 添加物品到可生成列表
    /// </summary>
    public void AddAvailableItem(Item item)
    {
        if (item != null && !availableItems.Contains(item))
        {
            availableItems.Add(item);
        }
    }
    
    /// <summary>
    /// 移除物品从可生成列表
    /// </summary>
    public void RemoveAvailableItem(Item item)
    {
        availableItems.Remove(item);
    }
    
    private void OnDrawGizmosSelected()
    {
        if (!showSpawnRange || playerTransform == null) return;
        
        // 绘制生成范围
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(playerTransform.position, minSpawnDistance);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(playerTransform.position, maxSpawnDistance);
    }
    
    private void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.BeginVertical(GUI.skin.box);
        
        GUILayout.Label("Item Spawner Debug", GUI.skin.label);
        GUILayout.Label($"Spawning: {(isSpawning ? "✅" : "❌")}");
        GUILayout.Label($"Items in world: {spawnedItems.Count}/{maxItemsInWorld}");
        GUILayout.Label($"Available items: {availableItems.Count}");
        
        if (GUILayout.Button(isSpawning ? "Stop Spawning" : "Start Spawning"))
        {
            if (isSpawning)
                StopSpawning();
            else
                StartSpawning();
        }
        
        if (GUILayout.Button("Spawn Item Now"))
        {
            SpawnItemNow();
        }
        
        if (GUILayout.Button("Clear All Items"))
        {
        ClearAllSpawnedItems();
        }
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}