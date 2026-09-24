using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [Header("血条设置")]
    [SerializeField] private Canvas healthBarCanvas;
    [SerializeField] private Image healthBarFill;
    [SerializeField] private Image healthBarBackground;
    [SerializeField] private Text healthText; // 可选的血量文本
    
    [Header("显示设置")]
    [SerializeField] private float showDistance = 10f; // 显示血条的距离
    [SerializeField] private bool alwaysShow = false; // 是否始终显示
    [SerializeField] private bool showHealthText = true; // 是否显示血量文本
    [SerializeField] private Vector3 offset = new Vector3(0, 1.5f, 0); // 血条相对敌人的偏移
    
    [Header("颜色设置")]
    [SerializeField] private Color fullHealthColor = Color.green;
    [SerializeField] private Color halfHealthColor = Color.yellow;
    [SerializeField] private Color lowHealthColor = Color.red;
    [SerializeField] private Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
    
    // 私有变量
    private Enemy targetEnemy;
    private Camera playerCamera;
    private RectTransform canvasRectTransform;
    private float maxHealth;
    private float currentHealth;
    
    // 共享的白色Sprite，用于所有Image组件
    private static Sprite whiteSprite;
    
    private void Awake()
    {
        // 获取主摄像机
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindObjectOfType<Camera>();
        }
        
        // 获取目标敌人
        targetEnemy = GetComponentInParent<Enemy>();
        if (targetEnemy == null)
        {
            Debug.LogError("EnemyHealthBar: 未找到Enemy组件！");
            return;
        }
        
        // 初始化Canvas
        SetupCanvas();
        
        // 订阅敌人血量变化事件
        targetEnemy.OnHealthChanged += OnHealthChanged;
        targetEnemy.OnDeath += OnEnemyDeath;
        
        // 初始化血量
        maxHealth = targetEnemy.GetMaxHealth();
        currentHealth = targetEnemy.GetCurrentHealth();
        
        UpdateHealthBar();
    }
    
    /// <summary>
    /// 创建一个1x1的白色Sprite，用于Image组件显示颜色
    /// </summary>
    private static Sprite CreateWhiteSprite()
    {
        if (whiteSprite == null)
        {
            // 创建1x1的白色纹理
            Texture2D whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
            
            // 创建Sprite
            whiteSprite = Sprite.Create(whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            whiteSprite.name = "WhiteSprite";
        }
        return whiteSprite;
    }
    
    private void SetupCanvas()
    {
        if (healthBarCanvas == null)
        {
            // 创建Canvas
            GameObject canvasObj = new GameObject("HealthBarCanvas");
            canvasObj.transform.SetParent(transform);
            canvasObj.transform.localPosition = offset;
            
            healthBarCanvas = canvasObj.AddComponent<Canvas>();
            healthBarCanvas.renderMode = RenderMode.WorldSpace;
            healthBarCanvas.worldCamera = playerCamera;
            healthBarCanvas.sortingOrder = 10;
            
            canvasRectTransform = healthBarCanvas.GetComponent<RectTransform>();
            canvasRectTransform.sizeDelta = new Vector2(2f, 0.3f);
            
            // 创建背景
            CreateBackground();
            
            // 创建血条填充
            CreateHealthFill();
            
            // 创建血量文本（如果需要）
            if (showHealthText)
            {
                CreateHealthText();
            }
        }
    }
    
    private void CreateBackground()
    {
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(healthBarCanvas.transform, false);
        
        healthBarBackground = bgObj.AddComponent<Image>();
        healthBarBackground.sprite = CreateWhiteSprite(); // 添加白色Sprite
        healthBarBackground.color = backgroundColor;
        
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
    }
    
    private void CreateHealthFill()
    {
        GameObject fillObj = new GameObject("HealthFill");
        fillObj.transform.SetParent(healthBarCanvas.transform, false);
        
        healthBarFill = fillObj.AddComponent<Image>();
        healthBarFill.sprite = CreateWhiteSprite(); // 添加白色Sprite
        healthBarFill.color = fullHealthColor;
        healthBarFill.type = Image.Type.Filled;
        healthBarFill.fillMethod = Image.FillMethod.Horizontal;
        
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2, 2); // 小边距
        fillRect.offsetMax = new Vector2(-2, -2);
    }
    
    private void CreateHealthText()
    {
        GameObject textObj = new GameObject("HealthText");
        textObj.transform.SetParent(healthBarCanvas.transform, false);
        
        healthText = textObj.AddComponent<Text>();
        healthText.text = $"{currentHealth:F0}/{maxHealth:F0}";
        healthText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        healthText.fontSize = 12;
        healthText.color = Color.white;
        healthText.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
    
    private void Update()
    {
        if (targetEnemy == null || targetEnemy.IsDead)
            return;
            
        // 更新Canvas朝向（始终面向摄像机）
        if (playerCamera != null && healthBarCanvas != null)
        {
            healthBarCanvas.transform.LookAt(playerCamera.transform);
            healthBarCanvas.transform.Rotate(0, 180, 0); // 翻转以正确显示
        }
        
        // 根据距离控制显示
        if (!alwaysShow)
        {
            float distance = Vector3.Distance(transform.position, playerCamera.transform.position);
            bool shouldShow = distance <= showDistance;
            
            if (healthBarCanvas.gameObject.activeSelf != shouldShow)
            {
                healthBarCanvas.gameObject.SetActive(shouldShow);
            }
        }
    }
    
    private void OnHealthChanged(Enemy enemy, float newHealth, float maxHp)
    {
        if (enemy != targetEnemy)
            return;
            
        currentHealth = newHealth;
        maxHealth = maxHp;
        UpdateHealthBar();
    }
    
    private void OnEnemyDeath(Enemy enemy)
    {
        if (enemy != targetEnemy)
            return;
            
        // 敌人死亡后隐藏血条或延迟销毁
        if (healthBarCanvas != null)
        {
            healthBarCanvas.gameObject.SetActive(false);
        }
    }
    
    private void UpdateHealthBar()
    {
        if (healthBarFill == null)
            return;
            
        // 更新血条填充
        float healthPercentage = maxHealth > 0 ? currentHealth / maxHealth : 0;
        healthBarFill.fillAmount = healthPercentage;
        
        // 更新血条颜色
        Color targetColor;
        if (healthPercentage > 0.6f)
        {
            targetColor = fullHealthColor;
        }
        else if (healthPercentage > 0.3f)
        {
            targetColor = halfHealthColor;
        }
        else
        {
            targetColor = lowHealthColor;
        }
        
        healthBarFill.color = targetColor;
        
        // 更新血量文本
        if (healthText != null && showHealthText)
        {
            healthText.text = $"{currentHealth:F0}/{maxHealth:F0}";
        }
    }
    
    private void OnDestroy()
    {
        // 取消订阅事件
        if (targetEnemy != null)
        {
            targetEnemy.OnHealthChanged -= OnHealthChanged;
            targetEnemy.OnDeath -= OnEnemyDeath;
        }
    }
    
    // 公共方法
    public void SetShowDistance(float distance)
    {
        showDistance = distance;
    }
    
    public void SetAlwaysShow(bool show)
    {
        alwaysShow = show;
        if (healthBarCanvas != null)
        {
            healthBarCanvas.gameObject.SetActive(show || Vector3.Distance(transform.position, playerCamera.transform.position) <= showDistance);
        }
    }
    
    public void SetOffset(Vector3 newOffset)
    {
        offset = newOffset;
        if (healthBarCanvas != null)
        {
            healthBarCanvas.transform.localPosition = offset;
        }
    }
}