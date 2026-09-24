using UnityEngine;
using TMPro;

public class ItemPickup : MonoBehaviour
{
    [Header("Item Settings")]
    public Item item;
    public int amount = 1;
    
    [Header("UI Display")]
    public GameObject pickupPrompt;
    public TextMeshProUGUI promptText;
    public KeyCode pickupKey = KeyCode.E;
    
    [Header("Detection Settings")]
    public float pickupRange = 2f;
    public LayerMask playerLayer = 1;
    
    [Header("Visual Effects")]
    public bool bobUpAndDown = true;
    public float bobSpeed = 2f;
    public float bobHeight = 0.5f;
    
    private bool playerInRange = false;
    private GameObject player;
    private InventoryUI inventoryUI;
    private Vector3 startPosition;
    private SpriteRenderer spriteRenderer;
    
    private void Start()
    {
        startPosition = transform.position;
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        // 设置物品图标
        if (item != null && item.icon != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = item.icon;
        }
        
        // 查找InventoryUI
        inventoryUI = FindObjectOfType<InventoryUI>();
        
        // 初始化提示UI
        if (pickupPrompt != null)
        {
            pickupPrompt.SetActive(false);
        }
        
        UpdatePromptText();
    }
    
    private void Update()
    {
        // 上下浮动效果
        if (bobUpAndDown)
        {
            float newY = startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
        }
        
        // 检测玩家距离
        CheckPlayerDistance();
        
        // 处理拾取输入
        if (playerInRange && Input.GetKeyDown(pickupKey))
        {
            TryPickupItem();
        }
    }
    
    private void CheckPlayerDistance()
    {
        // 查找玩家
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj;
            }
            else
            {
                return;
            }
        }
        
        float distance = Vector3.Distance(transform.position, player.transform.position);
        bool wasInRange = playerInRange;
        playerInRange = distance <= pickupRange;
        
        // 状态改变时更新UI
        if (wasInRange != playerInRange)
        {
            if (pickupPrompt != null)
            {
                pickupPrompt.SetActive(playerInRange);
            }
        }
    }
    
    private void TryPickupItem()
    {
        if (item == null || inventoryUI == null)
        {
            Debug.LogWarning("Item or Inventory UI not set!");
            return;
        }
        
        // 尝试添加到背包
        bool success = inventoryUI.AddItemToInventory(item, amount);
        
        if (success)
        {
            Debug.Log($"Picked up {amount}x {item.itemName}");
            
            // Play pickup sound (if available)
            // AudioSource.PlayClipAtPoint(pickupSound, transform.position);
            
            // Destroy item
            Destroy(gameObject);
        }
        else
        {
            Debug.Log("Inventory is full, cannot pickup item!");
            
            // Show inventory full message
            ShowInventoryFullMessage();
        }
    }
    
    private void UpdatePromptText()
    {
        if (promptText != null && item != null)
        {
            promptText.text = $"Press {pickupKey} to pickup {item.itemName} x{amount}";
        }
    }
    
    private void ShowInventoryFullMessage()
    {
        // Show inventory full UI prompt here
        Debug.Log("Inventory is full!");
    }
    
    // Set item data
    public void SetItem(Item newItem, int newAmount = 1)
    {
        item = newItem;
        amount = newAmount;
        
        // Update icon
        if (item != null && item.icon != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = item.icon;
        }
        
        UpdatePromptText();
    }
    
    // Show pickup range in Scene view
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
    
    // Called when item is created (for programmatic item generation)
    public static GameObject CreateItemPickup(Item item, Vector3 position, int amount = 1)
    {
        // Load item prefab from Resources folder
        GameObject pickupPrefab = Resources.Load<GameObject>("ItemWorldPrefab");
        
        // If not found in Resources, try to find it in the project
        if (pickupPrefab == null)
        {
#if UNITY_EDITOR
            // In editor, try to load from Assets folder
            string[] guids = UnityEditor.AssetDatabase.FindAssets("ItemWorldPrefab t:GameObject");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                pickupPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Debug.LogWarning($"ItemWorldPrefab loaded from: {path}. Consider moving it to Resources folder for runtime access.");
            }
#endif
            
            if (pickupPrefab == null)
            {
                Debug.LogError("ItemWorldPrefab prefab not found! Please ensure it exists in Resources folder or Assets/Prefabs folder.");
                return null;
            }
        }
        
        GameObject pickup = Instantiate(pickupPrefab, position, Quaternion.identity);
        ItemPickup pickupComponent = pickup.GetComponent<ItemPickup>();
        
        if (pickupComponent != null)
        {
            pickupComponent.SetItem(item, amount);
        }
        
        // 添加物理控制器防止被推动
        ItemPhysicsController physicsController = pickup.GetComponent<ItemPhysicsController>();
        if (physicsController == null)
        {
            physicsController = pickup.AddComponent<ItemPhysicsController>();
        }
        
        // 应用防推动设置
        physicsController.SetPreventPushing(true);
        physicsController.SetGravity(false);
        physicsController.SetTrigger(true);
        
        return pickup;
    }
}