using UnityEngine;

/// <summary>
/// 输入测试脚本
/// 用于测试键盘输入是否正常工作
/// </summary>
public class InputTester : MonoBehaviour
{
    [Header("测试设置")]
    public bool enableDebugLog = true;
    public KeyCode[] testKeys = { KeyCode.Tab, KeyCode.I, KeyCode.B, KeyCode.Escape };
    
    private void Update()
    {
        if (!enableDebugLog) return;
        
        // 测试所有指定的按键
        foreach (KeyCode key in testKeys)
        {
            if (Input.GetKeyDown(key))
            {
                Debug.Log($"✅ 检测到按键: {key}");
                
                // 特别处理Tab键
                if (key == KeyCode.Tab)
                {
                    TestInventoryToggle();
                }
            }
        }
        
        // 检测任意按键
        if (Input.inputString != "")
        {
            foreach (char c in Input.inputString)
            {
                if (c == '\b') // backspace
                {
                    Debug.Log("检测到退格键");
                }
                else if (c == '\n' || c == '\r') // enter
                {
                    Debug.Log("检测到回车键");
                }
                else if (c == '\t') // tab
                {
                    Debug.Log("✅ 通过inputString检测到Tab键");
                }
                else
                {
                    Debug.Log($"检测到字符输入: {c} (ASCII: {(int)c})");
                }
            }
        }
    }
    
    private void TestInventoryToggle()
    {
        var inventoryUI = FindObjectOfType<InventoryUI>();
        if (inventoryUI != null)
        {
            Debug.Log($"🎒 当前背包状态: {(inventoryUI.IsInventoryOpen() ? "打开" : "关闭")}");
            Debug.Log($"🎒 InventoryUI组件状态: Active={inventoryUI.gameObject.activeInHierarchy}, Enabled={inventoryUI.enabled}");
        }
        else
        {
            Debug.LogError("❌ 未找到InventoryUI组件！");
        }
        
        var inventoryManager = FindObjectOfType<InventoryManager>();
        if (inventoryManager != null)
        {
            Debug.Log($"📦 InventoryManager状态: Active={inventoryManager.gameObject.activeInHierarchy}, Enabled={inventoryManager.enabled}");
        }
        else
        {
            Debug.LogError("❌ 未找到InventoryManager组件！");
        }
    }
    
    private void OnGUI()
    {
        if (!enableDebugLog) return;
        
        GUILayout.BeginArea(new Rect(Screen.width - 250, 10, 240, 150));
        GUILayout.Label("输入测试器", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold });
        
        GUILayout.Label("测试按键:");
        foreach (KeyCode key in testKeys)
        {
            bool isPressed = Input.GetKey(key);
            GUILayout.Label($"{key}: {(isPressed ? "按下" : "释放")}", 
                new GUIStyle(GUI.skin.label) { normal = { textColor = isPressed ? Color.green : Color.white } });
        }
        
        GUILayout.Space(10);
        if (GUILayout.Button("强制切换背包"))
        {
            var inventoryUI = FindObjectOfType<InventoryUI>();
            if (inventoryUI != null)
            {
                if (inventoryUI.IsInventoryOpen())
                {
                    inventoryUI.CloseInventory();
                }
                else
                {
                    inventoryUI.OpenInventory();
                }
            }
        }
        
        GUILayout.EndArea();
    }
}