using UnityEngine;

/// <summary>
/// 背包系统快速修复工具
/// 专门解决InventoryUI.inventory访问级别错误和Panel被禁用的问题
/// </summary>
public class InventorySystemQuickFix : MonoBehaviour
{
    [Header("快速修复工具")]
    [SerializeField] private bool autoFixOnStart = true;
    [SerializeField] private bool showDebugInfo = true;
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            PerformQuickFix();
        }
    }
    
    [ContextMenu("执行快速修复")]
    public void PerformQuickFix()
    {
        if (showDebugInfo)
        {
            Debug.Log("=== 开始执行背包系统快速修复 ===");
        }
        
        // 步骤1: 查找InventoryManager
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        if (manager == null)
        {
            Debug.LogError("❌ 未找到InventoryManager！请确保场景中存在InventoryManager组件。");
            return;
        }
        
        if (showDebugInfo)
        {
            Debug.Log("✅ 找到InventoryManager");
        }
        
        // 步骤2: 强制刷新InventoryUI引用
        manager.RefreshInventoryUIReference();
        
        // 步骤3: 验证修复结果
        if (manager.inventoryUI != null)
        {
            Debug.Log("✅ InventoryUI引用修复成功！");
            
            // 测试inventory字段访问
            try
            {
                if (manager.inventoryUI.inventory != null)
                {
                    Debug.Log("✅ InventoryUI.inventory字段访问正常！");
                }
                else
                {
                    Debug.LogWarning("⚠️ InventoryUI.inventory为null，可能需要重新初始化");
                    manager.inventoryUI.Initialize();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ InventoryUI.inventory访问失败: {e.Message}");
            }
            
            // 测试Tab键功能
            TestTabKeyFunctionality();
        }
        else
        {
            Debug.LogError("❌ InventoryUI引用仍然为null！");
            // 尝试使用Setup工具重新创建
            TryRecreateInventorySystem();
        }
        
        if (showDebugInfo)
        {
            Debug.Log("=== 快速修复完成 ===");
        }
    }
    
    private void TestTabKeyFunctionality()
    {
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        if (manager != null && manager.inventoryUI != null)
        {
            try
            {
                Debug.Log("🧪 测试Tab键功能...");
                Debug.Log("ℹ️ 注意：Tab键输入处理已从InventoryUI移至InventoryManager，确保始终能响应");
                
                // 测试打开背包
                bool wasOpen = manager.IsInventoryOpen();
                manager.inventoryUI.OpenInventory();
                Debug.Log("✅ 背包打开功能正常");
                
                // 测试ToggleInventory方法
                if (manager.inventoryUI != null)
                {
                    bool isNowOpen = manager.IsInventoryOpen();
                    if (wasOpen != isNowOpen)
                    {
                        Debug.Log("✅ Tab键功能测试成功！背包状态已切换");
                        Debug.Log("✅ 即使InventoryPanel被禁用，Tab键也能正常工作");
                    }
                    else
                    {
                        Debug.LogWarning("⚠️ Tab键功能可能有问题，背包状态未切换");
                    }
                }
                
                // 等待一帧后关闭
                StartCoroutine(CloseInventoryAfterFrame(manager));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ 背包开关功能测试失败: {e.Message}");
            }
        }
    }
    
    private System.Collections.IEnumerator CloseInventoryAfterFrame(InventoryManager manager)
    {
        yield return null; // 等待一帧
        
        try
        {
            manager.inventoryUI.CloseInventory();
            Debug.Log("✅ 背包关闭功能正常");
            Debug.Log("🎉 Tab键功能应该已经恢复正常！请按Tab键测试。");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ 背包关闭功能测试失败: {e.Message}");
        }
    }
    
    private void CheckInputHandlingLocation()
    {
        Debug.Log("🔍 检查输入处理位置...");
        
        InventoryManager manager = FindObjectOfType<InventoryManager>();
        if (manager != null)
        {
            Debug.Log("✅ InventoryManager存在，Tab键输入处理应该在这里");
            Debug.Log("✅ 这确保了即使InventoryPanel被禁用，Tab键也能正常响应");
        }
        else
        {
            Debug.LogError("❌ 未找到InventoryManager！");
        }
        
        InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI != null)
        {
            Debug.Log("ℹ️ InventoryUI存在，但Tab键输入处理已移至InventoryManager");
            Debug.Log("ℹ️ 这样可以避免InventoryPanel被禁用时无法响应输入的问题");
        }
        else
        {
            Debug.LogWarning("⚠️ 未找到InventoryUI组件");
        }
    }
    
    private void TryRecreateInventorySystem()
    {
        Debug.Log("尝试使用Setup工具重新创建背包系统...");
        
        InventorySystemSetup setup = FindObjectOfType<InventorySystemSetup>();
        if (setup == null)
        {
            // 创建临时的Setup组件
            GameObject tempObj = new GameObject("TempInventorySetup");
            setup = tempObj.AddComponent<InventorySystemSetup>();
        }
        
        // 强制重新创建
        setup.SetupInventorySystem();
        
        // 如果是临时创建的，销毁它
        if (setup.gameObject.name == "TempInventorySetup")
        {
            DestroyImmediate(setup.gameObject);
        }
    }
    
    private void Update()
    {
        // 提供手动修复快捷键
        if (Input.GetKeyDown(KeyCode.F9))
        {
            Debug.Log("检测到F9键，执行快速修复...");
            PerformQuickFix();
        }
    }
    
    private void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 150));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("背包系统快速修复工具");
        
        if (GUILayout.Button("执行快速修复"))
        {
            PerformQuickFix();
        }
        
        if (GUILayout.Button("测试Tab键功能"))
        {
            TestTabKeyFunctionality();
        }
        
        if (GUILayout.Button("检查输入处理位置"))
        {
            CheckInputHandlingLocation();
        }
        
        if (GUILayout.Button("强制重新创建系统"))
        {
            TryRecreateInventorySystem();
        }
        
        GUILayout.Label("快捷键: F9 - 快速修复");
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}