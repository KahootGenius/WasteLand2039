using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemTooltip : MonoBehaviour
{
    [Header("UI组件")]
    public TextMeshProUGUI itemNameText;
    public TextMeshProUGUI itemTypeText;
    public TextMeshProUGUI itemLevelText;
    public TextMeshProUGUI itemDescriptionText;
    public Image backgroundImage;
    
    [Header("布局设置")]
    public float padding = 10f;
    public float maxWidth = 300f;
    
    private RectTransform rectTransform;
    private Canvas parentCanvas;
    
    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        gameObject.SetActive(false);
    }
    
    public void ShowTooltip(Item item, Vector3 position)
    {
        if (item == null) return;
        
        // 设置物品信息
        itemNameText.text = item.itemName;
        itemTypeText.text = GetItemTypeText(item.itemType);
        itemLevelText.text = $"Level: {item.itemLevel}";
        itemDescriptionText.text = item.description;
        
        // 调整布局
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        
        // 设置位置
        SetTooltipPosition(position);
        
        gameObject.SetActive(true);
    }
    
    public void HideTooltip()
    {
        gameObject.SetActive(false);
    }
    
    private void SetTooltipPosition(Vector3 worldPosition)
    {
        Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(parentCanvas.worldCamera, worldPosition);
        
        // 转换为Canvas坐标
        Vector2 localPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            screenPosition,
            parentCanvas.worldCamera,
            out localPosition
        );
        
        // 获取Canvas尺寸
        RectTransform canvasRect = parentCanvas.transform as RectTransform;
        Vector2 canvasSize = canvasRect.sizeDelta;
        
        // 获取提示框尺寸
        Vector2 tooltipSize = rectTransform.sizeDelta;
        
        // 调整位置以确保提示框在屏幕内
        float offsetX = 20f; // 鼠标偏移
        float offsetY = -20f;
        
        localPosition.x += offsetX;
        localPosition.y += offsetY;
        
        // 检查右边界
        if (localPosition.x + tooltipSize.x > canvasSize.x / 2)
        {
            localPosition.x = localPosition.x - tooltipSize.x - offsetX * 2;
        }
        
        // 检查下边界
        if (localPosition.y - tooltipSize.y < -canvasSize.y / 2)
        {
            localPosition.y = localPosition.y + tooltipSize.y - offsetY * 2;
        }
        
        // 检查左边界
        if (localPosition.x < -canvasSize.x / 2)
        {
            localPosition.x = -canvasSize.x / 2 + 10f;
        }
        
        // 检查上边界
        if (localPosition.y > canvasSize.y / 2)
        {
            localPosition.y = canvasSize.y / 2 - 10f;
        }
        
        rectTransform.localPosition = localPosition;
    }
    
    private string GetItemTypeText(ItemType itemType)
    {
        switch (itemType)
        {
            case ItemType.Weapon:
                return "Weapon";
            case ItemType.Armor:
                return "Armor";
            case ItemType.Consumable:
                return "Consumable";
            case ItemType.Material:
                return "Material";
            case ItemType.Quest:
                return "Quest Item";
            case ItemType.Misc:
                return "Misc";
            default:
                return "Unknown";
        }
    }
    
    private void Update()
    {
        // 如果提示框激活，跟随鼠标位置
        if (gameObject.activeInHierarchy)
        {
            Vector2 mousePosition = Input.mousePosition;
            Vector2 localPosition;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.transform as RectTransform,
                mousePosition,
                parentCanvas.worldCamera,
                out localPosition
            );
            
            // 应用偏移和边界检查
            SetTooltipPosition(mousePosition);
        }
    }
}