using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Header("UI组件")]
    public Image slotBackground;
    public Image itemIcon;
    public TextMeshProUGUI amountText;
    
    [Header("槽位状态图片")]
    public Sprite normalSlotSprite;
    public Sprite highlightedSlotSprite;
    
    private InventorySlot currentSlot;
    private int slotIndex;
    private InventoryUI inventoryUI;
    private Canvas canvas;
    private GraphicRaycaster graphicRaycaster;
    
    // 拖拽相关
    private GameObject draggedIcon;
    private bool isDragging = false;
    
    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        graphicRaycaster = GetComponentInParent<GraphicRaycaster>();
        
        if (slotBackground == null)
            slotBackground = GetComponent<Image>();
    }
    
    public void Initialize(int index, InventoryUI inventoryUI)
    {
        this.slotIndex = index;
        this.inventoryUI = inventoryUI;
        UpdateSlotUI();
    }
    
    public void SetSlotData(InventorySlot slot)
    {
        currentSlot = slot;
        UpdateSlotUI();
    }
    
    private void UpdateSlotUI()
    {
        if (currentSlot == null || currentSlot.IsEmpty())
        {
            // 空槽位
            itemIcon.sprite = null;
            itemIcon.color = new Color(1, 1, 1, 0); // 透明
            amountText.text = "";
        }
        else
        {
            // 有物品的槽位
            itemIcon.sprite = currentSlot.item.icon;
            itemIcon.color = Color.white;
            
            if (currentSlot.amount > 1)
            {
                amountText.text = currentSlot.amount.ToString();
            }
            else
            {
                amountText.text = "";
            }
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isDragging)
        {
            // 切换到高亮状态
            if (highlightedSlotSprite != null)
                slotBackground.sprite = highlightedSlotSprite;
            
            // 显示物品信息
            if (currentSlot != null && !currentSlot.IsEmpty() && inventoryUI != null)
            {
                inventoryUI.ShowItemTooltip(currentSlot.item, transform.position);
            }
        }
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isDragging)
        {
            // 切换回正常状态
            if (normalSlotSprite != null)
                slotBackground.sprite = normalSlotSprite;
            
            // 隐藏物品信息
            if (inventoryUI != null)
            {
                inventoryUI.HideItemTooltip();
            }
        }
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            // 右键点击 - 使用物品
            if (currentSlot != null && !currentSlot.IsEmpty())
            {
                inventoryUI.UseItem(slotIndex);
            }
        }
    }
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (currentSlot == null || currentSlot.IsEmpty()) return;
        
        isDragging = true;
        
        // 创建拖拽图标
        draggedIcon = new GameObject("DraggedIcon");
        draggedIcon.transform.SetParent(canvas.transform, false);
        draggedIcon.transform.SetAsLastSibling();
        
        Image dragImage = draggedIcon.AddComponent<Image>();
        dragImage.sprite = currentSlot.item.icon;
        dragImage.raycastTarget = false;
        
        RectTransform dragRect = draggedIcon.GetComponent<RectTransform>();
        dragRect.sizeDelta = itemIcon.rectTransform.sizeDelta;
        
        // 隐藏物品信息
        if (inventoryUI != null)
        {
            inventoryUI.HideItemTooltip();
        }
    }
    
    public void OnDrag(PointerEventData eventData)
    {
        if (draggedIcon != null)
        {
            draggedIcon.transform.position = eventData.position;
        }
    }
    
    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        
        if (draggedIcon != null)
        {
            Destroy(draggedIcon);
            draggedIcon = null;
        }
        
        // 恢复正常状态
        if (normalSlotSprite != null)
            slotBackground.sprite = normalSlotSprite;
    }
    
    public void OnDrop(PointerEventData eventData)
    {
        InventorySlotUI draggedSlot = eventData.pointerDrag?.GetComponent<InventorySlotUI>();
        if (draggedSlot != null && draggedSlot != this)
        {
            // 交换物品
            inventoryUI.SwapItems(draggedSlot.slotIndex, this.slotIndex);
        }
    }
    
    public InventorySlot GetSlot()
    {
        return currentSlot;
    }
    
    public int GetSlotIndex()
    {
        return slotIndex;
    }
}