using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 简单的物品添加器
/// 按1键向背包添加随机物品
/// </summary>
public class ItemAdder : MonoBehaviour
{
    [Header("可添加的物品列表")]
    public List<Item> availableItems = new List<Item>();
    
    [Header("添加设置")]
    [Tooltip("每次添加的物品数量范围")]
    public int minAmount = 1;
    public int maxAmount = 3;
    
    [Header("按键设置")]
    [Tooltip("添加物品的按键")]
    public KeyCode addItemKey = KeyCode.Alpha1;
    
    [Header("调试信息")]
    public bool showDebugMessages = true;
    
    private void Start()
    {
        // 如果没有手动设置物品列表，尝试自动加载
        if (availableItems.Count == 0)
        {
            LoadAvailableItems();
        }
        
        if (showDebugMessages)
        {
            Debug.Log($"物品添加器已初始化，共加载 {availableItems.Count} 个物品。按 {addItemKey} 键添加随机物品。");
        }
    }
    
    private void Update()
    {
        // 检测按键输入
        if (Input.GetKeyDown(addItemKey))
        {
            AddRandomItem();
        }
    }
    
    /// <summary>
    /// 添加随机物品到背包
    /// </summary>
    public void AddRandomItem()
    {
        // 检查InventoryManager是否存在
        if (InventoryManager.Instance == null)
        {
            if (showDebugMessages)
                Debug.LogError("未找到InventoryManager！请确保场景中有InventoryManager组件。");
            return;
        }
        
        // 检查是否有可用物品
        if (availableItems.Count == 0)
        {
            if (showDebugMessages)
                Debug.LogWarning("没有可用的物品！请在Inspector中添加物品或确保Items文件夹中有物品资源。");
            return;
        }
        
        // 随机选择一个物品
        Item randomItem = availableItems[Random.Range(0, availableItems.Count)];
        
        // 随机生成数量（考虑物品的最大堆叠数量）
        int amount = Random.Range(minAmount, maxAmount + 1);
        if (randomItem.isStackable)
        {
            amount = Mathf.Min(amount, randomItem.maxStackSize);
        }
        else
        {
            amount = 1; // 不可堆叠物品只能添加1个
        }
        
        // 添加物品到背包
        bool success = InventoryManager.Instance.AddItem(randomItem, amount);
        
        if (showDebugMessages)
        {
            if (success)
            {
                Debug.Log($"成功添加 {amount} 个 {randomItem.itemName} 到背包！");
            }
            else
            {
                Debug.LogWarning($"添加物品失败！背包可能已满。尝试添加的物品：{randomItem.itemName} x{amount}");
            }
        }
    }
    
    /// <summary>
    /// 自动加载可用的物品
    /// </summary>
    private void LoadAvailableItems()
    {
        // 尝试从Items文件夹加载所有物品
        Item[] itemsInFolder = Resources.LoadAll<Item>("");
        
        foreach (Item item in itemsInFolder)
        {
            if (item != null && !availableItems.Contains(item))
            {
                availableItems.Add(item);
            }
        }
        
        // 如果Resources中没有找到，尝试从Assets/Items文件夹加载
        #if UNITY_EDITOR
        if (availableItems.Count == 0)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Item", new[] { "Assets/Items" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                Item item = UnityEditor.AssetDatabase.LoadAssetAtPath<Item>(path);
                if (item != null && !availableItems.Contains(item))
                {
                    availableItems.Add(item);
                }
            }
        }
        #endif
        
        if (showDebugMessages)
        {
            Debug.Log($"自动加载了 {availableItems.Count} 个物品。");
        }
    }
    
    /// <summary>
    /// 添加指定物品到背包
    /// </summary>
    /// <param name="item">要添加的物品</param>
    /// <param name="amount">数量</param>
    public void AddSpecificItem(Item item, int amount = 1)
    {
        if (item == null)
        {
            if (showDebugMessages)
                Debug.LogWarning("尝试添加空物品！");
            return;
        }
        
        if (InventoryManager.Instance == null)
        {
            if (showDebugMessages)
                Debug.LogError("未找到InventoryManager！");
            return;
        }
        
        bool success = InventoryManager.Instance.AddItem(item, amount);
        
        if (showDebugMessages)
        {
            if (success)
            {
                Debug.Log($"成功添加 {amount} 个 {item.itemName} 到背包！");
            }
            else
            {
                Debug.LogWarning($"添加物品失败！背包可能已满。尝试添加的物品：{item.itemName} x{amount}");
            }
        }
    }
    
    /// <summary>
    /// 清空背包（调试用）
    /// </summary>
    [ContextMenu("清空背包")]
    public void ClearInventory()
    {
        if (InventoryManager.Instance?.inventory != null)
        {
            var slots = InventoryManager.Instance.inventory.GetAllSlots();
            foreach (var slot in slots)
            {
                slot.ClearSlot();
            }
            
            if (showDebugMessages)
                Debug.Log("背包已清空！");
        }
    }
    
    /// <summary>
    /// 显示当前背包状态（调试用）
    /// </summary>
    [ContextMenu("显示背包状态")]
    public void ShowInventoryStatus()
    {
        if (InventoryManager.Instance == null)
        {
            Debug.Log("未找到InventoryManager！");
            return;
        }
        
        int usedSlots = InventoryManager.Instance.GetUsedSlotCount();
        int totalSlots = InventoryManager.Instance.GetInventorySize();
        int freeSlots = InventoryManager.Instance.GetEmptySlotCount();
        
        Debug.Log($"背包状态：已使用 {usedSlots}/{totalSlots} 个槽位，剩余 {freeSlots} 个空槽位。");
    }
}