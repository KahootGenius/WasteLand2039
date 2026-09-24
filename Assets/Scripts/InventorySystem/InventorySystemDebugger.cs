using UnityEngine;
using TMPro;

/// <summary>
/// 背包系统调试工具
/// 用于诊断和修复常见问题
/// </summary>
public class InventorySystemDebugger : MonoBehaviour
{
    [Header("调试信息")]
    public bool showDebugInfo = true;
    public KeyCode debugKey = KeyCode.F12;
    
    [Header("自动修复")]
    public bool autoFixOnStart = true;
    
    private InventoryManager inventoryManager;
    private InventoryUI inventoryUI;
    private Inventory inventory;
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            DiagnoseAndFix();
        }
    }
    
    private void Update()
    {
        if (Input.GetKeyDown(debugKey))
        {
            DiagnoseAndFix();
        }
        
        if (showDebugInfo && Input.GetKeyDown(KeyCode.Tab))
        {
            Debug.Log("Tab键被按下，检查背包系统状态...");
            CheckInventorySystemStatus();
        }
    }
    
    [ContextMenu("诊断并修复背包系统")]
    public void DiagnoseAndFix()
    {
        Debug.Log("=== 背包系统诊断开始 ===");
        
        // 查找组件
        FindComponents();
        
        // 检查组件状态
        CheckComponents();
        
        // 修复常见问题
        FixCommonIssues();
        
        Debug.Log("=== 背包系统诊断完成 ===");
    }
    
    private void FindComponents()
    {
        inventoryManager = FindObjectOfType<InventoryManager>();
        inventoryUI = FindObjectOfType<InventoryUI>();
        inventory = FindObjectOfType<Inventory>();
        
        Debug.Log($"找到组件: InventoryManager={inventoryManager != null}, InventoryUI={inventoryUI != null}, Inventory={inventory != null}");
    }
    
    private void CheckComponents()
    {
        // 检查InventoryManager
        if (inventoryManager == null)
        {
            Debug.LogError("❌ 未找到InventoryManager组件！");
            return;
        }
        
        Debug.Log($"✅ InventoryManager状态: Active={inventoryManager.gameObject.activeInHierarchy}");
        Debug.Log($"   - inventory引用: {inventoryManager.inventory != null}");
        Debug.Log($"   - inventoryUI引用: {inventoryManager.inventoryUI != null}");
        Debug.Log($"   - itemTypeColors引用: {inventoryManager.itemTypeColors != null}");
        
        // 检查InventoryUI
        if (inventoryUI == null)
        {
            Debug.LogError("❌ 未找到InventoryUI组件！");
            return;
        }
        
        Debug.Log($"✅ InventoryUI状态: Active={inventoryUI.gameObject.activeInHierarchy}, Enabled={inventoryUI.enabled}");
        Debug.Log($"   - inventoryPanel: {inventoryUI.inventoryPanel != null}");
        Debug.Log($"   - slotsParent: {inventoryUI.slotsParent != null}");
        Debug.Log($"   - slotPrefab: {inventoryUI.slotPrefab != null}");
        Debug.Log($"   - toggleKey: {inventoryUI.toggleKey}");
        
        // 检查Inventory
        if (inventory == null)
        {
            Debug.LogError("❌ 未找到Inventory组件！");
            return;
        }
        
        Debug.Log($"✅ Inventory状态: Active={inventory.gameObject.activeInHierarchy}, Enabled={inventory.enabled}");
        Debug.Log($"   - inventorySize: {inventory.inventorySize}");
    }
    
    private void FixCommonIssues()
    {
        Debug.Log("🔧 开始修复常见问题...");
        
        // 修复1: 确保InventoryManager引用正确
        if (inventoryManager != null)
        {
            if (inventoryManager.inventory == null)
            {
                inventoryManager.inventory = inventory;
                Debug.Log("✅ 修复: 设置InventoryManager.inventory引用");
            }
            
            if (inventoryManager.inventoryUI == null)
            {
                inventoryManager.inventoryUI = inventoryUI;
                Debug.Log("✅ 修复: 设置InventoryManager.inventoryUI引用");
            }
        }
        
        // 修复2: 确保InventoryUI初始化正确
        if (inventoryUI != null)
        {
            if (inventoryUI.inventoryPanel != null && !inventoryUI.inventoryPanel.activeInHierarchy)
            {
                // 确保面板初始状态是关闭的，但GameObject是激活的
                inventoryUI.inventoryPanel.SetActive(false);
                Debug.Log("✅ 修复: 设置背包面板初始状态为关闭");
            }
        }
        
        // 修复3: 检查EventSystem
        var eventSystem = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Debug.Log("✅ 修复: 创建EventSystem");
        }
        
        // 修复4: 检查Canvas设置
        var canvas = FindObjectOfType<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning("⚠️ Canvas渲染模式不是ScreenSpaceOverlay，可能影响UI交互");
        }
    }
    
    private void CheckInventorySystemStatus()
    {
        if (inventoryUI == null)
        {
            Debug.LogError("❌ InventoryUI为空，无法响应Tab键");
            return;
        }
        
        if (!inventoryUI.enabled)
        {
            Debug.LogError("❌ InventoryUI组件被禁用");
            return;
        }
        
        if (!inventoryUI.gameObject.activeInHierarchy)
        {
            Debug.LogError("❌ InventoryUI GameObject未激活");
            return;
        }
        
        Debug.Log($"✅ InventoryUI状态正常，当前背包是否打开: {inventoryUI.IsInventoryOpen()}");
    }
    
    [ContextMenu("强制打开背包")]
    public void ForceOpenInventory()
    {
        if (inventoryUI != null)
        {
            inventoryUI.OpenInventory();
            Debug.Log("强制打开背包");
        }
    }
    
    [ContextMenu("强制关闭背包")]
    public void ForceCloseInventory()
    {
        if (inventoryUI != null)
        {
            inventoryUI.CloseInventory();
            Debug.Log("强制关闭背包");
        }
    }
    
    [ContextMenu("重新初始化背包系统")]
    public void ReinitializeInventorySystem()
    {
        if (inventoryUI != null && inventory != null)
        {
            // 重新初始化UI
            var initMethod = typeof(InventoryUI).GetMethod("InitializeUI", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (initMethod != null)
            {
                initMethod.Invoke(inventoryUI, null);
                Debug.Log("✅ 重新初始化背包UI");
            }
        }
    }
    
    private void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("背包系统调试信息", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });
        
        if (inventoryManager != null)
        {
            GUILayout.Label($"InventoryManager: ✅");
            GUILayout.Label($"背包是否打开: {(inventoryManager.IsInventoryOpen() ? "是" : "否")}");
        }
        else
        {
            GUILayout.Label("InventoryManager: ❌");
        }
        
        if (inventoryUI != null)
        {
            GUILayout.Label($"InventoryUI: ✅ (按键: {inventoryUI.toggleKey})");
        }
        else
        {
            GUILayout.Label("InventoryUI: ❌");
        }
        
        GUILayout.Space(10);
        GUILayout.Label($"按 {debugKey} 键进行诊断");
        GUILayout.Label("按 Tab 键测试背包开关");
        
        GUILayout.EndArea();
    }
}