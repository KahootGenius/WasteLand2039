using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 背包系统测试场景
/// 用于快速设置和测试背包系统的所有功能
/// </summary>
public class InventoryTestScene : MonoBehaviour
{
    [Header("测试设置")]
    [SerializeField] private bool autoSetupOnStart = true;
    [SerializeField] private bool addTestItemsOnStart = true;
    [SerializeField] private bool spawnWorldItemsOnStart = true;
    
    [Header("UI测试按钮")]
    [SerializeField] private Button addItemButton;
    [SerializeField] private Button removeItemButton;
    [SerializeField] private Button clearInventoryButton;
    [SerializeField] private Button spawnWorldItemButton;
    [SerializeField] private Button toggleInventoryButton;
    
    [Header("测试物品")]
    [SerializeField] private Item[] testItems;
    
    [Header("调试信息")]
    [SerializeField] private Text debugText;
    [SerializeField] private bool showDebugInfo = true;
    
    private InventoryManager inventoryManager;
    private ExampleItems exampleItems;
    
    private void Start()
    {
        if (autoSetupOnStart)
        {
            StartCoroutine(SetupTestScene());
        }
    }
    
    private IEnumerator SetupTestScene()
    {
        Debug.Log("开始设置测试场景...");
        
        // 等待一帧确保所有组件初始化完成
        yield return null;
        
        // 查找或创建InventoryManager
        inventoryManager = FindObjectOfType<InventoryManager>();
        if (inventoryManager == null)
        {
            Debug.LogWarning("未找到InventoryManager，请先设置背包系统！");
            yield break;
        }
        
        // 查找或创建ExampleItems
        exampleItems = FindObjectOfType<ExampleItems>();
        if (exampleItems == null)
        {
            GameObject exampleItemsGO = new GameObject("ExampleItems");
            exampleItems = exampleItemsGO.AddComponent<ExampleItems>();
        }
        
        // 设置UI按钮
        SetupUIButtons();
        
        // 加载测试物品
        LoadTestItems();
        
        // 添加测试物品到背包
        if (addTestItemsOnStart)
        {
            yield return new WaitForSeconds(0.5f);
            AddTestItemsToInventory();
        }
        
        // 在世界中生成物品
        if (spawnWorldItemsOnStart)
        {
            yield return new WaitForSeconds(0.5f);
            SpawnTestItemsInWorld();
        }
        
        // 开始调试信息更新
        if (showDebugInfo && debugText != null)
        {
            StartCoroutine(UpdateDebugInfo());
        }
        
        Debug.Log("测试场景设置完成！");
        
        // 显示使用说明
        ShowUsageInstructions();
    }
    
    private void SetupUIButtons()
    {
        // 查找UI按钮（如果没有手动分配）
        if (addItemButton == null)
            addItemButton = GameObject.Find("AddItemButton")?.GetComponent<Button>();
        if (removeItemButton == null)
            removeItemButton = GameObject.Find("RemoveItemButton")?.GetComponent<Button>();
        if (clearInventoryButton == null)
            clearInventoryButton = GameObject.Find("ClearInventoryButton")?.GetComponent<Button>();
        if (spawnWorldItemButton == null)
            spawnWorldItemButton = GameObject.Find("SpawnWorldItemButton")?.GetComponent<Button>();
        if (toggleInventoryButton == null)
            toggleInventoryButton = GameObject.Find("ToggleInventoryButton")?.GetComponent<Button>();
        
        // 设置按钮事件
        if (addItemButton != null)
            addItemButton.onClick.AddListener(AddRandomItem);
        if (removeItemButton != null)
            removeItemButton.onClick.AddListener(RemoveRandomItem);
        if (clearInventoryButton != null)
            clearInventoryButton.onClick.AddListener(ClearInventory);
        if (spawnWorldItemButton != null)
            spawnWorldItemButton.onClick.AddListener(SpawnRandomWorldItem);
        if (toggleInventoryButton != null)
            toggleInventoryButton.onClick.AddListener(ToggleInventory);
    }
    
    private void LoadTestItems()
    {
        // 从Resources文件夹加载所有Item
        Item[] allItems = Resources.LoadAll<Item>("");
        if (allItems.Length > 0)
        {
            testItems = allItems;
            Debug.Log($"加载了 {testItems.Length} 个测试物品");
        }
        else
        {
            Debug.LogWarning("未找到测试物品！请先创建一些Item ScriptableObject。");
        }
    }
    
    private void AddTestItemsToInventory()
    {
        if (testItems == null || testItems.Length == 0)
        {
            Debug.LogWarning("没有可用的测试物品！");
            return;
        }
        
        // 添加一些示例物品
        for (int i = 0; i < Mathf.Min(5, testItems.Length); i++)
        {
            Item item = testItems[i];
            int amount = item.isStackable ? Random.Range(1, 5) : 1;
            inventoryManager.AddItem(item, amount);
        }
        
        Debug.Log("添加了测试物品到背包");
    }
    
    private void SpawnTestItemsInWorld()
    {
        if (testItems == null || testItems.Length == 0)
        {
            Debug.LogWarning("没有可用的测试物品！");
            return;
        }
        
        // 在场景中随机位置生成物品
        for (int i = 0; i < 3; i++)
        {
            Item randomItem = testItems[Random.Range(0, testItems.Length)];
            Vector3 randomPos = new Vector3(
                Random.Range(-5f, 5f),
                Random.Range(-3f, 3f),
                0
            );
            
            int amount = randomItem.isStackable ? Random.Range(1, 3) : 1;
            inventoryManager.SpawnItemInWorld(randomItem, randomPos, amount);
        }
        
        Debug.Log("在世界中生成了测试物品");
    }
    
    private IEnumerator UpdateDebugInfo()
    {
        while (debugText != null && showDebugInfo)
        {
            if (inventoryManager != null)
            {
                string info = $"背包状态:\n";
                info += $"已使用槽位: {inventoryManager.GetUsedSlotCount()}/{inventoryManager.GetInventorySize()}\n";
                info += $"空闲槽位: {inventoryManager.GetEmptySlotCount()}\n";
                info += $"背包是否打开: {(inventoryManager.IsInventoryOpen() ? "是" : "否")}\n";
                info += $"\n操作说明:\n";
                info += $"Tab键: 开关背包\n";
                info += $"鼠标悬停: 查看物品信息\n";
                info += $"左键点击: 选择/使用物品\n";
                info += $"右键点击: 丢弃物品\n";
                
                debugText.text = info;
            }
            
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    #region 按钮事件
    
    public void AddRandomItem()
    {
        if (testItems == null || testItems.Length == 0) return;
        
        Item randomItem = testItems[Random.Range(0, testItems.Length)];
        int amount = randomItem.isStackable ? Random.Range(1, 3) : 1;
        
        bool success = inventoryManager.AddItem(randomItem, amount);
        Debug.Log(success ? $"添加了 {amount} 个 {randomItem.itemName}" : "背包已满！");
    }
    
    public void RemoveRandomItem()
    {
        if (testItems == null || testItems.Length == 0) return;
        
        Item randomItem = testItems[Random.Range(0, testItems.Length)];
        int amount = 1;
        
        bool success = inventoryManager.RemoveItem(randomItem, amount);
        Debug.Log(success ? $"移除了 {amount} 个 {randomItem.itemName}" : $"背包中没有 {randomItem.itemName}！");
    }
    
    public void ClearInventory()
    {
        inventoryManager.ClearInventory();
        Debug.Log("清空了背包");
    }
    
    public void SpawnRandomWorldItem()
    {
        if (testItems == null || testItems.Length == 0) return;
        
        Item randomItem = testItems[Random.Range(0, testItems.Length)];
        Vector3 randomPos = new Vector3(
            Random.Range(-3f, 3f),
            Random.Range(-2f, 2f),
            0
        );
        
        int amount = randomItem.isStackable ? Random.Range(1, 3) : 1;
        inventoryManager.SpawnItemInWorld(randomItem, randomPos, amount);
        
        Debug.Log($"在世界中生成了 {amount} 个 {randomItem.itemName}");
    }
    
    public void ToggleInventory()
    {
        inventoryManager.ToggleInventory();
    }
    
    #endregion
    
    #region 键盘快捷键
    
    private void Update()
    {
        // 快捷键测试
        if (Input.GetKeyDown(KeyCode.F1))
        {
            AddRandomItem();
        }
        
        if (Input.GetKeyDown(KeyCode.F2))
        {
            RemoveRandomItem();
        }
        
        if (Input.GetKeyDown(KeyCode.F3))
        {
            SpawnRandomWorldItem();
        }
        
        if (Input.GetKeyDown(KeyCode.F4))
        {
            ClearInventory();
        }
        
        if (Input.GetKeyDown(KeyCode.F5))
        {
            ShowUsageInstructions();
        }
    }
    
    #endregion
    
    private void ShowUsageInstructions()
    {
        string instructions = @"
=== 背包系统测试说明 ===

基本操作:
• Tab键: 开关背包界面
• 鼠标悬停物品: 显示详细信息
• 左键点击物品: 使用物品
• 右键点击物品: 丢弃物品
• 拖拽物品: 交换位置

测试快捷键:
• F1: 添加随机物品
• F2: 移除随机物品
• F3: 在世界生成随机物品
• F4: 清空背包
• F5: 显示此说明

物品拾取:
• 靠近世界中的物品
• 按E键拾取

注意事项:
• 确保场景中有InventoryManager
• 确保有可用的测试物品
• 背包满时无法添加新物品
• 任务物品无法丢弃

========================";
        
        Debug.Log(instructions);
    }
    
    #region 编辑器功能
    
    [ContextMenu("Setup Test Scene")]
    public void ManualSetupTestScene()
    {
        StartCoroutine(SetupTestScene());
    }
    
    [ContextMenu("Create Test UI Buttons")]
    public void CreateTestUIButtons()
    {
        #if UNITY_EDITOR
        // 查找或创建Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("TestCanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }
        
        // 创建按钮面板
        GameObject buttonPanel = new GameObject("TestButtonPanel");
        buttonPanel.transform.SetParent(canvas.transform, false);
        
        RectTransform panelRect = buttonPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.anchoredPosition = new Vector2(10, -10);
        panelRect.sizeDelta = new Vector2(200, 300);
        
        // 添加垂直布局组件
        VerticalLayoutGroup layoutGroup = buttonPanel.AddComponent<VerticalLayoutGroup>();
        layoutGroup.spacing = 5;
        layoutGroup.padding = new RectOffset(5, 5, 5, 5);
        
        // 创建按钮
        string[] buttonNames = { "AddItem", "RemoveItem", "ClearInventory", "SpawnWorldItem", "ToggleInventory" };
        string[] buttonTexts = { "添加物品", "移除物品", "清空背包", "生成世界物品", "开关背包" };
        
        for (int i = 0; i < buttonNames.Length; i++)
        {
            CreateTestButton(buttonPanel.transform, buttonNames[i] + "Button", buttonTexts[i]);
        }
        
        // 创建调试文本
        GameObject debugTextGO = new GameObject("DebugText");
        debugTextGO.transform.SetParent(canvas.transform, false);
        
        RectTransform debugRect = debugTextGO.AddComponent<RectTransform>();
        debugRect.anchorMin = new Vector2(1, 1);
        debugRect.anchorMax = new Vector2(1, 1);
        debugRect.anchoredPosition = new Vector2(-10, -10);
        debugRect.sizeDelta = new Vector2(300, 200);
        
        Text debugTextComponent = debugTextGO.AddComponent<Text>();
        debugTextComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        debugTextComponent.fontSize = 12;
        debugTextComponent.color = Color.white;
        debugTextComponent.alignment = TextAnchor.UpperLeft;
        
        debugText = debugTextComponent;
        
        Debug.Log("创建了测试UI按钮！");
        #endif
    }
    
    #if UNITY_EDITOR
    private void CreateTestButton(Transform parent, string name, string text)
    {
        GameObject buttonGO = new GameObject(name);
        buttonGO.transform.SetParent(parent, false);
        
        RectTransform buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(180, 30);
        
        Image buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        Button button = buttonGO.AddComponent<Button>();
        
        // 创建按钮文本
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        Text buttonText = textGO.AddComponent<Text>();
        buttonText.text = text;
        buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonText.fontSize = 14;
        buttonText.color = Color.white;
        buttonText.alignment = TextAnchor.MiddleCenter;
        
        // 分配按钮引用
        switch (name)
        {
            case "AddItemButton": addItemButton = button; break;
            case "RemoveItemButton": removeItemButton = button; break;
            case "ClearInventoryButton": clearInventoryButton = button; break;
            case "SpawnWorldItemButton": spawnWorldItemButton = button; break;
            case "ToggleInventoryButton": toggleInventoryButton = button; break;
        }
    }
    #endif
    
    #endregion
}