using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 示例物品创建器
/// 用于快速创建一些测试物品
/// </summary>
public class ExampleItems : MonoBehaviour
{
    [Header("示例物品列表")]
    public List<ItemData> exampleItems = new List<ItemData>();
    
    [System.Serializable]
    public class ItemData
    {
        public string itemName;
        public ItemType itemType;
        public int itemLevel;
        public bool isStackable;
        public int maxStackSize;
        public bool isConsumable;
        public string description;
        [TextArea(2, 4)]
        public string detailedDescription;
    }
    
    private void Start()
    {
        // 初始化示例物品数据
        InitializeExampleItems();
    }
    
    private void InitializeExampleItems()
    {
        exampleItems = new List<ItemData>
        {
            new ItemData
            {
                itemName = "Health Potion",
                itemType = ItemType.Consumable,
                itemLevel = 1,
                isStackable = true,
                maxStackSize = 10,
                isConsumable = true,
                description = "Restores 50 HP",
                detailedDescription = "A red potion with a faint herbal aroma. Drinking it can quickly heal wounds."
            },
            new ItemData
            {
                itemName = "Mana Potion",
                itemType = ItemType.Consumable,
                itemLevel = 1,
                isStackable = true,
                maxStackSize = 10,
                isConsumable = true,
                description = "Restores 30 MP",
                detailedDescription = "A blue mysterious potion containing pure magical energy."
            },
            new ItemData
            {
                itemName = "Iron Sword",
                itemType = ItemType.Weapon,
                itemLevel = 5,
                isStackable = false,
                maxStackSize = 1,
                isConsumable = false,
                description = "Attack +15",
                detailedDescription = "A sword forged from high-quality iron ore, sharp and sturdy. A reliable companion for adventurers."
            },
            new ItemData
            {
                itemName = "Leather Armor",
                itemType = ItemType.Armor,
                itemLevel = 3,
                isStackable = false,
                maxStackSize = 1,
                isConsumable = false,
                description = "Defense +8",
                detailedDescription = "Light armor made from beast leather, providing basic protection without hindering movement."
            },
            new ItemData
            {
                itemName = "Scrap Metal",
                itemType = ItemType.Material,
                itemLevel = 1,
                isStackable = true,
                maxStackSize = 50,
                isConsumable = false,
                description = "Crafting Material",
                detailedDescription = "Metal scraps recovered from ruins, can be used to craft simple tools and weapons."
            },
            new ItemData
            {
                itemName = "Electronic Component",
                itemType = ItemType.Material,
                itemLevel = 2,
                isStackable = true,
                maxStackSize = 20,
                isConsumable = false,
                description = "Advanced Crafting Material",
                detailedDescription = "Components dismantled from electronic devices left over from the old world, essential for crafting high-tech equipment."
            },
            new ItemData
            {
                itemName = "Mysterious Key",
                itemType = ItemType.Quest,
                itemLevel = 1,
                isStackable = false,
                maxStackSize = 1,
                isConsumable = false,
                description = "Quest Item",
                detailedDescription = "An ancient key engraved with mysterious runes. It seems to be able to open some important place."
            },
            new ItemData
            {
                itemName = "Old Photo",
                itemType = ItemType.Misc,
                itemLevel = 1,
                isStackable = true,
                maxStackSize = 5,
                isConsumable = false,
                description = "Memento",
                detailedDescription = "A yellowed photograph recording the good times before the apocalypse. Although it has no practical value, it carries precious memories."
            }
        };
    }
    
    /// <summary>
    /// 创建示例物品ScriptableObject文件
    /// </summary>
    [ContextMenu("Create Example Items")]
    public void CreateExampleItemAssets()
    {
        #if UNITY_EDITOR
        string folderPath = "Assets/Items/Examples";
        
        // 确保文件夹存在
        if (!UnityEditor.AssetDatabase.IsValidFolder(folderPath))
        {
            UnityEditor.AssetDatabase.CreateFolder("Assets/Items", "Examples");
        }
        
        foreach (var itemData in exampleItems)
        {
            // 创建Item ScriptableObject
            Item newItem = ScriptableObject.CreateInstance<Item>();
            
            // 设置属性
            newItem.itemName = itemData.itemName;
            newItem.itemType = itemData.itemType;
            newItem.itemLevel = itemData.itemLevel;
            newItem.isStackable = itemData.isStackable;
            newItem.maxStackSize = itemData.maxStackSize;
            newItem.isConsumable = itemData.isConsumable;
            newItem.description = string.IsNullOrEmpty(itemData.detailedDescription) ? 
                itemData.description : itemData.detailedDescription;
            
            // 保存文件
            string fileName = itemData.itemName.Replace(" ", "");
            string assetPath = $"{folderPath}/{fileName}.asset";
            
            UnityEditor.AssetDatabase.CreateAsset(newItem, assetPath);
            Debug.Log($"创建了示例物品: {assetPath}");
        }
        
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
        
        Debug.Log($"成功创建了 {exampleItems.Count} 个示例物品！");
        #endif
    }
    
    /// <summary>
    /// 添加所有示例物品到背包（仅在运行时）
    /// </summary>
    [ContextMenu("Add All Items to Inventory")]
    public void AddAllItemsToInventory()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("此功能仅在运行时可用！");
            return;
        }
        
        InventoryManager manager = InventoryManager.Instance;
        if (manager == null)
        {
            Debug.LogError("未找到InventoryManager！");
            return;
        }
        
        // 加载所有示例物品
        Item[] items = Resources.LoadAll<Item>("Examples");
        if (items.Length == 0)
        {
            Debug.LogWarning("未找到示例物品！请先运行 Create Example Items。");
            return;
        }
        
        foreach (var item in items)
        {
            int amount = item.isStackable ? Random.Range(1, Mathf.Min(5, item.maxStackSize)) : 1;
            manager.AddItem(item, amount);
            Debug.Log($"添加了 {amount} 个 {item.itemName}");
        }
        
        Debug.Log($"成功添加了 {items.Length} 种物品到背包！");
    }
    
    /// <summary>
    /// 在世界中随机生成物品
    /// </summary>
    [ContextMenu("Spawn Random Items in World")]
    public void SpawnRandomItemsInWorld()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("此功能仅在运行时可用！");
            return;
        }
        
        InventoryManager manager = InventoryManager.Instance;
        if (manager == null)
        {
            Debug.LogError("未找到InventoryManager！");
            return;
        }
        
        // 加载所有示例物品
        Item[] items = Resources.LoadAll<Item>("Examples");
        if (items.Length == 0)
        {
            Debug.LogWarning("未找到示例物品！");
            return;
        }
        
        // 在玩家周围随机生成物品
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Vector3 centerPos = player != null ? player.transform.position : Vector3.zero;
        
        for (int i = 0; i < 5; i++)
        {
            Item randomItem = items[Random.Range(0, items.Length)];
            Vector3 randomPos = centerPos + new Vector3(
                Random.Range(-5f, 5f),
                Random.Range(-3f, 3f),
                0
            );
            
            int amount = randomItem.isStackable ? Random.Range(1, 3) : 1;
            manager.SpawnItemInWorld(randomItem, randomPos, amount);
        }
        
        Debug.Log("在世界中生成了5个随机物品！");
    }
    
    /// <summary>
    /// 获取物品类型的中文名称
    /// </summary>
    public static string GetItemTypeDisplayName(ItemType itemType)
    {
        switch (itemType)
        {
            case ItemType.Weapon: return "Weapon";
            case ItemType.Armor: return "Armor";
            case ItemType.Consumable: return "Consumable";
            case ItemType.Material: return "Material";
            case ItemType.Quest: return "Quest Item";
            case ItemType.Misc: return "Misc";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// 获取物品稀有度描述
    /// </summary>
    public static string GetItemRarityDescription(int itemLevel)
    {
        if (itemLevel >= 50) return "Legendary";
        if (itemLevel >= 30) return "Epic";
        if (itemLevel >= 20) return "Rare";
        if (itemLevel >= 10) return "Excellent";
        return "Common";
    }
    
    /// <summary>
    /// 获取物品稀有度颜色
    /// </summary>
    public static Color GetItemRarityColor(int itemLevel)
    {
        if (itemLevel >= 50) return new Color(1f, 0.8f, 0f); // 金色
        if (itemLevel >= 30) return new Color(0.6f, 0.2f, 0.8f); // 紫色
        if (itemLevel >= 20) return new Color(0.2f, 0.6f, 1f); // 蓝色
        if (itemLevel >= 10) return new Color(0.2f, 0.8f, 0.2f); // 绿色
        return Color.white; // 白色
    }
}