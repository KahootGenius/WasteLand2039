using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// IngredientUI Position Adjuster - Tool for adjusting IngredientUI positioning and layout
/// </summary>
public class IngredientUIPositionAdjuster : MonoBehaviour
{
    [Header("IngredientUI Layout Settings")]
    [Tooltip("Size of each IngredientUI item")]
    public Vector2 ingredientUISize = new Vector2(200, 50);
    
    [Tooltip("Spacing between IngredientUI items")]
    public float itemSpacing = 10f;
    
    [Tooltip("Padding around the ingredients container")]
    public RectOffset containerPadding;
    
    [Header("Container Position")]
    [Tooltip("Position of the ingredients container relative to crafting panel")]
    public Vector2 containerPosition = new Vector2(0, -100);
    
    [Tooltip("Size of the ingredients container")]
    public Vector2 containerSize = new Vector2(300, 200);
    
    [Header("Layout Direction")]
    [Tooltip("Layout direction for ingredients")]
    public LayoutDirection layoutDirection = LayoutDirection.Vertical;
    
    [Header("Individual Element Positions (within each IngredientUI)")]
    [Tooltip("Position of the item icon within IngredientUI")]
    public Vector2 iconPosition = new Vector2(-75, 0);
    
    [Tooltip("Size of the item icon")]
    public Vector2 iconSize = new Vector2(40, 40);
    
    [Tooltip("Position of the item name text")]
    public Vector2 nameTextPosition = new Vector2(0, 10);
    
    [Tooltip("Position of the amount text")]
    public Vector2 amountTextPosition = new Vector2(75, -10);
    
    [Header("Auto-Apply Settings")]
    [Tooltip("Automatically apply settings on start")]
    public bool autoApplyOnStart = true;
    
    [Tooltip("Apply settings when values change in editor")]
    public bool applyOnValidate = true;
    
    public enum LayoutDirection
    {
        Vertical,
        Horizontal,
        Grid
    }
    
    private CraftingUI craftingUI;
    private Transform ingredientsParent;
    
    private void Awake()
    {
        // Initialize RectOffset to avoid constructor call in field initialization
        if (containerPadding == null)
        {
            containerPadding = new RectOffset(10, 10, 10, 10);
        }
    }
    
    private void Start()
    {
        if (autoApplyOnStart)
        {
            ApplyPositionSettings();
        }
    }
    
    private void OnValidate()
    {
        if (applyOnValidate && Application.isPlaying)
        {
            ApplyPositionSettings();
        }
    }
    
    [ContextMenu("Apply Position Settings")]
    public void ApplyPositionSettings()
    {
        Debug.Log("=== Applying IngredientUI Position Settings ===");
        
        // Find CraftingUI if not already found
        if (craftingUI == null)
        {
            craftingUI = FindObjectOfType<CraftingUI>();
        }
        
        if (craftingUI == null)
        {
            Debug.LogError("CraftingUI not found! Please ensure CraftingUI exists in the scene.");
            return;
        }
        
        // Apply container settings
        ApplyContainerSettings();
        
        // Apply layout settings
        ApplyLayoutSettings();
        
        // Apply individual IngredientUI settings
        ApplyIngredientUISettings();
        
        Debug.Log("✅ IngredientUI position settings applied successfully!");
    }
    
    private void ApplyContainerSettings()
    {
        ingredientsParent = craftingUI.ingredientsParent;
        
        if (ingredientsParent == null)
        {
            Debug.LogError("IngredientsParent not found in CraftingUI!");
            return;
        }
        
        RectTransform containerRect = ingredientsParent.GetComponent<RectTransform>();
        if (containerRect != null)
        {
            // Set container position and size
            containerRect.anchoredPosition = containerPosition;
            containerRect.sizeDelta = containerSize;
            
            Debug.Log($"Set container position to {containerPosition} and size to {containerSize}");
        }
        
        // Setup layout group
        SetupLayoutGroup();
    }
    
    private void SetupLayoutGroup()
    {
        // Remove existing layout groups
        var existingLayouts = ingredientsParent.GetComponents<LayoutGroup>();
        for (int i = 0; i < existingLayouts.Length; i++)
        {
            if (Application.isPlaying)
                Destroy(existingLayouts[i]);
            else
                DestroyImmediate(existingLayouts[i]);
        }
        
        // Add appropriate layout group based on direction
        switch (layoutDirection)
        {
            case LayoutDirection.Vertical:
                var verticalLayout = ingredientsParent.gameObject.AddComponent<VerticalLayoutGroup>();
                verticalLayout.spacing = itemSpacing;
                verticalLayout.padding = containerPadding;
                verticalLayout.childAlignment = TextAnchor.UpperCenter;
                verticalLayout.childControlWidth = true;
                verticalLayout.childControlHeight = false;
                verticalLayout.childForceExpandWidth = false;
                verticalLayout.childForceExpandHeight = false;
                Debug.Log("Applied Vertical Layout Group");
                break;
                
            case LayoutDirection.Horizontal:
                var horizontalLayout = ingredientsParent.gameObject.AddComponent<HorizontalLayoutGroup>();
                horizontalLayout.spacing = itemSpacing;
                horizontalLayout.padding = containerPadding;
                horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
                horizontalLayout.childControlWidth = false;
                horizontalLayout.childControlHeight = true;
                horizontalLayout.childForceExpandWidth = false;
                horizontalLayout.childForceExpandHeight = false;
                Debug.Log("Applied Horizontal Layout Group");
                break;
                
            case LayoutDirection.Grid:
                var gridLayout = ingredientsParent.gameObject.AddComponent<GridLayoutGroup>();
                gridLayout.cellSize = ingredientUISize;
                gridLayout.spacing = new Vector2(itemSpacing, itemSpacing);
                gridLayout.padding = containerPadding;
                gridLayout.childAlignment = TextAnchor.UpperLeft;
                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = 2; // 2 columns
                Debug.Log("Applied Grid Layout Group");
                break;
        }
        
        // Add Content Size Fitter for dynamic sizing
        var contentSizeFitter = ingredientsParent.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter == null)
        {
            contentSizeFitter = ingredientsParent.gameObject.AddComponent<ContentSizeFitter>();
        }
        
        if (layoutDirection == LayoutDirection.Vertical)
        {
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }
        else if (layoutDirection == LayoutDirection.Horizontal)
        {
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }
        else
        {
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
    
    private void ApplyLayoutSettings()
    {
        // Apply layout settings to existing IngredientUI objects
        var existingIngredients = ingredientsParent.GetComponentsInChildren<CraftingIngredientUI>();
        
        foreach (var ingredient in existingIngredients)
        {
            RectTransform rect = ingredient.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = ingredientUISize;
                
                // Add Layout Element for better control
                var layoutElement = ingredient.GetComponent<LayoutElement>();
                if (layoutElement == null)
                {
                    layoutElement = ingredient.gameObject.AddComponent<LayoutElement>();
                }
                
                layoutElement.preferredWidth = ingredientUISize.x;
                layoutElement.preferredHeight = ingredientUISize.y;
            }
        }
        
        Debug.Log($"Applied layout settings to {existingIngredients.Length} existing IngredientUI objects");
    }
    
    private void ApplyIngredientUISettings()
    {
        // Apply settings to the prefab if available
        if (craftingUI.ingredientUIPrefab != null)
        {
            ApplySettingsToPrefab(craftingUI.ingredientUIPrefab);
        }
        
        // Apply settings to existing instances
        var existingIngredients = ingredientsParent.GetComponentsInChildren<CraftingIngredientUI>();
        foreach (var ingredient in existingIngredients)
        {
            ApplySettingsToIngredientUI(ingredient.gameObject);
        }
    }
    
    private void ApplySettingsToPrefab(GameObject prefab)
    {
        var ingredientUI = prefab.GetComponent<CraftingIngredientUI>();
        if (ingredientUI != null)
        {
            ApplySettingsToIngredientUI(prefab);
            Debug.Log("Applied settings to IngredientUI prefab");
        }
    }
    
    private void ApplySettingsToIngredientUI(GameObject ingredientUIObj)
    {
        var ingredientUI = ingredientUIObj.GetComponent<CraftingIngredientUI>();
        if (ingredientUI == null) return;
        
        // Set main size
        RectTransform mainRect = ingredientUIObj.GetComponent<RectTransform>();
        if (mainRect != null)
        {
            mainRect.sizeDelta = ingredientUISize;
        }
        
        // Adjust icon position and size
        if (ingredientUI.itemIcon != null)
        {
            RectTransform iconRect = ingredientUI.itemIcon.GetComponent<RectTransform>();
            if (iconRect != null)
            {
                iconRect.anchoredPosition = iconPosition;
                iconRect.sizeDelta = iconSize;
            }
        }
        
        // Adjust name text position
        if (ingredientUI.itemNameText != null)
        {
            RectTransform nameRect = ingredientUI.itemNameText.GetComponent<RectTransform>();
            if (nameRect != null)
            {
                nameRect.anchoredPosition = nameTextPosition;
            }
        }
        
        // Adjust amount text position
        if (ingredientUI.amountText != null)
        {
            RectTransform amountRect = ingredientUI.amountText.GetComponent<RectTransform>();
            if (amountRect != null)
            {
                amountRect.anchoredPosition = amountTextPosition;
            }
        }
    }
    
    [ContextMenu("Reset to Default Positions")]
    public void ResetToDefaultPositions()
    {
        ingredientUISize = new Vector2(200, 50);
        itemSpacing = 10f;
        containerPadding = new RectOffset(10, 10, 10, 10);
        containerPosition = new Vector2(0, -100);
        containerSize = new Vector2(300, 200);
        layoutDirection = LayoutDirection.Vertical;
        iconPosition = new Vector2(-75, 0);
        iconSize = new Vector2(40, 40);
        nameTextPosition = new Vector2(0, 10);
        amountTextPosition = new Vector2(75, -10);
        
        ApplyPositionSettings();
        Debug.Log("Reset to default positions");
    }
    
    [ContextMenu("Compact Layout")]
    public void SetCompactLayout()
    {
        ingredientUISize = new Vector2(150, 35);
        itemSpacing = 5f;
        containerPadding = new RectOffset(5, 5, 5, 5);
        iconPosition = new Vector2(-60, 0);
        iconSize = new Vector2(30, 30);
        nameTextPosition = new Vector2(0, 5);
        amountTextPosition = new Vector2(60, -5);
        
        ApplyPositionSettings();
        Debug.Log("Applied compact layout");
    }
    
    [ContextMenu("Wide Layout")]
    public void SetWideLayout()
    {
        ingredientUISize = new Vector2(300, 60);
        itemSpacing = 15f;
        containerPadding = new RectOffset(15, 15, 15, 15);
        iconPosition = new Vector2(-120, 0);
        iconSize = new Vector2(50, 50);
        nameTextPosition = new Vector2(-20, 10);
        amountTextPosition = new Vector2(120, -10);
        
        ApplyPositionSettings();
        Debug.Log("Applied wide layout");
    }
    
    private void OnGUI()
    {
        if (Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 350, 350, 200));
            GUILayout.Label("IngredientUI Position Adjuster", GUI.skin.box);
            
            GUILayout.Label("Quick Actions:");
            
            if (GUILayout.Button("Apply Current Settings"))
            {
                ApplyPositionSettings();
            }
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Compact"))
            {
                SetCompactLayout();
            }
            if (GUILayout.Button("Default"))
            {
                ResetToDefaultPositions();
            }
            if (GUILayout.Button("Wide"))
            {
                SetWideLayout();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Label($"Layout: {layoutDirection}");
            GUILayout.Label($"Container: {containerPosition}");
            GUILayout.Label($"Item Size: {ingredientUISize}");
            
            GUILayout.EndArea();
        }
    }
}