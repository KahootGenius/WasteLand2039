using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("UI组件")]
    public GameObject inventoryPanel;
    public Transform slotsParent;
    public GameObject slotPrefab;
    public ItemTooltip itemTooltip;
    public Button closeButton;
    
    [Header("背包设置")]
    public int slotsPerRow = 5;
    public Vector2 slotSize = new Vector2(64, 64);
    public Vector2 slotSpacing = new Vector2(10, 10);
    
    public Inventory inventory;
    private List<InventorySlotUI> slotUIs = new List<InventorySlotUI>();
    private bool isInventoryOpen = false;
    
    [Header("输入设置")]
    public KeyCode toggleKey = KeyCode.Tab;
    
    private void Start()
    {
        inventory = FindObjectOfType<Inventory>();
        if (inventory == null)
        {
            Debug.LogError("未找到Inventory组件！");
            return;
        }
        
        InitializeUI();
        
        // 订阅背包变化事件
        Inventory.OnInventoryChanged += UpdateInventoryUI;
        
        // 关闭按钮事件
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseInventory);
        }
        
        // 初始状态关闭背包
        CloseInventory();
    }
    
    private void OnDestroy()
    {
        Inventory.OnInventoryChanged -= UpdateInventoryUI;
    }
    
    private void Update()
    {
        // 输入处理已移至InventoryManager，确保始终能响应
        // 这里保留其他可能的UI更新逻辑
    }
    
    /// <summary>
    /// 公共初始化方法，用于外部调用重新初始化UI
    /// </summary>
    public void Initialize()
    {
        Debug.Log("开始初始化InventoryUI...");
        
        // 重新查找Inventory组件
        if (inventory == null)
        {
            inventory = FindObjectOfType<Inventory>();
        }
        
        if (inventory == null)
        {
            Debug.LogError("InventoryUI.Initialize: 未找到Inventory组件！");
            return;
        }
        
        // 重新订阅事件
        Inventory.OnInventoryChanged -= UpdateInventoryUI;
        Inventory.OnInventoryChanged += UpdateInventoryUI;
        
        // 设置关闭按钮事件
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(CloseInventory);
        }
        
        // 初始化UI
        InitializeUI();
        
        // 初始状态关闭背包
        CloseInventory();
        
        Debug.Log("InventoryUI初始化完成！");
    }
    
    private void InitializeUI()
    {
        if (slotPrefab == null || slotsParent == null)
        {
            Debug.LogError("缺少必要的UI组件引用！");
            return;
        }
        
        // 清除现有槽位 - 更彻底的清理
        Debug.Log($"清理前槽位数量: {slotsParent.childCount}");
        
        // 先清空列表
        slotUIs.Clear();
        
        // 销毁所有子对象
        while (slotsParent.childCount > 0)
        {
            Transform child = slotsParent.GetChild(0);
            child.SetParent(null);
            DestroyImmediate(child.gameObject);
        }
        
        Debug.Log($"清理后槽位数量: {slotsParent.childCount}");
        
        // 添加GridLayoutGroup组件来自动管理布局
        GridLayoutGroup gridLayout = slotsParent.GetComponent<GridLayoutGroup>();
        if (gridLayout == null)
        {
            gridLayout = slotsParent.gameObject.AddComponent<GridLayoutGroup>();
            Debug.Log("创建了新的GridLayoutGroup组件");
        }
        else
        {
            Debug.Log("使用现有的GridLayoutGroup组件");
        }
        
        // 配置GridLayoutGroup
        gridLayout.cellSize = slotSize;
        gridLayout.spacing = slotSpacing;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = slotsPerRow;
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        gridLayout.childAlignment = TextAnchor.UpperLeft;
        
        // 确保布局组件正确配置
        gridLayout.padding = new RectOffset(0, 0, 0, 0);
        
        Debug.Log($"GridLayoutGroup配置: cellSize={gridLayout.cellSize}, spacing={gridLayout.spacing}, constraintCount={gridLayout.constraintCount}");
        
        // 创建槽位UI
        Debug.Log($"开始创建 {inventory.inventorySize} 个槽位");
        
        for (int i = 0; i < inventory.inventorySize; i++)
        {
            GameObject slotObj = Instantiate(slotPrefab, slotsParent);
            slotObj.name = $"InventorySlot_{i}";
            
            InventorySlotUI slotUI = slotObj.GetComponent<InventorySlotUI>();
            
            if (slotUI == null)
            {
                Debug.LogWarning($"槽位 {i} 缺少InventorySlotUI组件，正在添加");
                slotUI = slotObj.AddComponent<InventorySlotUI>();
            }
            
            // 确保槽位UI组件引用正确
            if (slotUI.slotBackground == null)
            {
                slotUI.slotBackground = slotObj.GetComponent<Image>();
            }
            
            if (slotUI.itemIcon == null)
            {
                Transform iconTransform = slotObj.transform.Find("ItemIcon");
                if (iconTransform != null)
                {
                    slotUI.itemIcon = iconTransform.GetComponent<Image>();
                }
            }
            
            if (slotUI.amountText == null)
            {
                Transform amountTransform = slotObj.transform.Find("AmountText");
                if (amountTransform != null)
                {
                    slotUI.amountText = amountTransform.GetComponent<TextMeshProUGUI>();
                }
            }
            
            slotUI.Initialize(i, this);
            slotUIs.Add(slotUI);
            
            // GridLayoutGroup会自动处理位置，所以不需要手动设置位置
            RectTransform slotRect = slotObj.GetComponent<RectTransform>();
            slotRect.sizeDelta = slotSize;
        }
        
        Debug.Log($"成功创建了 {slotUIs.Count} 个槽位UI");
        
        // 调整父容器大小以适应内容
        RectTransform parentRect = slotsParent.GetComponent<RectTransform>();
        int totalRows = Mathf.CeilToInt((float)inventory.inventorySize / slotsPerRow);
        Vector2 containerSize = new Vector2(
            slotsPerRow * slotSize.x + (slotsPerRow - 1) * slotSpacing.x,
            totalRows * slotSize.y + (totalRows - 1) * slotSpacing.y
        );
        parentRect.sizeDelta = containerSize;
        
        Debug.Log($"设置容器大小: {containerSize}, 总行数: {totalRows}");
        
        // 强制刷新布局
        Canvas.ForceUpdateCanvases();
        if (gridLayout != null)
        {
            gridLayout.enabled = false;
            gridLayout.enabled = true;
        }
        
        UpdateInventoryUI(inventory.GetAllSlots());
        
        Debug.Log("InventoryUI初始化完成，布局已刷新");
    }
    
    private void UpdateInventoryUI(List<InventorySlot> slots)
    {
        for (int i = 0; i < slotUIs.Count && i < slots.Count; i++)
        {
            slotUIs[i].SetSlotData(slots[i]);
        }
    }
    
    public void ToggleInventory()
    {
        if (isInventoryOpen)
        {
            CloseInventory();
        }
        else
        {
            OpenInventory();
        }
    }
    
    public void OpenInventory()
    {
        isInventoryOpen = true;
        inventoryPanel.SetActive(true);
        
        // 暂停游戏时间（可选）
        // Time.timeScale = 0f;
        
        // 显示鼠标光标
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    
    public void CloseInventory()
    {
        isInventoryOpen = false;
        inventoryPanel.SetActive(false);
        
        // 恢复游戏时间
        // Time.timeScale = 1f;
        
        // 隐藏物品提示
        if (itemTooltip != null)
        {
            itemTooltip.HideTooltip();
        }
        
        // 隐藏鼠标光标（根据游戏需要）
        // Cursor.lockState = CursorLockMode.Locked;
        // Cursor.visible = false;
    }
    
    public void ShowItemTooltip(Item item, Vector3 position)
    {
        if (itemTooltip != null && item != null)
        {
            itemTooltip.ShowTooltip(item, position);
        }
    }
    
    public void HideItemTooltip()
    {
        if (itemTooltip != null)
        {
            itemTooltip.HideTooltip();
        }
    }
    
    /// <summary>
    /// 检查背包是否打开
    /// </summary>
    public bool IsInventoryOpen()
    {
        return isInventoryOpen;
    }
    
    public void UseItem(int slotIndex)
    {
        InventorySlot slot = inventory.GetSlot(slotIndex);
        if (slot != null && !slot.IsEmpty())
        {
            Item item = slot.item;
            
            if (item.isConsumable)
            {
                // 使用消耗品
                Debug.Log($"使用了物品: {item.itemName}");
                inventory.RemoveItem(item, 1);
                
                // 这里可以添加具体的物品使用效果
                // 例如：恢复生命值、增加属性等
            }
            else
            {
                Debug.Log($"物品 {item.itemName} 不能被使用");
            }
        }
    }
    
    public void SwapItems(int fromIndex, int toIndex)
    {
        inventory.SwapSlots(fromIndex, toIndex);
    }
    
    // 公共方法：添加物品到背包
    public bool AddItemToInventory(Item item, int amount = 1)
    {
        return inventory.AddItem(item, amount);
    }
    
    // 公共方法：从背包移除物品
    public bool RemoveItemFromInventory(Item item, int amount = 1)
    {
        return inventory.RemoveItem(item, amount);
    }
    
    // 公共方法：检查背包中是否有指定物品
    public bool HasItemInInventory(Item item, int amount = 1)
    {
        return inventory.HasItem(item, amount);
    }
    
    // 公共方法：获取物品数量
    public int GetItemAmount(Item item)
    {
        return inventory.GetItemAmount(item);
    }
}