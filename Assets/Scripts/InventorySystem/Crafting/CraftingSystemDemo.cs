using UnityEngine;
using System.Collections.Generic;

public class CraftingSystemDemo : MonoBehaviour
{
    [Header("演示设置")]
    public bool enableDemo = true;
    public List<Item> demoItems = new List<Item>();
    public List<CraftingRecipe> demoRecipes = new List<CraftingRecipe>();
    
    [Header("UI提示")]
    public GameObject helpPanel;
    public TMPro.TextMeshProUGUI helpText;
    
    private Inventory playerInventory;
    private CraftingManager craftingManager;
    private bool isHelpVisible = false;
    
    private void Start()
    {
        if (!enableDemo) return;
        
        playerInventory = FindObjectOfType<Inventory>();
        craftingManager = CraftingManager.Instance;
        
        SetupDemo();
        ShowHelp();
    }
    
    private void Update()
    {
        if (!enableDemo) return;
        
        // F1显示/隐藏帮助
        if (Input.GetKeyDown(KeyCode.F1))
        {
            ToggleHelp();
        }
        
        // F2添加演示物品
        if (Input.GetKeyDown(KeyCode.F2))
        {
            AddDemoItems();
        }
        
        // F3清空背包
        if (Input.GetKeyDown(KeyCode.F3))
        {
            ClearInventory();
        }
    }
    
    private void SetupDemo()
    {
        Debug.Log("合成系统演示已启动！");
        Debug.Log("按F1查看帮助，按F2添加演示物品，按F3清空背包");
        
        // 添加演示配方到合成管理器
        if (craftingManager != null)
        {
            foreach (var recipe in demoRecipes)
            {
                if (recipe != null)
                {
                    craftingManager.AddGlobalRecipe(recipe);
                }
            }
        }
        
        // 设置所有合成台的配方
        var craftingStations = FindObjectsOfType<CraftingStation>();
        foreach (var station in craftingStations)
        {
            if (craftingManager != null)
            {
                craftingManager.SetupDefaultCraftingStation(station);
            }
        }
    }
    
    private void AddDemoItems()
    {
        if (playerInventory == null)
        {
            Debug.LogWarning("未找到玩家背包！");
            return;
        }
        
        foreach (var item in demoItems)
        {
            if (item != null)
            {
                int amount = Random.Range(1, 6); // 随机添加1-5个
                playerInventory.AddItem(item, amount);
                Debug.Log($"添加了 {item.itemName} x{amount}");
            }
        }
        
        Debug.Log("演示物品已添加到背包！");
    }
    
    private void ClearInventory()
    {
        if (playerInventory == null)
        {
            Debug.LogWarning("未找到玩家背包！");
            return;
        }
        
        var slots = playerInventory.GetAllSlots();
        foreach (var slot in slots)
        {
            slot.ClearSlot();
        }
        
        playerInventory.TriggerInventoryChanged();
        Debug.Log("背包已清空！");
    }
    
    private void ShowHelp()
    {
        if (helpPanel != null)
        {
            helpPanel.SetActive(true);
            isHelpVisible = true;
        }
        
        if (helpText != null)
        {
            helpText.text = GetHelpText();
        }
    }
    
    private void ToggleHelp()
    {
        isHelpVisible = !isHelpVisible;
        
        if (helpPanel != null)
        {
            helpPanel.SetActive(isHelpVisible);
        }
    }
    
    private string GetHelpText()
    {
        return @"合成系统使用说明：

基本操作：
• Tab键 - 打开/关闭背包
• E键 - 与合成台交互
• C键 - 快速打开最近的合成台
• Esc键 - 关闭合成界面

演示快捷键：
• F1 - 显示/隐藏此帮助
• F2 - 添加演示物品到背包
• F3 - 清空背包

合成流程：
1. 靠近合成台按E键打开合成界面
2. 在下拉菜单中选择配方
3. 查看配方描述和材料需求
4. 确保背包中有足够材料
5. 点击合成按钮完成合成

配方编辑：
• 在Project窗口右键选择Create > Inventory > Crafting Recipe
• 或使用Assets菜单中的Crafting Recipe Wizard
• 设置配方名称、描述、材料和结果物品
• 将配方放入Resources/CraftingRecipes文件夹";
    }
    
    private void OnGUI()
    {
        if (!enableDemo) return;
        
        // 在屏幕右上角显示快捷键提示
        GUI.Box(new Rect(Screen.width - 200, 10, 190, 80), "合成系统演示");
        GUI.Label(new Rect(Screen.width - 190, 30, 180, 20), "F1: 帮助");
        GUI.Label(new Rect(Screen.width - 190, 45, 180, 20), "F2: 添加物品");
        GUI.Label(new Rect(Screen.width - 190, 60, 180, 20), "F3: 清空背包");
        GUI.Label(new Rect(Screen.width - 190, 75, 180, 20), "E: 合成台交互");
    }
}