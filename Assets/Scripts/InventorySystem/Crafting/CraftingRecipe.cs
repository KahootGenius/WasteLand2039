using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New Crafting Recipe", menuName = "Inventory/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    [Header("配方基本信息")]
    public string recipeName = "新配方";
    [TextArea(3, 5)]
    public string description = "配方描述";
    public Sprite recipeIcon;
    
    [Header("配方材料")]
    public List<CraftingIngredient> ingredients = new List<CraftingIngredient>();
    
    [Header("配方产出")]
    public Item resultItem;
    public int resultAmount = 1;
    
    [Header("配方设置")]
    public bool isUnlocked = true;
    public int craftingTime = 3; // 合成时间（秒）
    public int experienceReward = 10;
    
    /// <summary>
    /// 检查是否有足够的材料进行合成
    /// </summary>
    /// <param name="inventory">要检查的背包</param>
    /// <returns>是否可以合成</returns>
    public bool CanCraft(Inventory inventory)
    {
        if (!isUnlocked || resultItem == null)
            return false;
            
        foreach (var ingredient in ingredients)
        {
            if (!inventory.HasItem(ingredient.item, ingredient.amount))
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// 消耗材料进行合成
    /// </summary>
    /// <param name="inventory">要消耗材料的背包</param>
    /// <returns>是否成功消耗材料</returns>
    public bool ConsumeMaterials(Inventory inventory)
    {
        if (!CanCraft(inventory))
            return false;
            
        foreach (var ingredient in ingredients)
        {
            inventory.RemoveItem(ingredient.item, ingredient.amount);
        }
        
        return true;
    }
    
    /// <summary>
    /// 获取缺失的材料信息
    /// </summary>
    /// <param name="inventory">要检查的背包</param>
    /// <returns>缺失材料的列表</returns>
    public List<CraftingIngredient> GetMissingIngredients(Inventory inventory)
    {
        List<CraftingIngredient> missing = new List<CraftingIngredient>();
        
        foreach (var ingredient in ingredients)
        {
            int currentAmount = inventory.GetItemCount(ingredient.item);
            if (currentAmount < ingredient.amount)
            {
                missing.Add(new CraftingIngredient
                {
                    item = ingredient.item,
                    amount = ingredient.amount - currentAmount
                });
            }
        }
        
        return missing;
    }
}

[System.Serializable]
public class CraftingIngredient
{
    public Item item;
    [Range(1, 999)]
    public int amount = 1;
}