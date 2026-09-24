using UnityEngine;
using System.Collections.Generic;

public class CraftingStation : MonoBehaviour
{
    [Header("合成台设置")]
    public string stationName = "合成台";
    public List<CraftingRecipe> availableRecipes = new List<CraftingRecipe>();
    public float interactionRange = 2f;
    
    [Header("UI引用")]
    public CraftingUI craftingUI;
    
    [Header("音效设置")]
    public AudioSource audioSource;
    public AudioClip craftingSound;
    public AudioClip successSound;
    public AudioClip failSound;
    
    private PlayerController player;
    private bool isPlayerInRange = false;
    
    private void Start()
    {
        player = FindObjectOfType<PlayerController>();
        
        if (craftingUI == null)
            craftingUI = FindObjectOfType<CraftingUI>();
            
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
            
        // 如果没有手动配置配方，则从CraftingManager获取全局配方
        if (availableRecipes.Count == 0 && CraftingManager.Instance != null)
        {
            availableRecipes.AddRange(CraftingManager.Instance.globalRecipes);
            Debug.Log($"合成台 {stationName} 自动加载了 {availableRecipes.Count} 个全局配方");
        }
    }
    
    private void Update()
    {
        CheckPlayerDistance();
        HandleInput();
    }
    
    private void CheckPlayerDistance()
    {
        if (player == null) return;
        
        float distance = Vector2.Distance(transform.position, player.transform.position);
        isPlayerInRange = distance <= interactionRange;
    }
    
    private void HandleInput()
    {
        if (isPlayerInRange && Input.GetKeyDown(KeyCode.E))
        {
            OpenCraftingUI();
        }
    }
    
    public void OpenCraftingUI()
    {
        if (craftingUI != null)
        {
            craftingUI.OpenCraftingUI(this);
        }
        else
        {
            Debug.LogWarning("CraftingUI未找到！");
        }
    }
    
    public void CloseCraftingUI()
    {
        if (craftingUI != null)
        {
            craftingUI.CloseCraftingUI();
        }
    }
    
    /// <summary>
    /// 执行合成
    /// </summary>
    /// <param name="recipe">要合成的配方</param>
    /// <returns>是否成功合成</returns>
    public bool CraftItem(CraftingRecipe recipe)
    {
        if (recipe == null)
        {
            Debug.LogWarning("配方为空！");
            return false;
        }
        
        Inventory playerInventory = FindObjectOfType<Inventory>();
        if (playerInventory == null)
        {
            Debug.LogError("未找到玩家背包！");
            return false;
        }
        
        // 检查是否可以合成
        if (!recipe.CanCraft(playerInventory))
        {
            PlaySound(failSound);
            Debug.Log($"材料不足，无法合成 {recipe.recipeName}");
            return false;
        }
        
        // 检查背包是否有空间
        if (playerInventory.GetFirstEmptySlot() == -1 && !CanStackResult(playerInventory, recipe))
        {
            PlaySound(failSound);
            Debug.Log("背包已满，无法合成！");
            return false;
        }
        
        // 消耗材料
        if (recipe.ConsumeMaterials(playerInventory))
        {
            // 添加合成结果
            if (playerInventory.AddItem(recipe.resultItem, recipe.resultAmount))
            {
                PlaySound(successSound);
                Debug.Log($"成功合成 {recipe.resultItem.itemName} x{recipe.resultAmount}");
                return true;
            }
            else
            {
                // 如果添加失败，需要返还材料
                RestoreMaterials(playerInventory, recipe);
                PlaySound(failSound);
                Debug.LogError("合成失败：无法添加物品到背包");
                return false;
            }
        }
        
        PlaySound(failSound);
        return false;
    }
    
    /// <summary>
    /// 检查合成结果是否可以堆叠到现有物品上
    /// </summary>
    private bool CanStackResult(Inventory inventory, CraftingRecipe recipe)
    {
        if (!recipe.resultItem.isStackable) return false;
        
        var slots = inventory.GetAllSlots();
        foreach (var slot in slots)
        {
            if (!slot.IsEmpty() && slot.item == recipe.resultItem)
            {
                if (slot.amount + recipe.resultAmount <= recipe.resultItem.maxStackSize)
                {
                    return true;
                }
            }
        }
        return false;
    }
    
    /// <summary>
    /// 返还材料（合成失败时使用）
    /// </summary>
    private void RestoreMaterials(Inventory inventory, CraftingRecipe recipe)
    {
        foreach (var ingredient in recipe.ingredients)
        {
            inventory.AddItem(ingredient.item, ingredient.amount);
        }
    }
    
    /// <summary>
    /// 播放音效
    /// </summary>
    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }
    
    /// <summary>
    /// 获取可用配方列表
    /// </summary>
    public List<CraftingRecipe> GetAvailableRecipes()
    {
        return new List<CraftingRecipe>(availableRecipes);
    }
    
    /// <summary>
    /// 添加配方到合成台
    /// </summary>
    public void AddRecipe(CraftingRecipe recipe)
    {
        if (recipe != null && !availableRecipes.Contains(recipe))
        {
            availableRecipes.Add(recipe);
        }
    }
    
    /// <summary>
    /// 从合成台移除配方
    /// </summary>
    public void RemoveRecipe(CraftingRecipe recipe)
    {
        if (recipe != null && availableRecipes.Contains(recipe))
        {
            availableRecipes.Remove(recipe);
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        // 在Scene视图中显示交互范围
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}