using UnityEngine;
using UnityEngine.UI;

public class SimpleEnemyHealthBar : MonoBehaviour
{
    [Header("血条设置")]
    [SerializeField] private float barWidth = 1f;
    [SerializeField] private float barHeight = 0.15f;
    [SerializeField] private Vector3 offset = new Vector3(0, 1.2f, 0);
    
    [Header("颜色设置")]
    [SerializeField] private Color fullHealthColor = Color.green;
    [SerializeField] private Color halfHealthColor = Color.yellow;
    [SerializeField] private Color lowHealthColor = Color.red;
    [SerializeField] private Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
    
    [Header("显示设置")]
    [SerializeField] private float showDistance = 8f;
    [SerializeField] private bool alwaysShow = false;
    
    // UI组件
    private Canvas canvas;
    private Image backgroundImage;
    private Image fillImage;
    
    // 引用
    private Enemy targetEnemy;
    private Camera playerCamera;
    private Transform playerTransform;
    
    private void Start()
    {
        // 获取敌人组件
        targetEnemy = GetComponent<Enemy>();
        if (targetEnemy == null)
        {
            Debug.LogError("SimpleEnemyHealthBar: 未找到Enemy组件！");
            Destroy(this);
            return;
        }
        
        // 获取摄像机和玩家
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindObjectOfType<Camera>();
        }
        
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
        
        // 创建血条UI
        CreateHealthBarUI();
        
        // 订阅事件
        targetEnemy.OnHealthChanged += OnHealthChanged;
        targetEnemy.OnDeath += OnEnemyDeath;
        
        // 初始更新
        UpdateHealthBar();
        
        VerboseLog.Log($"SimpleEnemyHealthBar: 已为 {gameObject.name} 创建血条，初始血量: {targetEnemy.GetCurrentHealth()}/{targetEnemy.GetMaxHealth()}");
    }
    
    private void CreateHealthBarUI()
    {
        // 创建共享的白色纹理
        Texture2D whiteTexture = new Texture2D(1, 1);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();
        Sprite whiteSprite = Sprite.Create(whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        
        // 创建Canvas
        GameObject canvasObj = new GameObject("HealthBarCanvas");
        canvasObj.transform.SetParent(transform);
        canvasObj.transform.localPosition = offset;
        
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = playerCamera;
        canvas.sortingOrder = 10;
        
        // 设置Canvas大小
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(barWidth, barHeight);
        
        // 创建背景
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvasObj.transform, false);
        
        backgroundImage = bgObj.AddComponent<Image>();
        backgroundImage.sprite = whiteSprite;
        backgroundImage.color = backgroundColor;
        
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        // 创建血条填充
        GameObject fillObj = new GameObject("HealthFill");
        fillObj.transform.SetParent(canvasObj.transform, false);
        
        fillImage = fillObj.AddComponent<Image>();
        fillImage.sprite = whiteSprite;
        fillImage.color = fullHealthColor;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(1, 1); // 小边距
        fillRect.offsetMax = new Vector2(-1, -1);
    }
    
    private void Update()
    {
        if (targetEnemy == null || targetEnemy.IsDead || canvas == null)
            return;
        
        // 让血条始终面向摄像机
        if (playerCamera != null)
        {
            canvas.transform.LookAt(playerCamera.transform);
            canvas.transform.Rotate(0, 180, 0);
        }
        
        // 根据距离控制显示
        if (!alwaysShow && playerTransform != null)
        {
            float distance = Vector3.Distance(transform.position, playerTransform.position);
            bool shouldShow = distance <= showDistance;
            
            if (canvas.gameObject.activeSelf != shouldShow)
            {
                canvas.gameObject.SetActive(shouldShow);
            }
        }
    }
    
    private void OnHealthChanged(Enemy enemy, float currentHealth, float maxHealth)
    {
        UpdateHealthBar();
    }
    
    private void OnEnemyDeath(Enemy enemy)
    {
        if (canvas != null)
        {
            canvas.gameObject.SetActive(false);
        }
    }
    
    private void UpdateHealthBar()
    {
        if (fillImage == null || targetEnemy == null)
        {
            Debug.LogWarning("SimpleEnemyHealthBar: fillImage或targetEnemy为null");
            return;
        }
        
        float currentHealth = targetEnemy.GetCurrentHealth();
        float maxHealth = targetEnemy.GetMaxHealth();
        
        // 更新填充量
        float healthPercentage = maxHealth > 0 ? currentHealth / maxHealth : 0;
        fillImage.fillAmount = healthPercentage;
        
        // 更新颜色
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
        
        fillImage.color = targetColor;
        
        // 调试信息
        VerboseLog.Log($"SimpleEnemyHealthBar: 血量 {currentHealth}/{maxHealth} ({healthPercentage:P0}), 颜色: {targetColor}, fillAmount: {fillImage.fillAmount}");
    }
    
    private void OnDestroy()
    {
        // 取消事件订阅
        if (targetEnemy != null)
        {
            targetEnemy.OnHealthChanged -= OnHealthChanged;
            targetEnemy.OnDeath -= OnEnemyDeath;
        }
    }
    
    // 公共方法用于运行时调整
    public void SetShowDistance(float distance)
    {
        showDistance = distance;
    }
    
    public void SetAlwaysShow(bool show)
    {
        alwaysShow = show;
        if (canvas != null)
        {
            canvas.gameObject.SetActive(show);
        }
    }
    
    public void SetOffset(Vector3 newOffset)
    {
        offset = newOffset;
        if (canvas != null)
        {
            canvas.transform.localPosition = offset;
        }
    }
    
    public void SetBarSize(float width, float height)
    {
        barWidth = width;
        barHeight = height;
        
        if (canvas != null)
        {
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(barWidth, barHeight);
        }
    }
}