using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemTypeColors", menuName = "Inventory/Item Type Colors")]
public class ItemTypeColors : ScriptableObject
{
    [System.Serializable]
    public class ItemTypeColor
    {
        public ItemType itemType;
        public Color color = Color.white;
        public string displayName;
    }
    
    [Header("物品类型颜色配置")]
    public List<ItemTypeColor> typeColors = new List<ItemTypeColor>
    {
        new ItemTypeColor { itemType = ItemType.Weapon, color = new Color(1f, 0.5f, 0.5f), displayName = "Weapon" },
        new ItemTypeColor { itemType = ItemType.Armor, color = new Color(0.5f, 0.5f, 1f), displayName = "Armor" },
        new ItemTypeColor { itemType = ItemType.Consumable, color = new Color(0.5f, 1f, 0.5f), displayName = "Consumable" },
        new ItemTypeColor { itemType = ItemType.Material, color = new Color(1f, 1f, 0.5f), displayName = "Material" },
        new ItemTypeColor { itemType = ItemType.Quest, color = new Color(1f, 0.5f, 1f), displayName = "Quest Item" },
        new ItemTypeColor { itemType = ItemType.Misc, color = new Color(0.8f, 0.8f, 0.8f), displayName = "Misc" }
    };
    
    [Header("品质颜色配置")]
    public Color commonColor = Color.white;
    public Color uncommonColor = Color.green;
    public Color rareColor = Color.blue;
    public Color epicColor = Color.magenta;
    public Color legendaryColor = Color.yellow;
    
    /// <summary>
    /// 根据物品类型获取颜色
    /// </summary>
    public Color GetColorByType(ItemType itemType)
    {
        foreach (var typeColor in typeColors)
        {
            if (typeColor.itemType == itemType)
            {
                return typeColor.color;
            }
        }
        return Color.white;
    }
    
    /// <summary>
    /// 根据物品类型获取显示名称
    /// </summary>
    public string GetDisplayNameByType(ItemType itemType)
    {
        foreach (var typeColor in typeColors)
        {
            if (typeColor.itemType == itemType)
            {
                return typeColor.displayName;
            }
        }
        return itemType.ToString();
    }
    
    /// <summary>
    /// 根据物品等级获取品质颜色
    /// </summary>
    public Color GetQualityColor(int itemLevel)
    {
        if (itemLevel >= 50)
            return legendaryColor;
        else if (itemLevel >= 30)
            return epicColor;
        else if (itemLevel >= 20)
            return rareColor;
        else if (itemLevel >= 10)
            return uncommonColor;
        else
            return commonColor;
    }
    
    /// <summary>
    /// 获取品质名称
    /// </summary>
    public string GetQualityName(int itemLevel)
    {
        if (itemLevel >= 50)
            return "Legendary";
        else if (itemLevel >= 30)
            return "Epic";
        else if (itemLevel >= 20)
            return "Rare";
        else if (itemLevel >= 10)
            return "Excellent";
        else
            return "Common";
    }
}