using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Quick fix for IngredientUI overlap and text crowding issues
/// </summary>
public class IngredientUIQuickFix : MonoBehaviour
{
    [Header("Quick Fix Settings")]
    [Tooltip("Automatically fix on start")]
    public bool autoFixOnStart = true;
    
    [Header("Layout Fix Settings")]
    [Tooltip("Size for each IngredientUI item")]
    public Vector2 ingredientUISize = new Vector2(250, 60);
    
    [Tooltip("Spacing between items")]
    public float itemSpacing = 15f;
    
    [Header("Text Layout Settings")]
    [Tooltip("Icon position within IngredientUI")]
    public Vector2 iconPosition = new Vector2(-100, 0);
    
    [Tooltip("Icon size")]
    public Vector2 iconSize = new Vector2(45, 45);
    
    [Tooltip("Name text position")]
    public Vector2 nameTextPosition = new Vector2(-20, 15);
    
    [Tooltip("Amount text position")]
    public Vector2 amountTextPosition = new Vector2(90, -15);
    
    [Tooltip("Name text font size")]
    public float nameTextFontSize = 14f;
    
    [Tooltip("Amount text font size")]
    public float amountTextFontSize = 12f;
    
    private CraftingUI craftingUI;
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            FixIngredientUIIssues();
        }
    }
    
    [ContextMenu("Fix IngredientUI Issues")]
    public void FixIngredientUIIssues()
    {
        Debug.Log("=== Starting IngredientUI Quick Fix ===");
        
        // Find CraftingUI
        if (craftingUI == null)
        {
            craftingUI = FindObjectOfType<CraftingUI>();
        }
        
        if (craftingUI == null)
        {
            Debug.LogError("CraftingUI not found! Please ensure CraftingUI exists in the scene.");
            return;
        }
        
        // Fix container layout
        FixContainerLayout();
        
        // Fix existing IngredientUI items
        FixExistingIngredientUIs();
        
        // Fix prefab if available
        FixIngredientUIPrefab();
        
        Debug.Log("✅ IngredientUI issues fixed successfully!");
    }
    
    private void FixContainerLayout()
    {
        Transform ingredientsParent = craftingUI.ingredientsParent;
        
        if (ingredientsParent == null)
        {
            Debug.LogError("IngredientsParent not found in CraftingUI!");
            return;
        }
        
        // Remove existing layout groups to avoid conflicts
        var existingLayouts = ingredientsParent.GetComponents<LayoutGroup>();
        for (int i = 0; i < existingLayouts.Length; i++)
        {
            if (Application.isPlaying)
                Destroy(existingLayouts[i]);
            else
                DestroyImmediate(existingLayouts[i]);
        }
        
        // Add Vertical Layout Group for proper spacing
        var verticalLayout = ingredientsParent.gameObject.AddComponent<VerticalLayoutGroup>();
        verticalLayout.spacing = itemSpacing;
        verticalLayout.padding = new RectOffset(10, 10, 10, 10);
        verticalLayout.childAlignment = TextAnchor.UpperCenter;
        verticalLayout.childControlWidth = true;
        verticalLayout.childControlHeight = false;
        verticalLayout.childForceExpandWidth = false;
        verticalLayout.childForceExpandHeight = false;
        
        // Add Content Size Fitter for dynamic sizing
        var contentSizeFitter = ingredientsParent.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter == null)
        {
            contentSizeFitter = ingredientsParent.gameObject.AddComponent<ContentSizeFitter>();
        }
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        
        Debug.Log("Fixed container layout with Vertical Layout Group");
    }
    
    private void FixExistingIngredientUIs()
    {
        if (craftingUI.ingredientsParent == null) return;
        
        var existingIngredients = craftingUI.ingredientsParent.GetComponentsInChildren<CraftingIngredientUI>();
        
        foreach (var ingredient in existingIngredients)
        {
            FixSingleIngredientUI(ingredient.gameObject);
        }
        
        Debug.Log($"Fixed {existingIngredients.Length} existing IngredientUI objects");
    }
    
    private void FixIngredientUIPrefab()
    {
        if (craftingUI.ingredientUIPrefab != null)
        {
            FixSingleIngredientUI(craftingUI.ingredientUIPrefab);
            Debug.Log("Fixed IngredientUI prefab");
        }
        else
        {
            Debug.LogWarning("IngredientUI prefab not found in CraftingUI");
        }
    }
    
    private void FixSingleIngredientUI(GameObject ingredientUIObj)
    {
        var ingredientUI = ingredientUIObj.GetComponent<CraftingIngredientUI>();
        if (ingredientUI == null) return;
        
        // Fix main size
        RectTransform mainRect = ingredientUIObj.GetComponent<RectTransform>();
        if (mainRect != null)
        {
            mainRect.sizeDelta = ingredientUISize;
        }
        
        // Add Layout Element for better control
        var layoutElement = ingredientUIObj.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = ingredientUIObj.AddComponent<LayoutElement>();
        }
        layoutElement.preferredWidth = ingredientUISize.x;
        layoutElement.preferredHeight = ingredientUISize.y;
        
        // Fix icon
        if (ingredientUI.itemIcon != null)
        {
            RectTransform iconRect = ingredientUI.itemIcon.GetComponent<RectTransform>();
            if (iconRect != null)
            {
                iconRect.anchoredPosition = iconPosition;
                iconRect.sizeDelta = iconSize;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            }
        }
        
        // Fix name text
        if (ingredientUI.itemNameText != null)
        {
            RectTransform nameRect = ingredientUI.itemNameText.GetComponent<RectTransform>();
            if (nameRect != null)
            {
                nameRect.anchoredPosition = nameTextPosition;
                nameRect.anchorMin = new Vector2(0.5f, 0.5f);
                nameRect.anchorMax = new Vector2(0.5f, 0.5f);
                nameRect.sizeDelta = new Vector2(120, 30);
            }
            
            // Fix font size
            ingredientUI.itemNameText.fontSize = nameTextFontSize;
            ingredientUI.itemNameText.alignment = TextAlignmentOptions.Left;
        }
        
        // Fix amount text
        if (ingredientUI.amountText != null)
        {
            RectTransform amountRect = ingredientUI.amountText.GetComponent<RectTransform>();
            if (amountRect != null)
            {
                amountRect.anchoredPosition = amountTextPosition;
                amountRect.anchorMin = new Vector2(0.5f, 0.5f);
                amountRect.anchorMax = new Vector2(0.5f, 0.5f);
                amountRect.sizeDelta = new Vector2(80, 25);
            }
            
            // Fix font size
            ingredientUI.amountText.fontSize = amountTextFontSize;
            ingredientUI.amountText.alignment = TextAlignmentOptions.Right;
        }
        
        // Fix background if exists
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
    
    [ContextMenu("Apply Compact Layout")]
    public void ApplyCompactLayout()
    {
        ingredientUISize = new Vector2(200, 45);
        itemSpacing = 8f;
        iconPosition = new Vector2(-80, 0);
        iconSize = new Vector2(35, 35);
        nameTextPosition = new Vector2(-15, 10);
        amountTextPosition = new Vector2(70, -10);
        nameTextFontSize = 12f;
        amountTextFontSize = 10f;
        
        FixIngredientUIIssues();
        Debug.Log("Applied compact layout");
    }
    
    [ContextMenu("Apply Wide Layout")]
    public void ApplyWideLayout()
    {
        ingredientUISize = new Vector2(320, 70);
        itemSpacing = 20f;
        iconPosition = new Vector2(-130, 0);
        iconSize = new Vector2(55, 55);
        nameTextPosition = new Vector2(-30, 20);
        amountTextPosition = new Vector2(120, -20);
        nameTextFontSize = 16f;
        amountTextFontSize = 14f;
        
        FixIngredientUIIssues();
        Debug.Log("Applied wide layout");
    }
    
    [ContextMenu("Reset to Default")]
    public void ResetToDefault()
    {
        ingredientUISize = new Vector2(250, 60);
        itemSpacing = 15f;
        iconPosition = new Vector2(-100, 0);
        iconSize = new Vector2(45, 45);
        nameTextPosition = new Vector2(-20, 15);
        amountTextPosition = new Vector2(90, -15);
        nameTextFontSize = 14f;
        amountTextFontSize = 12f;
        
        FixIngredientUIIssues();
        Debug.Log("Reset to default layout");
    }
    
    private void OnGUI()
    {
        if (Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.Label("IngredientUI Quick Fix", GUI.skin.box);
            
            if (GUILayout.Button("Fix Issues Now"))
            {
                FixIngredientUIIssues();
            }
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Compact"))
            {
                ApplyCompactLayout();
            }
            if (GUILayout.Button("Default"))
            {
                ResetToDefault();
            }
            if (GUILayout.Button("Wide"))
            {
                ApplyWideLayout();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Label($"Item Size: {ingredientUISize}");
            GUILayout.Label($"Spacing: {itemSpacing}");
            
            GUILayout.EndArea();
        }
    }
}