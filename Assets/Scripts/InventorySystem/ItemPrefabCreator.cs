using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;

/// <summary>
/// 物品预制体创建器
/// 用于快速创建ItemPickup预制体
/// </summary>
public class ItemPrefabCreator : MonoBehaviour
{
    [Header("Prefab Creation Settings")]
    [SerializeField] private string prefabSavePath = "Assets/Prefabs/";
    [SerializeField] private string prefabName = "ItemWorldPrefab";
    
    [Header("Default Settings")]
    [SerializeField] private float defaultPickupRange = 2f;
    [SerializeField] private KeyCode defaultPickupKey = KeyCode.E;
    [SerializeField] private bool enableBobbing = true;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float bobHeight = 0.5f;
    
    [Header("Visual Settings")]
    [SerializeField] private Sprite defaultItemSprite;
    [SerializeField] private Color defaultSpriteColor = Color.white;
    [SerializeField] private Vector3 spriteScale = Vector3.one;
    
    /// <summary>
    /// 创建基础的ItemPickup预制体
    /// </summary>
    [ContextMenu("Create Item Pickup Prefab")]
    public void CreateItemPickupPrefab()
    {
        // 创建根GameObject
        GameObject itemPickupObj = new GameObject("ItemPickup");
        
        // 添加必要的组件
        SetupComponents(itemPickupObj);
        
        // 设置默认值
        ConfigureItemPickup(itemPickupObj);
        
        // 创建UI提示
        CreatePickupPrompt(itemPickupObj);
        
        // 保存为预制体
        SaveAsPrefab(itemPickupObj);
        
        Debug.Log($"ItemPickup prefab created successfully at {prefabSavePath}{prefabName}.prefab");
    }
    
    /// <summary>
    /// 设置必要的组件
    /// </summary>
    private void SetupComponents(GameObject obj)
    {
        // 添加SpriteRenderer
        SpriteRenderer spriteRenderer = obj.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = defaultItemSprite;
        spriteRenderer.color = defaultSpriteColor;
        obj.transform.localScale = spriteScale;
        
        // 添加Collider2D用于检测
        CircleCollider2D collider = obj.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.5f;
        
        // 添加ItemPickup脚本
        ItemPickup itemPickup = obj.AddComponent<ItemPickup>();
        
        // 添加Rigidbody2D（可选，用于物理效果）
        Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f; // 不受重力影响
        rb.freezeRotation = true; // 防止旋转
    }
    
    /// <summary>
    /// 配置ItemPickup组件
    /// </summary>
    private void ConfigureItemPickup(GameObject obj)
    {
        ItemPickup itemPickup = obj.GetComponent<ItemPickup>();
        if (itemPickup != null)
        {
            itemPickup.pickupRange = defaultPickupRange;
            itemPickup.pickupKey = defaultPickupKey;
            itemPickup.bobUpAndDown = enableBobbing;
            itemPickup.bobSpeed = bobSpeed;
            itemPickup.bobHeight = bobHeight;
            itemPickup.playerLayer = LayerMask.GetMask("Default"); // 默认图层
        }
    }
    
    /// <summary>
    /// 创建拾取提示UI
    /// </summary>
    private void CreatePickupPrompt(GameObject parent)
    {
        // 创建Canvas
        GameObject canvasObj = new GameObject("PickupPromptCanvas");
        canvasObj.transform.SetParent(parent.transform);
        canvasObj.transform.localPosition = Vector3.zero;
        
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 10;
        
        // 设置Canvas大小
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3f, 1f);
        canvasRect.localScale = Vector3.one * 0.01f; // 缩小以适应世界空间
        canvasRect.anchoredPosition = new Vector2(0, 1f); // 在物品上方
        
        // 添加CanvasScaler
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        
        // 创建背景面板
        GameObject panelObj = new GameObject("PromptPanel");
        panelObj.transform.SetParent(canvasObj.transform);
        
        UnityEngine.UI.Image panelImage = panelObj.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0, 0, 0, 0.7f); // 半透明黑色背景
        
        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panelRect.anchoredPosition = Vector2.zero;
        
        // 创建文本
        GameObject textObj = new GameObject("PromptText");
        textObj.transform.SetParent(panelObj.transform);
        
        TMPro.TextMeshProUGUI text = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        text.text = "Press E to pickup";
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.fontStyle = TMPro.FontStyles.Bold;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;
        
        // 设置ItemPickup的UI引用
        ItemPickup itemPickup = parent.GetComponent<ItemPickup>();
        if (itemPickup != null)
        {
            itemPickup.pickupPrompt = panelObj;
            itemPickup.promptText = text;
        }
        
        // 默认隐藏提示
        panelObj.SetActive(false);
    }
    
    /// <summary>
    /// 保存为预制体
    /// </summary>
    private void SaveAsPrefab(GameObject obj)
    {
#if UNITY_EDITOR
        // 确保目录存在
        if (!Directory.Exists(prefabSavePath))
        {
            Directory.CreateDirectory(prefabSavePath);
        }
        
        string fullPath = prefabSavePath + prefabName + ".prefab";
        
        // 如果预制体已存在，删除旧的
        if (File.Exists(fullPath))
        {
            AssetDatabase.DeleteAsset(fullPath);
        }
        
        // 创建新预制体
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(obj, fullPath);
        
        // 刷新资源数据库
        AssetDatabase.Refresh();
        
        // 选中新创建的预制体
        Selection.activeObject = prefab;
        
        // 删除场景中的临时对象
        DestroyImmediate(obj);
#else
        Debug.LogWarning("Prefab creation is only available in the Unity Editor.");
        DestroyImmediate(obj);
#endif
    }
    
    /// <summary>
    /// 创建简单的物品预制体（无UI）
    /// </summary>
    [ContextMenu("Create Simple Item Prefab")]
    public void CreateSimpleItemPrefab()
    {
        GameObject itemObj = new GameObject("SimpleItemPickup");
        
        // 只添加基础组件
        SpriteRenderer spriteRenderer = itemObj.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = defaultItemSprite;
        spriteRenderer.color = defaultSpriteColor;
        itemObj.transform.localScale = spriteScale;
        
        CircleCollider2D collider = itemObj.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.5f;
        
        ItemPickup itemPickup = itemObj.AddComponent<ItemPickup>();
        ConfigureItemPickup(itemObj);
        
        // 保存为预制体
        prefabName = "SimpleItemPickup";
        SaveAsPrefab(itemObj);
        
        Debug.Log($"Simple ItemPickup prefab created successfully at {prefabSavePath}{prefabName}.prefab");
    }
    
    /// <summary>
    /// 在场景中创建测试物品
    /// </summary>
    [ContextMenu("Create Test Item In Scene")]
    public void CreateTestItemInScene()
    {
        GameObject testItem = new GameObject("TestItem");
        testItem.transform.position = transform.position + Vector3.right * 2f;
        
        SetupComponents(testItem);
        ConfigureItemPickup(testItem);
        CreatePickupPrompt(testItem);
        
        // 设置一个测试物品
        ItemPickup itemPickup = testItem.GetComponent<ItemPickup>();
        if (itemPickup != null)
        {
            // 这里可以设置一个默认的测试物品
            // itemPickup.SetItem(testItemScriptableObject, 1);
        }
        
        Debug.Log("Test item created in scene at position: " + testItem.transform.position);
    }
    
    private void OnDrawGizmosSelected()
    {
        // 绘制预制体创建位置
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, Vector3.one);
        Gizmos.DrawIcon(transform.position, "sv_icon_dot11_pix16_gizmo", true);
        
        // 绘制测试物品位置
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position + Vector3.right * 2f, Vector3.one * 0.5f);
    }
}