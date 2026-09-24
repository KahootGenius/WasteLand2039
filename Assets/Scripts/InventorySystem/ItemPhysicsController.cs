using UnityEngine;

/// <summary>
/// 物品物理控制器
/// 防止掉落物品被玩家推动，同时保持必要的物理效果
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ItemPhysicsController : MonoBehaviour
{
    [Header("物理设置")]
    [SerializeField] private bool preventPushing = true; // 防止被推动
    [SerializeField] private bool allowGravity = false; // 是否允许重力
    [SerializeField] private bool freezeRotation = true; // 冻结旋转
    [SerializeField] private float mass = 0.1f; // 质量（较小避免影响玩家）
    
    [Header("碰撞设置")]
    [SerializeField] private bool isTrigger = true; // 设置为触发器
    [SerializeField] private PhysicsMaterial2D physicsMaterial; // 物理材质
    
    [Header("位置锁定")]
    [SerializeField] private bool lockXPosition = false; // 锁定X轴位置
    [SerializeField] private bool lockYPosition = false; // 锁定Y轴位置
    
    private Rigidbody2D rb2d;
    private Collider2D col2d;
    private Vector3 originalPosition;
    private bool isInitialized = false;
    
    private void Awake()
    {
        InitializePhysics();
    }
    
    private void Start()
    {
        // 记录原始位置
        originalPosition = transform.position;
        
        // 应用设置
        ApplyPhysicsSettings();
        
        isInitialized = true;
    }
    
    private void FixedUpdate()
    {
        if (!isInitialized) return;
        
        // 防止被推动的核心逻辑
        if (preventPushing)
        {
            PreventPushing();
        }
        
        // 位置锁定
        if (lockXPosition || lockYPosition)
        {
            LockPosition();
        }
    }
    
    /// <summary>
    /// 初始化物理组件
    /// </summary>
    private void InitializePhysics()
    {
        // 获取或添加Rigidbody2D
        rb2d = GetComponent<Rigidbody2D>();
        if (rb2d == null)
        {
            rb2d = gameObject.AddComponent<Rigidbody2D>();
        }
        
        // 获取碰撞器
        col2d = GetComponent<Collider2D>();
        if (col2d == null)
        {
            // 如果没有碰撞器，添加一个圆形碰撞器
            CircleCollider2D circleCol = gameObject.AddComponent<CircleCollider2D>();
            circleCol.radius = 0.5f;
            col2d = circleCol;
        }
    }
    
    /// <summary>
    /// 应用物理设置
    /// </summary>
    private void ApplyPhysicsSettings()
    {
        if (rb2d == null) return;
        
        // 基础物理设置
        rb2d.mass = mass;
        rb2d.gravityScale = allowGravity ? 1f : 0f;
        
        // 冻结设置
        RigidbodyConstraints2D constraints = RigidbodyConstraints2D.None;
        
        if (freezeRotation)
        {
            constraints |= RigidbodyConstraints2D.FreezeRotation;
        }
        
        if (preventPushing)
        {
            // 如果防止推动，可以选择冻结位置
            // constraints |= RigidbodyConstraints2D.FreezePosition;
        }
        
        rb2d.constraints = constraints;
        
        // 碰撞器设置
        if (col2d != null)
        {
            col2d.isTrigger = isTrigger;
            
            if (physicsMaterial != null)
            {
                col2d.sharedMaterial = physicsMaterial;
            }
        }
        
        // 如果完全防止推动，设置为运动学
        if (preventPushing)
        {
            rb2d.bodyType = RigidbodyType2D.Kinematic;
        }
    }
    
    /// <summary>
    /// 防止被推动的核心方法
    /// </summary>
    private void PreventPushing()
    {
        if (rb2d == null) return;
        
        // 方法1：直接停止所有速度
        if (rb2d.bodyType != RigidbodyType2D.Kinematic)
        {
            rb2d.velocity = Vector2.zero;
            rb2d.angularVelocity = 0f;
        }
        
        // 方法2：如果位置发生了意外变化，恢复到原始位置
        // 这个方法适用于需要保持物品在固定位置的情况
        /*
        float maxDistance = 0.1f; // 允许的最大偏移距离
        if (Vector3.Distance(transform.position, originalPosition) > maxDistance)
        {
            transform.position = originalPosition;
        }
        */
    }
    
    /// <summary>
    /// 锁定位置
    /// </summary>
    private void LockPosition()
    {
        Vector3 currentPos = transform.position;
        
        if (lockXPosition)
        {
            currentPos.x = originalPosition.x;
        }
        
        if (lockYPosition)
        {
            currentPos.y = originalPosition.y;
        }
        
        transform.position = currentPos;
    }
    
    /// <summary>
    /// 设置防推动状态
    /// </summary>
    public void SetPreventPushing(bool prevent)
    {
        preventPushing = prevent;
        
        if (rb2d != null)
        {
            if (prevent)
            {
                rb2d.bodyType = RigidbodyType2D.Kinematic;
                rb2d.velocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
            }
            else
            {
                rb2d.bodyType = RigidbodyType2D.Dynamic;
            }
        }
    }
    
    /// <summary>
    /// 设置重力
    /// </summary>
    public void SetGravity(bool enableGravity)
    {
        allowGravity = enableGravity;
        
        if (rb2d != null)
        {
            rb2d.gravityScale = enableGravity ? 1f : 0f;
        }
    }
    
    /// <summary>
    /// 设置为触发器
    /// </summary>
    public void SetTrigger(bool trigger)
    {
        isTrigger = trigger;
        
        if (col2d != null)
        {
            col2d.isTrigger = trigger;
        }
    }
    
    /// <summary>
    /// 重置位置到原始位置
    /// </summary>
    public void ResetToOriginalPosition()
    {
        transform.position = originalPosition;
        
        if (rb2d != null)
        {
            rb2d.velocity = Vector2.zero;
            rb2d.angularVelocity = 0f;
        }
    }
    
    /// <summary>
    /// 更新原始位置
    /// </summary>
    public void UpdateOriginalPosition()
    {
        originalPosition = transform.position;
    }
    
    /// <summary>
    /// 碰撞检测 - 防止被其他物体推动
    /// </summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (preventPushing)
        {
            // 如果是玩家碰撞，忽略物理效果
            if (collision.gameObject.CompareTag("Player"))
            {
                // 可以在这里添加特殊处理逻辑
                // 例如：播放音效、显示提示等
            }
            
            // 停止所有物理运动
            if (rb2d != null)
            {
                rb2d.velocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
            }
        }
    }
    
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (preventPushing && rb2d != null)
        {
            // 持续停止运动
            rb2d.velocity = Vector2.zero;
            rb2d.angularVelocity = 0f;
        }
    }
    
    /// <summary>
    /// 在编辑器中显示设置信息
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // 显示原始位置
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(originalPosition, 0.2f);
        
        // 显示当前位置
        Gizmos.color = preventPushing ? Color.red : Color.blue;
        Gizmos.DrawWireSphere(transform.position, 0.15f);
        
        // 如果位置被锁定，显示锁定轴
        if (lockXPosition)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.down * 0.5f);
        }
        
        if (lockYPosition)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position + Vector3.left * 0.5f, transform.position + Vector3.right * 0.5f);
        }
    }
    
    #region 编辑器工具方法
    
#if UNITY_EDITOR
    [ContextMenu("应用推荐设置 - 防推动")]
    private void ApplyAntiPushSettings()
    {
        preventPushing = true;
        allowGravity = false;
        freezeRotation = true;
        isTrigger = true;
        mass = 0.1f;
        
        ApplyPhysicsSettings();
        
        Debug.Log("已应用防推动设置");
    }
    
    [ContextMenu("应用推荐设置 - 正常物理")]
    private void ApplyNormalPhysicsSettings()
    {
        preventPushing = false;
        allowGravity = true;
        freezeRotation = false;
        isTrigger = false;
        mass = 1f;
        
        ApplyPhysicsSettings();
        
        Debug.Log("已应用正常物理设置");
    }
#endif
    
    #endregion
}