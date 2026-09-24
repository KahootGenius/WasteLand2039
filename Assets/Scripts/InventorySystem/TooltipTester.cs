using UnityEngine;

/// <summary>
/// Tooltip测试脚本
/// 用于快速测试Tooltip显示功能
/// </summary>
public class TooltipTester : MonoBehaviour
{
    [Header("测试设置")]
    public KeyCode testKey = KeyCode.T;
    
    private InventoryUI inventoryUI;
    private bool isTestingTooltip = false;
    
    private void Start()
    {
        inventoryUI = FindObjectOfType<InventoryUI>();
        
        if (inventoryUI == null)
        {
            Debug.LogError("TooltipTester: 未找到InventoryUI组件！");
            enabled = false;
            return;
        }
        
        if (inventoryUI.itemTooltip == null)
        {
            Debug.LogError("TooltipTester: InventoryUI.itemTooltip为null！");
        }
        else
        {
            Debug.Log("TooltipTester: Tooltip系统准备就绪，按T键测试");
        }
    }
    
    private void Update()
    {
        if (Input.GetKeyDown(testKey))
        {
            if (!isTestingTooltip)
            {
                ShowTestTooltip();
            }
            else
            {
                HideTestTooltip();
            }
        }
    }
    
    private void ShowTestTooltip()
    {
        if (inventoryUI == null || inventoryUI.itemTooltip == null)
        {
            Debug.LogError("无法显示测试Tooltip：组件引用为null");
            return;
        }
        
        // 创建测试物品
        Item testItem = ScriptableObject.CreateInstance<Item>();
        testItem.itemName = "Test Weapon";
        testItem.description = "This is a test item used to test the Tooltip display functionality. It should correctly display item information.";
        testItem.itemType = ItemType.Weapon;
        testItem.itemLevel = 5;
        
        // 显示Tooltip
        Vector3 mousePos = Input.mousePosition;
        inventoryUI.ShowItemTooltip(testItem, mousePos);
        
        isTestingTooltip = true;
        Debug.Log("✅ 显示测试Tooltip成功！再按T键隐藏");
    }
    
    private void HideTestTooltip()
    {
        if (inventoryUI != null)
        {
            inventoryUI.HideItemTooltip();
            isTestingTooltip = false;
            Debug.Log("隐藏测试Tooltip");
        }
    }
    
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 200, 300, 100));
        GUILayout.Label("Tooltip测试工具", GUI.skin.box);
        GUILayout.Label($"按 {testKey} 键测试Tooltip显示/隐藏");
        
        if (inventoryUI != null)
        {
            string status = inventoryUI.itemTooltip != null ? "✅ 正常" : "❌ 缺失";
            GUILayout.Label($"Tooltip状态: {status}");
        }
        
        GUILayout.EndArea();
    }
}