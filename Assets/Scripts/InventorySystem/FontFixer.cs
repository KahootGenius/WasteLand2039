using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Font Fixer - Resolves Unicode character display issues
/// Automatically assigns fallback fonts for TextMeshPro components
/// </summary>
public class FontFixer : MonoBehaviour
{
    [Header("Font Settings")]
    [SerializeField] private TMP_FontAsset fallbackFont;
    [SerializeField] private bool autoFixOnStart = true;
    [SerializeField] private bool debugMode = true;
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            FixAllTextMeshProFonts();
        }
    }
    
    /// <summary>
    /// Fix all TextMeshPro components in the scene
    /// </summary>
    [ContextMenu("Fix All TextMeshPro Fonts")]
    public void FixAllTextMeshProFonts()
    {
        // Find all TextMeshProUGUI components
        TextMeshProUGUI[] allTextComponents = FindObjectsOfType<TextMeshProUGUI>(true);
        
        int fixedCount = 0;
        
        foreach (var textComponent in allTextComponents)
        {
            if (FixTextMeshProFont(textComponent))
            {
                fixedCount++;
            }
        }
        
        if (debugMode)
        {
            Debug.Log($"FontFixer: Fixed {fixedCount} TextMeshPro components out of {allTextComponents.Length} total.");
        }
    }
    
    /// <summary>
    /// Fix a specific TextMeshPro component
    /// </summary>
    /// <param name="textComponent">The TextMeshPro component to fix</param>
    /// <returns>True if the component was modified</returns>
    public bool FixTextMeshProFont(TextMeshProUGUI textComponent)
    {
        if (textComponent == null) return false;
        
        // Get the default TextMeshPro font if no fallback is specified
        if (fallbackFont == null)
        {
            fallbackFont = Resources.GetBuiltinResource<TMP_FontAsset>("LiberationSans SDF");
        }
        
        // Check if the component needs fixing
        bool needsFix = false;
        
        // Check if using LiberationSans and has non-ASCII characters
        if (textComponent.font != null && 
            textComponent.font.name.Contains("LiberationSans") && 
            ContainsNonASCIICharacters(textComponent.text))
        {
            needsFix = true;
        }
        
        if (needsFix)
        {
            // Try to find a better font asset
            TMP_FontAsset betterFont = FindBetterFont();
            
            if (betterFont != null)
            {
                textComponent.font = betterFont;
                
                if (debugMode)
                {
                    Debug.Log($"FontFixer: Updated font for '{textComponent.gameObject.name}' from '{textComponent.font.name}' to '{betterFont.name}'");
                }
                
                return true;
            }
            else
            {
                // If no better font found, clear problematic characters
                string cleanText = CleanNonASCIICharacters(textComponent.text);
                if (cleanText != textComponent.text)
                {
                    textComponent.text = cleanText;
                    
                    if (debugMode)
                    {
                        Debug.Log($"FontFixer: Cleaned non-ASCII characters from '{textComponent.gameObject.name}'");
                    }
                    
                    return true;
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if text contains non-ASCII characters
    /// </summary>
    private bool ContainsNonASCIICharacters(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        
        foreach (char c in text)
        {
            if (c > 127) // Non-ASCII character
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Clean non-ASCII characters from text
    /// </summary>
    private string CleanNonASCIICharacters(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        // Replace common Chinese words with English equivalents
        text = text.Replace("级", "Lv");
        text = text.Replace("类型", "Type");
        text = text.Replace("描述", "Description");
        text = text.Replace("合成", "Craft");
        text = text.Replace("配方", "Recipe");
        text = text.Replace("材料", "Materials");
        text = text.Replace("物品", "Item");
        text = text.Replace("背包", "Inventory");
        text = text.Replace("请选择配方", "Please Select A Recipe");
        text = text.Replace("合成台", "Crafting Station");
        
        // Remove any remaining non-ASCII characters
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        
        foreach (char c in text)
        {
            if (c <= 127) // ASCII character
            {
                sb.Append(c);
            }
            // Skip non-ASCII characters that weren't replaced above
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Try to find a better font that supports more characters
    /// </summary>
    private TMP_FontAsset FindBetterFont()
    {
        // Try to find Arial or other system fonts
        TMP_FontAsset[] allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        
        foreach (var font in allFonts)
        {
            if (font.name.Contains("Arial") || 
                font.name.Contains("NotoSans") || 
                font.name.Contains("SourceHan"))
            {
                return font;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Manual fix button for inspector
    /// </summary>
    [ContextMenu("Fix Current GameObject")]
    public void FixCurrentGameObject()
    {
        TextMeshProUGUI textComponent = GetComponent<TextMeshProUGUI>();
        if (textComponent != null)
        {
            FixTextMeshProFont(textComponent);
        }
        else
        {
            Debug.LogWarning("FontFixer: No TextMeshProUGUI component found on this GameObject.");
        }
    }
}