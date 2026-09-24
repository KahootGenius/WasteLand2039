using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI Overlap Fixer - Fixes UI overlapping issues by setting proper Canvas sort orders
/// </summary>
public class UIOverlapFixer : MonoBehaviour
{
    [Header("Canvas Sort Order Settings")]
    [Tooltip("Sort order for inventory UI (should be lower than crafting UI)")]
    public int inventoryCanvasSortOrder = 5;
    
    [Tooltip("Sort order for crafting UI (should be higher than inventory UI)")]
    public int craftingCanvasSortOrder = 10;
    
    [Tooltip("Sort order for tooltip UI (should be highest)")]
    public int tooltipCanvasSortOrder = 15;
    
    [Header("Auto Fix Settings")]
    [Tooltip("Automatically fix UI overlapping on start")]
    public bool autoFixOnStart = true;
    
    private void Start()
    {
        if (autoFixOnStart)
        {
            FixUIOverlapping();
        }
    }
    
    [ContextMenu("Fix UI Overlapping")]
    public void FixUIOverlapping()
    {
        Debug.Log("=== Starting UI Overlap Fix ===");
        
        // Find all Canvas components in the scene
        Canvas[] allCanvases = FindObjectsOfType<Canvas>();
        
        foreach (Canvas canvas in allCanvases)
        {
            FixCanvasSettings(canvas);
        }
        
        Debug.Log("✅ UI Overlap Fix completed");
    }
    
    private void FixCanvasSettings(Canvas canvas)
    {
        if (canvas == null) return;
        
        // Ensure proper render mode
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Debug.Log($"Fixed Canvas '{canvas.name}' render mode to ScreenSpaceOverlay");
        }
        
        // Set sort order based on canvas name/purpose
        string canvasName = canvas.name.ToLower();
        
        if (canvasName.Contains("inventory") || canvasName.Contains("bag"))
        {
            canvas.sortingOrder = inventoryCanvasSortOrder;
            Debug.Log($"Set '{canvas.name}' sort order to {inventoryCanvasSortOrder} (Inventory UI)");
        }
        else if (canvasName.Contains("crafting") || canvasName.Contains("craft"))
        {
            canvas.sortingOrder = craftingCanvasSortOrder;
            Debug.Log($"Set '{canvas.name}' sort order to {craftingCanvasSortOrder} (Crafting UI)");
        }
        else if (canvasName.Contains("tooltip") || canvasName.Contains("tip"))
        {
            canvas.sortingOrder = tooltipCanvasSortOrder;
            Debug.Log($"Set '{canvas.name}' sort order to {tooltipCanvasSortOrder} (Tooltip UI)");
        }
        else
        {
            // Default UI gets a middle sort order
            if (canvas.sortingOrder == 0)
            {
                canvas.sortingOrder = 1;
                Debug.Log($"Set '{canvas.name}' sort order to 1 (Default UI)");
            }
        }
        
        // Ensure GraphicRaycaster exists
        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            Debug.Log($"Added GraphicRaycaster to '{canvas.name}'");
        }
        
        // Check for CanvasScaler
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Debug.Log($"Added CanvasScaler to '{canvas.name}'");
        }
    }
    
    [ContextMenu("List All Canvases")]
    public void ListAllCanvases()
    {
        Debug.Log("=== Canvas List ===");
        Canvas[] allCanvases = FindObjectsOfType<Canvas>();
        
        foreach (Canvas canvas in allCanvases)
        {
            Debug.Log($"Canvas: '{canvas.name}' - Sort Order: {canvas.sortingOrder} - Render Mode: {canvas.renderMode}");
        }
        
        Debug.Log($"Total Canvases found: {allCanvases.Length}");
    }
    
    private void OnGUI()
    {
        if (Application.isPlaying)
        {
            GUILayout.BeginArea(new Rect(10, 200, 300, 150));
            GUILayout.Label("UI Overlap Fixer", GUI.skin.box);
            GUILayout.Label("F9: Fix UI Overlapping");
            GUILayout.Label("F10: List All Canvases");
            
            if (GUILayout.Button("Fix UI Overlapping"))
            {
                FixUIOverlapping();
            }
            
            if (GUILayout.Button("List All Canvases"))
            {
                ListAllCanvases();
            }
            
            GUILayout.EndArea();
        }
    }
    
    private void Update()
    {
        // Hotkeys for debugging
        if (Input.GetKeyDown(KeyCode.F9))
        {
            FixUIOverlapping();
        }
        
        if (Input.GetKeyDown(KeyCode.F10))
        {
            ListAllCanvases();
        }
    }
}