using UnityEngine;
using System.Collections.Generic;

public class InventoryManager : MonoBehaviour
{
    [Header("组件引用")]
    public Inventory inventory;
    public InventoryUI inventoryUI;
    public ItemTypeColors itemTypeColors;
    
    [Header("角色控制")]
    public PlayerController playerController;
    public bool disablePlayerWhenInventoryOpen = true;
    
    [Header("合成系统设置")]
    public bool enableCraftingSystem = true;
    public List<CraftingRecipe> defaultRecipes = new List<CraftingRecipe>();
    
    [Header("测试物品")]
    public List<Item> testItems = new List<Item>();
    public bool addTestItemsOnStart = false;
    
    public static InventoryManager Instance { get; private set; }
    
    // 角色控制状态
    private bool wasPlayerControlEnabled = true;
    
    // 私有引用
    private CraftingManager craftingManager;
    
    private void Awake()
    {
        // 单例模式。随 MainGame 场景创建和销毁，不用 DontDestroyOnLoad：
        // 背包 UI、玩家都在场景里，跨场景保留会让这些引用失效；背包也不存档，
        // 所以每次进入 MainGame 都是新的一局（新背包 + 新手物资）
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // 自动查找组件
        if (inventory == null)
            inventory = GetComponent<Inventory>();
        if (inventoryUI == null)
            inventoryUI = FindObjectOfType<InventoryUI>();
        if (itemTypeColors == null)
            itemTypeColors = Resources.Load<ItemTypeColors>("ItemTypeColors");
        if (playerController == null)
            playerController = FindObjectOfType<PlayerController>();
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    
    private void Start()
    {
        // 验证组件
        ValidateComponents();
        
        // 初始化合成系统
        if (enableCraftingSystem)
        {
            InitializeCraftingSystem();
        }
        
        // 添加测试物品
        if (addTestItemsOnStart)
        {
            AddTestItems();
        }
    }
    
    private void Update()
    {
        // 处理背包开关输入（从InventoryUI移到这里，确保始终能响应）
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            ToggleInventory();
        }
        
        // ESC键关闭背包
        if (Input.GetKeyDown(KeyCode.Escape) && IsInventoryOpen())
        {
            CloseInventory();
        }
    }
    
    private void ValidateComponents()
    {
        if (inventory == null)
        {
            Debug.LogError("InventoryManager: 未找到Inventory组件！");
        }
        
        if (inventoryUI == null)
        {
            Debug.LogError("InventoryManager: 未找到InventoryUI组件！");
            // 尝试重新查找InventoryUI
            RefreshInventoryUIReference();
        }
        
        if (itemTypeColors == null)
        {
            Debug.LogWarning("InventoryManager: 未找到ItemTypeColors配置文件！");
        }
    }
    
    /// <summary>
    /// 重新查找并设置InventoryUI引用
    /// </summary>
    public void RefreshInventoryUIReference()
    {
        Debug.Log("开始重新查找InventoryUI组件...");
        
        // 首先尝试找到所有InventoryUI组件（包括被禁用的）
        InventoryUI[] allInventoryUIs = Resources.FindObjectsOfTypeAll<InventoryUI>();
        
        foreach (InventoryUI ui in allInventoryUIs)
        {
            // 确保这是场景中的对象，而不是预制体
            if (ui.gameObject.scene.name != null)
            {
                Debug.Log($"找到InventoryUI组件，位于: {ui.gameObject.name}");
                
                // 如果InventoryPanel被禁用，临时启用它
                bool wasDisabled = false;
                if (ui.inventoryPanel != null && !ui.inventoryPanel.activeInHierarchy)
                {
                    ui.inventoryPanel.SetActive(true);
                    wasDisabled = true;
                    Debug.Log("临时启用了InventoryPanel以建立引用");
                }
                
                // 设置引用
                inventoryUI = ui;
                
                // 如果之前是禁用的，重新禁用
                if (wasDisabled && ui.inventoryPanel != null)
                {
                    ui.inventoryPanel.SetActive(false);
                    Debug.Log("恢复InventoryPanel的禁用状态");
                }
                
                Debug.Log("成功重新建立InventoryUI引用！");
                return;
            }
        }
        
        Debug.LogError("仍然无法找到InventoryUI组件！");
    }
    
    private void AddTestItems()
    {
        foreach (var item in testItems)
        {
            if (item != null)
            {
                AddItem(item, Random.Range(1, item.maxStackSize));
            }
        }
    }
    
    #region 公共API
    
    /// <summary>
    /// 添加物品到背包
    /// </summary>
    public bool AddItem(Item item, int amount = 1)
    {
        if (inventory == null) return false;
        return inventory.AddItem(item, amount);
    }
    
    /// <summary>
    /// 从背包移除物品
    /// </summary>
    public bool RemoveItem(Item item, int amount = 1)
    {
        if (inventory == null) return false;
        return inventory.RemoveItem(item, amount);
    }
    
    /// <summary>
    /// 检查背包中是否有指定物品
    /// </summary>
    public bool HasItem(Item item, int amount = 1)
    {
        if (inventory == null) return false;
        return inventory.HasItem(item, amount);
    }
    
    /// <summary>
    /// 获取物品数量
    /// </summary>
    public int GetItemCount(Item item)
    {
        return GetItemAmount(item);
    }

    public int GetItemAmount(Item item)
    {
        if (inventory == null) return 0;
        return inventory.GetItemAmount(item);
    }
    
    /// <summary>
    /// 获取已使用的槽位数量
    /// </summary>
    public int GetUsedSlotCount()
    {
        if (inventory == null) return 0;
        
        int usedCount = 0;
        var slots = inventory.GetAllSlots();
        foreach (var slot in slots)
        {
            if (!slot.IsEmpty())
            {
                usedCount++;
            }
        }
        return usedCount;
    }
    
    /// <summary>
    /// 获取背包总大小
    /// </summary>
    public int GetInventorySize()
    {
        if (inventory == null) return 0;
        return inventory.inventorySize;
    }
    
    /// <summary>
    /// 获取空闲槽位数量
    /// </summary>
    public int GetEmptySlotCount()
    {
        if (inventory == null) return 0;
        return GetInventorySize() - GetUsedSlotCount();
    }
    
    /// <summary>
    /// 检查背包是否打开
    /// </summary>
    public bool IsInventoryOpen()
    {
        if (inventoryUI == null) return false;
        return inventoryUI.IsInventoryOpen();
    }
    
    /// <summary>
    /// 使用物品
    /// </summary>
    public bool UseItem(Item item, int amount = 1)
    {
        if (item == null || !item.isConsumable) return false;
        
        if (HasItem(item, amount))
        {
            // 执行物品使用效果
            ExecuteItemEffect(item, amount);
            
            // 从背包中移除
            return RemoveItem(item, amount);
        }
        
        return false;
    }
    
    /// <summary>
    /// 打开背包
    /// </summary>
    public void OpenInventory()
    {
        if (inventoryUI != null)
        {
            inventoryUI.OpenInventory();
            
            // 禁用角色控制
            if (disablePlayerWhenInventoryOpen && playerController != null)
            {
                wasPlayerControlEnabled = playerController.IsControlEnabled;
                playerController.DisableControl();
                Debug.Log("背包打开，角色控制已禁用");
            }
        }
    }
    
    /// <summary>
    /// 关闭背包
    /// </summary>
    public void CloseInventory()
    {
        if (inventoryUI != null)
        {
            inventoryUI.CloseInventory();
            
            // 恢复角色控制
            if (disablePlayerWhenInventoryOpen && playerController != null && wasPlayerControlEnabled)
            {
                playerController.EnableControl();
                Debug.Log("背包关闭，角色控制已恢复");
            }
        }
    }
    
    /// <summary>
    /// 切换背包显示状态
    /// </summary>
    public void ToggleInventory()
    {
        if (inventoryUI != null)
        {
            inventoryUI.ToggleInventory();
        }
    }
    
    /// <summary>
    /// 获取物品类型颜色
    /// </summary>
    public Color GetItemTypeColor(ItemType itemType)
    {
        if (itemTypeColors != null)
        {
            return itemTypeColors.GetColorByType(itemType);
        }
        return Color.white;
    }
    
    /// <summary>
    /// 获取物品品质颜色
    /// </summary>
    public Color GetItemQualityColor(int itemLevel)
    {
        if (itemTypeColors != null)
        {
            return itemTypeColors.GetQualityColor(itemLevel);
        }
        return Color.white;
    }
    
    /// <summary>
    /// 在世界中生成物品
    /// </summary>
    public GameObject SpawnItemInWorld(Item item, Vector3 position, int amount = 1)
    {
        return ItemPickup.CreateItemPickup(item, position, amount);
    }
    
    /// <summary>
    /// 丢弃物品到世界中
    /// </summary>
    public bool DropItem(Item item, Vector3 position, int amount = 1)
    {
        if (HasItem(item, amount))
        {
            if (RemoveItem(item, amount))
            {
                SpawnItemInWorld(item, position, amount);
                return true;
            }
        }
        return false;
    }
    
    #endregion
    
    #region 物品效果系统
    
    /// <summary>
    /// 执行物品使用效果
    /// </summary>
    private void ExecuteItemEffect(Item item, int amount)
    {
        Debug.Log($"使用了 {amount} 个 {item.itemName}");
        
        // 根据物品类型执行不同效果
        switch (item.itemType)
        {
            case ItemType.Consumable:
                ExecuteConsumableEffect(item, amount);
                break;
            case ItemType.Weapon:
                EquipWeapon(item);
                break;
            case ItemType.Armor:
                EquipArmor(item);
                break;
            default:
                Debug.Log($"物品 {item.itemName} 没有特殊效果");
                break;
        }
    }
    
    private void ExecuteConsumableEffect(Item item, int amount)
    {
        // 这里可以根据物品名称或ID执行具体效果
        // 例如：恢复生命值、增加属性等
        Debug.Log($"执行消耗品效果: {item.itemName}");
        
        // 示例：如果是生命药水
        if (item.itemName.Contains("生命药水") || item.itemName.Contains("Health Potion"))
        {
            // 恢复生命值
            // PlayerHealth.Instance.Heal(50 * amount);
        }
    }
    
    private void EquipWeapon(Item weapon)
    {
        Debug.Log($"装备武器: {weapon.itemName}");
        // 这里实现武器装备逻辑
    }
    
    private void EquipArmor(Item armor)
    {
        Debug.Log($"装备护甲: {armor.itemName}");
        // 这里实现护甲装备逻辑
    }
    
    #endregion
    
    #region 合成系统集成
    
    /// <summary>
    /// 初始化合成系统
    /// </summary>
    private void InitializeCraftingSystem()
    {
        // 获取合成管理器实例
        craftingManager = CraftingManager.Instance;
        
        if (craftingManager == null)
        {
            Debug.LogWarning("InventoryManager: 未找到CraftingManager实例！");
            return;
        }
        
        // 添加默认配方
        foreach (var recipe in defaultRecipes)
        {
            if (recipe != null)
            {
                craftingManager.AddGlobalRecipe(recipe);
            }
        }
        
        Debug.Log($"合成系统已初始化，添加了 {defaultRecipes.Count} 个默认配方");
    }
    
    /// <summary>
    /// 获取合成管理器
    /// </summary>
    public CraftingManager GetCraftingManager()
    {
        return craftingManager;
    }
    
    /// <summary>
    /// 检查是否可以合成指定配方
    /// </summary>
    public bool CanCraftRecipe(CraftingRecipe recipe)
    {
        if (recipe == null || inventory == null) return false;
        return recipe.CanCraft(inventory);
    }
    
    /// <summary>
    /// 执行合成
    /// </summary>
    public bool CraftItem(CraftingRecipe recipe)
    {
        if (recipe == null || inventory == null) return false;
        
        // 检查是否可以合成
        if (!recipe.CanCraft(inventory))
        {
            Debug.LogWarning($"无法合成 {recipe.recipeName}：材料不足");
            return false;
        }
        
        // 检查背包空间
        if (GetEmptySlotCount() < 1)
        {
            Debug.LogWarning($"无法合成 {recipe.recipeName}：背包空间不足");
            return false;
        }
        
        // 消耗材料
        recipe.ConsumeMaterials(inventory);
        
        // 添加结果物品
        bool success = AddItem(recipe.resultItem, recipe.resultAmount);
        
        if (success)
        {
            Debug.Log($"成功合成了 {recipe.resultItem.itemName} x{recipe.resultAmount}");
            
            // 触发合成事件
            if (craftingManager != null)
            {
                craftingManager.TriggerCraftingEvent(recipe, true);
            }
        }
        else
        {
            Debug.LogError($"合成失败：无法添加结果物品 {recipe.resultItem.itemName}");
            
            // 返还材料
            foreach (var ingredient in recipe.ingredients)
            {
                AddItem(ingredient.item, ingredient.amount);
            }
        }
        
        return success;
    }
    
    /// <summary>
    /// 获取可合成的配方列表
    /// </summary>
    public List<CraftingRecipe> GetCraftableRecipes()
    {
        var craftableRecipes = new List<CraftingRecipe>();
        
        if (craftingManager != null)
        {
            var allRecipes = craftingManager.GetGlobalRecipes();
            foreach (var recipe in allRecipes)
            {
                if (recipe != null && recipe.isUnlocked && CanCraftRecipe(recipe))
                {
                    craftableRecipes.Add(recipe);
                }
            }
        }
        
        return craftableRecipes;
    }
    
    /// <summary>
    /// 解锁配方
    /// </summary>
    public void UnlockRecipe(CraftingRecipe recipe)
    {
        if (recipe != null && craftingManager != null)
        {
            craftingManager.UnlockRecipe(recipe);
            Debug.Log($"解锁了配方: {recipe.recipeName}");
        }
    }
    
    /// <summary>
    /// 锁定配方
    /// </summary>
    public void LockRecipe(CraftingRecipe recipe)
    {
        if (recipe != null && craftingManager != null)
        {
            craftingManager.LockRecipe(recipe);
            Debug.Log($"锁定了配方: {recipe.recipeName}");
        }
    }
    
    #endregion
    
    #region 调试功能
    
    [ContextMenu("添加随机物品")]
    public void AddRandomTestItem()
    {
        if (testItems.Count > 0)
        {
            Item randomItem = testItems[Random.Range(0, testItems.Count)];
            AddItem(randomItem, Random.Range(1, 5));
        }
    }
    
    [ContextMenu("清空背包")]
    public void ClearInventory()
    {
        if (inventory != null)
        {
            for (int i = 0; i < inventory.inventorySize; i++)
            {
                var slot = inventory.GetSlot(i);
                if (slot != null)
                {
                    slot.ClearSlot();
                }
            }
            // 触发UI更新
            if (inventory != null)
            {
                inventory.TriggerInventoryChanged();
            }
        }
    }
    
    #endregion
}