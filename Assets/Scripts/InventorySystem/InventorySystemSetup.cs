using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 背包系统快速设置工具
/// 在编辑器中右键菜单选择"Inventory/Setup Inventory System"来自动创建UI
/// </summary>
public class InventorySystemSetup : MonoBehaviour
{
    [Header("自动设置配置")]
    public bool createInventoryManager = true;
    public bool createInventoryUI = true;
    public bool createItemTooltip = true;
    public bool setupCanvas = true;
    
    [Header("UI配置")]
    public int inventorySize = 20;
    public int slotsPerRow = 5;
    public Vector2 slotSize = new Vector2(64, 64);
    public Vector2 slotSpacing = new Vector2(10, 10);
    
    [Header("资源路径")]
    public string inventoryBackgroundPath = "InventoryUI/InventoryRect";
    public string slotNormalPath = "InventoryUI/inventorySlot (1)";
    public string slotHighlightPath = "InventoryUI/inventorySlot (1)"; // 可以是不同的高亮图片
    
    [ContextMenu("Setup Inventory System")]
    public void SetupInventorySystem()
    {
        Debug.Log("开始设置背包系统...");
        
        // 1. 创建或找到Canvas
        Canvas canvas = SetupCanvas();
        
        // 2. 创建InventoryManager
        if (createInventoryManager)
        {
            SetupInventoryManager();
        }
        
        // 3. 创建背包UI
        if (createInventoryUI)
        {
            SetupInventoryUI(canvas);
        }
        
        // 4. 物品提示框已在SetupInventoryUI中创建和关联
        
        // 5. 强制重新建立所有引用关系
        ForceRebuildReferences();
        
        Debug.Log("背包系统设置完成！");
    }
    
    [ContextMenu("Force Rebuild References")]
    public void ForceRebuildReferences()
    {
        Debug.Log("=== 开始重建背包系统引用关系 ===");
        
        // 查找所有相关组件
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        Inventory inventory = FindObjectOfType<Inventory>();
        ItemTooltip tooltip = FindObjectOfType<ItemTooltip>();
        
        // 调试信息
        Debug.Log($"找到的组件: Manager={manager != null}, UI={inventoryUI != null}, Inventory={inventory != null}, Tooltip={tooltip != null}");
        
        if (manager == null)
        {
            Debug.LogError("未找到InventoryManager！请先运行Setup Inventory System");
            return;
        }
        
        if (inventoryUI == null)
        {
            Debug.LogError("未找到InventoryUI！请先运行Setup Inventory System");
            return;
        }
        
        // 确保InventoryPanel处于启用状态以便初始化
        if (inventoryUI.inventoryPanel != null)
        {
            inventoryUI.inventoryPanel.SetActive(true);
        }
        
        // 重建InventoryManager的引用
        if (inventory == null)
        {
            inventory = manager.GetComponent<Inventory>();
        }
        
        manager.inventory = inventory;
        manager.inventoryUI = inventoryUI;
        
        // 重建InventoryUI的引用
        if (tooltip != null)
        {
            inventoryUI.itemTooltip = tooltip;
        }
        
        // 强制初始化InventoryUI（这会在最后调用CloseInventory禁用面板）
        if (inventoryUI != null)
        {
            inventoryUI.Initialize();
            Debug.Log("InventoryUI已重新初始化");
        }
        
        // 让InventoryManager重新查找InventoryUI引用
        if (manager != null)
        {
            manager.RefreshInventoryUIReference();
        }
        
        Debug.Log("=== 引用关系重建完成 ===");
    }
    
    private Canvas SetupCanvas()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        
        if (canvas == null && setupCanvas)
        {
            // 创建Canvas
            GameObject canvasObj = new GameObject("Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            // 添加CanvasScaler
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            
            // 添加GraphicRaycaster
            canvasObj.AddComponent<GraphicRaycaster>();
            
            Debug.Log("创建了新的Canvas");
        }
        
        return canvas;
    }
    
    private void SetupInventoryManager()
    {
        // 查找现有的InventoryManager
        InventoryManager existingManager = FindObjectOfType<InventoryManager>();
        if (existingManager != null)
        {
            Debug.Log("找到现有的InventoryManager");
            return;
        }
        
        // 创建InventoryManager
        GameObject managerObj = new GameObject("InventoryManager");
        InventoryManager manager = managerObj.AddComponent<InventoryManager>();
        
        // 添加Inventory组件
        Inventory inventory = managerObj.AddComponent<Inventory>();
        inventory.inventorySize = inventorySize;
        
        manager.inventory = inventory;
        
        Debug.Log("创建了InventoryManager和Inventory组件");
    }
    
    private void SetupInventoryUI(Canvas canvas)
    {
        // 创建背包面板
        GameObject inventoryPanel = CreateUIPanel("InventoryPanel", canvas.transform);
        
        // 设置面板属性
        RectTransform panelRect = inventoryPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        
        // 计算面板大小
        int rows = Mathf.CeilToInt((float)inventorySize / slotsPerRow);
        Vector2 panelSize = new Vector2(
            slotsPerRow * slotSize.x + (slotsPerRow - 1) * slotSpacing.x + 40,
            rows * slotSize.y + (rows - 1) * slotSpacing.y + 80
        );
        panelRect.sizeDelta = panelSize;
        
        // 设置背景图片
        Image panelImage = inventoryPanel.GetComponent<Image>();
        Sprite backgroundSprite = Resources.Load<Sprite>(inventoryBackgroundPath);
        if (backgroundSprite != null)
        {
            panelImage.sprite = backgroundSprite;
            panelImage.type = Image.Type.Sliced;
        }
        
        // 创建标题
        GameObject titleObj = CreateUIText("Title", inventoryPanel.transform, "Inventory");
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -10);
        titleRect.sizeDelta = new Vector2(0, 30);
        
        // 创建关闭按钮
        GameObject closeButton = CreateUIButton("CloseButton", inventoryPanel.transform, "X");
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1, 1);
        closeRect.anchorMax = new Vector2(1, 1);
        closeRect.pivot = new Vector2(1, 1);
        closeRect.anchoredPosition = new Vector2(-10, -10);
        closeRect.sizeDelta = new Vector2(30, 30);
        
        // 创建槽位容器
        GameObject slotsContainer = new GameObject("SlotsContainer");
        slotsContainer.transform.SetParent(inventoryPanel.transform, false);
        RectTransform containerRect = slotsContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = new Vector2(0, -15);
        
        // 创建槽位预制体
        GameObject slotPrefab = CreateSlotPrefab();
        
        // 添加InventoryUI组件
        InventoryUI inventoryUI = inventoryPanel.AddComponent<InventoryUI>();
        inventoryUI.inventoryPanel = inventoryPanel;
        inventoryUI.slotsParent = slotsContainer.transform;
        inventoryUI.slotPrefab = slotPrefab;
        inventoryUI.closeButton = closeButton.GetComponent<Button>();
        inventoryUI.slotsPerRow = slotsPerRow;
        inventoryUI.slotSize = slotSize;
        inventoryUI.slotSpacing = slotSpacing;
        
        // 先创建ItemTooltip，然后关联到InventoryUI
        SetupItemTooltip(canvas);
        ItemTooltip tooltip = FindObjectOfType<ItemTooltip>();
        if (tooltip != null)
        {
            inventoryUI.itemTooltip = tooltip;
            Debug.Log("✅ 成功关联ItemTooltip到InventoryUI");
        }
        else
        {
            Debug.LogError("❌ 未找到ItemTooltip组件！");
        }
        
        // 关联到InventoryManager
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        if (manager != null)
        {
            manager.inventoryUI = inventoryUI;
            Debug.Log("成功关联InventoryUI到InventoryManager");
        }
        else
        {
            Debug.LogError("未找到InventoryManager，无法关联InventoryUI！");
        }
        
        // 确保InventoryPanel在初始化前保持启用状态
        inventoryPanel.SetActive(true);
        
        // 强制重新初始化InventoryUI（这会在最后调用CloseInventory禁用面板）
        if (inventoryUI != null)
        {
            inventoryUI.Initialize();
        }
        
        // 让InventoryManager重新查找InventoryUI引用
        if (manager != null)
        {
            manager.RefreshInventoryUIReference();
        }
        
        Debug.Log("创建了背包UI");
    }
    
    private GameObject CreateSlotPrefab()
    {
        // 创建槽位预制体
        GameObject slotObj = new GameObject("InventorySlot");
        RectTransform slotRect = slotObj.AddComponent<RectTransform>();
        slotRect.sizeDelta = slotSize;
        
        // 背景图片
        Image slotImage = slotObj.AddComponent<Image>();
        Sprite slotSprite = Resources.Load<Sprite>(slotNormalPath);
        if (slotSprite != null)
        {
            slotImage.sprite = slotSprite;
        }
        
        // 物品图标
        GameObject iconObj = new GameObject("ItemIcon");
        iconObj.transform.SetParent(slotObj.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.sizeDelta = Vector2.zero;
        iconRect.anchoredPosition = Vector2.zero;
        
        Image iconImage = iconObj.AddComponent<Image>();
        iconImage.color = new Color(1, 1, 1, 0); // 初始透明
        
        // 数量文本
        GameObject amountObj = CreateUIText("AmountText", slotObj.transform, "");
        RectTransform amountRect = amountObj.GetComponent<RectTransform>();
        amountRect.anchorMin = new Vector2(1, 0);
        amountRect.anchorMax = new Vector2(1, 0);
        amountRect.pivot = new Vector2(1, 0);
        amountRect.anchoredPosition = new Vector2(-5, 5);
        amountRect.sizeDelta = new Vector2(30, 20);
        
        TextMeshProUGUI amountText = amountObj.GetComponent<TextMeshProUGUI>();
        amountText.fontSize = 12;
        amountText.alignment = TextAlignmentOptions.BottomRight;
        
        // 添加InventorySlotUI组件
        InventorySlotUI slotUI = slotObj.AddComponent<InventorySlotUI>();
        slotUI.slotBackground = slotImage;
        slotUI.itemIcon = iconImage;
        slotUI.amountText = amountText;
        slotUI.normalSlotSprite = slotSprite;
        slotUI.highlightedSlotSprite = Resources.Load<Sprite>(slotHighlightPath);
        
        return slotObj;
    }
    
    private void SetupItemTooltip(Canvas canvas)
    {
        // 创建提示框面板
        GameObject tooltipPanel = CreateUIPanel("ItemTooltip", canvas.transform);
        
        // 设置提示框属性
        RectTransform tooltipRect = tooltipPanel.GetComponent<RectTransform>();
        tooltipRect.anchorMin = Vector2.zero;
        tooltipRect.anchorMax = Vector2.zero;
        tooltipRect.pivot = Vector2.zero;
        tooltipRect.sizeDelta = new Vector2(250, 150);
        
        // 设置背景
        Image tooltipImage = tooltipPanel.GetComponent<Image>();
        Sprite backgroundSprite = Resources.Load<Sprite>(inventoryBackgroundPath);
        if (backgroundSprite != null)
        {
            tooltipImage.sprite = backgroundSprite;
            tooltipImage.type = Image.Type.Sliced;
        }
        
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
        
        // 初始状态隐藏
        tooltipPanel.SetActive(false);
        
        Debug.Log("创建了物品提示框");
    }
    
    private GameObject CreateUIPanel(string name, Transform parent)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        
        RectTransform rect = panel.AddComponent<RectTransform>();
        Image image = panel.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        return panel;
    }
    
    private GameObject CreateUIText(string name, Transform parent, string text)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);
        
        RectTransform rect = textObj.AddComponent<RectTransform>();
        TextMeshProUGUI textComponent = textObj.AddComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.fontSize = 14;
        textComponent.color = Color.white;
        
        return textObj;
    }
    
    private GameObject CreateUIButton(string name, Transform parent, string text)
    {
        GameObject buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);
        
        RectTransform rect = buttonObj.AddComponent<RectTransform>();
        Image image = buttonObj.AddComponent<Image>();
        Button button = buttonObj.AddComponent<Button>();
        
        // 创建按钮文本
        GameObject textObj = CreateUIText("Text", buttonObj.transform, text);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;
        
        TextMeshProUGUI textComponent = textObj.GetComponent<TextMeshProUGUI>();
        textComponent.alignment = TextAlignmentOptions.Center;
        
        return buttonObj;
    }
    
    private void SetupTooltipText(GameObject textObj, float fontSize, FontStyles fontStyle)
    {
        TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        
        // 添加LayoutElement
        LayoutElement layoutElement = textObj.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = fontSize + 5;
    }
}