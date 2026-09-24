using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class MainBase : MonoBehaviour, IDamageable
{
    [Header("基地属性")]
    [SerializeField] private float maxHealth = 1000f;
    [SerializeField] private float currentHealth;
    
    [Header("UI显示")]
    [SerializeField] private Slider healthBar;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI damageStatusText;
    
    [Header("视觉反馈")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Color[] damageColors = new Color[4] 
    {
        Color.white,    // 完好 (100%-75%)
        Color.yellow,   // 轻微损坏 (75%-50%)
        new Color(1f, 0.5f, 0f, 1f),   // 中度损坏 (50%-25%) - 橙色
        Color.red       // 严重损坏 (25%-0%)
    };
    
    [Header("玩家交互")]
    [SerializeField] private float healingRate = 10f; // 每秒恢复生命值
    [SerializeField] private float healingDuration = 10f; // 持续时间
    [SerializeField] private float interactionRange = 2f; // 交互范围
    [SerializeField] private KeyCode interactionKey = KeyCode.E; // 交互按键
    [SerializeField] private KeyCode repairKey = KeyCode.R; // 修复按键
    
    [Header("基地修复系统")]
    [SerializeField] private List<RepairItem> repairItems = new List<RepairItem>(); // 可用于修复的物品列表
    [SerializeField] private float maxRepairPercentage = 1f; // 最大修复百分比（1为100%）
    
    [Header("音效")]
    [SerializeField] private AudioClip damageSound;
    [SerializeField] private AudioClip healingSound;
    [SerializeField] private AudioClip destroyedSound;
    
    [Header("游戏管理")]
    [SerializeField] private GameObject gameOverUI;
    
    // 私有变量
    private AudioSource audioSource;
    private bool isDestroyed = false;
    private Transform player;
    private bool playerInRange = false;
    private Color originalColor;
    private InventoryManager playerInventory; // 玩家背包引用
    
    // 事件
    public System.Action<MainBase> OnDestroyed;
    public System.Action<MainBase, float> OnDamageTaken;
    public System.Action<MainBase, float, float> OnHealthChanged;
    public System.Action<MainBase> OnPlayerInteraction;
    
    // 属性
    public bool IsDestroyed => isDestroyed;
    public float HealthPercentage => currentHealth / maxHealth;
    public Vector3 Position => transform.position;
    
    private void Awake()
    {
        // 初始化血量
        currentHealth = maxHealth;
        
        // 获取组件
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }
    
    private void Start()
    {
        // 查找玩家
        FindPlayer();
        
        // 获取玩家背包组件
        if (player != null)
        {
            playerInventory = InventoryManager.Instance;
            if (playerInventory == null)
            {
                Debug.LogWarning("MainBase: InventoryManager instance not found");
            }
        }
        
        // 初始化UI
        UpdateUI();
        UpdateVisualDamage();
        
        // 触发初始血量变化事件
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
    }
    
    private void Update()
    {
        if (isDestroyed)
            return;
            
        // 检查玩家是否在交互范围内
        CheckPlayerInteraction();
    }
    
    #region IDamageable 实现
    
    public void TakeDamage(float damage)
    {
        if (isDestroyed || damage <= 0)
            return;
            
        // 减少血量
        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);
        
        // 播放受伤音效
        PlaySound(damageSound);
        
        // 更新UI和视觉效果
        UpdateUI();
        UpdateVisualDamage();
        
        // 触发事件
        OnDamageTaken?.Invoke(this, damage);
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        
        // 检查是否被摧毁
        if (currentHealth <= 0 && !isDestroyed)
        {
            DestroyBase();
        }
        
        VerboseLog.Log($"Main Base took {damage} damage, remaining health: {currentHealth}/{maxHealth}");
    }
    
    public float GetCurrentHealth()
    {
        return currentHealth;
    }
    
    public float GetMaxHealth()
    {
        return maxHealth;
    }
    
    public bool IsAlive()
    {
        return !isDestroyed && currentHealth > 0;
    }
    
    #endregion
    
    #region 玩家交互系统
    
    /// <summary>
    /// 查找玩家对象
    /// </summary>
    private void FindPlayer()
    {
        // 首先尝试通过标签查找
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            return;
        }
        
        // 如果没有找到，尝试查找PlayerController组件
        PlayerController playerController = FindObjectOfType<PlayerController>();
        if (playerController != null)
        {
            player = playerController.transform;
            return;
        }
        
        Debug.LogWarning("MainBase: 未找到玩家对象，请确保玩家对象有'Player'标签或PlayerController组件");
    }
    
    /// <summary>
    /// 检查玩家交互
    /// </summary>
    private void CheckPlayerInteraction()
    {
        if (player == null)
        {
            FindPlayer();
            return;
        }
        
        float distanceToPlayer = Vector2.Distance(transform.position, player.position);
        bool wasInRange = playerInRange;
        playerInRange = distanceToPlayer <= interactionRange;
        
        // 显示交互提示
        if (playerInRange && !wasInRange)
        {
            ShowInteractionPrompt(true);
        }
        else if (!playerInRange && wasInRange)
        {
            ShowInteractionPrompt(false);
        }
        
        // 检查交互输入
        if (playerInRange && Input.GetKeyDown(interactionKey))
        {
            StartHealing();
        }
        
        // 检查修复输入
        if (playerInRange && Input.GetKeyDown(repairKey))
        {
            TryRepairBase();
        }
    }
    
    /// <summary>
    /// 显示交互提示
    /// </summary>
    /// <param name="show">是否显示</param>
    private void ShowInteractionPrompt(bool show)
    {
        if (show)
        {
            string prompt = "Press E to restore health";
            if (CanRepairBase())
            {
                prompt += " | Press R to repair base";
            }
            VerboseLog.Log(prompt);
        }
        else
        {
            VerboseLog.Log("Left Main Base interaction range");
        }
    }
    
    /// <summary>
    /// 开始治疗玩家
    /// </summary>
    private void StartHealing()
    {
        if (player == null)
            return;
            
        // 获取玩家的生命恢复组件
        PlayerHealthRegeneration playerRegen = player.GetComponent<PlayerHealthRegeneration>();
        if (playerRegen == null)
        {
            // 如果玩家没有生命恢复组件，添加一个
            playerRegen = player.gameObject.AddComponent<PlayerHealthRegeneration>();
        }
        
        // 启动生命恢复
        playerRegen.StartRegeneration(healingRate, healingDuration);
        
        // 播放治疗音效
        PlaySound(healingSound);
        
        // 触发交互事件
        OnPlayerInteraction?.Invoke(this);
        
        VerboseLog.Log($"Player started health regeneration: {healingRate} HP per second for {healingDuration} seconds");
    }
    
    #endregion
    
    #region 基地修复系统
    
    /// <summary>
    /// 尝试修复基地
    /// </summary>
    private void TryRepairBase()
    {
        if (playerInventory == null)
        {
            VerboseLog.Log("Player inventory not found");
            return;
        }
        
        if (currentHealth >= maxHealth)
        {
            VerboseLog.Log("Base is already at full health");
            return;
        }
        
        // 查找可用的修复物品
        RepairItem availableItem = FindAvailableRepairItem();
        if (availableItem == null)
        {
            VerboseLog.Log("No repair items available in inventory");
            return;
        }
        
        // 消耗物品并修复基地
        if (playerInventory.RemoveItem(availableItem.item, availableItem.requiredAmount))
        {
            float repairAmount = availableItem.repairAmount;
            Heal(repairAmount);
            
            // 播放修复音效
            PlaySound(healingSound);
            
            VerboseLog.Log($"Base repaired for {repairAmount} HP using {availableItem.requiredAmount}x {availableItem.item.itemName}");
        }
        else
        {
            VerboseLog.Log($"Not enough {availableItem.item.itemName} to repair base");
        }
    }
    
    /// <summary>
    /// 查找可用的修复物品
    /// </summary>
    /// <returns>可用的修复物品，如果没有则返回null</returns>
    private RepairItem FindAvailableRepairItem()
    {
        if (playerInventory == null || repairItems.Count == 0)
            return null;
            
        foreach (RepairItem repairItem in repairItems)
        {
            if (repairItem.item != null && playerInventory.HasItem(repairItem.item, repairItem.requiredAmount))
            {
                return repairItem;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 检查是否可以修复基地
    /// </summary>
    /// <returns>是否可以修复</returns>
    private bool CanRepairBase()
    {
        return currentHealth < maxHealth && FindAvailableRepairItem() != null;
    }
    
    /// <summary>
    /// 添加修复物品配置
    /// </summary>
    /// <param name="item">物品</param>
    /// <param name="requiredAmount">需要数量</param>
    /// <param name="repairAmount">修复血量</param>
    public void AddRepairItem(Item item, int requiredAmount, float repairAmount)
    {
        RepairItem newRepairItem = new RepairItem
        {
            item = item,
            requiredAmount = requiredAmount,
            repairAmount = repairAmount
        };
        repairItems.Add(newRepairItem);
    }
    
    /// <summary>
    /// 获取修复物品列表信息
    /// </summary>
    /// <returns>修复物品信息</returns>
    public string GetRepairItemsInfo()
    {
        if (repairItems.Count == 0)
            return "No repair items configured";
            
        string info = "Available repair items:\n";
        foreach (RepairItem repairItem in repairItems)
        {
            if (repairItem.item != null)
            {
                bool hasEnough = playerInventory != null && playerInventory.HasItem(repairItem.item, repairItem.requiredAmount);
                string status = hasEnough ? "[Available]" : "[Not enough]";
                info += $"- {repairItem.requiredAmount}x {repairItem.item.itemName} → {repairItem.repairAmount} HP {status}\n";
            }
        }
        return info;
    }
    
    #endregion
    
    #region UI和视觉更新
    
    /// <summary>
    /// 更新UI显示
    /// </summary>
    private void UpdateUI()
    {
        // 更新血量条
        if (healthBar != null)
        {
            healthBar.value = HealthPercentage;
        }
        
        // 更新血量文本
        if (healthText != null)
        {
            healthText.text = $"{currentHealth:F0}/{maxHealth:F0}";
        }
        
        // 更新破损状态文本
        if (damageStatusText != null)
        {
            damageStatusText.text = GetDamageStatusText();
        }
    }
    
    /// <summary>
    /// 获取破损状态文本
    /// </summary>
    /// <returns>状态描述</returns>
    private string GetDamageStatusText()
    {
        float percentage = HealthPercentage;
        
        if (percentage > 0.75f)
            return "Base Status: Intact";
        else if (percentage > 0.5f)
            return "Base Status: Lightly Damaged";
        else if (percentage > 0.25f)
            return "Base Status: Moderately Damaged";
        else if (percentage > 0f)
            return "Base Status: Heavily Damaged";
        else
            return "Base Status: Destroyed";
    }
    
    /// <summary>
    /// 更新视觉损坏效果
    /// </summary>
    private void UpdateVisualDamage()
    {
        if (spriteRenderer == null || damageColors.Length == 0)
            return;
            
        float percentage = HealthPercentage;
        Color targetColor;
        
        if (percentage > 0.75f)
            targetColor = damageColors[0]; // 完好
        else if (percentage > 0.5f)
            targetColor = damageColors[1]; // 轻微损坏
        else if (percentage > 0.25f)
            targetColor = damageColors[2]; // 中度损坏
        else
            targetColor = damageColors[3]; // 严重损坏
            
        spriteRenderer.color = targetColor;
    }
    
    #endregion
    
    #region 基地摧毁和游戏结束
    
    /// <summary>
    /// 摧毁基地
    /// </summary>
    private void DestroyBase()
    {
        if (isDestroyed)
            return;
            
        isDestroyed = true;
        
        // 播放摧毁音效
        PlaySound(destroyedSound);
        
        // 更新UI
        UpdateUI();
        UpdateVisualDamage();
        
        // 触发摧毁事件
        OnDestroyed?.Invoke(this);
        
        // 游戏结束
        GameOver();
        
        VerboseLog.Log("Main Base has been destroyed! Game Over!");
    }
    
    /// <summary>
    /// 游戏结束
    /// </summary>
    private void GameOver()
    {
        // 显示游戏结束UI
        if (gameOverUI != null)
        {
            gameOverUI.SetActive(true);
        }
        
        // 暂停游戏
        Time.timeScale = 0f;
        
        // 可以在这里添加更多游戏结束逻辑
        VerboseLog.Log("Game Over!");
    }
    
    #endregion
    
    #region 工具方法
    
    /// <summary>
    /// 播放音效
    /// </summary>
    /// <param name="clip">音频剪辑</param>
    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }
    
    /// <summary>
    /// 治疗基地
    /// </summary>
    /// <param name="healAmount">治疗量</param>
    public void Heal(float healAmount)
    {
        if (isDestroyed || healAmount <= 0)
            return;
            
        currentHealth += healAmount;
        currentHealth = Mathf.Min(maxHealth, currentHealth);
        
        UpdateUI();
        UpdateVisualDamage();
        
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        
        VerboseLog.Log($"Main Base healed {healAmount} HP, current health: {currentHealth}/{maxHealth}");
    }
    
    /// <summary>
    /// 设置最大血量
    /// </summary>
    /// <param name="newMaxHealth">新的最大血量</param>
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = newMaxHealth;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        UpdateUI();
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
    }
    
    #endregion
    
    private void OnDrawGizmosSelected()
    {
        // 绘制交互范围
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
        
        // 绘制血量条
        if (maxHealth > 0)
        {
            Vector3 healthBarPos = transform.position + Vector3.up * 2f;
            float healthPercentage = currentHealth / maxHealth;
            
            // 背景
            Gizmos.color = Color.red;
            Gizmos.DrawLine(healthBarPos - Vector3.right * 1f, healthBarPos + Vector3.right * 1f);
            
            // 当前血量
            Gizmos.color = Color.green;
            Vector3 healthEnd = healthBarPos - Vector3.right * 1f + Vector3.right * (2f * healthPercentage);
            Gizmos.DrawLine(healthBarPos - Vector3.right * 1f, healthEnd);
        }
    }
}

/// <summary>
/// 修复物品配置
/// </summary>
[System.Serializable]
public class RepairItem
{
    [Header("修复物品配置")]
    public Item item; // 需要的物品
    public int requiredAmount = 1; // 需要的数量
    public float repairAmount = 100f; // 修复的血量
    
    [Header("描述")]
    [TextArea(2, 4)]
    public string description = "Repair item description"; // 物品描述
}