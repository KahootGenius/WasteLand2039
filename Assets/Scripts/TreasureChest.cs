using UnityEngine;
using System.Collections.Generic;

public class TreasureChest : MonoBehaviour
{
    [Header("交互设置")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private float interactionRange = 2f;
    [SerializeField] private LayerMask playerLayer = -1;
    
    [Header("宝箱状态")]
    [SerializeField] private bool isOpened = false;
    [SerializeField] private bool canReopenChest = false;
    
    [Header("随机物品配置")]
    [SerializeField] private List<ChestItem> possibleItems = new List<ChestItem>();
    [SerializeField] private int minItemCount = 1;
    [SerializeField] private int maxItemCount = 3;
    
    [Header("视觉反馈")]
    [SerializeField] private SpriteRenderer chestSpriteRenderer;
    [SerializeField] private Sprite closedChestSprite;
    [SerializeField] private Sprite openedChestSprite;
    [SerializeField] private GameObject closedChestVisual; // 保留兼容性
    [SerializeField] private GameObject openedChestVisual; // 保留兼容性
    [SerializeField] private ParticleSystem openEffect;
    [SerializeField] private AudioClip openSound;
    
    [Header("UI提示")]
    [SerializeField] private GameObject interactionPrompt;
    [SerializeField] private string promptText = "按 E 键开启宝箱";
    
    // 私有变量
    private bool playerInRange = false;
    private Transform playerTransform;
    private InventoryManager inventoryManager;
    private AudioSource audioSource;
    
    [System.Serializable]
    public class ChestItem
    {
        public Item item;
        [Range(1, 100)]
        public int dropChance = 50; // 掉落概率 (%)
        [Range(1, 10)]
        public int minAmount = 1;
        [Range(1, 10)]
        public int maxAmount = 1;
    }
    
    private void Awake()
    {
        // 获取音频组件
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // 自动获取SpriteRenderer组件（如果没有手动指定）
        if (chestSpriteRenderer == null)
        {
            chestSpriteRenderer = GetComponent<SpriteRenderer>();
        }
    }
    
    private void Start()
    {
        // 获取背包管理器
        inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null)
        {
            Debug.LogWarning("[TreasureChest] InventoryManager not found!");
        }
        
        // 查找玩家
        FindPlayer();
        
        // 初始化视觉状态
        UpdateVisualState();
        
        // 初始化交互提示
        if (interactionPrompt != null)
        {
            interactionPrompt.SetActive(false);
        }
        
        Debug.Log($"[TreasureChest] 宝箱初始化完成，包含 {possibleItems.Count} 种可能物品");
    }
    
    private void Update()
    {
        // 检查玩家距离
        CheckPlayerDistance();
        
        // 处理交互输入
        if (playerInRange && !isOpened && Input.GetKeyDown(interactKey))
        {
            OpenChest();
        }
        else if (playerInRange && isOpened && canReopenChest && Input.GetKeyDown(interactKey))
        {
            OpenChest();
        }
    }
    
    private void FindPlayer()
    {
        // 尝试通过标签查找玩家
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            return;
        }
        
        // 如果没有找到，尝试查找PlayerController组件
        PlayerController playerController = FindObjectOfType<PlayerController>();
        if (playerController != null)
        {
            playerTransform = playerController.transform;
            return;
        }
        
        Debug.LogWarning("[TreasureChest] 未找到玩家对象！请确保玩家对象有'Player'标签或PlayerController组件");
    }
    
    private void CheckPlayerDistance()
    {
        if (playerTransform == null) return;
        
        float distance = Vector3.Distance(transform.position, playerTransform.position);
        bool wasInRange = playerInRange;
        playerInRange = distance <= interactionRange;
        
        // 玩家进入/离开交互范围时的处理
        if (playerInRange != wasInRange)
        {
            UpdateInteractionPrompt();
        }
    }
    
    private void UpdateInteractionPrompt()
    {
        if (interactionPrompt == null) return;
        
        bool shouldShowPrompt = playerInRange && (!isOpened || canReopenChest);
        interactionPrompt.SetActive(shouldShowPrompt);
        
        if (shouldShowPrompt)
        {
            Debug.Log($"[TreasureChest] 玩家进入交互范围 - {promptText}");
        }
    }
    
    private void OpenChest()
    {
        if (inventoryManager == null)
        {
            Debug.LogError("[TreasureChest] 无法开启宝箱：InventoryManager未找到！");
            return;
        }
        
        Debug.Log($"[TreasureChest] 开启宝箱... 配置了 {possibleItems.Count} 种可能物品");
        
        // 检查物品配置
        for (int i = 0; i < possibleItems.Count; i++)
        {
            if (possibleItems[i].item == null)
            {
                Debug.LogWarning($"[TreasureChest] 第 {i} 个物品配置为空！");
            }
            else
            {
                Debug.Log($"[TreasureChest] 物品 {i}: {possibleItems[i].item.itemName}, 掉落率: {possibleItems[i].dropChance}%");
            }
        }
        
        // 生成随机物品
        List<ChestItem> itemsToGive = GenerateRandomItems();
        
        if (itemsToGive.Count == 0)
        {
            Debug.LogWarning("[TreasureChest] 宝箱是空的！没有生成任何物品");
        }
        else
        {
            Debug.Log($"[TreasureChest] 生成了 {itemsToGive.Count} 种物品");
            // 将物品添加到背包
            GiveItemsToPlayer(itemsToGive);
        }
        
        // 更新宝箱状态
        if (!canReopenChest)
        {
            isOpened = true;
        }
        
        // 播放视觉和音效
        PlayOpenEffects();
        
        // 更新视觉状态
        UpdateVisualState();
        
        // 更新交互提示
        UpdateInteractionPrompt();
    }
    
    private List<ChestItem> GenerateRandomItems()
    {
        List<ChestItem> selectedItems = new List<ChestItem>();
        
        if (possibleItems.Count == 0)
        {
            Debug.LogWarning("[TreasureChest] 没有配置可能的物品！");
            return selectedItems;
        }
        
        // 计算所有物品的权重总和
        float totalWeight = CalculateTotalWeight();
        if (totalWeight <= 0)
        {
            Debug.LogWarning("[TreasureChest] 所有物品的权重总和为0，无法生成物品！");
            return selectedItems;
        }
        
        int itemCount = Random.Range(minItemCount, maxItemCount + 1);
        Debug.Log($"[TreasureChest] 尝试生成 {itemCount} 个物品，权重总和: {totalWeight}");
        
        for (int i = 0; i < itemCount; i++)
        {
            // 使用权重随机选择物品
            ChestItem selectedItem = SelectItemByWeight(totalWeight);
            
            if (selectedItem != null)
            {
                selectedItems.Add(selectedItem);
                float probability = (selectedItem.dropChance / totalWeight) * 100f;
                Debug.Log($"[TreasureChest] 选中物品: {selectedItem.item.itemName} (权重: {selectedItem.dropChance}, 概率: {probability:F1}%)");
            }
        }
        
        return selectedItems;
    }
    
    /// <summary>
    /// 计算所有物品的权重总和
    /// </summary>
    private float CalculateTotalWeight()
    {
        float totalWeight = 0f;
        foreach (ChestItem item in possibleItems)
        {
            if (item.item != null && item.dropChance > 0)
            {
                totalWeight += item.dropChance;
            }
        }
        return totalWeight;
    }
    
    /// <summary>
    /// 基于权重随机选择物品
    /// </summary>
    private ChestItem SelectItemByWeight(float totalWeight)
    {
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        
        foreach (ChestItem item in possibleItems)
        {
            if (item.item != null && item.dropChance > 0)
            {
                currentWeight += item.dropChance;
                if (randomValue <= currentWeight)
                {
                    return item;
                }
            }
        }
        
        // 如果没有选中任何物品，返回第一个有效物品作为备选
        foreach (ChestItem item in possibleItems)
        {
            if (item.item != null && item.dropChance > 0)
            {
                return item;
            }
        }
        
        return null;
    }
    
    private void GiveItemsToPlayer(List<ChestItem> items)
    {
        int successCount = 0;
        int totalItems = 0;
        
        Debug.Log($"[TreasureChest] 开始添加 {items.Count} 种物品到背包");
        
        foreach (ChestItem chestItem in items)
        {
            if (chestItem.item == null) 
            {
                Debug.LogWarning("[TreasureChest] 跳过空物品");
                continue;
            }
            
            int amount = Random.Range(chestItem.minAmount, chestItem.maxAmount + 1);
            totalItems += amount;
            
            Debug.Log($"[TreasureChest] 尝试添加: {chestItem.item.itemName} x{amount}");
            
            bool success = inventoryManager.AddItem(chestItem.item, amount);
            if (success)
            {
                successCount += amount;
                Debug.Log($"[TreasureChest] ✅ 成功添加物品: {chestItem.item.itemName} x{amount}");
            }
            else
            {
                Debug.LogWarning($"[TreasureChest] ❌ 背包已满，无法添加: {chestItem.item.itemName} x{amount}");
            }
        }
        
        // 显示获得物品的信息
        if (successCount > 0)
        {
            Debug.Log($"[TreasureChest] 🎉 宝箱开启完成！获得 {successCount}/{totalItems} 个物品");
            
            // 通过InventoryManager刷新背包UI（避免直接调用静态事件）
            if (inventoryManager.inventory != null)
            {
                Debug.Log("[TreasureChest] 背包物品添加完成，UI应该会自动刷新");
            }
        }
        else if (totalItems > 0)
        {
            Debug.LogWarning("[TreasureChest] ⚠️ 背包已满，无法获得任何物品！");
        }
    }
    
    private void PlayOpenEffects()
    {
        // 播放粒子效果
        if (openEffect != null)
        {
            openEffect.Play();
        }
        
        // 播放音效
        if (openSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(openSound);
        }
    }
    
    private void UpdateVisualState()
    {
        // 优先使用Sprite切换方式
        if (chestSpriteRenderer != null)
        {
            if (isOpened && openedChestSprite != null)
            {
                chestSpriteRenderer.sprite = openedChestSprite;
                Debug.Log("[TreasureChest] 切换到开启状态Sprite");
            }
            else if (!isOpened && closedChestSprite != null)
            {
                chestSpriteRenderer.sprite = closedChestSprite;
                Debug.Log("[TreasureChest] 切换到关闭状态Sprite");
            }
        }
        
        // 兼容原有的GameObject切换方式
        if (closedChestVisual != null)
        {
            closedChestVisual.SetActive(!isOpened);
        }
        
        if (openedChestVisual != null)
        {
            openedChestVisual.SetActive(isOpened);
        }
    }
    
    // 公共方法：重置宝箱状态
    public void ResetChest()
    {
        isOpened = false;
        UpdateVisualState();
        UpdateInteractionPrompt();
        Debug.Log("[TreasureChest] 宝箱状态已重置");
    }
    
    // 公共方法：强制开启宝箱
    public void ForceOpenChest()
    {
        OpenChest();
    }
    
    // 公共方法：添加物品到宝箱
    public void AddItemToChest(Item item, int dropChance = 50, int minAmount = 1, int maxAmount = 1)
    {
        ChestItem newItem = new ChestItem
        {
            item = item,
            dropChance = dropChance,
            minAmount = minAmount,
            maxAmount = maxAmount
        };
        
        possibleItems.Add(newItem);
        Debug.Log($"[TreasureChest] 添加物品到宝箱: {item.itemName}");
    }
    
    // 调试用：在Scene视图中显示交互范围
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = playerInRange ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
        
        if (playerTransform != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, playerTransform.position);
        }
    }
}