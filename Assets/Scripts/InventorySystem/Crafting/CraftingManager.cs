using UnityEngine;
using System.Collections.Generic;

public class CraftingManager : MonoBehaviour
{
    [Header("合成系统设置")]
    public CraftingUI craftingUI;
    public List<CraftingRecipe> globalRecipes = new List<CraftingRecipe>();
    
    [Header("输入设置")]
    public KeyCode craftingKey = KeyCode.C;
    
    public static CraftingManager Instance { get; private set; }
    
    // 事件
    public static System.Action<CraftingRecipe> OnItemCrafted;
    public static System.Action<CraftingStation> OnCraftingStationOpened;
    public static System.Action OnCraftingStationClosed;
    
    private void Awake()
    {
        // 单例模式
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // 自动查找组件
        if (craftingUI == null)
            craftingUI = FindObjectOfType<CraftingUI>();
    }
    
    private void Start()
    {
        LoadGlobalRecipes();
    }
    
    private void Update()
    {
        // 快捷键打开最近的合成台
        if (Input.GetKeyDown(craftingKey))
        {
            OpenNearestCraftingStation();
        }
    }
    
    /// <summary>
    /// 加载全局配方
    /// </summary>
    private void LoadGlobalRecipes()
    {
        // 从Resources文件夹加载所有配方
        CraftingRecipe[] recipes = Resources.LoadAll<CraftingRecipe>("CraftingRecipes");
        
        foreach (var recipe in recipes)
        {
            if (recipe != null && !globalRecipes.Contains(recipe))
            {
                globalRecipes.Add(recipe);
            }
        }
        
        Debug.Log($"加载了 {globalRecipes.Count} 个全局配方");
    }
    
    /// <summary>
    /// 打开最近的合成台
    /// </summary>
    private void OpenNearestCraftingStation()
    {
        var player = FindObjectOfType<PlayerController>();
        if (player == null) return;
        
        CraftingStation nearestStation = null;
        float nearestDistance = float.MaxValue;
        
        CraftingStation[] stations = FindObjectsOfType<CraftingStation>();
        
        foreach (var station in stations)
        {
            float distance = Vector2.Distance(player.transform.position, station.transform.position);
            if (distance < station.interactionRange && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestStation = station;
            }
        }
        
        if (nearestStation != null)
        {
            nearestStation.OpenCraftingUI();
        }
        else
        {
            Debug.Log("附近没有可用的合成台");
        }
    }
    
    /// <summary>
    /// 获取全局配方列表
    /// </summary>
    public List<CraftingRecipe> GetGlobalRecipes()
    {
        return new List<CraftingRecipe>(globalRecipes);
    }
    
    /// <summary>
    /// 添加全局配方
    /// </summary>
    public void AddGlobalRecipe(CraftingRecipe recipe)
    {
        if (recipe != null && !globalRecipes.Contains(recipe))
        {
            globalRecipes.Add(recipe);
            Debug.Log($"添加全局配方: {recipe.recipeName}");
        }
    }
    
    /// <summary>
    /// 移除全局配方
    /// </summary>
    public void RemoveGlobalRecipe(CraftingRecipe recipe)
    {
        if (recipe != null && globalRecipes.Contains(recipe))
        {
            globalRecipes.Remove(recipe);
            Debug.Log($"移除全局配方: {recipe.recipeName}");
        }
    }
    
    /// <summary>
    /// 解锁配方
    /// </summary>
    public void UnlockRecipe(CraftingRecipe recipe)
    {
        if (recipe != null)
        {
            recipe.isUnlocked = true;
            Debug.Log($"解锁配方: {recipe.recipeName}");
        }
    }
    
    /// <summary>
    /// 锁定配方
    /// </summary>
    public void LockRecipe(CraftingRecipe recipe)
    {
        if (recipe != null)
        {
            recipe.isUnlocked = false;
            Debug.Log($"锁定配方: {recipe.recipeName}");
        }
    }
    
    /// <summary>
    /// 检查玩家是否可以合成指定配方
    /// </summary>
    public bool CanPlayerCraft(CraftingRecipe recipe)
    {
        if (recipe == null) return false;
        
        var inventory = FindObjectOfType<Inventory>();
        if (inventory == null) return false;
        
        return recipe.CanCraft(inventory);
    }
    
    /// <summary>
    /// 获取玩家可以合成的配方列表
    /// </summary>
    public List<CraftingRecipe> GetCraftableRecipes()
    {
        List<CraftingRecipe> craftableRecipes = new List<CraftingRecipe>();
        var inventory = FindObjectOfType<Inventory>();
        
        if (inventory == null) return craftableRecipes;
        
        foreach (var recipe in globalRecipes)
        {
            if (recipe != null && recipe.isUnlocked && recipe.CanCraft(inventory))
            {
                craftableRecipes.Add(recipe);
            }
        }
        
        return craftableRecipes;
    }
    
    /// <summary>
    /// 触发物品合成事件
    /// </summary>
    public void TriggerItemCrafted(CraftingRecipe recipe)
    {
        OnItemCrafted?.Invoke(recipe);
    }
    
    /// <summary>
    /// 触发合成台打开事件
    /// </summary>
    public void TriggerCraftingStationOpened(CraftingStation station)
    {
        OnCraftingStationOpened?.Invoke(station);
    }
    
    /// <summary>
    /// 触发合成台关闭事件
    /// </summary>
    public void TriggerCraftingStationClosed()
    {
        OnCraftingStationClosed?.Invoke();
    }
    
    /// <summary>
    /// 触发合成事件
    /// </summary>
    public void TriggerCraftingEvent(CraftingRecipe recipe, bool success)
    {
        if (success)
        {
            OnItemCrafted?.Invoke(recipe);
            Debug.Log($"合成成功: {recipe.recipeName}");
        }
        else
        {
            Debug.Log($"合成失败: {recipe.recipeName}");
        }
    }
    
    /// <summary>
    /// 创建默认合成台配方
    /// </summary>
    public void SetupDefaultCraftingStation(CraftingStation station)
    {
        if (station == null) return;
        
        // 添加所有解锁的全局配方到合成台
        foreach (var recipe in globalRecipes)
        {
            if (recipe != null && recipe.isUnlocked)
            {
                station.AddRecipe(recipe);
            }
        }
        
        Debug.Log($"为合成台 {station.stationName} 设置了 {station.GetAvailableRecipes().Count} 个配方");
    }
    
    /// <summary>
    /// 保存合成系统数据
    /// </summary>
    public void SaveCraftingData()
    {
        // 这里可以实现保存逻辑，比如保存解锁的配方等
        // 暂时使用PlayerPrefs作为示例
        
        List<string> unlockedRecipes = new List<string>();
        foreach (var recipe in globalRecipes)
        {
            if (recipe != null && recipe.isUnlocked)
            {
                unlockedRecipes.Add(recipe.name);
            }
        }
        
        string unlockedRecipesData = string.Join(";", unlockedRecipes);
        PlayerPrefs.SetString("UnlockedRecipes", unlockedRecipesData);
        PlayerPrefs.Save();
        
        Debug.Log("合成系统数据已保存");
    }
    
    /// <summary>
    /// 加载合成系统数据
    /// </summary>
    public void LoadCraftingData()
    {
        string unlockedRecipesData = PlayerPrefs.GetString("UnlockedRecipes", "");
        
        if (!string.IsNullOrEmpty(unlockedRecipesData))
        {
            string[] unlockedRecipeNames = unlockedRecipesData.Split(';');
            
            foreach (var recipeName in unlockedRecipeNames)
            {
                var recipe = globalRecipes.Find(r => r.name == recipeName);
                if (recipe != null)
                {
                    recipe.isUnlocked = true;
                }
            }
            
            Debug.Log($"加载了 {unlockedRecipeNames.Length} 个解锁的配方");
        }
    }
}