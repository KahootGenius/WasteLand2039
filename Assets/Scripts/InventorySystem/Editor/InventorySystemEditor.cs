#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class InventorySystemEditor : EditorWindow
{
    private int inventorySize = 20;
    private int slotsPerRow = 5;
    private Vector2 slotSize = new Vector2(64, 64);
    private Vector2 slotSpacing = new Vector2(10, 10);
    
    private bool createInventoryManager = true;
    private bool createInventoryUI = true;
    private bool createItemTooltip = true;
    private bool setupCanvas = true;
    
    [MenuItem("Tools/Inventory System/Setup Inventory System")]
    public static void ShowWindow()
    {
        InventorySystemEditor window = GetWindow<InventorySystemEditor>("Inventory System Setup");
        window.minSize = new Vector2(400, 500);
        window.Show();
    }
    
    [MenuItem("Tools/Inventory System/Create Item")]
    public static void CreateNewItem()
    {
        // 创建新的Item ScriptableObject
        Item newItem = ScriptableObject.CreateInstance<Item>();
        
        string path = EditorUtility.SaveFilePanelInProject(
            "Create New Item",
            "New Item",
            "asset",
            "Choose where to save the new item",
            "Assets/Items"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(newItem, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = newItem;
            
            Debug.Log($"Created new item at: {path}");
        }
    }
    
    [MenuItem("Tools/Inventory System/Create Item Type Colors")]
    public static void CreateItemTypeColors()
    {
        // 创建ItemTypeColors配置文件
        ItemTypeColors colorConfig = ScriptableObject.CreateInstance<ItemTypeColors>();
        
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Item Type Colors",
            "ItemTypeColors",
            "asset",
            "Choose where to save the color configuration",
            "Assets/Resources"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(colorConfig, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = colorConfig;
            
            Debug.Log($"Created ItemTypeColors at: {path}");
        }
    }
    
    private void OnGUI()
    {
        GUILayout.Label("Inventory System Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "这个工具将帮助您快速设置完整的背包系统。\n" +
            "包括背包管理器、UI界面、物品提示框等组件。",
            MessageType.Info
        );
        
        GUILayout.Space(10);
        
        // 基本设置
        GUILayout.Label("基本设置", EditorStyles.boldLabel);
        inventorySize = EditorGUILayout.IntField("背包大小", inventorySize);
        slotsPerRow = EditorGUILayout.IntField("每行槽位数", slotsPerRow);
        slotSize = EditorGUILayout.Vector2Field("槽位大小", slotSize);
        slotSpacing = EditorGUILayout.Vector2Field("槽位间距", slotSpacing);
        
        GUILayout.Space(10);
        
        // 组件创建选项
        GUILayout.Label("创建组件", EditorStyles.boldLabel);
        createInventoryManager = EditorGUILayout.Toggle("创建背包管理器", createInventoryManager);
        createInventoryUI = EditorGUILayout.Toggle("创建背包UI", createInventoryUI);
        createItemTooltip = EditorGUILayout.Toggle("创建物品提示框", createItemTooltip);
        setupCanvas = EditorGUILayout.Toggle("设置Canvas", setupCanvas);
        
        GUILayout.Space(20);
        
        // 设置按钮
        if (GUILayout.Button("开始设置背包系统", GUILayout.Height(30)))
        {
            SetupInventorySystem();
        }
        
        GUILayout.Space(10);
        
        // 其他工具按钮
        GUILayout.Label("其他工具", EditorStyles.boldLabel);
        
        if (GUILayout.Button("创建新物品"))
        {
            CreateNewItem();
        }
        
        if (GUILayout.Button("创建物品类型颜色配置"))
        {
            CreateItemTypeColors();
        }
        
        if (GUILayout.Button("验证系统完整性"))
        {
            ValidateSystem();
        }
        
        GUILayout.Space(10);
        
        // 帮助信息
        EditorGUILayout.HelpBox(
            "设置完成后，请检查：\n" +
            "1. Canvas是否正确设置\n" +
            "2. InventoryManager是否关联了所有组件\n" +
            "3. 物品槽预制体是否配置正确\n" +
            "4. 资源文件是否在正确路径",
            MessageType.Warning
        );
    }
    
    private void SetupInventorySystem()
    {
        // 创建设置对象
        GameObject setupObj = new GameObject("InventorySystemSetup");
        InventorySystemSetup setup = setupObj.AddComponent<InventorySystemSetup>();
        
        // 配置参数
        setup.createInventoryManager = createInventoryManager;
        setup.createInventoryUI = createInventoryUI;
        setup.createItemTooltip = createItemTooltip;
        setup.setupCanvas = setupCanvas;
        setup.inventorySize = inventorySize;
        setup.slotsPerRow = slotsPerRow;
        setup.slotSize = slotSize;
        setup.slotSpacing = slotSpacing;
        
        // 执行设置
        setup.SetupInventorySystem();
        
        // 清理设置对象
        DestroyImmediate(setupObj);
        
        EditorUtility.DisplayDialog(
            "设置完成",
            "背包系统设置完成！\n请检查场景中的组件配置。",
            "确定"
        );
    }
    
    private void ValidateSystem()
    {
        bool isValid = true;
        string issues = "";
        
        // 检查InventoryManager
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        if (manager == null)
        {
            isValid = false;
            issues += "- 未找到InventoryManager\n";
        }
        else
        {
            if (manager.inventory == null)
            {
                isValid = false;
                issues += "- InventoryManager缺少Inventory组件引用\n";
            }
            
            if (manager.inventoryUI == null)
            {
                isValid = false;
                issues += "- InventoryManager缺少InventoryUI组件引用\n";
            }
        }
        
        // 检查InventoryUI
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI == null)
        {
            isValid = false;
            issues += "- 未找到InventoryUI\n";
        }
        else
        {
            if (inventoryUI.slotPrefab == null)
            {
                isValid = false;
                issues += "- InventoryUI缺少槽位预制体\n";
            }
            
            if (inventoryUI.itemTooltip == null)
            {
                isValid = false;
                issues += "- InventoryUI缺少物品提示框引用\n";
            }
        }
        
        // 检查Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            isValid = false;
            issues += "- 未找到Canvas\n";
        }
        
        // 检查资源文件
        Sprite inventoryBg = Resources.Load<Sprite>("InventoryUI/InventoryRect");
        if (inventoryBg == null)
        {
            isValid = false;
            issues += "- 未找到背包背景图片 (InventoryUI/InventoryRect)\n";
        }
        
        Sprite slotSprite = Resources.Load<Sprite>("InventoryUI/inventorySlot (1)");
        if (slotSprite == null)
        {
            isValid = false;
            issues += "- 未找到槽位图片 (InventoryUI/inventorySlot (1))\n";
        }
        
        // 显示结果
        if (isValid)
        {
            EditorUtility.DisplayDialog(
                "验证通过",
                "背包系统配置正确！",
                "确定"
            );
        }
        else
        {
            EditorUtility.DisplayDialog(
                "验证失败",
                "发现以下问题：\n\n" + issues,
                "确定"
            );
        }
    }
}

/// <summary>
/// 自定义Item编辑器
/// </summary>
[CustomEditor(typeof(Item))]
public class ItemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        Item item = (Item)target;
        
        EditorGUILayout.Space();
        
        // 显示物品预览
        if (item.icon != null)
        {
            GUILayout.Label("物品预览", EditorStyles.boldLabel);
            
            Rect previewRect = GUILayoutUtility.GetRect(64, 64, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(previewRect, item.icon.texture);
            
            EditorGUILayout.Space();
        }
        
        // 绘制默认Inspector
        DrawDefaultInspector();
        
        EditorGUILayout.Space();
        
        // 添加测试按钮
        if (Application.isPlaying)
        {
            GUILayout.Label("测试功能", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("添加到背包"))
            {
                InventoryManager manager = FindObjectOfType<InventoryManager>();
                if (manager != null)
                {
                    manager.AddItem(item, 1);
                    Debug.Log($"添加了物品: {item.itemName}");
                }
                else
                {
                    Debug.LogWarning("未找到InventoryManager！");
                }
            }
            
            if (GUILayout.Button("生成到世界"))
            {
                InventoryManager manager = FindObjectOfType<InventoryManager>();
                if (manager != null)
                {
                    Vector3 spawnPos = Vector3.zero;
                    if (Camera.main != null)
                    {
                        spawnPos = Camera.main.transform.position + Vector3.forward * 2;
                    }
                    
                    manager.SpawnItemInWorld(item, spawnPos, 1);
                    Debug.Log($"在世界中生成了物品: {item.itemName}");
                }
                else
                {
                    Debug.LogWarning("未找到InventoryManager！");
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif