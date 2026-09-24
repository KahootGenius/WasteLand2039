using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class PlayerController : MonoBehaviour, IDamageable
{
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f;
    
    [Header("血量系统")]
    [SerializeField] private float maxHealth = 100f;
    public float currentHealth;
    public GameObject DiePanel;
    
    [Header("受伤效果")]
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float damageFlashDuration = 0.2f;
    
    [Header("武器系统")]
    [SerializeField] private WeaponManager weaponManager;
    
    
    // 组件引用
    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    
    // 动画参数名称
    private readonly string IS_MOVING = "IsMoving";
    private readonly string ATTACK = "Attack";
    private readonly string FACING_RIGHT = "FacingRight";
    
    // 状态变量
    private bool isFacingRight = true;
    private bool isAttacking = false;
    private Vector2 moveDirection;
    
    // 控制状态
    private bool isControlEnabled = true;
    public bool IsControlEnabled => isControlEnabled;
    
    // 血量系统相关
    private bool isDead = false;
    private Color originalColor;
    private bool isFlashing = false;
    
    // 属性
    public bool IsDead => isDead;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    
    private void Awake()
    {
        // 获取组件引用
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        // 获取武器管理器
        if (weaponManager == null)
        {
            weaponManager = GetComponentInChildren<WeaponManager>();
        }
        
        // 确保刚体设置正确
        if (rb != null)
        {
            rb.gravityScale = 0f; // 2D顶视角游戏不需要重力
            rb.freezeRotation = true; // 防止角色旋转
        }
        
        // 初始化血量系统
        currentHealth = maxHealth;
        
        // 保存原始颜色
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }
    
    private void Update()
    {
        // 处理输入
        HandleInput();
        
        // 更新动画状态
        UpdateAnimationState();
        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
        
    }
    
    private void FixedUpdate()
    {
        // 处理移动
        if (!isAttacking)
        {
            Move();
        }
    }
    
    private void HandleInput()
    {
        // 如果控制被禁用或玩家死亡，清除移动方向并返回
        if (!isControlEnabled || isDead)
        {
            moveDirection = Vector2.zero;
            return;
        }
        
        // 获取移动输入
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        
        // 计算移动方向
        moveDirection = new Vector2(moveX, moveY).normalized;
        
        // 处理朝向
        if (moveX != 0)
        {
            isFacingRight = moveX > 0;
        }
        
        // 攻击输入现在由WeaponManager处理
    }
    
    private void Move()
    {
        // 应用移动
        rb.velocity = moveDirection * moveSpeed;
    }
    
    private void TryAttack()
    {
        // 使用武器管理器进行攻击
        if (weaponManager != null && weaponManager.HasWeapon)
        {
            bool fired = weaponManager.TryFire();
            
            if (fired)
            {
                // 执行攻击
                isAttacking = true;
                
                // 触发攻击动画
                animator.SetTrigger(ATTACK);
                
                // 攻击结束后恢复移动（通过动画事件调用）
                // 需要在动画中添加事件调用OnAttackComplete方法
            }
        }
    }
    
    // 由动画事件调用，标记攻击结束
    public void OnAttackComplete()
    {
        VerboseLog.Log("[PlayerController] 攻击动画完成 - isAttacking设置为false");
        isAttacking = false;
    }
    
    /// <summary>
    /// 触发攻击动画（由WeaponManager调用）
    /// </summary>
    public void TriggerAttackAnimation()
    {
        VerboseLog.Log("[PlayerController] 触发攻击动画 - isAttacking设置为true");
        isAttacking = true;
        if (animator != null)
        {
            animator.SetTrigger(ATTACK);
            VerboseLog.Log("[PlayerController] 攻击动画触发器已设置: " + ATTACK);
        }
        else
        {
            Debug.LogError("[PlayerController] Animator组件为空，无法触发攻击动画");
        }
    }
    
    private void UpdateAnimationState()
    {
        // 更新移动状态
        bool isMoving = moveDirection.magnitude > 0.1f;
        animator.SetBool(IS_MOVING, isMoving);
        
        // 更新朝向
        animator.SetBool(FACING_RIGHT, isFacingRight);
    }
    
    #region 控制管理
    
    /// <summary>
    /// 启用角色控制
    /// </summary>
    public void EnableControl()
    {
        isControlEnabled = true;
        VerboseLog.Log("角色控制已启用");
    }
    
    /// <summary>
    /// 禁用角色控制
    /// </summary>
    public void DisableControl()
    {
        isControlEnabled = false;
        moveDirection = Vector2.zero;
        rb.velocity = Vector2.zero; // 立即停止移动
        VerboseLog.Log("角色控制已禁用");
    }
    
    /// <summary>
    /// 设置控制状态
    /// </summary>
    /// <param name="enabled">是否启用控制</param>
    public void SetControlEnabled(bool enabled)
    {
        if (enabled)
        {
            EnableControl();
        }
        else
        {
            DisableControl();
        }
    }
    
    #endregion
    
    #region IDamageable 实现
    
    public void TakeDamage(float damage)
    {
        if (isDead || damage <= 0)
            return;
            
        // 减少血量
        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);
        
        // 控制台输出血量信息
        VerboseLog.Log($"[玩家血量] 受到 {damage} 点伤害，当前血量: {currentHealth}/{maxHealth}");
        
        // 显示受伤效果
        if (!isFlashing)
        {
            StartCoroutine(DamageFlashEffect());
        }
        
        // 检查是否死亡
        
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
        return !isDead && currentHealth > 0;
    }
    
    #endregion
    
    #region 血量系统方法
    
    /// <summary>
    /// 受伤闪烁效果
    /// </summary>
    private IEnumerator DamageFlashEffect()
    {
        if (spriteRenderer == null)
            yield break;
            
        isFlashing = true;
        
        // 变红
        spriteRenderer.color = damageColor;
        
        // 等待
        yield return new WaitForSeconds(damageFlashDuration);
        
        // 恢复原色
        spriteRenderer.color = originalColor;
        
        isFlashing = false;
    }
    
    /// <summary>
    /// 治疗
    /// </summary>
    /// <param name="healAmount">治疗量</param>
    public void Heal(float healAmount)
    {
        if (isDead || healAmount <= 0)
            return;
            
        currentHealth += healAmount;
        currentHealth = Mathf.Min(maxHealth, currentHealth);
        
        VerboseLog.Log($"[玩家血量] 恢复 {healAmount} 点血量，当前血量: {currentHealth}/{maxHealth}");
    }
    
    /// <summary>
    /// 设置最大血量
    /// </summary>
    /// <param name="newMaxHealth">新的最大血量</param>
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = newMaxHealth;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        VerboseLog.Log($"[玩家血量] 最大血量设置为: {maxHealth}");
    }
    
    /// <summary>
    /// 完全恢复血量
    /// </summary>
    public void FullHeal()
    {
        if (!isDead)
        {
            currentHealth = maxHealth;
            VerboseLog.Log($"[玩家血量] 血量完全恢复: {currentHealth}/{maxHealth}");
        }
    }
    
    /// <summary>
    /// 玩家死亡处理
    /// </summary>
    private void Die()
    {
        if (isDead)
            return;
            
        isDead = true;
        
        VerboseLog.Log("[玩家血量] 玩家死亡！");
        
        // 禁用控制
        DisableControl();
        
        // 停止移动
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
        
        // 可以在这里添加死亡动画或其他效果
        if (animator != null)
        {
            // 如果有死亡动画参数，可以在这里触发
          animator.SetTrigger("Die");
        }
      
        
        // 可以在这里触发游戏结束逻辑
        // GameManager.Instance.GameOver();
    }
    
    /// <summary>
    /// 复活玩家
    /// </summary>
    public void Revive()
    {
        if (!isDead)
            return;
            
        isDead = false;
        currentHealth = maxHealth;
        
        VerboseLog.Log($"[玩家血量] 玩家复活！血量: {currentHealth}/{maxHealth}");
        
        // 重新启用控制
        EnableControl();
        
        // 恢复原始颜色
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
    void AwakeDiePanel(){
          DiePanel.SetActive(true);
    }
    
    #endregion
    
}