using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 背包系统修复工具
/// 自动检测并修复缺失的背包系统组件
/// </summary>
public class InventorySystemFixer : MonoBehaviour
{
    [Header("修复设置")]
    public bool autoFixOnStart = true;
    public bool showDebugInfo = true;
    
    [Header("系统配置")]
    public int inventorySize = 20;
    public int slotsPerRow = 5;
    public Vector2 slotSize = new Vector2(64, 64);
    public Vector2 slotSpacing = new Vector2(10, 10);
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            StartCoroutine(DelayedFix());
        }
    }
    
    private System.Collections.IEnumerator DelayedFix()
    {
        // 等待一帧，确保所有组件都已初始化
        yield return null;
        
        CheckAndFixInventorySystem();
    }
    
    [ContextMenu("检查并修复背包系统")]
    public void CheckAndFixInventorySystem()
    {
        Debug.Log("=== 开始检查背包系统 ===");
        
        bool needsFix = false;
        
        // 检查InventoryManager
        InventoryManager inventoryManager = FindObjectOfType<InventoryManager>();
        if (inventoryManager == null)
        {
            Debug.LogWarning("❌ 未找到InventoryManager组件");
            needsFix = true;
        }
        else
        {
            Debug.Log("✅ 找到InventoryManager组件");
        }
        
        // 检查InventoryUI
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI == null)
        {
            Debug.LogWarning("❌ 未找到InventoryUI组件");
            needsFix = true;
        }
        else
        {
            Debug.Log("✅ 找到InventoryUI组件");
        }
        
        // 检查Inventory
        Inventory inventory = FindObjectOfType<Inventory>();
        if (inventory == null)
        {
            Debug.LogWarning("❌ 未找到Inventory组件");
            needsFix = true;
        }
        else
        {
            Debug.Log("✅ 找到Inventory组件");
        }
        
        // 检查Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("❌ 未找到Canvas组件");
            needsFix = true;
        }
        else
        {
            Debug.Log("✅ 找到Canvas组件");
        }
        
        // 检查EventSystem
        UnityEngine.EventSystems.EventSystem eventSystem = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null)
        {
            Debug.LogWarning("❌ 未找到EventSystem组件");
            needsFix = true;
        }
        else
        {
            Debug.Log("✅ 找到EventSystem组件");
        }
        
        if (needsFix)
        {
            Debug.Log("🔧 检测到缺失组件，开始自动修复...");
            FixInventorySystem();
        }
        else
        {
            Debug.Log("✅ 背包系统检查完成，所有组件都存在");
            
            // 即使组件存在，也要检查引用是否正确
            CheckReferences();
        }
    }
    
    private void FixInventorySystem()
    {
        // 创建或获取InventorySystemSetup组件
        InventorySystemSetup setup = FindObjectOfType<InventorySystemSetup>();
        if (setup == null)
        {
            GameObject setupObj = new GameObject("InventorySystemSetup");
            setup = setupObj.AddComponent<InventorySystemSetup>();
            Debug.Log("创建了InventorySystemSetup组件");
        }
        
        // 配置设置参数
        setup.inventorySize = inventorySize;
        setup.slotsPerRow = slotsPerRow;
        setup.slotSize = slotSize;
        setup.slotSpacing = slotSpacing;
        
        // 执行系统设置
        try
        {
            setup.SetupInventorySystem();
            Debug.Log("✅ 背包系统修复完成！");
            
            // 验证修复结果
            StartCoroutine(VerifyFix());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ 背包系统修复失败: {e.Message}");
        }
    }
    
    private System.Collections.IEnumerator VerifyFix()
    {
        // 等待一帧让组件完全初始化
        yield return null;
        
        Debug.Log("=== 验证修复结果 ===");
        
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        InventoryUI ui = FindObjectOfType<InventoryUI>();
        Inventory inventory = FindObjectOfType<Inventory>();
        
        if (manager != null && ui != null && inventory != null)
        {
            Debug.Log("✅ 修复成功！所有组件都已创建");
            Debug.Log($"📦 背包大小: {inventory.inventorySize}");
            Debug.Log($"🎮 按键设置: {ui.toggleKey}");
            Debug.Log("🎯 现在可以按Tab键打开背包了！");
        }
        else
        {
            Debug.LogError("❌ 修复失败，仍有组件缺失");
        }
    }
    
    private void CheckReferences()
    {
        Debug.Log("=== 检查组件引用 ===");
        
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        if (manager != null)
        {
            bool hasIssues = false;
            
            if (manager.inventory == null)
            {
                Debug.LogWarning("⚠️ InventoryManager.inventory 引用为空");
                manager.inventory = FindObjectOfType<Inventory>();
                if (manager.inventory != null)
                {
                    Debug.Log("✅ 自动修复了 InventoryManager.inventory 引用");
                }
                hasIssues = true;
            }
            
            if (manager.inventoryUI == null)
            {
                Debug.LogWarning("⚠️ InventoryManager.inventoryUI 引用为空");
                manager.inventoryUI = FindObjectOfType<InventoryUI>();
                if (manager.inventoryUI != null)
                {
                    Debug.Log("✅ 自动修复了 InventoryManager.inventoryUI 引用");
                }
                hasIssues = true;
            }
            
            if (!hasIssues)
            {
                Debug.Log("✅ InventoryManager 引用检查完成，无问题");
            }
        }
        
        InventoryUI ui = FindObjectOfType<InventoryUI>();
        if (ui != null)
        {
            bool hasIssues = false;
            
            if (ui.inventoryPanel == null)
            {
                Debug.LogWarning("⚠️ InventoryUI.inventoryPanel 引用为空");
                hasIssues = true;
            }
            
            if (ui.slotsParent == null)
            {
                Debug.LogWarning("⚠️ InventoryUI.slotsParent 引用为空");
                hasIssues = true;
            }
            
            if (ui.slotPrefab == null)
            {
                Debug.LogWarning("⚠️ InventoryUI.slotPrefab 引用为空");
                hasIssues = true;
            }
            
            if (hasIssues)
            {
                Debug.LogWarning("⚠️ InventoryUI 存在引用问题，建议重新创建");
            }
            else
            {
                Debug.Log("✅ InventoryUI 引用检查完成，无问题");
            }
        }
    }
    
    [ContextMenu("强制重新创建背包系统")]
    public void ForceRecreateInventorySystem()
    {
        Debug.Log("=== 强制重新创建背包系统 ===");
        
        // 删除现有组件
        InventoryManager[] managers = FindObjectsOfType<InventoryManager>();
        InventoryUI[] uis = FindObjectsOfType<InventoryUI>();
        Inventory[] inventories = FindObjectsOfType<Inventory>();
        
        foreach (var manager in managers)
        {
            if (manager.gameObject.name == "InventoryManager")
            {
                DestroyImmediate(manager.gameObject);
                Debug.Log("删除了现有的InventoryManager");
            }
        }
        
        foreach (var ui in uis)
        {
            if (ui.gameObject.name == "InventoryPanel")
            {
                DestroyImmediate(ui.gameObject);
                Debug.Log("删除了现有的InventoryUI");
            }
        }
        
        // 重新创建
        FixInventorySystem();
    }
    
    private void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, Screen.height - 150, 300, 140));
        GUILayout.Label("背包系统修复工具", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });
        
        if (GUILayout.Button("检查背包系统"))
        {
            CheckAndFixInventorySystem();
        }
        
        if (GUILayout.Button("强制重新创建"))
        {
            ForceRecreateInventorySystem();
        }
        
        if (GUILayout.Button("测试Tab键"))
        {
            var ui = FindObjectOfType<InventoryUI>();
            if (ui != null)
            {
                ui.ToggleInventory();
                Debug.Log("手动切换背包状态");
            }
            else
            {
                Debug.LogError("未找到InventoryUI组件！");
            }
        }
        
        GUILayout.EndArea();
    }
}