using UnityEngine;
using System.Collections.Generic;
using System.Collections;
public class PlayerController : MonoBehaviour, IDamageable
{
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f;
    [Header("血量系统")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;
    [Header("受伤效果")]
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float damageFlashDuration = 0.2f;
    [Header("武器系统")]
    [SerializeField] private WeaponManager weaponManager;
    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private readonly string IS_MOVING = "IsMoving";
    private readonly string ATTACK = "Attack";
    private readonly string FACING_RIGHT = "FacingRight";
    private bool isFacingRight = true;
    private bool isAttacking = false;
    private Vector2 moveDirection;
    private bool isControlEnabled = true;
    public bool IsControlEnabled => isControlEnabled;
    private bool isDead = false;
    private Color originalColor;
    private bool isFlashing = false;
    public bool IsDead => isDead;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (weaponManager == null)
        {
            weaponManager = GetComponentInChildren<WeaponManager>();
        }
        if (rb != null)
        {
            rb.gravityScale = 0f; 
            rb.freezeRotation = true; 
        }
        currentHealth = maxHealth;
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }
    private void Update()
    {
        HandleInput();
        UpdateAnimationState();
    }
    private void FixedUpdate()
    {
        if (!isAttacking)
        {
            Move();
        }
    }
    private void HandleInput()
    {
        if (!isControlEnabled || isDead)
        {
            moveDirection = Vector2.zero;
            return;
        }
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        moveDirection = new Vector2(moveX, moveY).normalized;
        if (moveX != 0)
        {
            isFacingRight = moveX > 0;
        }
    }
    private void Move()
    {
        rb.velocity = moveDirection * moveSpeed;
    }
    private void TryAttack()
    {
        if (weaponManager != null && weaponManager.HasWeapon)
        {
            bool fired = weaponManager.TryFire();
            if (fired)
            {
                isAttacking = true;
                animator.SetTrigger(ATTACK);
            }
        }
    }
    public void OnAttackComplete()
    {
        Debug.Log("[PlayerController] 攻击动画完成 - isAttacking设置为false");
        isAttacking = false;
    }
    public void TriggerAttackAnimation()
    {
        Debug.Log("[PlayerController] 触发攻击动画 - isAttacking设置为true");
        isAttacking = true;
        if (animator != null)
        {
            animator.SetTrigger(ATTACK);
            Debug.Log("[PlayerController] 攻击动画触发器已设置: " + ATTACK);
        }
        else
        {
            Debug.LogError("[PlayerController] Animator组件为空，无法触发攻击动画");
        }
    }
    private void UpdateAnimationState()
    {
        bool isMoving = moveDirection.magnitude > 0.1f;
        animator.SetBool(IS_MOVING, isMoving);
        animator.SetBool(FACING_RIGHT, isFacingRight);
    }
    #region 控制管理
    public void EnableControl()
    {
        isControlEnabled = true;
        Debug.Log("角色控制已启用");
    }
    public void DisableControl()
    {
        isControlEnabled = false;
        moveDirection = Vector2.zero;
        rb.velocity = Vector2.zero; 
        Debug.Log("角色控制已禁用");
    }
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
        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);
        Debug.Log($"[玩家血量] 受到 {damage} 点伤害，当前血量: {currentHealth}/{maxHealth}");
        if (!isFlashing)
        {
            StartCoroutine(DamageFlashEffect());
        }
        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
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
    private IEnumerator DamageFlashEffect()
    {
        if (spriteRenderer == null)
            yield break;
        isFlashing = true;
        spriteRenderer.color = damageColor;
        yield return new WaitForSeconds(damageFlashDuration);
        spriteRenderer.color = originalColor;
        isFlashing = false;
    }
    public void Heal(float healAmount)
    {
        if (isDead || healAmount <= 0)
            return;
        currentHealth += healAmount;
        currentHealth = Mathf.Min(maxHealth, currentHealth);
        Debug.Log($"[玩家血量] 恢复 {healAmount} 点血量，当前血量: {currentHealth}/{maxHealth}");
    }
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = newMaxHealth;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        Debug.Log($"[玩家血量] 最大血量设置为: {maxHealth}");
    }
    public void FullHeal()
    {
        if (!isDead)
        {
            currentHealth = maxHealth;
            Debug.Log($"[玩家血量] 血量完全恢复: {currentHealth}/{maxHealth}");
        }
    }
    private void Die()
    {
        if (isDead)
            return;
        isDead = true;
        Debug.Log("[玩家血量] 玩家死亡！");
        DisableControl();
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
        if (animator != null)
        {
        }
    }
    public void Revive()
    {
        if (!isDead)
            return;
        isDead = false;
        currentHealth = maxHealth;
        Debug.Log($"[玩家血量] 玩家复活！血量: {currentHealth}/{maxHealth}");
        EnableControl();
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
    #endregion
}
