using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 新手物资发放系统
/// 用于在游戏开始时给玩家发放初始物品
/// </summary>
public class StarterItemGiver : MonoBehaviour
{
    [Header("新手物资配置")]
    [SerializeField] private List<StarterItem> starterItems = new List<StarterItem>();
    
    [Header("发放设置")]
    [SerializeField] private bool giveItemsOnStart = true; // 是否在Start时自动发放
    [SerializeField] private bool giveItemsOnlyOnce = true; // 是否只发放一次
    [SerializeField] private string saveKey = "StarterItemsGiven"; // 存档键名
    
    [Header("调试设置")]
    [SerializeField] private bool enableDebugLog = true;
    [SerializeField] private bool showGiveItemsButton = true;
    
    // 私有变量
    private InventoryManager inventoryManager;
    private bool itemsAlreadyGiven = false;
    
    // 事件
    public System.Action<List<StarterItem>> OnItemsGiven;
    public System.Action<StarterItem> OnItemGiven;
    
    private void Awake()
    {
        // 获取背包管理器
        inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null)
        {
            Debug.LogWarning("[StarterItemGiver] InventoryManager instance not found");
        }
    }
    
    private void Start()
    {
        if (giveItemsOnStart)
        {
            // 延迟一帧执行，确保所有系统都已初始化
            Invoke(nameof(GiveStarterItems), 0.1f);
        }
    }
    
    /// <summary>
    /// 发放新手物资
    /// </summary>
    [ContextMenu("发放新手物资")]
    public void GiveStarterItems()
    {
        if (inventoryManager == null)
        {
            inventoryManager = InventoryManager.Instance;
            if (inventoryManager == null)
            {
                Debug.LogError("[StarterItemGiver] Cannot give items: InventoryManager not found");
                return;
            }
        }
        
        // 检查是否已经发放过
        if (giveItemsOnlyOnce && HasItemsBeenGiven())
        {
            if (enableDebugLog)
            {
                Debug.Log("[StarterItemGiver] Starter items have already been given");
            }
            return;
        }
        
        if (starterItems.Count == 0)
        {
            Debug.LogWarning("[StarterItemGiver] No starter items configured");
            return;
        }
        
        if (enableDebugLog)
        {
            Debug.Log($"[StarterItemGiver] Giving {starterItems.Count} starter items to player");
        }
        
        int successCount = 0;
        int failCount = 0;
        
        foreach (StarterItem starterItem in starterItems)
        {
            if (starterItem.item == null)
            {
                Debug.LogWarning("[StarterItemGiver] Skipping null item in starter items list");
                failCount++;
                continue;
            }
            
            if (starterItem.amount <= 0)
            {
                Debug.LogWarning($"[StarterItemGiver] Skipping {starterItem.item.itemName} with invalid amount: {starterItem.amount}");
                failCount++;
                continue;
            }
            
            bool success = inventoryManager.AddItem(starterItem.item, starterItem.amount);
            
            if (success)
            {
                successCount++;
                if (enableDebugLog)
                {
                    Debug.Log($"[StarterItemGiver] Added {starterItem.amount}x {starterItem.item.itemName}");
                }
                
                // 触发单个物品发放事件
                OnItemGiven?.Invoke(starterItem);
            }
            else
            {
                failCount++;
                Debug.LogWarning($"[StarterItemGiver] Failed to add {starterItem.amount}x {starterItem.item.itemName} - inventory might be full");
            }
        }
        
        // 记录已发放状态
        if (giveItemsOnlyOnce && successCount > 0)
        {
            MarkItemsAsGiven();
        }
        
        // 触发批量发放事件
        OnItemsGiven?.Invoke(starterItems);
        
        if (enableDebugLog)
        {
            Debug.Log($"[StarterItemGiver] Starter items distribution complete - Success: {successCount}, Failed: {failCount}");
        }
    }
    
    /// <summary>
    /// 强制发放物资（忽略已发放状态）
    /// </summary>
    [ContextMenu("强制发放物资")]
    public void ForceGiveStarterItems()
    {
        bool originalOnlyOnce = giveItemsOnlyOnce;
        giveItemsOnlyOnce = false;
        
        GiveStarterItems();
        
        giveItemsOnlyOnce = originalOnlyOnce;
        
        if (enableDebugLog)
        {
            Debug.Log("[StarterItemGiver] Force gave starter items");
        }
    }
    
    /// <summary>
    /// 添加新手物品
    /// </summary>
    /// <param name="item">物品</param>
    /// <param name="amount">数量</param>
    /// <param name="description">描述</param>
    public void AddStarterItem(Item item, int amount, string description = "")
    {
        if (item == null)
        {
            Debug.LogWarning("[StarterItemGiver] Cannot add null item");
            return;
        }
        
        StarterItem newItem = new StarterItem
        {
            item = item,
            amount = amount,
            description = description
        };
        
        starterItems.Add(newItem);
        
        if (enableDebugLog)
        {
            Debug.Log($"[StarterItemGiver] Added starter item: {amount}x {item.itemName}");
        }
    }
    
    /// <summary>
    /// 移除新手物品
    /// </summary>
    /// <param name="item">要移除的物品</param>
    public void RemoveStarterItem(Item item)
    {
        if (item == null) return;
        
        for (int i = starterItems.Count - 1; i >= 0; i--)
        {
            if (starterItems[i].item == item)
            {
                starterItems.RemoveAt(i);
                if (enableDebugLog)
                {
                    Debug.Log($"[StarterItemGiver] Removed starter item: {item.itemName}");
                }
            }
        }
    }
    
    /// <summary>
    /// 清空新手物品列表
    /// </summary>
    [ContextMenu("清空物品列表")]
    public void ClearStarterItems()
    {
        starterItems.Clear();
        if (enableDebugLog)
        {
            Debug.Log("[StarterItemGiver] Cleared all starter items");
        }
    }
    
    /// <summary>
    /// 重置发放状态
    /// </summary>
    [ContextMenu("重置发放状态")]
    public void ResetGivenStatus()
    {
        PlayerPrefs.DeleteKey(saveKey);
        itemsAlreadyGiven = false;
        
        if (enableDebugLog)
        {
            Debug.Log("[StarterItemGiver] Reset given status - items can be given again");
        }
    }
    
    /// <summary>
    /// 检查是否已经发放过物品
    /// </summary>
    /// <returns>是否已发放</returns>
    private bool HasItemsBeenGiven()
    {
        if (itemsAlreadyGiven)
        {
            return true;
        }
        
        itemsAlreadyGiven = PlayerPrefs.GetInt(saveKey, 0) == 1;
        return itemsAlreadyGiven;
    }
    
    /// <summary>
    /// 标记物品已发放
    /// </summary>
    private void MarkItemsAsGiven()
    {
        PlayerPrefs.SetInt(saveKey, 1);
        PlayerPrefs.Save();
        itemsAlreadyGiven = true;
        
        if (enableDebugLog)
        {
            Debug.Log("[StarterItemGiver] Marked starter items as given");
        }
    }
    
    /// <summary>
    /// 获取新手物品列表信息
    /// </summary>
    /// <returns>物品列表信息</returns>
    public string GetStarterItemsInfo()
    {
        if (starterItems.Count == 0)
        {
            return "No starter items configured";
        }
        
        string info = $"Starter Items ({starterItems.Count}):\n";
        foreach (StarterItem starterItem in starterItems)
        {
            if (starterItem.item != null)
            {
                info += $"- {starterItem.amount}x {starterItem.item.itemName}";
                if (!string.IsNullOrEmpty(starterItem.description))
                {
                    info += $" ({starterItem.description})";
                }
                info += "\n";
            }
        }
        
        info += $"Status: {(HasItemsBeenGiven() ? "Already given" : "Not given yet")}";
        return info;
    }
    
    /// <summary>
    /// 设置是否只发放一次
    /// </summary>
    /// <param name="onlyOnce">是否只发放一次</param>
    public void SetGiveOnlyOnce(bool onlyOnce)
    {
        giveItemsOnlyOnce = onlyOnce;
        if (enableDebugLog)
        {
            Debug.Log($"[StarterItemGiver] Set give only once: {onlyOnce}");
        }
    }
    
    /// <summary>
    /// 设置是否在开始时自动发放
    /// </summary>
    /// <param name="autoGive">是否自动发放</param>
    public void SetAutoGiveOnStart(bool autoGive)
    {
        giveItemsOnStart = autoGive;
        if (enableDebugLog)
        {
            Debug.Log($"[StarterItemGiver] Set auto give on start: {autoGive}");
        }
    }
    
    #region Unity Editor
    
#if UNITY_EDITOR
    private void OnValidate()
    {
        // 验证物品数量
        foreach (StarterItem starterItem in starterItems)
        {
            if (starterItem.amount < 0)
            {
                starterItem.amount = 1;
            }
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        // 在Scene视图中显示信息
        if (starterItems.Count > 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 2f, Vector3.one * 0.5f);
        }
    }
#endif
    
    #endregion
}

/// <summary>
/// 新手物品配置
/// </summary>
[System.Serializable]
public class StarterItem
{
    [Header("物品配置")]
    public Item item; // 物品引用
    
    [Range(1, 999)]
    public int amount = 1; // 数量
    
    [Header("描述信息")]
    [TextArea(2, 4)]
    public string description = ""; // 物品描述或备注
    
    /// <summary>
    /// 获取物品信息字符串
    /// </summary>
    /// <returns>物品信息</returns>
    public override string ToString()
    {
        if (item == null)
        {
            return "[Null Item]";
        }
        
        string info = $"{amount}x {item.itemName}";
        if (!string.IsNullOrEmpty(description))
        {
            info += $" - {description}";
        }
        return info;
    }
}