using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 背包系统诊断和修复工具
/// 专门用于解决InventoryUI组件缺失和Tab键无响应的问题
/// </summary>
public class InventorySystemDiagnostic : MonoBehaviour
{
    [Header("诊断设置")]
    public bool autoFixOnStart = true;
    public bool showDebugGUI = true;
    
    [Header("修复配置")]
    public int inventorySize = 20;
    public int slotsPerRow = 5;
    public Vector2 slotSize = new Vector2(64, 64);
    public Vector2 slotSpacing = new Vector2(10, 10);
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            StartCoroutine(DelayedDiagnosis());
        }
    }
    
    private System.Collections.IEnumerator DelayedDiagnosis()
    {
        // 等待一帧，确保所有组件都已初始化
        yield return null;
        
        Debug.Log("=== 开始背包系统诊断 ===");
        RunFullDiagnosis();
    }
    
    [ContextMenu("运行完整诊断")]
    public void RunFullDiagnosis()
    {
        Debug.Log("🔍 开始诊断背包系统...");
        
        bool hasIssues = false;
        
        // 1. 检查EventSystem
        if (!CheckEventSystem())
        {
            hasIssues = true;
        }
        
        // 2. 检查Canvas
        if (!CheckCanvas())
        {
            hasIssues = true;
        }
        
        // 3. 检查InventoryManager
        if (!CheckInventoryManager())
        {
            hasIssues = true;
        }
        
        // 4. 检查InventoryUI
        if (!CheckInventoryUI())
        {
            hasIssues = true;
        }
        
        // 5. 检查组件引用关系
        if (!CheckComponentReferences())
        {
            hasIssues = true;
        }
        
        if (!hasIssues)
        {
            Debug.Log("✅ 诊断完成：背包系统正常！");
            TestTabKeyFunctionality();
        }
        else
        {
            Debug.LogWarning("⚠️ 发现问题，建议运行自动修复");
        }
    }
    
    private bool CheckEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>();
        if (eventSystem == null)
        {
            Debug.LogError("❌ 缺少EventSystem！正在创建...");
            
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();
            
            Debug.Log("✅ EventSystem已创建");
            return false;
        }
        
        Debug.Log("✅ EventSystem检查通过");
        return true;
    }
    
    private bool CheckCanvas()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("❌ 缺少Canvas！正在创建...");
            
            GameObject canvasObj = new GameObject("Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasObj.AddComponent<GraphicRaycaster>();
            
            Debug.Log("✅ Canvas已创建");
            return false;
        }
        
        Debug.Log("✅ Canvas检查通过");
        return true;
    }
    
    private bool CheckInventoryManager()
    {
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        if (manager == null)
        {
            Debug.LogError("❌ 缺少InventoryManager！正在创建...");
            
            GameObject managerObj = new GameObject("InventoryManager");
            manager = managerObj.AddComponent<InventoryManager>();
            
            Inventory inventory = managerObj.AddComponent<Inventory>();
            inventory.inventorySize = inventorySize;
            manager.inventory = inventory;
            
            Debug.Log("✅ InventoryManager已创建");
            return false;
        }
        
        // 检查Inventory组件
        if (manager.inventory == null)
        {
            Debug.LogError("❌ InventoryManager缺少Inventory组件！正在修复...");
            
            Inventory inventory = manager.GetComponent<Inventory>();
            if (inventory == null)
            {
                inventory = manager.gameObject.AddComponent<Inventory>();
                inventory.inventorySize = inventorySize;
            }
            manager.inventory = inventory;
            
            Debug.Log("✅ Inventory组件已修复");
            return false;
        }
        
        Debug.Log("✅ InventoryManager检查通过");
        return true;
    }
    
    private bool CheckInventoryUI()
    {
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI == null)
        {
            Debug.LogError("❌ 缺少InventoryUI组件！正在创建完整的背包UI...");
            
            // 使用InventorySystemSetup创建完整的UI
            InventorySystemSetup setup = FindObjectOfType<InventorySystemSetup>();
            if (setup == null)
            {
                GameObject setupObj = new GameObject("InventorySystemSetup");
                setup = setupObj.AddComponent<InventorySystemSetup>();
            }
            
            setup.inventorySize = inventorySize;
            setup.slotsPerRow = slotsPerRow;
            setup.slotSize = slotSize;
            setup.slotSpacing = slotSpacing;
            setup.SetupInventorySystem();
            
            Debug.Log("✅ InventoryUI已创建");
            return false;
        }
        
        Debug.Log("✅ 找到InventoryUI组件");
        
        // 检查InventoryPanel是否被禁用
        if (inventoryUI.inventoryPanel != null && !inventoryUI.inventoryPanel.activeInHierarchy)
        {
            Debug.LogWarning("⚠️ InventoryPanel被禁用，这可能导致FindObjectOfType无法找到InventoryUI");
        }
        
        // 检查InventoryUI的必要组件
        bool hasIssues = false;
        
        if (inventoryUI.inventoryPanel == null)
        {
            Debug.LogError("❌ InventoryUI缺少inventoryPanel引用！");
            hasIssues = true;
        }
        
        if (inventoryUI.slotsParent == null)
        {
            Debug.LogError("❌ InventoryUI缺少slotsParent引用！");
            hasIssues = true;
        }
        
        if (inventoryUI.slotPrefab == null)
        {
            Debug.LogError("❌ InventoryUI缺少slotPrefab引用！");
            hasIssues = true;
        }
        
        if (hasIssues)
        {
            Debug.LogWarning("⚠️ InventoryUI组件引用不完整，建议重新创建");
            return false;
        }
        
        Debug.Log("✅ InventoryUI检查通过");
        return true;
    }
    
    private bool CheckComponentReferences()
    {
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        
        if (manager == null || inventoryUI == null)
        {
            Debug.LogError("❌ 无法检查组件引用：缺少基础组件");
            return false;
        }
        
        bool hasIssues = false;
        
        // 检查InventoryManager的引用
        if (manager.inventoryUI != inventoryUI)
        {
            Debug.LogWarning("⚠️ InventoryManager.inventoryUI引用不正确，正在修复...");
            manager.inventoryUI = inventoryUI;
            hasIssues = true;
        }
        
        if (manager.inventory == null)
        {
            Debug.LogWarning("⚠️ InventoryManager.inventory引用为空，正在修复...");
            manager.inventory = manager.GetComponent<Inventory>();
            hasIssues = true;
        }
        
        if (hasIssues)
        {
            FixMissingReferences();
            Debug.Log("✅ 组件引用已修复");
            return false;
        }
        
        Debug.Log("✅ 组件引用检查通过");
        return true;
    }
    
    private void FixMissingReferences()
    {
        Debug.Log("开始修复缺失的引用关系...");
        
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        Inventory inventory = FindObjectOfType<Inventory>();
        ItemTooltip tooltip = FindObjectOfType<ItemTooltip>();
        
        // 确保InventoryPanel处于启用状态
        if (inventoryUI != null && inventoryUI.inventoryPanel != null)
        {
            inventoryUI.inventoryPanel.SetActive(true);
            Debug.Log("✅ 确保InventoryPanel处于启用状态");
        }
        
        if (manager != null && inventoryUI != null)
        {
            manager.inventoryUI = inventoryUI;
            Debug.Log("✅ 修复了InventoryManager.inventoryUI引用");
        }
        
        if (manager != null && inventory != null)
        {
            manager.inventory = inventory;
            Debug.Log("✅ 修复了InventoryManager.inventory引用");
        }
        
        if (inventoryUI != null && inventory != null)
        {
            inventoryUI.inventory = inventory;
            Debug.Log("✅ 修复了InventoryUI.inventory引用");
        }
        
        if (inventoryUI != null && tooltip != null)
        {
            inventoryUI.itemTooltip = tooltip;
            Debug.Log("✅ 修复了InventoryUI.itemTooltip引用");
        }
        
        // 重新初始化InventoryUI（这会在最后调用CloseInventory禁用面板）
        if (inventoryUI != null)
        {
            inventoryUI.Initialize();
            Debug.Log("✅ 重新初始化了InventoryUI");
        }
        
        // 让InventoryManager重新查找InventoryUI引用
        if (manager != null)
        {
            manager.RefreshInventoryUIReference();
            Debug.Log("✅ InventoryManager重新查找了InventoryUI引用");
        }
        
        Debug.Log("引用关系修复完成！");
    }
    
    private void TestTabKeyFunctionality()
    {
        Debug.Log("🧪 测试Tab键功能...");
        
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI != null)
        {
            Debug.Log($"✅ Tab键设置: {inventoryUI.toggleKey}");
            Debug.Log($"✅ InventoryUI状态: Active={inventoryUI.gameObject.activeInHierarchy}, Enabled={inventoryUI.enabled}");
            Debug.Log("🎯 现在可以按Tab键测试背包开关功能！");
        }
        else
        {
            Debug.LogError("❌ 无法测试Tab键功能：InventoryUI组件缺失");
        }
    }
    
    [ContextMenu("强制重新创建背包系统")]
    public void ForceRecreateInventorySystem()
    {
        Debug.Log("🔄 强制重新创建背包系统...");
        
        // 删除现有组件
        InventoryUI[] existingUIs = FindObjectsOfType<InventoryUI>();
        foreach (var ui in existingUIs)
        {
            if (ui.inventoryPanel != null)
            {
                DestroyImmediate(ui.inventoryPanel);
            }
            DestroyImmediate(ui.gameObject);
        }
        
        InventoryManager[] existingManagers = FindObjectsOfType<InventoryManager>();
        foreach (var manager in existingManagers)
        {
            DestroyImmediate(manager.gameObject);
        }
        
        // 重新创建
        InventorySystemSetup setup = FindObjectOfType<InventorySystemSetup>();
        if (setup == null)
        {
            GameObject setupObj = new GameObject("InventorySystemSetup");
            setup = setupObj.AddComponent<InventorySystemSetup>();
        }
        
        setup.inventorySize = inventorySize;
        setup.slotsPerRow = slotsPerRow;
        setup.slotSize = slotSize;
        setup.slotSpacing = slotSpacing;
        setup.SetupInventorySystem();
        
        Debug.Log("✅ 背包系统重新创建完成！");
    }
    
    [ContextMenu("测试Tab键")]
    public void TestTabKey()
    {
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI != null)
        {
            inventoryUI.ToggleInventory();
            Debug.Log($"🎮 手动切换背包状态: {(inventoryUI.IsInventoryOpen() ? "打开" : "关闭")}");
        }
        else
        {
            Debug.LogError("❌ 无法测试：InventoryUI组件缺失！");
        }
    }
    
    private void OnGUI()
    {
        if (!showDebugGUI) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("背包系统诊断工具", GUI.skin.box);
        
        if (GUILayout.Button("运行完整诊断"))
        {
            RunFullDiagnosis();
        }
        
        if (GUILayout.Button("强制重新创建"))
        {
            ForceRecreateInventorySystem();
        }
        
        if (GUILayout.Button("测试Tab键"))
        {
            TestTabKey();
        }
        
        GUILayout.Space(10);
        
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        
        GUILayout.Label($"InventoryManager: {(manager != null ? "✅" : "❌")}");
        GUILayout.Label($"InventoryUI: {(inventoryUI != null ? "✅" : "❌")}");
        
        if (inventoryUI != null)
        {
            GUILayout.Label($"Tab键: {inventoryUI.toggleKey}");
        }
        
        GUILayout.EndArea();
    }
}