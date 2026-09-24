using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 物品系统演示脚本
/// 用于快速设置和测试物品生成与拾取系统
/// </summary>
public class ItemSystemDemo : MonoBehaviour
{
    [Header("演示设置")]
    [SerializeField] private bool autoSetupOnStart = true; // 启动时自动设置
    [SerializeField] private bool showDemoUI = true; // 显示演示UI
    
    [Header("组件引用")]
    [SerializeField] private GameObject playerPrefab; // 玩家预制体
    [SerializeField] private Transform spawnPoint; // 生成点
    
    [Header("系统组件")]
    [SerializeField] private ItemSpawner itemSpawner;
    [SerializeField] private PlayerItemCollector itemCollector;
    [SerializeField] private InventoryManager inventoryManager;
    
    // 私有变量
    private GameObject playerInstance;
    private bool systemsInitialized = false;
    
    private void Start()
    {
        if (autoSetupOnStart)
        {
            SetupDemo();
        }
    }
    
    /// <summary>
    /// 设置演示环境
    /// </summary>
    [ContextMenu("Setup Demo")]
    public void SetupDemo()
    {
        Debug.Log("Setting up Item System Demo...");
        
        // 查找或创建玩家
        SetupPlayer();
        
        // 设置物品生成器
        SetupItemSpawner();
        
        // 设置物品收集器
        SetupItemCollector();
        
        // 确保背包管理器存在
        SetupInventoryManager();
        
        systemsInitialized = true;
        Debug.Log("Item System Demo setup complete!");
    }
    
    /// <summary>
    /// 设置玩家
    /// </summary>
    private void SetupPlayer()
    {
        // 查找现有玩家
        GameObject existingPlayer = GameObject.FindGameObjectWithTag("Player");
        
        if (existingPlayer != null)
        {
            playerInstance = existingPlayer;
            Debug.Log("Found existing player");
        }
        else if (playerPrefab != null)
        {
            // 创建新玩家
            Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            playerInstance = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
            playerInstance.name = "Player";
            Debug.Log("Created new player instance");
        }
        else
        {
            Debug.LogWarning("No player found and no player prefab assigned!");
        }
    }
    
    /// <summary>
    /// 设置物品生成器
    /// </summary>
    private void SetupItemSpawner()
    {
        if (itemSpawner == null)
        {
            itemSpawner = FindObjectOfType<ItemSpawner>();
        }
        
        if (itemSpawner == null)
        {
            // 创建物品生成器
            GameObject spawnerObj = new GameObject("ItemSpawner");
            itemSpawner = spawnerObj.AddComponent<ItemSpawner>();
            Debug.Log("Created ItemSpawner");
        }
        
        // 确保生成器有玩家引用
        if (playerInstance != null)
        {
            // ItemSpawner会自动查找玩家，这里只是确保设置正确
            Debug.Log("ItemSpawner configured");
        }
    }
    
    /// <summary>
    /// 设置物品收集器
    /// </summary>
    private void SetupItemCollector()
    {
        if (playerInstance == null) return;
        
        itemCollector = playerInstance.GetComponent<PlayerItemCollector>();
        
        if (itemCollector == null)
        {
            itemCollector = playerInstance.AddComponent<PlayerItemCollector>();
            Debug.Log("Added PlayerItemCollector to player");
        }
        else
        {
            Debug.Log("PlayerItemCollector already exists on player");
        }
    }
    
    /// <summary>
    /// 设置背包管理器
    /// </summary>
    private void SetupInventoryManager()
    {
        if (inventoryManager == null)
        {
            inventoryManager = FindObjectOfType<InventoryManager>();
        }
        
        if (inventoryManager == null)
        {
            Debug.LogWarning("InventoryManager not found! Please ensure it exists in the scene.");
        }
        else
        {
            Debug.Log("InventoryManager found and configured");
        }
    }
    
    /// <summary>
    /// 开始物品生成
    /// </summary>
    [ContextMenu("Start Item Spawning")]
    public void StartItemSpawning()
    {
        if (itemSpawner != null)
        {
            itemSpawner.StartSpawning();
            Debug.Log("Item spawning started");
        }
        else
        {
            Debug.LogWarning("ItemSpawner not found!");
        }
    }
    
    /// <summary>
    /// 停止物品生成
    /// </summary>
    [ContextMenu("Stop Item Spawning")]
    public void StopItemSpawning()
    {
        if (itemSpawner != null)
        {
            itemSpawner.StopSpawning();
            Debug.Log("Item spawning stopped");
        }
    }
    
    /// <summary>
    /// 立即生成物品
    /// </summary>
    [ContextMenu("Spawn Item Now")]
    public void SpawnItemNow()
    {
        if (itemSpawner != null)
        {
            itemSpawner.SpawnItemNow();
            Debug.Log("Item spawned manually");
        }
    }
    
    /// <summary>
    /// 清理所有生成的物品
    /// </summary>
    [ContextMenu("Clear All Items")]
    public void ClearAllItems()
    {
        if (itemSpawner != null)
        {
            itemSpawner.ClearAllSpawnedItems();
            Debug.Log("All spawned items cleared");
        }
    }
    
    /// <summary>
    /// 切换自动拾取
    /// </summary>
    [ContextMenu("Toggle Auto Pickup")]
    public void ToggleAutoPickup()
    {
        if (itemCollector != null)
        {
            // 这里需要通过反射或公共方法来切换，因为autoPickup是私有的
            Debug.Log("Auto pickup toggled (implement in PlayerItemCollector)");
        }
    }
    
    private void OnGUI()
    {
        if (!showDemoUI || !systemsInitialized) return;
        
        // 演示控制面板
        GUILayout.BeginArea(new Rect(10, 10, 250, 200));
        GUILayout.BeginVertical(GUI.skin.box);
        
        GUILayout.Label("Item System Demo", GUI.skin.label);
        
        if (GUILayout.Button("Setup Demo"))
        {
            SetupDemo();
        }
        
        GUILayout.Space(5);
        
        if (GUILayout.Button("Start Spawning"))
        {
            StartItemSpawning();
        }
        
        if (GUILayout.Button("Stop Spawning"))
        {
            StopItemSpawning();
        }
        
        if (GUILayout.Button("Spawn Item Now"))
        {
            SpawnItemNow();
        }
        
        if (GUILayout.Button("Clear All Items"))
        {
            ClearAllItems();
        }
        
        GUILayout.Space(5);
        
        // 显示系统状态
        GUILayout.Label("System Status:", GUI.skin.label);
        GUILayout.Label($"Spawner: {(itemSpawner != null ? "✅" : "❌")}");
        GUILayout.Label($"Collector: {(itemCollector != null ? "✅" : "❌")}");
        GUILayout.Label($"Inventory: {(inventoryManager != null ? "✅" : "❌")}");
        GUILayout.Label($"Player: {(playerInstance != null ? "✅" : "❌")}");
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    private void OnDrawGizmosSelected()
    {
        // 绘制生成点
        if (spawnPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.5f);
            Gizmos.DrawIcon(spawnPoint.position, "sv_icon_dot3_pix16_gizmo", true);
        }
    }
}