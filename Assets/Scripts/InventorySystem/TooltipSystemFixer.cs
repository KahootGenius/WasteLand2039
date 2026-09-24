using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tooltip系统自动修复工具
/// 检测并修复所有可能导致Tooltip不显示的问题
/// </summary>
public class TooltipSystemFixer : MonoBehaviour
{
    [Header("自动修复设置")]
    public bool autoFixOnStart = true;
    public bool enableDebugLogs = true;
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            Invoke("FixTooltipSystem", 0.5f); // 延迟执行确保所有组件都已初始化
        }
    }
    
    [ContextMenu("修复Tooltip系统")]
    public void FixTooltipSystem()
    {
        Debug.Log("=== 开始修复Tooltip系统 ===");
        
        bool hasIssues = false;
        
        // 1. 检查并修复InventoryUI引用
        hasIssues |= FixInventoryUIReferences();
        
        // 2. 检查并修复ItemTooltip组件
        hasIssues |= FixItemTooltipComponent();
        
        // 3. 检查并修复Canvas设置
        hasIssues |= FixCanvasSettings();
        
        // 4. 验证修复结果
        ValidateTooltipSystem();
        
        if (!hasIssues)
        {
            Debug.Log("✅ Tooltip系统检查完成，未发现问题");
        }
        else
        {
            Debug.Log("🔧 Tooltip系统修复完成");
        }
        
        Debug.Log("=== Tooltip系统修复结束 ===");
    }
    
    private bool FixInventoryUIReferences()
    {
        bool hasIssues = false;
        
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI == null)
        {
            Debug.LogError("❌ 未找到InventoryUI组件！请先设置背包系统");
            return true;
        }
        
        if (inventoryUI.itemTooltip == null)
        {
            Debug.LogWarning("⚠️ InventoryUI.itemTooltip引用为null，正在修复...");
            
            ItemTooltip tooltip = FindObjectOfType<ItemTooltip>();
            if (tooltip != null)
            {
                inventoryUI.itemTooltip = tooltip;
                Debug.Log("✅ 修复了InventoryUI.itemTooltip引用");
                hasIssues = true;
            }
            else
            {
                Debug.LogError("❌ 未找到ItemTooltip组件，需要创建");
                CreateItemTooltip();
                hasIssues = true;
            }
        }
        else
        {
            if (enableDebugLogs)
                Debug.Log("✅ InventoryUI.itemTooltip引用正常");
        }
        
        return hasIssues;
    }
    
    private bool FixItemTooltipComponent()
    {
        bool hasIssues = false;
        
        ItemTooltip tooltip = FindObjectOfType<ItemTooltip>();
        if (tooltip == null)
        {
            Debug.LogWarning("⚠️ 未找到ItemTooltip组件，正在创建...");
            CreateItemTooltip();
            return true;
        }
        
        // 检查ItemTooltip的UI组件引用
        bool needsRepair = false;
        
        if (tooltip.itemNameText == null)
        {
            Debug.LogWarning("⚠️ ItemTooltip.itemNameText为null");
            needsRepair = true;
        }
        
        if (tooltip.itemTypeText == null)
        {
            Debug.LogWarning("⚠️ ItemTooltip.itemTypeText为null");
            needsRepair = true;
        }
        
        if (tooltip.itemLevelText == null)
        {
            Debug.LogWarning("⚠️ ItemTooltip.itemLevelText为null");
            needsRepair = true;
        }
        
        if (tooltip.itemDescriptionText == null)
        {
            Debug.LogWarning("⚠️ ItemTooltip.itemDescriptionText为null");
            needsRepair = true;
        }
        
        if (tooltip.backgroundImage == null)
        {
            Debug.LogWarning("⚠️ ItemTooltip.backgroundImage为null");
            needsRepair = true;
        }
        
        if (needsRepair)
        {
            RepairItemTooltipReferences(tooltip);
            hasIssues = true;
        }
        else
        {
            if (enableDebugLogs)
                Debug.Log("✅ ItemTooltip组件引用正常");
        }
        
        return hasIssues;
    }
    
    private bool FixCanvasSettings()
    {
        bool hasIssues = false;
        
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("❌ 未找到Canvas组件！");
            return true;
        }
        
        // 检查Canvas设置
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning("⚠️ Canvas renderMode不是ScreenSpaceOverlay，可能影响Tooltip显示");
        }
        
        // 检查GraphicRaycaster
        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            Debug.LogWarning("⚠️ Canvas缺少GraphicRaycaster组件，正在添加...");
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            hasIssues = true;
        }
        
        return hasIssues;
    }
    
    private void CreateItemTooltip()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("❌ 无法创建ItemTooltip：未找到Canvas");
            return;
        }
        
        Debug.Log("🔧 正在创建ItemTooltip组件...");
        
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
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI != null)
        {
            inventoryUI.itemTooltip = tooltip;
            Debug.Log("✅ 成功创建ItemTooltip并关联到InventoryUI");
        }
        
        // 初始状态隐藏
        tooltipPanel.SetActive(false);
    }
    
    private void RepairItemTooltipReferences(ItemTooltip tooltip)
    {
        Debug.Log("🔧 正在修复ItemTooltip组件引用...");
        
        Transform tooltipTransform = tooltip.transform;
        
        // 查找或创建文本组件
        if (tooltip.itemNameText == null)
        {
            Transform nameTransform = tooltipTransform.Find("ItemName");
            if (nameTransform != null)
            {
                tooltip.itemNameText = nameTransform.GetComponent<TextMeshProUGUI>();
            }
        }
        
        if (tooltip.itemTypeText == null)
        {
            Transform typeTransform = tooltipTransform.Find("ItemType");
            if (typeTransform != null)
            {
                tooltip.itemTypeText = typeTransform.GetComponent<TextMeshProUGUI>();
            }
        }
        
        if (tooltip.itemLevelText == null)
        {
            Transform levelTransform = tooltipTransform.Find("ItemLevel");
            if (levelTransform != null)
            {
                tooltip.itemLevelText = levelTransform.GetComponent<TextMeshProUGUI>();
            }
        }
        
        if (tooltip.itemDescriptionText == null)
        {
            Transform descTransform = tooltipTransform.Find("ItemDescription");
            if (descTransform != null)
            {
                tooltip.itemDescriptionText = descTransform.GetComponent<TextMeshProUGUI>();
            }
        }
        
        if (tooltip.backgroundImage == null)
        {
            tooltip.backgroundImage = tooltip.GetComponent<Image>();
        }
        
        Debug.Log("✅ ItemTooltip组件引用修复完成");
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
    
    private void ValidateTooltipSystem()
    {
        Debug.Log("--- Tooltip系统验证 ---");
        
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        ItemTooltip tooltip = FindObjectOfType<ItemTooltip>();
        
        if (inventoryUI == null)
        {
            Debug.LogError("❌ 验证失败：未找到InventoryUI");
            return;
        }
        
        if (tooltip == null)
        {
            Debug.LogError("❌ 验证失败：未找到ItemTooltip");
            return;
        }
        
        if (inventoryUI.itemTooltip == null)
        {
            Debug.LogError("❌ 验证失败：InventoryUI.itemTooltip为null");
            return;
        }
        
        if (inventoryUI.itemTooltip != tooltip)
        {
            Debug.LogWarning("⚠️ 验证警告：InventoryUI.itemTooltip指向了不同的组件");
        }
        
        // 检查UI组件
        bool allComponentsValid = true;
        allComponentsValid &= tooltip.itemNameText != null;
        allComponentsValid &= tooltip.itemTypeText != null;
        allComponentsValid &= tooltip.itemLevelText != null;
        allComponentsValid &= tooltip.itemDescriptionText != null;
        allComponentsValid &= tooltip.backgroundImage != null;
        
        if (allComponentsValid)
        {
            Debug.Log("✅ Tooltip系统验证通过！");
        }
        else
        {
            Debug.LogError("❌ Tooltip系统验证失败：部分UI组件引用缺失");
        }
    }
    
    [ContextMenu("测试Tooltip显示")]
    public void TestTooltipDisplay()
    {
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI == null || inventoryUI.itemTooltip == null)
        {
            Debug.LogError("无法测试Tooltip：组件引用缺失");
            return;
        }
        
        // 创建测试物品
        Item testItem = ScriptableObject.CreateInstance<Item>();
        testItem.itemName = "Repair Test Item";
        testItem.description = "This is a test item used to verify if the Tooltip repair was successful. If you can see this tooltip, the repair was successful!";
        testItem.itemType = ItemType.Misc;
        testItem.itemLevel = 1;
        
        Debug.Log("显示测试Tooltip...");
        inventoryUI.ShowItemTooltip(testItem, Input.mousePosition);
        
        // 3秒后隐藏
        Invoke("HideTestTooltip", 3f);
    }
    
    private void HideTestTooltip()
    {
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI != null)
        {
            inventoryUI.HideItemTooltip();
            Debug.Log("隐藏测试Tooltip");
        }
    }
    
    private void Update()
    {
        // 快捷键
        if (Input.GetKeyDown(KeyCode.F7))
        {
            FixTooltipSystem();
        }
        
        if (Input.GetKeyDown(KeyCode.F8))
        {
            TestTooltipDisplay();
        }
    }
    
    private void OnGUI()
    {
        if (!enableDebugLogs) return;
        
        GUILayout.BeginArea(new Rect(10, 320, 300, 150));
        GUILayout.Label("Tooltip修复工具", GUI.skin.box);
        GUILayout.Label("F7: 修复Tooltip系统");
        GUILayout.Label("F8: 测试Tooltip显示");
        
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI != null)
        {
            string status = inventoryUI.itemTooltip != null ? "✅ 正常" : "❌ 异常";
            GUILayout.Label($"Tooltip状态: {status}");
        }
        
        GUILayout.EndArea();
    }
}