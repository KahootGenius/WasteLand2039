using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// IngredientUI固定位置布局管理器
/// 将IngredientUI排列在CraftingPanel右边400单位的位置
/// </summary>
public class IngredientUIFixedLayout : MonoBehaviour
{
    [Header("布局设置")]
    [Tooltip("相对于CraftingPanel的X偏移量")]
    public float xOffset = 400f;
    
    [Tooltip("IngredientUI的尺寸")]
    public Vector2 ingredientUISize = new Vector2(250, 60);
    
    [Tooltip("IngredientUI之间的垂直间距")]
    public float verticalSpacing = 15f;
    
    [Tooltip("起始Y位置偏移")]
    public float startYOffset = 0f;
    
    [Tooltip("是否自动应用布局")]
    public bool autoApplyLayout = true;
    
    private CraftingUI craftingUI;
    private RectTransform craftingPanelRect;
    
    private void Start()
    {
        // 查找CraftingUI组件
        craftingUI = FindObjectOfType<CraftingUI>();
        if (craftingUI == null)
        {
            Debug.LogError("CraftingUI not found!");
            return;
        }
        
        // 获取CraftingPanel的RectTransform
        if (craftingUI.craftingPanel != null)
        {
            craftingPanelRect = craftingUI.craftingPanel.GetComponent<RectTransform>();
        }
        
        if (autoApplyLayout)
        {
            ApplyFixedLayout();
        }
    }
    
    [ContextMenu("Apply Fixed Layout")]
    public void ApplyFixedLayout()
    {
        if (craftingUI == null || craftingUI.ingredientsParent == null)
        {
            Debug.LogError("CraftingUI or ingredientsParent not found!");
            return;
        }
        
        Debug.Log("=== 应用IngredientUI固定布局 ===");
        
        // 设置ingredientsParent的位置
        SetupIngredientsParentPosition();
        
        // 移除可能存在的Layout Group组件，使用手动布局
        RemoveLayoutGroups();
        
        // 应用固定布局到现有的IngredientUI
        ApplyLayoutToExistingIngredients();
        
        // 设置预制体
        SetupIngredientUIPrefab();
        
        Debug.Log("✅ 固定布局应用完成!");
    }
    
    private void SetupIngredientsParentPosition()
    {
        RectTransform ingredientsParentRect = craftingUI.ingredientsParent.GetComponent<RectTransform>();
        if (ingredientsParentRect == null) return;
        
        // 设置锚点为左上角
        ingredientsParentRect.anchorMin = new Vector2(0, 1);
        ingredientsParentRect.anchorMax = new Vector2(0, 1);
        ingredientsParentRect.pivot = new Vector2(0, 1);
        
        // 设置位置：CraftingPanel右边400单位
        Vector2 targetPosition = new Vector2(xOffset, startYOffset);
        ingredientsParentRect.anchoredPosition = targetPosition;
        
        // 设置合适的尺寸
        ingredientsParentRect.sizeDelta = new Vector2(ingredientUISize.x + 20, 500); // 给一些额外空间
        
        Debug.Log($"IngredientsParent positioned at: {targetPosition}");
    }
    
    private void RemoveLayoutGroups()
    {
        Transform ingredientsParent = craftingUI.ingredientsParent;
        
        // 移除所有Layout Group组件
        var layoutGroups = ingredientsParent.GetComponents<LayoutGroup>();
        for (int i = 0; i < layoutGroups.Length; i++)
        {
            if (Application.isPlaying)
                Destroy(layoutGroups[i]);
            else
                DestroyImmediate(layoutGroups[i]);
        }
        
        // 移除Content Size Fitter
        var contentSizeFitter = ingredientsParent.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null)
        {
            if (Application.isPlaying)
                Destroy(contentSizeFitter);
            else
                DestroyImmediate(contentSizeFitter);
        }
        
        Debug.Log("Removed Layout Groups for manual positioning");
    }
    
    private void ApplyLayoutToExistingIngredients()
    {
        if (craftingUI.ingredientsParent == null) return;
        
        var existingIngredients = craftingUI.ingredientsParent.GetComponentsInChildren<CraftingIngredientUI>();
        
        for (int i = 0; i < existingIngredients.Length; i++)
        {
            PositionIngredientUI(existingIngredients[i].gameObject, i);
        }
        
        Debug.Log($"Applied fixed layout to {existingIngredients.Length} existing IngredientUI objects");
    }
    
    private void SetupIngredientUIPrefab()
    {
        if (craftingUI.ingredientUIPrefab != null)
        {
            PositionIngredientUI(craftingUI.ingredientUIPrefab, 0);
            Debug.Log("Setup IngredientUI prefab layout");
        }
    }
    
    private void PositionIngredientUI(GameObject ingredientUIObj, int index)
    {
        var ingredientUI = ingredientUIObj.GetComponent<CraftingIngredientUI>();
        if (ingredientUI == null) return;
        
        RectTransform rect = ingredientUIObj.GetComponent<RectTransform>();
        if (rect == null) return;
        
        // 设置锚点为左上角
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        
        // 设置尺寸
        rect.sizeDelta = ingredientUISize;
        
        // 计算位置：垂直排列
        float yPosition = -(index * (ingredientUISize.y + verticalSpacing));
        rect.anchoredPosition = new Vector2(0, yPosition);
        
        // 移除Layout Element组件（如果存在）
        var layoutElement = ingredientUIObj.GetComponent<LayoutElement>();
        if (layoutElement != null)
        {
            if (Application.isPlaying)
                Destroy(layoutElement);
            else
                DestroyImmediate(layoutElement);
        }
        
        // 设置内部元素位置
        SetupInternalElements(ingredientUI);
        
        Debug.Log($"Positioned IngredientUI {index} at Y: {yPosition}");
    }
    
    private void SetupInternalElements(CraftingIngredientUI ingredientUI)
    {
        // 设置图标位置
        if (ingredientUI.itemIcon != null)
        {
            RectTransform iconRect = ingredientUI.itemIcon.GetComponent<RectTransform>();
            if (iconRect != null)
            {
                iconRect.anchorMin = new Vector2(0, 0.5f);
                iconRect.anchorMax = new Vector2(0, 0.5f);
                iconRect.pivot = new Vector2(0, 0.5f);
                iconRect.anchoredPosition = new Vector2(10, 0);
                iconRect.sizeDelta = new Vector2(45, 45);
            }
        }
        
        // 设置名称文字位置
        if (ingredientUI.itemNameText != null)
        {
            RectTransform nameRect = ingredientUI.itemNameText.GetComponent<RectTransform>();
            if (nameRect != null)
            {
                nameRect.anchorMin = new Vector2(0, 0.5f);
                nameRect.anchorMax = new Vector2(1, 0.5f);
                nameRect.pivot = new Vector2(0, 0.5f);
                nameRect.anchoredPosition = new Vector2(65, 10);
                nameRect.sizeDelta = new Vector2(-130, 25);
            }
            
            ingredientUI.itemNameText.fontSize = 14f;
            ingredientUI.itemNameText.alignment = TextAlignmentOptions.Left;
        }
        
        // 设置数量文字位置
        if (ingredientUI.amountText != null)
        {
            RectTransform amountRect = ingredientUI.amountText.GetComponent<RectTransform>();
            if (amountRect != null)
            {
                amountRect.anchorMin = new Vector2(1, 0.5f);
                amountRect.anchorMax = new Vector2(1, 0.5f);
                amountRect.pivot = new Vector2(1, 0.5f);
                amountRect.anchoredPosition = new Vector2(-10, -10);
                amountRect.sizeDelta = new Vector2(60, 20);
            }
            
            ingredientUI.amountText.fontSize = 12f;
            ingredientUI.amountText.alignment = TextAlignmentOptions.Right;
        }
        
        // 设置背景
        if (ingredientUI.backgroundImage != null)
        {
            RectTransform bgRect = ingredientUI.backgroundImage.GetComponent<RectTransform>();
            if (bgRect != null)
            {
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.sizeDelta = Vector2.zero;
                bgRect.anchoredPosition = Vector2.zero;
            }
        }
    }
    
    /// <summary>
    /// 当CraftingUI创建新的IngredientUI时调用此方法
    /// </summary>
    public void OnIngredientUICreated()
    {
        if (!autoApplyLayout) return;
        
        // 延迟一帧应用布局，确保新创建的UI已经添加到父对象
        StartCoroutine(DelayedLayoutApplication());
    }
    
    private System.Collections.IEnumerator DelayedLayoutApplication()
    {
        yield return null; // 等待一帧
        ApplyLayoutToExistingIngredients();
    }
    
    [ContextMenu("Adjust Position")]
    public void AdjustPosition()
    {
        SetupIngredientsParentPosition();
    }
    
    [ContextMenu("Test Layout")]
    public void TestLayout()
    {
        Debug.Log($"Current settings:");
        Debug.Log($"- X Offset: {xOffset}");
        Debug.Log($"- Ingredient UI Size: {ingredientUISize}");
        Debug.Log($"- Vertical Spacing: {verticalSpacing}");
        Debug.Log($"- Start Y Offset: {startYOffset}");
        
        ApplyFixedLayout();
    }
    
    private void OnGUI()
    {
        if (Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 200, 300, 120));
            GUILayout.Label("IngredientUI Fixed Layout", GUI.skin.box);
            
            if (GUILayout.Button("Apply Fixed Layout"))
            {
                ApplyFixedLayout();
            }
            
            if (GUILayout.Button("Adjust Position Only"))
            {
                AdjustPosition();
            }
            
            GUILayout.Label($"Position: CraftingPanel + ({xOffset}, {startYOffset})");
            GUILayout.Label($"Size: {ingredientUISize}, Spacing: {verticalSpacing}");
            
            GUILayout.EndArea();
        }
    }
}