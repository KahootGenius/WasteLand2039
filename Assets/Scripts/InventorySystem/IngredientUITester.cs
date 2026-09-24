using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// IngredientUI测试和调试工具
/// 用于验证快速修复是否正常工作
/// </summary>
public class IngredientUITester : MonoBehaviour
{
    [Header("测试设置")]
    [Tooltip("是否在开始时运行测试")]
    public bool runTestOnStart = false;
    
    [Tooltip("是否显示调试信息")]
    public bool showDebugInfo = true;
    
    [Header("测试结果")]
    [SerializeField] private bool hasLayoutGroup;
    [SerializeField] private bool hasContentSizeFitter;
    [SerializeField] private int ingredientUICount;
    [SerializeField] private Vector2 containerSize;
    [SerializeField] private float actualSpacing;
    
    private CraftingUI craftingUI;
    private IngredientUIQuickFix quickFix;
    
    private void Start()
    {
        if (runTestOnStart)
        {
            Invoke(nameof(RunDiagnostics), 1f); // 延迟1秒确保其他组件初始化完成
        }
    }
    
    [ContextMenu("Run Diagnostics")]
    public void RunDiagnostics()
    {
        Debug.Log("=== IngredientUI 诊断开始 ===");
        
        FindComponents();
        CheckLayoutIssues();
        CheckIngredientUIIssues();
        ProvideRecommendations();
        
        Debug.Log("=== IngredientUI 诊断完成 ===");
    }
    
    private void FindComponents()
    {
        // 查找CraftingUI
        if (craftingUI == null)
        {
            craftingUI = FindObjectOfType<CraftingUI>();
        }
        
        if (craftingUI == null)
        {
            Debug.LogError("❌ CraftingUI not found in scene!");
            return;
        }
        else
        {
            Debug.Log("✅ CraftingUI found");
        }
        
        // 查找QuickFix组件
        quickFix = FindObjectOfType<IngredientUIQuickFix>();
        if (quickFix == null)
        {
            Debug.LogWarning("⚠️ IngredientUIQuickFix not found. Consider adding it to fix layout issues.");
        }
        else
        {
            Debug.Log("✅ IngredientUIQuickFix found");
        }
    }
    
    private void CheckLayoutIssues()
    {
        if (craftingUI == null || craftingUI.ingredientsParent == null)
        {
            Debug.LogError("❌ IngredientsParent not found!");
            return;
        }
        
        Transform ingredientsParent = craftingUI.ingredientsParent;
        
        // 检查Layout Group
        var layoutGroups = ingredientsParent.GetComponents<LayoutGroup>();
        hasLayoutGroup = layoutGroups.Length > 0;
        
        if (hasLayoutGroup)
        {
            Debug.Log($"✅ Layout Group found: {layoutGroups[0].GetType().Name}");
            
            if (layoutGroups[0] is VerticalLayoutGroup vlg)
            {
                actualSpacing = vlg.spacing;
                Debug.Log($"   - Spacing: {actualSpacing}");
                Debug.Log($"   - Padding: {vlg.padding.left}, {vlg.padding.right}, {vlg.padding.top}, {vlg.padding.bottom}");
            }
            else if (layoutGroups[0] is HorizontalLayoutGroup hlg)
            {
                actualSpacing = hlg.spacing;
                Debug.Log($"   - Spacing: {actualSpacing}");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ No Layout Group found - this may cause overlapping issues");
        }
        
        // 检查Content Size Fitter
        var contentSizeFitter = ingredientsParent.GetComponent<ContentSizeFitter>();
        hasContentSizeFitter = contentSizeFitter != null;
        
        if (hasContentSizeFitter)
        {
            Debug.Log($"✅ Content Size Fitter found - Vertical: {contentSizeFitter.verticalFit}, Horizontal: {contentSizeFitter.horizontalFit}");
        }
        else
        {
            Debug.LogWarning("⚠️ No Content Size Fitter found - container may not resize properly");
        }
        
        // 检查容器尺寸
        RectTransform containerRect = ingredientsParent.GetComponent<RectTransform>();
        if (containerRect != null)
        {
            containerSize = containerRect.sizeDelta;
            Debug.Log($"📏 Container Size: {containerSize}");
        }
    }
    
    private void CheckIngredientUIIssues()
    {
        if (craftingUI == null || craftingUI.ingredientsParent == null) return;
        
        var ingredientUIs = craftingUI.ingredientsParent.GetComponentsInChildren<CraftingIngredientUI>();
        ingredientUICount = ingredientUIs.Length;
        
        Debug.Log($"📊 Found {ingredientUICount} IngredientUI objects");
        
        if (ingredientUICount == 0)
        {
            Debug.LogWarning("⚠️ No IngredientUI objects found. Try selecting a recipe first.");
            return;
        }
        
        // 检查每个IngredientUI的布局
        for (int i = 0; i < ingredientUIs.Length; i++)
        {
            var ingredientUI = ingredientUIs[i];
            CheckSingleIngredientUI(ingredientUI, i);
        }
        
        // 检查重叠
        CheckForOverlapping(ingredientUIs);
    }
    
    private void CheckSingleIngredientUI(CraftingIngredientUI ingredientUI, int index)
    {
        GameObject obj = ingredientUI.gameObject;
        RectTransform rect = obj.GetComponent<RectTransform>();
        
        Debug.Log($"🔍 IngredientUI {index}:");
        
        if (rect != null)
        {
            Debug.Log($"   - Size: {rect.sizeDelta}");
            Debug.Log($"   - Position: {rect.anchoredPosition}");
            
            // 检查Layout Element
            var layoutElement = obj.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                Debug.Log($"   - Layout Element: Preferred Size {layoutElement.preferredWidth}x{layoutElement.preferredHeight}");
            }
            else
            {
                Debug.LogWarning($"   ⚠️ No Layout Element found on IngredientUI {index}");
            }
        }
        
        // 检查子组件位置
        if (ingredientUI.itemIcon != null)
        {
            var iconRect = ingredientUI.itemIcon.GetComponent<RectTransform>();
            Debug.Log($"   - Icon Position: {iconRect.anchoredPosition}, Size: {iconRect.sizeDelta}");
        }
        
        if (ingredientUI.itemNameText != null)
        {
            var nameRect = ingredientUI.itemNameText.GetComponent<RectTransform>();
            Debug.Log($"   - Name Position: {nameRect.anchoredPosition}, Font Size: {ingredientUI.itemNameText.fontSize}");
        }
        
        if (ingredientUI.amountText != null)
        {
            var amountRect = ingredientUI.amountText.GetComponent<RectTransform>();
            Debug.Log($"   - Amount Position: {amountRect.anchoredPosition}, Font Size: {ingredientUI.amountText.fontSize}");
        }
    }
    
    private void CheckForOverlapping(CraftingIngredientUI[] ingredientUIs)
    {
        if (ingredientUIs.Length < 2) return;
        
        Debug.Log("🔍 Checking for overlapping...");
        
        for (int i = 0; i < ingredientUIs.Length - 1; i++)
        {
            for (int j = i + 1; j < ingredientUIs.Length; j++)
            {
                var rect1 = ingredientUIs[i].GetComponent<RectTransform>();
                var rect2 = ingredientUIs[j].GetComponent<RectTransform>();
                
                if (rect1 != null && rect2 != null)
                {
                    Vector2 pos1 = rect1.anchoredPosition;
                    Vector2 pos2 = rect2.anchoredPosition;
                    Vector2 size1 = rect1.sizeDelta;
                    Vector2 size2 = rect2.sizeDelta;
                    
                    // 简单的重叠检测
                    bool overlapping = Mathf.Abs(pos1.x - pos2.x) < (size1.x + size2.x) / 2 &&
                                     Mathf.Abs(pos1.y - pos2.y) < (size1.y + size2.y) / 2;
                    
                    if (overlapping)
                    {
                        Debug.LogWarning($"❌ IngredientUI {i} and {j} are overlapping!");
                        Debug.LogWarning($"   - UI {i}: Pos {pos1}, Size {size1}");
                        Debug.LogWarning($"   - UI {j}: Pos {pos2}, Size {size2}");
                    }
                }
            }
        }
    }
    
    private void ProvideRecommendations()
    {
        Debug.Log("💡 Recommendations:");
        
        if (!hasLayoutGroup)
        {
            Debug.Log("   - Add a Layout Group (Vertical/Horizontal) to prevent overlapping");
        }
        
        if (!hasContentSizeFitter)
        {
            Debug.Log("   - Add a Content Size Fitter for dynamic container sizing");
        }
        
        if (ingredientUICount > 1 && actualSpacing < 5f)
        {
            Debug.Log("   - Increase spacing between items to improve readability");
        }
        
        if (quickFix == null)
        {
            Debug.Log("   - Add IngredientUIQuickFix component for automatic fixes");
        }
        else
        {
            Debug.Log("   - Use IngredientUIQuickFix.FixIngredientUIIssues() to apply automatic fixes");
        }
    }
    
    [ContextMenu("Apply Quick Fix")]
    public void ApplyQuickFix()
    {
        if (quickFix == null)
        {
            Debug.LogError("IngredientUIQuickFix component not found!");
            return;
        }
        
        quickFix.FixIngredientUIIssues();
        
        // 重新运行诊断查看结果
        Invoke(nameof(RunDiagnostics), 0.5f);
    }
    
    private void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(Screen.width - 320, 10, 300, 200));
        GUILayout.Label("IngredientUI Tester", GUI.skin.box);
        
        GUILayout.Label($"Layout Group: {(hasLayoutGroup ? "✅" : "❌")}");
        GUILayout.Label($"Content Size Fitter: {(hasContentSizeFitter ? "✅" : "❌")}");
        GUILayout.Label($"IngredientUI Count: {ingredientUICount}");
        GUILayout.Label($"Container Size: {containerSize}");
        GUILayout.Label($"Spacing: {actualSpacing}");
        
        if (GUILayout.Button("Run Diagnostics"))
        {
            RunDiagnostics();
        }
        
        if (GUILayout.Button("Apply Quick Fix"))
        {
            ApplyQuickFix();
        }
        
        GUILayout.EndArea();
    }
}