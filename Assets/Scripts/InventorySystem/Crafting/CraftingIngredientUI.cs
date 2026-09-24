using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CraftingIngredientUI : MonoBehaviour
{
    [Header("UI组件")]
    public Image itemIcon;
    public TextMeshProUGUI itemNameText;
    public TextMeshProUGUI amountText;
    public Image backgroundImage;
    
    [Header("状态颜色")]
    public Color sufficientColor = Color.green;
    public Color insufficientColor = Color.red;
    public Color normalBackgroundColor = Color.white;
    public Color insufficientBackgroundColor = new Color(1f, 0.8f, 0.8f, 1f);
    
    private CraftingIngredient currentIngredient;
    private int currentAmount;
    
    /// <summary>
    /// 设置材料信息
    /// </summary>
    /// <param name="ingredient">材料信息</param>
    /// <param name="currentAmount">当前拥有数量</param>
    public void SetIngredient(CraftingIngredient ingredient, int currentAmount)
    {
        this.currentIngredient = ingredient;
        this.currentAmount = currentAmount;
        
        UpdateUI();
    }
    
    /// <summary>
    /// 更新当前拥有数量
    /// </summary>
    /// <param name="newAmount">新的拥有数量</param>
    public void UpdateAmount(int newAmount)
    {
        this.currentAmount = newAmount;
        UpdateAmountDisplay();
    }
    
    /// <summary>
    /// 更新UI显示
    /// </summary>
    private void UpdateUI()
    {
        if (currentIngredient == null || currentIngredient.item == null) return;
        
        // 设置物品图标
        if (itemIcon != null)
        {
            if (currentIngredient.item.icon != null)
            {
                itemIcon.sprite = currentIngredient.item.icon;
                itemIcon.color = Color.white;
            }
            else
            {
                itemIcon.sprite = null;
                itemIcon.color = new Color(1, 1, 1, 0);
            }
        }
        
        // 设置物品名称
        if (itemNameText != null)
        {
            itemNameText.text = currentIngredient.item.itemName;
        }
        
        // 更新数量显示
        UpdateAmountDisplay();
    }
    
    /// <summary>
    /// 更新数量显示和状态
    /// </summary>
    private void UpdateAmountDisplay()
    {
        if (currentIngredient == null) return;
        
        bool hasSufficient = currentAmount >= currentIngredient.amount;
        
        // 更新数量文本
        if (amountText != null)
        {
            amountText.text = $"{currentAmount}/{currentIngredient.amount}";
            amountText.color = hasSufficient ? sufficientColor : insufficientColor;
        }
        
        // 更新背景颜色
        if (backgroundImage != null)
        {
            backgroundImage.color = hasSufficient ? normalBackgroundColor : insufficientBackgroundColor;
        }
        
        // 更新物品名称颜色
        if (itemNameText != null)
        {
            itemNameText.color = hasSufficient ? Color.white : insufficientColor;
        }
    }
    
    /// <summary>
    /// 获取当前材料信息
    /// </summary>
    public CraftingIngredient GetIngredient()
    {
        return currentIngredient;
    }
    
    /// <summary>
    /// 检查材料是否充足
    /// </summary>
    public bool IsSufficient()
    {
        return currentIngredient != null && currentAmount >= currentIngredient.amount;
    }
}