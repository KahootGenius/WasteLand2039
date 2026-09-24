using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 背包系统调试工具
/// 用于诊断和修复背包UI问题
/// </summary>
public class InventoryDebugger : MonoBehaviour
{
    [Header("调试设置")]
    public bool enableDebugLogs = true;
    public bool showSlotInfo = false;
    
    private InventoryUI inventoryUI;
    private InventoryManager inventoryManager;
    private Inventory inventory;
    
    private void Start()
    {
        // 查找组件
        inventoryUI = FindObjectOfType<InventoryUI>();
        inventoryManager = FindObjectOfType<InventoryManager>();
        inventory = FindObjectOfType<Inventory>();
        
        if (enableDebugLogs)
        {
            LogSystemStatus();
        }
    }
    
    [ContextMenu("诊断背包系统")]
    public void DiagnoseInventorySystem()
    {
        Debug.Log("=== 背包系统诊断开始 ===");
        
        // 重新查找组件
        inventoryUI = FindObjectOfType<InventoryUI>();
        inventoryManager = FindObjectOfType<InventoryManager>();
        inventory = FindObjectOfType<Inventory>();
        
        LogSystemStatus();
        CheckUIComponents();
        CheckSlotComponents();
        CheckLayoutComponents();
        
        Debug.Log("=== 背包系统诊断完成 ===");
    }
    
    [ContextMenu("修复背包UI")]
    public void FixInventoryUI()
    {
        Debug.Log("=== 开始修复背包UI ===");
        
        if (inventoryUI == null)
        {
            Debug.LogError("未找到InventoryUI组件！");
            return;
        }
        
        // 强制重新初始化
        inventoryUI.Initialize();
        
        // 等待一帧后再次检查
        StartCoroutine(DelayedCheck());
    }
    
    private System.Collections.IEnumerator DelayedCheck()
    {
        yield return null; // 等待一帧
        
        Debug.Log("=== 修复后检查 ===");
        CheckSlotComponents();
        CheckLayoutComponents();
    }
    
    [ContextMenu("清理重复槽位")]
    public void CleanDuplicateSlots()
    {
        if (inventoryUI == null || inventoryUI.slotsParent == null)
        {
            Debug.LogError("无法找到槽位父对象！");
            return;
        }
        
        Transform slotsParent = inventoryUI.slotsParent;
        int childCount = slotsParent.childCount;
        
        Debug.Log($"清理前槽位数量: {childCount}");
        
        // 获取所有InventorySlotUI组件
        List<InventorySlotUI> validSlots = new List<InventorySlotUI>();
        List<GameObject> objectsToDestroy = new List<GameObject>();
        
        for (int i = 0; i < childCount; i++)
        {
            Transform child = slotsParent.GetChild(i);
            InventorySlotUI slotUI = child.GetComponent<InventorySlotUI>();
            
            if (slotUI != null)
            {
                // 检查是否是有效的槽位
                if (slotUI.GetSlotIndex() >= 0 && slotUI.GetSlotIndex() < inventory.inventorySize)
                {
                    validSlots.Add(slotUI);
                }
                else
                {
                    Debug.Log($"发现无效槽位: 索引 {slotUI.GetSlotIndex()}");
                    objectsToDestroy.Add(child.gameObject);
                }
            }
            else
            {
                Debug.Log($"发现没有InventorySlotUI组件的对象: {child.name}");
                objectsToDestroy.Add(child.gameObject);
            }
        }
        
        // 销毁无效对象
        foreach (GameObject obj in objectsToDestroy)
        {
            DestroyImmediate(obj);
        }
        
        Debug.Log($"清理完成，保留 {validSlots.Count} 个有效槽位，删除了 {objectsToDestroy.Count} 个无效对象");
    }
    
    private void LogSystemStatus()
    {
        Debug.Log($"InventoryManager: {(inventoryManager != null ? "找到" : "未找到")}");
        Debug.Log($"InventoryUI: {(inventoryUI != null ? "找到" : "未找到")}");
        Debug.Log($"Inventory: {(inventory != null ? "找到" : "未找到")}");
        
        if (inventoryUI != null)
        {
            Debug.Log($"InventoryPanel: {(inventoryUI.inventoryPanel != null ? "找到" : "未找到")}");
            Debug.Log($"SlotsParent: {(inventoryUI.slotsParent != null ? "找到" : "未找到")}");
            Debug.Log($"SlotPrefab: {(inventoryUI.slotPrefab != null ? "找到" : "未找到")}");
            Debug.Log($"ItemTooltip: {(inventoryUI.itemTooltip != null ? "找到" : "未找到")}");
        }
    }
    
    private void CheckUIComponents()
    {
        if (inventoryUI == null) return;
        
        Debug.Log("--- UI组件检查 ---");
        
        if (inventoryUI.inventoryPanel == null)
        {
            Debug.LogError("InventoryPanel引用丢失！");
        }
        
        if (inventoryUI.slotsParent == null)
        {
            Debug.LogError("SlotsParent引用丢失！");
        }
        
        if (inventoryUI.slotPrefab == null)
        {
            Debug.LogError("SlotPrefab引用丢失！");
        }
        
        if (inventoryUI.itemTooltip == null)
        {
            Debug.LogWarning("ItemTooltip引用丢失！");
        }
    }
    
    private void CheckSlotComponents()
    {
        if (inventoryUI == null || inventoryUI.slotsParent == null) return;
        
        Debug.Log("--- 槽位组件检查 ---");
        
        Transform slotsParent = inventoryUI.slotsParent;
        int childCount = slotsParent.childCount;
        int validSlots = 0;
        int invalidSlots = 0;
        
        for (int i = 0; i < childCount; i++)
        {
            Transform child = slotsParent.GetChild(i);
            InventorySlotUI slotUI = child.GetComponent<InventorySlotUI>();
            
            if (slotUI != null)
            {
                bool isValid = true;
                
                if (slotUI.slotBackground == null)
                {
                    Debug.LogError($"槽位 {i} 缺少slotBackground引用");
                    isValid = false;
                }
                
                if (slotUI.itemIcon == null)
                {
                    Debug.LogError($"槽位 {i} 缺少itemIcon引用");
                    isValid = false;
                }
                
                if (slotUI.amountText == null)
                {
                    Debug.LogError($"槽位 {i} 缺少amountText引用");
                    isValid = false;
                }
                
                if (isValid)
                {
                    validSlots++;
                    if (showSlotInfo)
                    {
                        Debug.Log($"槽位 {i} 状态正常，索引: {slotUI.GetSlotIndex()}");
                    }
                }
                else
                {
                    invalidSlots++;
                }
            }
            else
            {
                Debug.LogError($"子对象 {i} ({child.name}) 缺少InventorySlotUI组件");
                invalidSlots++;
            }
        }
        
        Debug.Log($"槽位检查完成: 有效 {validSlots}, 无效 {invalidSlots}, 总计 {childCount}");
    }
    
    private void CheckLayoutComponents()
    {
        if (inventoryUI == null || inventoryUI.slotsParent == null) return;
        
        Debug.Log("--- 布局组件检查 ---");
        
        GridLayoutGroup gridLayout = inventoryUI.slotsParent.GetComponent<GridLayoutGroup>();
        if (gridLayout == null)
        {
            Debug.LogError("SlotsParent缺少GridLayoutGroup组件！");
            return;
        }
        
        Debug.Log($"GridLayoutGroup配置:");
        Debug.Log($"  Cell Size: {gridLayout.cellSize}");
        Debug.Log($"  Spacing: {gridLayout.spacing}");
        Debug.Log($"  Constraint: {gridLayout.constraint}");
        Debug.Log($"  Constraint Count: {gridLayout.constraintCount}");
        Debug.Log($"  Start Corner: {gridLayout.startCorner}");
        Debug.Log($"  Start Axis: {gridLayout.startAxis}");
        Debug.Log($"  Child Alignment: {gridLayout.childAlignment}");
        Debug.Log($"  Padding: {gridLayout.padding.left}, {gridLayout.padding.right}, {gridLayout.padding.top}, {gridLayout.padding.bottom}");
        
        RectTransform parentRect = inventoryUI.slotsParent.GetComponent<RectTransform>();
        Debug.Log($"SlotsParent Size: {parentRect.sizeDelta}");
        Debug.Log($"SlotsParent Position: {parentRect.anchoredPosition}");
    }
    
    private void Update()
    {
        // 快捷键调试
        if (Input.GetKeyDown(KeyCode.F1))
        {
            DiagnoseInventorySystem();
        }
        
        if (Input.GetKeyDown(KeyCode.F2))
        {
            FixInventoryUI();
        }
        
        if (Input.GetKeyDown(KeyCode.F3))
        {
            CleanDuplicateSlots();
        }
    }
    
    private void OnGUI()
    {
        if (!enableDebugLogs) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("背包系统调试工具", GUI.skin.box);
        GUILayout.Label("F1: 诊断系统");
        GUILayout.Label("F2: 修复UI");
        GUILayout.Label("F3: 清理重复槽位");
        
        if (inventoryUI != null && inventoryUI.slotsParent != null)
        {
            GUILayout.Label($"当前槽位数量: {inventoryUI.slotsParent.childCount}");
        }
        
        GUILayout.EndArea();
    }
}