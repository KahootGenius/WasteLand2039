using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 物品拾取系统诊断工具
/// 用于诊断物品生成但无法看见和拾取的问题
/// </summary>
public class ItemPickupDiagnostic : MonoBehaviour
{
    [Header("诊断设置")]
    public bool showDebugInfo = true;
    public bool autoRunDiagnostic = true;
    
    [Header("测试物品")]
    public Item testItem;
    
    private void Start()
    {
        if (autoRunDiagnostic)
        {
            Invoke("RunFullDiagnostic", 1f); // 延迟1秒执行，确保所有组件都已初始化
        }
    }
    
    [ContextMenu("运行完整诊断")]
    public void RunFullDiagnostic()
    {
        Debug.Log("=== 物品拾取系统诊断开始 ===");
        
        DiagnoseItemSpawner();
        DiagnoseInventorySystem();
        DiagnosePlayerItemCollector();
        DiagnoseItemPickupPrefab();
        DiagnoseSpawnedItems();
        
        Debug.Log("=== 物品拾取系统诊断完成 ===");
    }
    
    private void DiagnoseItemSpawner()
    {
        Debug.Log("--- 检查ItemSpawner ---");
        
        ItemSpawner spawner = FindObjectOfType<ItemSpawner>();
        if (spawner == null)
        {
            Debug.LogError("❌ 未找到ItemSpawner组件！");
            return;
        }
        
        Debug.Log($"✅ ItemSpawner找到: {spawner.name}");
        Debug.Log($"   - 组件状态: 已找到");
        Debug.Log($"   - 可用物品数量: {spawner.AvailableItems.Count}");
        Debug.Log($"   - 最大物品数量: {spawner.MaxItemsInWorld}");
        Debug.Log($"   - 生成间隔: {spawner.SpawnInterval}秒");
        Debug.Log($"   - 当前物品数量: {spawner.CurrentItemCount}");
        Debug.Log($"   - 是否正在生成: {spawner.IsSpawning}");
        
        if (spawner.AvailableItems.Count == 0)
        {
            Debug.LogWarning("⚠️ 可用物品列表为空！");
        }
    }
    
    private void DiagnoseInventorySystem()
    {
        Debug.Log("--- 检查背包系统 ---");
        
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        Inventory inventory = FindObjectOfType<Inventory>();
        
        Debug.Log($"InventoryManager: {(manager != null ? "✅" : "❌")}");
        Debug.Log($"InventoryUI: {(inventoryUI != null ? "✅" : "❌")}");
        Debug.Log($"Inventory: {(inventory != null ? "✅" : "❌")}");
        
        if (manager != null)
        {
            Debug.Log($"   - Manager.inventory引用: {(manager.inventory != null ? "✅" : "❌")}");
            Debug.Log($"   - Manager.inventoryUI引用: {(manager.inventoryUI != null ? "✅" : "❌")}");
        }
        
        if (inventoryUI != null)
        {
            Debug.Log($"   - InventoryUI.inventory引用: {(inventoryUI.inventory != null ? "✅" : "❌")}");
            Debug.Log($"   - 背包面板: {(inventoryUI.inventoryPanel != null ? "✅" : "❌")}");
        }
        
        if (inventory != null)
        {
            Debug.Log($"   - 背包大小: {inventory.inventorySize}");
            Debug.Log($"   - 已用槽位: {inventory.GetAllSlots().FindAll(s => !s.IsEmpty()).Count}");
        }
    }
    
    private void DiagnosePlayerItemCollector()
    {
        Debug.Log("--- 检查PlayerItemCollector ---");
        
        PlayerItemCollector collector = FindObjectOfType<PlayerItemCollector>();
        if (collector == null)
        {
            Debug.LogWarning("⚠️ 未找到PlayerItemCollector组件！");
            return;
        }
        
        Debug.Log($"✅ PlayerItemCollector找到: {collector.name}");
        Debug.Log($"   - 检测半径: {collector.detectionRadius}");
        Debug.Log($"   - 拾取按键: {collector.pickupKey}");
        Debug.Log($"   - 全拾取按键: {collector.pickupAllKey}");
        Debug.Log($"   - 附近物品数量: {collector.GetNearbyItemCount()}");
        Debug.Log($"   - 物品层级: {collector.itemLayer.value}");
        Debug.Log($"   - 背包管理器: {(collector.InventoryManager != null ? "✅" : "❌")}");
        Debug.Log($"   - 最近物品: {(collector.ClosestItem != null ? collector.ClosestItem.item.itemName : "无")}");
        Debug.Log($"   - 自动拾取: {(collector.AutoPickup ? "✅" : "❌")}");
    }
    
    private void DiagnoseItemPickupPrefab()
    {
        Debug.Log("--- 检查ItemWorldPrefab ---");
        
        GameObject prefab = Resources.Load<GameObject>("ItemWorldPrefab");
        if (prefab == null)
        {
            Debug.LogError("❌ ItemWorldPrefab预制体未找到！");
            return;
        }
        
        Debug.Log($"✅ ItemWorldPrefab预制体找到");
        
        ItemPickup itemPickup = prefab.GetComponent<ItemPickup>();
        SpriteRenderer spriteRenderer = prefab.GetComponent<SpriteRenderer>();
        Collider2D collider = prefab.GetComponent<Collider2D>();
        
        Debug.Log($"   - ItemPickup组件: {(itemPickup != null ? "✅" : "❌")}");
        Debug.Log($"   - SpriteRenderer组件: {(spriteRenderer != null ? "✅" : "❌")}");
        Debug.Log($"   - Collider2D组件: {(collider != null ? "✅" : "❌")}");
        
        if (spriteRenderer != null)
        {
            Debug.Log($"   - 默认精灵: {(spriteRenderer.sprite != null ? "✅" : "❌")}");
            Debug.Log($"   - 排序层级: {spriteRenderer.sortingOrder}");
            Debug.Log($"   - 颜色: {spriteRenderer.color}");
        }
        
        if (collider != null)
        {
            Debug.Log($"   - 碰撞器大小: {collider.bounds.size}");
            Debug.Log($"   - 是否为触发器: {collider.isTrigger}");
        }
    }
    
    private void DiagnoseSpawnedItems()
    {
        Debug.Log("--- 检查已生成的物品 ---");
        
        ItemPickup[] spawnedItems = FindObjectsOfType<ItemPickup>();
        Debug.Log($"场景中的ItemPickup数量: {spawnedItems.Length}");
        
        for (int i = 0; i < spawnedItems.Length; i++)
        {
            ItemPickup pickup = spawnedItems[i];
            Debug.Log($"   物品 {i + 1}:");
            Debug.Log($"     - 名称: {pickup.name}");
            Debug.Log($"     - 位置: {pickup.transform.position}");
            Debug.Log($"     - 激活状态: {pickup.gameObject.activeInHierarchy}");
            Debug.Log($"     - 物品数据: {(pickup.item != null ? pickup.item.itemName : "无")}");
            Debug.Log($"     - 数量: {pickup.amount}");
            
            SpriteRenderer sr = pickup.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Debug.Log($"     - 精灵: {(sr.sprite != null ? sr.sprite.name : "无")}");
                Debug.Log($"     - 可见性: {sr.enabled}");
                Debug.Log($"     - 颜色: {sr.color}");
                Debug.Log($"     - 排序层级: {sr.sortingOrder}");
            }
            
            Collider2D col = pickup.GetComponent<Collider2D>();
            if (col != null)
            {
                Debug.Log($"     - 碰撞器启用: {col.enabled}");
                Debug.Log($"     - 碰撞器大小: {col.bounds.size}");
            }
        }
    }
    
    [ContextMenu("创建测试物品")]
    public void CreateTestItem()
    {
        if (testItem == null)
        {
            Debug.LogError("请先设置测试物品！");
            return;
        }
        
        Vector3 spawnPos = transform.position + Vector3.right * 2f;
        GameObject testPickup = ItemPickup.CreateItemPickup(testItem, spawnPos, 1);
        
        if (testPickup != null)
        {
            Debug.Log($"✅ 测试物品创建成功: {testItem.itemName} at {spawnPos}");
            
            // 立即诊断这个物品
            ItemPickup pickup = testPickup.GetComponent<ItemPickup>();
            if (pickup != null)
            {
                Debug.Log("--- 测试物品详细信息 ---");
                Debug.Log($"物品名称: {pickup.item?.itemName}");
                Debug.Log($"数量: {pickup.amount}");
                Debug.Log($"位置: {pickup.transform.position}");
                
                SpriteRenderer sr = pickup.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    Debug.Log($"精灵: {(sr.sprite != null ? sr.sprite.name : "无")}");
                    Debug.Log($"颜色: {sr.color}");
                }
            }
        }
        else
        {
            Debug.LogError("❌ 测试物品创建失败！");
        }
    }
    
    [ContextMenu("测试背包添加")]
    public void TestInventoryAdd()
    {
        if (testItem == null)
        {
            Debug.LogError("请先设置测试物品！");
            return;
        }
        
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        
        if (manager != null)
        {
            Debug.Log("测试InventoryManager.AddItem...");
            bool success1 = manager.AddItem(testItem, 1);
            Debug.Log($"InventoryManager.AddItem结果: {success1}");
        }
        
        if (inventoryUI != null)
        {
            Debug.Log("测试InventoryUI.AddItemToInventory...");
            bool success2 = inventoryUI.AddItemToInventory(testItem, 1);
            Debug.Log($"InventoryUI.AddItemToInventory结果: {success2}");
        }
    }
    
    private void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("=== 物品拾取诊断 ===");
        
        if (GUILayout.Button("运行完整诊断"))
        {
            RunFullDiagnostic();
        }
        
        if (GUILayout.Button("创建测试物品"))
        {
            CreateTestItem();
        }
        
        if (GUILayout.Button("测试背包添加"))
        {
            TestInventoryAdd();
        }
        
        ItemSpawner spawner = FindObjectOfType<ItemSpawner>();
        if (spawner != null)
        {
            if (GUILayout.Button("立即生成物品"))
            {
                spawner.SpawnItemNow();
            }
        }
        
        GUILayout.EndArea();
    }
}