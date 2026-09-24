using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tooltip系统调试工具
/// 用于诊断和修复Tooltip显示问题
/// </summary>
public class TooltipDebugger : MonoBehaviour
{
    [Header("调试设置")]
    public bool enableDebugLogs = true;
    
    private InventoryUI inventoryUI;
    private ItemTooltip itemTooltip;
    
    private void Start()
    {
        DiagnoseTooltipSystem();
    }
    
    [ContextMenu("诊断Tooltip系统")]
    public void DiagnoseTooltipSystem()
    {
        Debug.Log("=== Tooltip系统诊断开始 ===");
        
        // 查找组件
        inventoryUI = FindObjectOfType<InventoryUI>();
        itemTooltip = FindObjectOfType<ItemTooltip>();
        
        CheckTooltipComponents();
        CheckTooltipReferences();
        
        Debug.Log("=== Tooltip系统诊断完成 ===");
    }
    
    [ContextMenu("修复Tooltip引用")]
    public void FixTooltipReferences()
    {
        Debug.Log("=== 开始修复Tooltip引用 ===");
        
        inventoryUI = FindObjectOfType<InventoryUI>();
        itemTooltip = FindObjectOfType<ItemTooltip>();
        
        if (inventoryUI == null)
        {
            Debug.LogError("未找到InventoryUI组件！");
            return;
        }
        
        if (itemTooltip == null)
        {
            Debug.LogWarning("未找到ItemTooltip组件，正在创建...");
            CreateTooltip();
        }
        else
        {
            // 修复引用
            inventoryUI.itemTooltip = itemTooltip;
            Debug.Log("✅ 修复了InventoryUI.itemTooltip引用");
        }
        
        // 验证修复结果
        DiagnoseTooltipSystem();
    }
    
    [ContextMenu("创建Tooltip")]
    public void CreateTooltip()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("未找到Canvas，无法创建Tooltip！");
            return;
        }
        
        // 创建提示框面板
        GameObject tooltipPanel = new GameObject("ItemTooltip");
        tooltipPanel.transform.SetParent(canvas.transform, false);
        
        // 设置RectTransform
        RectTransform tooltipRect = tooltipPanel.AddComponent<RectTransform>();
        tooltipRect.anchorMin = Vector2.zero;
        tooltipRect.anchorMax = Vector2.zero;
        tooltipRect.pivot = Vector2.zero;
        tooltipRect.sizeDelta = new Vector2(250, 150);
        
        // 添加背景图片
        Image tooltipImage = tooltipPanel.AddComponent<Image>();
        tooltipImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        
        // 创建垂直布局组
        VerticalLayoutGroup layoutGroup = tooltipPanel.AddComponent<VerticalLayoutGroup>();
        layoutGroup.padding = new RectOffset(10, 10, 10, 10);
        layoutGroup.spacing = 5;
        layoutGroup.childControlHeight = false;
        layoutGroup.childControlWidth = true;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = true;
        
        // 添加ContentSizeFitter
        ContentSizeFitter sizeFitter = tooltipPanel.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // 创建文本组件
        GameObject nameText = CreateUIText("ItemName", tooltipPanel.transform, "Item Name");
        GameObject typeText = CreateUIText("ItemType", tooltipPanel.transform, "Item Type");
        GameObject levelText = CreateUIText("ItemLevel", tooltipPanel.transform, "Level");
        GameObject descText = CreateUIText("ItemDescription", tooltipPanel.transform, "Item Description");
        
        // 设置文本样式
        SetupTooltipText(nameText, 16, FontStyles.Bold);
        SetupTooltipText(typeText, 12, FontStyles.Normal);
        SetupTooltipText(levelText, 12, FontStyles.Normal);
        SetupTooltipText(descText, 10, FontStyles.Normal);
        
        // 添加ItemTooltip组件
        ItemTooltip tooltip = tooltipPanel.AddComponent<ItemTooltip>();
        tooltip.itemNameText = nameText.GetComponent<TextMeshProUGUI>();
        tooltip.itemTypeText = typeText.GetComponent<TextMeshProUGUI>();
        tooltip.itemLevelText = levelText.GetComponent<TextMeshProUGUI>();
        tooltip.itemDescriptionText = descText.GetComponent<TextMeshProUGUI>();
        tooltip.backgroundImage = tooltipImage;
        
        // 关联到InventoryUI
        if (inventoryUI != null)
        {
            inventoryUI.itemTooltip = tooltip;
            Debug.Log("✅ 成功创建Tooltip并关联到InventoryUI");
        }
        
        itemTooltip = tooltip;
        
        // 初始状态隐藏
        tooltipPanel.SetActive(false);
    }
    
    private GameObject CreateUIText(string name, Transform parent, string text)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);
        
        TextMeshProUGUI textComponent = textObj.AddComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.fontSize = 14;
        textComponent.color = Color.white;
        textComponent.alignment = TextAlignmentOptions.Left;
        
        // 添加LayoutElement
        LayoutElement layoutElement = textObj.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;
        
        return textObj;
    }
    
    private void SetupTooltipText(GameObject textObj, float fontSize, FontStyles fontStyle)
    {
        TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();
        if (text != null)
        {
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
        }
    }
    
    private void CheckTooltipComponents()
    {
        Debug.Log("--- Tooltip组件检查 ---");
        
        Debug.Log($"InventoryUI: {(inventoryUI != null ? "找到" : "未找到")}");
        Debug.Log($"ItemTooltip: {(itemTooltip != null ? "找到" : "未找到")}");
        
        if (itemTooltip != null)
        {
            Debug.Log($"Tooltip GameObject: {itemTooltip.gameObject.name}");
            Debug.Log($"Tooltip Active: {itemTooltip.gameObject.activeInHierarchy}");
            Debug.Log($"Tooltip Enabled: {itemTooltip.enabled}");
            
            // 检查Tooltip的UI组件
            Debug.Log($"  itemNameText: {(itemTooltip.itemNameText != null ? "找到" : "未找到")}");
            Debug.Log($"  itemTypeText: {(itemTooltip.itemTypeText != null ? "找到" : "未找到")}");
            Debug.Log($"  itemLevelText: {(itemTooltip.itemLevelText != null ? "找到" : "未找到")}");
            Debug.Log($"  itemDescriptionText: {(itemTooltip.itemDescriptionText != null ? "找到" : "未找到")}");
            Debug.Log($"  backgroundImage: {(itemTooltip.backgroundImage != null ? "找到" : "未找到")}");
        }
    }
    
    private void CheckTooltipReferences()
    {
        Debug.Log("--- Tooltip引用检查 ---");
        
        if (inventoryUI == null)
        {
            Debug.LogError("InventoryUI组件未找到！");
            return;
        }
        
        if (inventoryUI.itemTooltip == null)
        {
            Debug.LogError("InventoryUI.itemTooltip引用为null！");
            
            if (itemTooltip != null)
            {
                Debug.Log("发现ItemTooltip组件存在，但引用丢失，正在修复...");
                inventoryUI.itemTooltip = itemTooltip;
                Debug.Log("✅ 修复了InventoryUI.itemTooltip引用");
            }
        }
        else
        {
            Debug.Log("✅ InventoryUI.itemTooltip引用正常");
            
            if (inventoryUI.itemTooltip == itemTooltip)
            {
                Debug.Log("✅ 引用指向正确的ItemTooltip组件");
            }
            else
            {
                Debug.LogWarning("⚠️ InventoryUI.itemTooltip指向了不同的ItemTooltip组件");
            }
        }
    }
    
    [ContextMenu("测试Tooltip显示")]
    public void TestTooltipDisplay()
    {
        if (inventoryUI == null || inventoryUI.itemTooltip == null)
        {
            Debug.LogError("无法测试Tooltip：InventoryUI或itemTooltip为null");
            return;
        }
        
        // 创建一个测试物品
        Item testItem = ScriptableObject.CreateInstance<Item>();
        testItem.itemName = "Test Item";
        testItem.description = "This is a test item used to verify the Tooltip display functionality.";
        testItem.itemType = ItemType.Misc;
        testItem.itemLevel = 1;
        
        Debug.Log("显示测试Tooltip...");
        inventoryUI.ShowItemTooltip(testItem, Input.mousePosition);
        
        // 3秒后隐藏
        Invoke("HideTestTooltip", 3f);
    }
    
    private void HideTestTooltip()
    {
        if (inventoryUI != null)
        {
            inventoryUI.HideItemTooltip();
            Debug.Log("隐藏测试Tooltip");
        }
    }
    
    private void Update()
    {
        // 快捷键调试
        if (Input.GetKeyDown(KeyCode.F4))
        {
            DiagnoseTooltipSystem();
        }
        
        if (Input.GetKeyDown(KeyCode.F5))
        {
            FixTooltipReferences();
        }
        
        if (Input.GetKeyDown(KeyCode.F6))
        {
            TestTooltipDisplay();
        }
    }
    
    private void OnGUI()
    {
        if (!enableDebugLogs) return;
        
        GUILayout.BeginArea(new Rect(320, 10, 300, 200));
        GUILayout.Label("Tooltip调试工具", GUI.skin.box);
        GUILayout.Label("F4: 诊断Tooltip系统");
        GUILayout.Label("F5: 修复Tooltip引用");
        GUILayout.Label("F6: 测试Tooltip显示");
        
        if (inventoryUI != null)
        {
            GUILayout.Label($"ItemTooltip: {(inventoryUI.itemTooltip != null ? "✅" : "❌")}");
        }
        
        GUILayout.EndArea();
    }
}