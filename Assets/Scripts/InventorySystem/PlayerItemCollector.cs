using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 玩家物品收集器
/// 增强版拾取系统，支持自动检测、批量拾取等功能
/// </summary>
public class PlayerItemCollector : MonoBehaviour
{
    [Header("检测设置")]
    [SerializeField] public float detectionRadius = 3f; // 检测半径
    [SerializeField] public LayerMask itemLayer = -1; // 物品图层
    [SerializeField] private float detectionInterval = 0.2f; // 检测间隔
    
    [Header("拾取设置")]
    [SerializeField] public KeyCode pickupKey = KeyCode.E; // 拾取按键
    [SerializeField] public KeyCode pickupAllKey = KeyCode.F; // 拾取所有按键
    [SerializeField] private bool autoPickup = false; // 自动拾取
    [SerializeField] private float autoPickupDelay = 1f; // 自动拾取延迟
    
    [Header("UI设置")]
    [SerializeField] private bool showPickupPrompt = true; // 显示拾取提示
    [SerializeField] private float promptDisplayTime = 3f; // 提示显示时间
    
    [Header("音效设置")]
    [SerializeField] private AudioClip pickupSound; // 拾取音效
    [SerializeField] private AudioClip inventoryFullSound; // 背包满音效
    [SerializeField] private float soundVolume = 0.7f; // 音效音量
    
    [Header("调试设置")]
    [SerializeField] private bool showDebugInfo = true; // 显示调试信息
    [SerializeField] private bool showDetectionRange = true; // 显示检测范围
    
    // 私有变量
    private List<ItemPickup> nearbyItems = new List<ItemPickup>();
    private ItemPickup closestItem;
    private InventoryManager inventoryManager;
    private AudioSource audioSource;
    private float lastDetectionTime;
    private float lastPickupTime;
    
    // 公共属性用于外部访问
    public InventoryManager InventoryManager => inventoryManager;
    public ItemPickup ClosestItem => closestItem;
    public bool AutoPickup => autoPickup;
    
    // UI相关
    private string currentPromptText = "";
    private float promptStartTime;
    
    private void Start()
    {
        // 获取组件引用
        inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null)
        {
            Debug.LogError("PlayerItemCollector: InventoryManager not found!");
        }
        
        // 设置音频源
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.volume = soundVolume;
        audioSource.playOnAwake = false;
        
        if (showDebugInfo)
        {
            Debug.Log("PlayerItemCollector initialized");
        }
    }
    
    private void Update()
    {
        // 定期检测附近物品
        if (Time.time >= lastDetectionTime + detectionInterval)
        {
            DetectNearbyItems();
            lastDetectionTime = Time.time;
        }
        
        // 处理输入
        HandleInput();
        
        // 自动拾取
        if (autoPickup && closestItem != null)
        {
            if (Time.time >= lastPickupTime + autoPickupDelay)
            {
                TryPickupItem(closestItem);
            }
        }
    }
    
    /// <summary>
    /// 检测附近的物品
    /// </summary>
    private void DetectNearbyItems()
    {
        nearbyItems.Clear();
        
        // 使用物理检测查找附近的物品
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, detectionRadius, itemLayer);
        
        foreach (Collider2D collider in colliders)
        {
            ItemPickup itemPickup = collider.GetComponent<ItemPickup>();
            if (itemPickup != null && itemPickup.item != null)
            {
                nearbyItems.Add(itemPickup);
            }
        }
        
        // 找到最近的物品
        UpdateClosestItem();
    }
    
    /// <summary>
    /// 更新最近的物品
    /// </summary>
    private void UpdateClosestItem()
    {
        ItemPickup previousClosest = closestItem;
        closestItem = null;
        
        if (nearbyItems.Count > 0)
        {
            float closestDistance = float.MaxValue;
            
            foreach (ItemPickup item in nearbyItems)
            {
                if (item == null) continue;
                
                float distance = Vector3.Distance(transform.position, item.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestItem = item;
                }
            }
        }
        
        // 更新提示文本
        if (closestItem != previousClosest)
        {
            UpdatePromptText();
        }
    }
    
    /// <summary>
    /// 处理输入
    /// </summary>
    private void HandleInput()
    {
        // 拾取单个物品
        if (Input.GetKeyDown(pickupKey))
        {
            if (closestItem != null)
            {
                TryPickupItem(closestItem);
            }
        }
        
        // 拾取所有物品
        if (Input.GetKeyDown(pickupAllKey))
        {
            TryPickupAllItems();
        }
    }
    
    /// <summary>
    /// 尝试拾取物品
    /// </summary>
    private bool TryPickupItem(ItemPickup itemPickup)
    {
        if (itemPickup == null || itemPickup.item == null || inventoryManager == null)
        {
            return false;
        }
        
        // 尝试添加到背包
        bool success = inventoryManager.AddItem(itemPickup.item, itemPickup.amount);
        
        if (success)
        {
            // 播放拾取音效
            PlayPickupSound();
            
            // 显示拾取信息
            if (showDebugInfo)
            {
                Debug.Log($"Picked up {itemPickup.amount}x {itemPickup.item.itemName}");
            }
            
            // 更新提示
            ShowPickupMessage($"Picked up {itemPickup.amount}x {itemPickup.item.itemName}");
            
            // 从列表中移除
            nearbyItems.Remove(itemPickup);
            
            // 销毁物品
            Destroy(itemPickup.gameObject);
            
            // 更新最近物品
            UpdateClosestItem();
            
            lastPickupTime = Time.time;
            return true;
        }
        else
        {
            // 背包已满
            PlayInventoryFullSound();
            ShowPickupMessage("Inventory is full!");
            
            if (showDebugInfo)
            {
                Debug.Log("Cannot pickup item: Inventory is full");
            }
            
            return false;
        }
    }
    
    /// <summary>
    /// 尝试拾取所有附近的物品
    /// </summary>
    private void TryPickupAllItems()
    {
        if (nearbyItems.Count == 0)
        {
            ShowPickupMessage("No items nearby");
            return;
        }
        
        int pickedUpCount = 0;
        List<ItemPickup> itemsToRemove = new List<ItemPickup>();
        
        foreach (ItemPickup item in nearbyItems.ToList())
        {
            if (TryPickupItem(item))
            {
                pickedUpCount++;
                itemsToRemove.Add(item);
            }
            else
            {
                // 如果背包满了，停止拾取
                break;
            }
        }
        
        // 从列表中移除已拾取的物品
        foreach (ItemPickup item in itemsToRemove)
        {
            nearbyItems.Remove(item);
        }
        
        if (pickedUpCount > 0)
        {
            ShowPickupMessage($"Picked up {pickedUpCount} items");
        }
        
        UpdateClosestItem();
    }
    
    /// <summary>
    /// 播放拾取音效
    /// </summary>
    private void PlayPickupSound()
    {
        if (pickupSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(pickupSound);
        }
    }
    
    /// <summary>
    /// 播放背包满音效
    /// </summary>
    private void PlayInventoryFullSound()
    {
        if (inventoryFullSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(inventoryFullSound);
        }
    }
    
    /// <summary>
    /// 更新提示文本
    /// </summary>
    private void UpdatePromptText()
    {
        if (closestItem != null)
        {
            currentPromptText = $"Press {pickupKey} to pickup {closestItem.item.itemName} x{closestItem.amount}\nPress {pickupAllKey} to pickup all ({nearbyItems.Count} items)";
        }
        else
        {
            currentPromptText = "";
        }
        
        promptStartTime = Time.time;
    }
    
    /// <summary>
    /// 显示拾取消息
    /// </summary>
    private void ShowPickupMessage(string message)
    {
        currentPromptText = message;
        promptStartTime = Time.time;
    }
    
    /// <summary>
    /// 设置自动拾取
    /// </summary>
    public void SetAutoPickup(bool enabled)
    {
        autoPickup = enabled;
        
        if (showDebugInfo)
        {
            Debug.Log($"Auto pickup {(enabled ? "enabled" : "disabled")}");
        }
    }
    
    /// <summary>
    /// 获取附近物品数量
    /// </summary>
    public int GetNearbyItemCount()
    {
        return nearbyItems.Count;
    }
    
    /// <summary>
    /// 获取附近物品列表
    /// </summary>
    public List<ItemPickup> GetNearbyItems()
    {
        return new List<ItemPickup>(nearbyItems);
    }
    
    private void OnDrawGizmosSelected()
    {
        if (!showDetectionRange) return;
        
        // 绘制检测范围
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        
        // 绘制到最近物品的连线
        if (closestItem != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, closestItem.transform.position);
        }
    }
    
    private void OnGUI()
    {
        if (!showPickupPrompt) return;
        
        // 显示拾取提示
        if (!string.IsNullOrEmpty(currentPromptText) && Time.time - promptStartTime < promptDisplayTime)
        {
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            
            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.fontSize = 16;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = Color.white;
            
            Vector2 textSize = style.CalcSize(new GUIContent(currentPromptText));
            Rect rect = new Rect(
                (screenWidth - textSize.x) / 2,
                screenHeight * 0.7f,
                textSize.x + 20,
                textSize.y + 10
            );
            
            GUI.Box(rect, currentPromptText, style);
        }
        
        // 调试信息
        if (showDebugInfo)
        {
            GUILayout.BeginArea(new Rect(10, 220, 300, 150));
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUILayout.Label("Item Collector Debug", GUI.skin.label);
            GUILayout.Label($"Nearby items: {nearbyItems.Count}");
            GUILayout.Label($"Closest item: {(closestItem != null ? closestItem.item.itemName : "None")}");
            GUILayout.Label($"Auto pickup: {(autoPickup ? "✅" : "❌")}");
            
            if (GUILayout.Button(autoPickup ? "Disable Auto Pickup" : "Enable Auto Pickup"))
            {
                SetAutoPickup(!autoPickup);
            }
            
            if (GUILayout.Button("Pickup All Items"))
            {
                TryPickupAllItems();
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}