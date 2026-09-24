using UnityEngine;

/// <summary>
/// 快速物品测试脚本
/// 用于验证物品生成和拾取系统是否正常工作
/// </summary>
public class QuickItemTest : MonoBehaviour
{
    [Header("测试设置")]
    [SerializeField] private KeyCode testSpawnKey = KeyCode.T;
    [SerializeField] private KeyCode diagnosticKey = KeyCode.Y;
    
    private ItemSpawner itemSpawner;
    private PlayerItemCollector itemCollector;
    private InventoryManager inventoryManager;
    
    private void Start()
    {
        // 获取组件引用
        itemSpawner = FindObjectOfType<ItemSpawner>();
        itemCollector = FindObjectOfType<PlayerItemCollector>();
        inventoryManager = InventoryManager.Instance;
        
        Debug.Log("=== 快速物品测试初始化 ===");
        Debug.Log($"ItemSpawner找到: {itemSpawner != null}");
        Debug.Log($"PlayerItemCollector找到: {itemCollector != null}");
        Debug.Log($"InventoryManager找到: {inventoryManager != null}");
        
        if (itemCollector != null)
        {
            GameObject player = itemCollector.gameObject;
            Debug.Log($"玩家对象: {player.name}, 标签: {player.tag}, 层级: {player.layer}");
        }
    }
    
    private void Update()
    {
        // T键生成测试物品
        if (Input.GetKeyDown(testSpawnKey))
        {
            TestSpawnItem();
        }
        
        // Y键运行诊断
        if (Input.GetKeyDown(diagnosticKey))
        {
            RunDiagnostic();
        }
    }
    
    private void TestSpawnItem()
    {
        Debug.Log("=== 测试生成物品 ===");
        
        if (itemSpawner == null)
        {
            Debug.LogError("ItemSpawner未找到！");
            return;
        }
        
        // 获取玩家位置
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogError("玩家未找到！");
            return;
        }
        
        Vector3 spawnPos = player.transform.position + Vector3.right * 2f;
        Debug.Log($"在位置 {spawnPos} 生成物品");
        
        // 调用生成方法
        itemSpawner.SpawnRandomItem();
    }
    
    private void RunDiagnostic()
    {
        Debug.Log("=== 系统诊断 ===");
        
        // 检查场景中的物品
        ItemPickup[] items = FindObjectsOfType<ItemPickup>();
        Debug.Log($"场景中物品数量: {items.Length}");
        
        for (int i = 0; i < items.Length; i++)
        {
            ItemPickup item = items[i];
            Debug.Log($"物品 {i+1}: {item.name}");
            Debug.Log($"  - 位置: {item.transform.position}");
            Debug.Log($"  - 层级: {item.gameObject.layer}");
            Debug.Log($"  - 激活状态: {item.gameObject.activeInHierarchy}");
            Debug.Log($"  - 物品数据: {(item.item != null ? item.item.itemName : "NULL")}");
            
            // 检查组件
            SpriteRenderer sr = item.GetComponent<SpriteRenderer>();
            Collider2D col = item.GetComponent<Collider2D>();
            Debug.Log($"  - SpriteRenderer: {sr != null}, 精灵: {(sr?.sprite != null ? sr.sprite.name : "NULL")}");
            Debug.Log($"  - Collider2D: {col != null}, 大小: {(col as BoxCollider2D)?.size}");
        }
        
        // 检查玩家收集器
        if (itemCollector != null)
        {
            GameObject player = itemCollector.gameObject;
            Debug.Log($"玩家位置: {player.transform.position}");
            
            // 手动检测附近物品
            Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(player.transform.position, 5f);
            Debug.Log($"玩家周围5米内碰撞器数量: {nearbyColliders.Length}");
            
            foreach (var col in nearbyColliders)
            {
                ItemPickup pickup = col.GetComponent<ItemPickup>();
                if (pickup != null)
                {
                    float distance = Vector3.Distance(player.transform.position, pickup.transform.position);
                    Debug.Log($"  - 发现物品: {pickup.name}, 距离: {distance:F2}m, 层级: {pickup.gameObject.layer}");
                }
            }
        }
        
        // 检查背包状态
        if (inventoryManager != null)
        {
            Debug.Log($"背包已用槽位: {inventoryManager.GetUsedSlotCount()}/{inventoryManager.GetInventorySize()}");
        }
    }
    
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 100));
        GUILayout.Label("快速物品测试");
        GUILayout.Label($"按 {testSpawnKey} 生成测试物品");
        GUILayout.Label($"按 {diagnosticKey} 运行诊断");
        GUILayout.Label("按 F 拾取所有物品");
        GUILayout.EndArea();
    }
}