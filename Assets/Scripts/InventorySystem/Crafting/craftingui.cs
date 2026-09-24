using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CraftingUI : MonoBehaviour
{
    [Header("UI面板")]
    public GameObject craftingPanel;
    public Button closeButton;
    
    [Header("配方选择")]
    public TMP_Dropdown recipeDropdown;
    public Image recipeIcon;
    
    [Header("配方信息")]
    public TextMeshProUGUI recipeNameText;
    public TextMeshProUGUI recipeDescriptionText;
    public Transform ingredientsParent;
    public GameObject ingredientUIPrefab;
    
    [Header("合成控制")]
    public Button craftButton;
    public TextMeshProUGUI craftButtonText;
    public Image resultItemIcon;
    public TextMeshProUGUI resultItemText;
    
    [Header("状态显示")]
    public TextMeshProUGUI statusText;
    public Color canCraftColor = Color.green;
    public Color cannotCraftColor = Color.red;
    
    [Header("输入设置")]
    public KeyCode closeKey = KeyCode.Escape;
    
    private CraftingStation currentStation;
    private List<CraftingRecipe> availableRecipes = new List<CraftingRecipe>();
    private CraftingRecipe selectedRecipe;
    private Inventory playerInventory;
    private List<GameObject> ingredientUIObjects = new List<GameObject>();
    
    private void Start()
    {
        playerInventory = FindObjectOfType<Inventory>();
        
        // 设置UI事件
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseCraftingUI);
            
        if (craftButton != null)
            craftButton.onClick.AddListener(OnCraftButtonClicked);
            
        if (recipeDropdown != null)
            recipeDropdown.onValueChanged.AddListener(OnRecipeSelected);
        
        // 初始状态关闭面板
        CloseCraftingUI();
    }
    
    private void Update()
    {
        if (craftingPanel.activeInHierarchy && Input.GetKeyDown(closeKey))
        {
            CloseCraftingUI();
        }
        
        // 实时更新合成状态
        if (selectedRecipe != null && craftingPanel.activeInHierarchy)
        {
            UpdateCraftingStatus();
        }
    }
    
    /// <summary>
    /// 打开合成UI
    /// </summary>
    public void OpenCraftingUI(CraftingStation station)
    {
        Debug.Log("[CraftingUI] 打开合成UI - " + station.stationName);
        currentStation = station;
        craftingPanel.SetActive(true);
        
        // 正确禁用玩家控制
        var inventoryManager = InventoryManager.Instance;
        if (inventoryManager != null && inventoryManager.playerController != null)
        {
            inventoryManager.playerController.DisableControl();
            Debug.Log("[CraftingUI] 已禁用玩家控制");
        }
        
        // 使用GameManager显示鼠标光标
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ShowCursor();
            Debug.Log("[CraftingUI] 通过GameManager显示光标");
        }
        else
        {
            // 备用方案
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("[CraftingUI] 直接显示光标（备用方案）");
        }
        
        LoadRecipes();
    }
    
    /// <summary>
    /// 关闭合成UI
    /// </summary>
    public void CloseCraftingUI()
    {
        Debug.Log("[CraftingUI] 关闭合成UI");
        craftingPanel.SetActive(false);
        currentStation = null;
        selectedRecipe = null;
        
        // 正确恢复玩家控制
        var inventoryManager = InventoryManager.Instance;
        if (inventoryManager != null && inventoryManager.playerController != null)
        {
            inventoryManager.playerController.EnableControl();
            Debug.Log("[CraftingUI] 已恢复玩家控制");
        }
        
        // 使用GameManager恢复光标状态（保持显示）
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ShowCursor(); // 保持光标显示，不隐藏
            Debug.Log("[CraftingUI] 通过GameManager保持光标显示");
        }
        else
        {
            // 备用方案 - 保持光标显示
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("[CraftingUI] 直接保持光标显示（备用方案）");
        }
        
        ClearIngredientUI();
    }
    
    /// <summary>
    /// 加载配方到下拉菜单
    /// </summary>
    private void LoadRecipes()
    {
        if (currentStation == null || recipeDropdown == null) return;
        
        availableRecipes = currentStation.GetAvailableRecipes();
        
        // 清空下拉菜单
        recipeDropdown.ClearOptions();
        
        if (availableRecipes.Count == 0)
        {
            recipeDropdown.options.Add(new TMP_Dropdown.OptionData("No Available Recipes"));
            recipeDropdown.interactable = false;
            return;
        }
        
        // 添加配方选项
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("Select Recipe..."));
        
        foreach (var recipe in availableRecipes)
        {
            if (recipe != null && recipe.isUnlocked)
            {
                string optionText = recipe.recipeName;
                if (recipe.recipeIcon != null)
                {
                    options.Add(new TMP_Dropdown.OptionData(optionText, recipe.recipeIcon));
                }
                else
                {
                    options.Add(new TMP_Dropdown.OptionData(optionText));
                }
            }
        }
        
        recipeDropdown.AddOptions(options);
        recipeDropdown.interactable = true;
        recipeDropdown.value = 0;
        
        // 清空当前选择
        ClearRecipeDisplay();
    }
    
    /// <summary>
    /// 配方选择事件
    /// </summary>
    private void OnRecipeSelected(int index)
    {
        if (index <= 0 || index > availableRecipes.Count)
        {
            selectedRecipe = null;
            ClearRecipeDisplay();
            return;
        }
        
        selectedRecipe = availableRecipes[index - 1];
        DisplayRecipe(selectedRecipe);
    }
    
    /// <summary>
    /// 显示选中的配方信息
    /// </summary>
    private void DisplayRecipe(CraftingRecipe recipe)
    {
        if (recipe == null) return;
        
        // 显示配方基本信息
        if (recipeNameText != null)
            recipeNameText.text = recipe.recipeName;
            
        if (recipeDescriptionText != null)
            recipeDescriptionText.text = recipe.description;
            
        if (recipeIcon != null)
        {
            if (recipe.recipeIcon != null)
            {
                recipeIcon.sprite = recipe.recipeIcon;
                recipeIcon.color = Color.white;
            }
            else
            {
                recipeIcon.sprite = null;
                recipeIcon.color = new Color(1, 1, 1, 0);
            }
        }
        
        // 显示合成结果
        if (resultItemIcon != null && recipe.resultItem != null)
        {
            resultItemIcon.sprite = recipe.resultItem.icon;
            resultItemIcon.color = Color.white;
        }
        
        if (resultItemText != null && recipe.resultItem != null)
        {
            resultItemText.text = $"{recipe.resultItem.itemName} x{recipe.resultAmount}";
        }
        
        // 显示材料需求
        DisplayIngredients(recipe);
        
        // 更新合成状态
        UpdateCraftingStatus();
    }
    
    /// <summary>
    /// 显示配方材料
    /// </summary>
    private void DisplayIngredients(CraftingRecipe recipe)
    {
        ClearIngredientUI();
        
        if (ingredientsParent == null || ingredientUIPrefab == null) return;
        
        foreach (var ingredient in recipe.ingredients)
        {
            GameObject ingredientObj = Instantiate(ingredientUIPrefab, ingredientsParent);
            ingredientUIObjects.Add(ingredientObj);
            
            // 设置材料UI
            var ingredientUI = ingredientObj.GetComponent<CraftingIngredientUI>();
            if (ingredientUI != null)
            {
                int currentAmount = playerInventory != null ? playerInventory.GetItemCount(ingredient.item) : 0;
                ingredientUI.SetIngredient(ingredient, currentAmount);
            }
        }
        
        // 通知固定布局脚本应用布局
        var fixedLayout = FindObjectOfType<IngredientUIFixedLayout>();
        if (fixedLayout != null)
        {
            fixedLayout.OnIngredientUICreated();
        }
    }
    
    /// <summary>
    /// 清空材料UI
    /// </summary>
    private void ClearIngredientUI()
    {
        foreach (var obj in ingredientUIObjects)
        {
            if (obj != null)
                DestroyImmediate(obj);
        }
        ingredientUIObjects.Clear();
    }
    
    /// <summary>
    /// 清空配方显示
    /// </summary>
    private void ClearRecipeDisplay()
    {
        if (recipeNameText != null)
            recipeNameText.text = "";
            
        if (recipeDescriptionText != null)
            recipeDescriptionText.text = "Select a recipe to view details";
            
        if (recipeIcon != null)
        {
            recipeIcon.sprite = null;
            recipeIcon.color = new Color(1, 1, 1, 0);
        }
        
        if (resultItemIcon != null)
        {
            resultItemIcon.sprite = null;
            resultItemIcon.color = new Color(1, 1, 1, 0);
        }
        
        if (resultItemText != null)
            resultItemText.text = "";
            
        if (craftButton != null)
            craftButton.interactable = false;
            
        if (craftButtonText != null)
            craftButtonText.text = "Craft";
            
        if (statusText != null)
            statusText.text = "";
            
        ClearIngredientUI();
    }
    
    /// <summary>
    /// 更新合成状态
    /// </summary>
    private void UpdateCraftingStatus()
    {
        if (selectedRecipe == null || playerInventory == null)
        {
            if (craftButton != null)
                craftButton.interactable = false;
            return;
        }
        
        bool canCraft = selectedRecipe.CanCraft(playerInventory);
        
        // 更新合成按钮
        if (craftButton != null)
        {
            craftButton.interactable = canCraft;
        }
        
        // 更新状态文本
        if (statusText != null)
        {
            if (canCraft)
            {
                statusText.text = "Can Craft";
                statusText.color = canCraftColor;
            }
            else
            {
                var missingIngredients = selectedRecipe.GetMissingIngredients(playerInventory);
                if (missingIngredients.Count > 0)
                {
                    statusText.text = "Insufficient Materials";
                    statusText.color = cannotCraftColor;
                }
                else
                {
                    statusText.text = "Cannot Craft";
                    statusText.color = cannotCraftColor;
                }
            }
        }
        
        // 更新材料UI状态
        UpdateIngredientUIStatus();
    }
    
    /// <summary>
    /// 更新材料UI状态
    /// </summary>
    private void UpdateIngredientUIStatus()
    {
        if (selectedRecipe == null) return;
        
        for (int i = 0; i < ingredientUIObjects.Count && i < selectedRecipe.ingredients.Count; i++)
        {
            var ingredientUI = ingredientUIObjects[i].GetComponent<CraftingIngredientUI>();
            if (ingredientUI != null)
            {
                var ingredient = selectedRecipe.ingredients[i];
                int currentAmount = playerInventory != null ? playerInventory.GetItemCount(ingredient.item) : 0;
                ingredientUI.UpdateAmount(currentAmount);
            }
        }
    }
    
    /// <summary>
    /// 合成按钮点击事件
    /// </summary>
    private void OnCraftButtonClicked()
    {
        if (selectedRecipe == null || currentStation == null) return;
        
        bool success = currentStation.CraftItem(selectedRecipe);
        
        if (success)
        {
            // 合成成功，更新UI状态
            UpdateCraftingStatus();
        }
    }
}