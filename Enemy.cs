using UnityEngine;
using System.Collections.Generic;
using System.Collections;
public class Enemy : MonoBehaviour, IDamageable
{
    [Header("敌人类型设置")]
    [SerializeField] private bool isMovingEnemy = true;
    [Header("敌人属性")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;
    [Header("视觉反馈")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float damageFlashDuration = 0.1f;
    [Header("死亡设置")]
    [SerializeField] private GameObject deathEffectPrefab;
    [SerializeField] private float deathDelay = 0f;
    [Header("掉落物品设置")]
    [SerializeField] private List<Item> dropItems = new List<Item>();
    [SerializeField] private float dropChance = 0.8f;
    [SerializeField] private int minDropAmount = 1;
    [SerializeField] private int maxDropAmount = 3;
    [Header("掉落稀有度权重")]
    [SerializeField] private float commonWeight = 50f;
    [SerializeField] private float rareWeight = 25f;
    [SerializeField] private float epicWeight = 15f;
    [SerializeField] private float legendaryWeight = 10f;
    [Header("移动和AI设置")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float detectionRadius = 5f;
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private float attackDamage = 20f;
    [SerializeField] private LayerMask obstacleLayerMask = -1;
    [SerializeField] private LayerMask playerLayerMask = 1;
    [Header("寻路设置")]
    [SerializeField] private float pathUpdateInterval = 0.5f;
    [SerializeField] private float stuckThreshold = 0.1f;
    [SerializeField] private float stuckTime = 2f;
    [Header("死亡动画设置")]
    [SerializeField] private float deathAnimationDuration = 1f;
    [SerializeField] private bool waitForDeathAnimation = true;
    [Header("集群意识设置")]
    [SerializeField] private bool enableSwarmBehavior = true;
    [SerializeField] private float swarmRadius = 8f;
    [SerializeField] private int maxSwarmPropagation = 2;
    [SerializeField] private LayerMask enemyLayerMask = -1;
    [Header("音效")]
    [SerializeField] private AudioClip damageSound;
    [SerializeField] private AudioClip deathSound;
    [SerializeField] private AudioClip attackSound;
    private Color originalColor;
    private AudioSource audioSource;
    private bool isDead = false;
    private Transform player;
    private Transform mainBase;
    private Rigidbody2D rb;
    private Animator animator;
    private bool isPlayerInRange = false;
    private bool isMainBaseInRange = false;
    private bool isAttacking = false;
    private float lastAttackTime;
    private Vector3 lastPosition;
    private float stuckTimer;
    private Coroutine pathfindingCoroutine;
    private Vector2 currentTarget;
    private bool hasTarget = false;
    private bool targetingMainBase = false;
    private bool isFacingRight = true;
    private Vector2 lastMoveDirection = Vector2.right;
    private bool isAlerted = false;
    private int currentPropagationLevel = 0;
    private float lastSwarmCheckTime = 0f;
    private const float SWARM_CHECK_INTERVAL = 0.5f;
    private readonly string ANIM_IS_MOVING = "IsMoving";
    private readonly string ANIM_MOVE_X = "MoveX";
    private readonly string ANIM_MOVE_Y = "MoveY";
    private readonly string ANIM_ATTACK = "Attack";
    private readonly string ANIM_DEATH = "Death";
    private readonly string ANIM_FACING_RIGHT = "FacingRight";
    public System.Action<Enemy> OnDeath;
    public System.Action<Enemy, float> OnDamageTaken; 
    public System.Action<Enemy, float, float> OnHealthChanged; 
    public bool IsDead => isDead;
    private BoxCollider2D boxCollider2D;
    private void Awake()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (rb == null && isMovingEnemy)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }
        currentHealth = maxHealth;
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        lastPosition = transform.position;
        boxCollider2D = GetComponent<BoxCollider2D>();
    }
    private void Start()
    {
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        if (isMovingEnemy)
        {
            FindPlayer();
            FindMainBase();
            if (player != null || mainBase != null)
            {
                pathfindingCoroutine = StartCoroutine(PathfindingUpdate());
            }
        }
        if (waitForDeathAnimation)
        {
            AutoSetDeathAnimationDuration();
        }
    }
    #region IDamageable 实现
    public void TakeDamage(float damage)
    {
        if (isDead || damage <= 0)
            return;
        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);
        PlaySound(damageSound);
        ShowDamageEffect();
        OnDamageTaken?.Invoke(this, damage);
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
        Debug.Log($"{gameObject.name} 受到 {damage} 点伤害，剩余血量: {currentHealth}/{maxHealth}");
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
    private void Update()
    {
        if (!isMovingEnemy || isDead)
            return;
        DetectTargets();
        if (enableSwarmBehavior)
        {
            HandleSwarmBehavior();
        }
        HandleAttack();
        CheckIfStuck();
    }
    private void FixedUpdate()
    {
        if (!isMovingEnemy || isDead || isAttacking)
            return;
        MoveTowardsTarget();
    }
    #region AI系统
    private void FindPlayer()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            return;
        }
        PlayerController playerController = FindObjectOfType<PlayerController>();
        if (playerController != null)
        {
            player = playerController.transform;
            return;
        }
        Debug.LogWarning($"{gameObject.name}: 未找到玩家对象，请确保玩家对象有'Player'标签或PlayerController组件");
    }
    private void FindMainBase()
    {
        GameObject baseObj = GameObject.FindGameObjectWithTag("MainBase");
        if (baseObj != null)
        {
            mainBase = baseObj.transform;
            return;
        }
        MainBase mainBaseComponent = FindObjectOfType<MainBase>();
        if (mainBaseComponent != null)
        {
            mainBase = mainBaseComponent.transform;
            return;
        }
        Debug.LogWarning($"{gameObject.name}: 未找到主基地对象，请确保主基地对象有'MainBase'标签或MainBase组件");
    }
    private void DetectTargets()
    {
        DetectPlayer();
        if (!isPlayerInRange)
        {
            DetectMainBase();
        }
        else
        {
            if (targetingMainBase)
            {
                targetingMainBase = false;
                isMainBaseInRange = false;
                Debug.Log($"{gameObject.name} 发现玩家，停止攻击基地");
            }
        }
    }
    private void DetectPlayer()
    {
        if (player == null)
        {
            FindPlayer();
            return;
        }
        float distanceToPlayer = Vector2.Distance(transform.position, player.position);
        bool wasInRange = isPlayerInRange;
        isPlayerInRange = distanceToPlayer <= detectionRadius;
        if (isPlayerInRange != wasInRange)
        {
            if (isPlayerInRange)
            {
                currentTarget = player.position;
                hasTarget = true;
                targetingMainBase = false; 
                if (enableSwarmBehavior && !isAlerted)
                {
                    TriggerSwarmAlert(0); 
                }
                Debug.Log($"{gameObject.name} 检测到玩家，开始追击");
            }
            else
            {
                if (!CheckForMainBaseTarget())
                {
                    hasTarget = false;
                    isAlerted = false; 
                    currentPropagationLevel = 0; 
                    if (rb != null)
                    {
                        rb.velocity = Vector2.zero;
                    }
                    UpdateMovementAnimation(Vector2.zero, false);
                }
                Debug.Log($"{gameObject.name} 玩家离开检测范围");
            }
        }
        if (isPlayerInRange)
        {
            currentTarget = player.position;
            hasTarget = true;
        }
    }
    private void DetectMainBase()
    {
        if (mainBase == null)
        {
            FindMainBase();
            return;
        }
        if (isMovingEnemy && !isPlayerInRange)
        {
            float distanceToBase = Vector2.Distance(transform.position, mainBase.position);
            bool wasInRange = isMainBaseInRange;
            isMainBaseInRange = true; 
            if (!wasInRange && isMainBaseInRange)
            {
                currentTarget = mainBase.position;
                hasTarget = true;
                targetingMainBase = true;
                Debug.Log($"{gameObject.name} 开始攻击主基地");
            }
            if (targetingMainBase)
            {
                currentTarget = mainBase.position;
                hasTarget = true;
            }
        }
    }
    private bool CheckForMainBaseTarget()
    {
        if (mainBase != null && isMovingEnemy)
        {
            currentTarget = mainBase.position;
            hasTarget = true;
            targetingMainBase = true;
            isMainBaseInRange = true;
            Debug.Log($"{gameObject.name} 转向攻击主基地");
            return true;
        }
        return false;
    }
    private IEnumerator PathfindingUpdate()
    {
        while (!isDead)
        {
            if (hasTarget)
            {
                Vector2 targetPosition;
                Vector2 directionToTarget;
                float distanceToTarget;
                if (isPlayerInRange && player != null)
                {
                    targetPosition = player.position;
                }
                else if (targetingMainBase && mainBase != null)
                {
                    targetPosition = mainBase.position;
                }
                else
                {
                    yield return new WaitForSeconds(pathUpdateInterval);
                    continue;
                }
                directionToTarget = (targetPosition - (Vector2)transform.position).normalized;
                distanceToTarget = Vector2.Distance(transform.position, targetPosition);
                RaycastHit2D hit = Physics2D.Raycast(transform.position, directionToTarget, distanceToTarget, obstacleLayerMask);
                if (hit.collider == null)
                {
                    currentTarget = targetPosition;
                }
                else
                {
                    Vector2 avoidanceTarget = FindAvoidancePath(hit.point, directionToTarget);
                    currentTarget = avoidanceTarget;
                }
            }
            yield return new WaitForSeconds(pathUpdateInterval);
        }
    }
    private Vector2 FindAvoidancePath(Vector2 obstaclePoint, Vector2 originalDirection)
    {
        Vector2 leftDirection = new Vector2(-originalDirection.y, originalDirection.x);
        Vector2 rightDirection = new Vector2(originalDirection.y, -originalDirection.x);
        float checkDistance = 2f;
        Vector2 leftTarget = (Vector2)transform.position + leftDirection * checkDistance;
        RaycastHit2D leftHit = Physics2D.Raycast(transform.position, leftDirection, checkDistance, obstacleLayerMask);
        Vector2 rightTarget = (Vector2)transform.position + rightDirection * checkDistance;
        RaycastHit2D rightHit = Physics2D.Raycast(transform.position, rightDirection, checkDistance, obstacleLayerMask);
        if (leftHit.collider == null && rightHit.collider != null)
        {
            return leftTarget;
        }
        else if (rightHit.collider == null && leftHit.collider != null)
        {
            return rightTarget;
        }
        else if (leftHit.collider == null && rightHit.collider == null)
        {
            float leftDistance = Vector2.Distance(leftTarget, player.position);
            float rightDistance = Vector2.Distance(rightTarget, player.position);
            return leftDistance < rightDistance ? leftTarget : rightTarget;
        }
        return transform.position;
    }
    private void MoveTowardsTarget()
    {
        if (!hasTarget || rb == null)
        {
            UpdateMovementAnimation(Vector2.zero, false);
            return;
        }
        Vector2 direction = (currentTarget - (Vector2)transform.position).normalized;
        float distanceToTarget = Vector2.Distance(transform.position, currentTarget);
        if (distanceToTarget < 0.1f)
        {
            rb.velocity = Vector2.zero;
            UpdateMovementAnimation(Vector2.zero, false);
            return;
        }
        if ((isPlayerInRange || targetingMainBase) && distanceToTarget <= attackRange)
        {
            rb.velocity = Vector2.zero;
            UpdateMovementAnimation(Vector2.zero, false);
            return;
        }
        rb.velocity = direction * moveSpeed;
        UpdateMovementAnimation(direction, true);
        UpdateFacing(direction);
    }
    private void HandleAttack()
    {
        if (isAttacking)
            return;
        if (isPlayerInRange && player != null)
        {
            float distanceToPlayer = Vector2.Distance(transform.position, player.position);
            if (distanceToPlayer <= attackRange && Time.time >= lastAttackTime + attackCooldown)
            {
                StartCoroutine(PerformAttack(player, "玩家"));
            }
        }
        else if (targetingMainBase && mainBase != null)
        {
            float distanceToBase = Vector2.Distance(transform.position, mainBase.position);
            if (distanceToBase <= attackRange && Time.time >= lastAttackTime + attackCooldown)
            {
                StartCoroutine(PerformAttack(mainBase, "主基地"));
            }
        }
    }
    private IEnumerator PerformAttack(Transform target, string targetName)
    {
        isAttacking = true;
        lastAttackTime = Time.time;
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
        if (animator != null)
        {
            animator.SetTrigger(ANIM_ATTACK);
            animator.SetBool(ANIM_IS_MOVING, false);
        }
        PlaySound(attackSound);
        yield return new WaitForSeconds(0.5f);
        if (target != null)
        {
            float distanceToTarget = Vector2.Distance(transform.position, target.position);
            if (distanceToTarget <= attackRange)
            {
                IDamageable targetDamageable = target.GetComponent<IDamageable>();
                if (targetDamageable != null)
                {
                    targetDamageable.TakeDamage(attackDamage);
                    Debug.Log($"{gameObject.name} 攻击了{targetName}，造成 {attackDamage} 点伤害");
                }
            }
        }
        isAttacking = false;
    }
    private void CheckIfStuck()
    {
        if (!hasTarget || (!isPlayerInRange && !targetingMainBase))
        {
            stuckTimer = 0f;
            return;
        }
        float distanceMoved = Vector3.Distance(transform.position, lastPosition);
        if (distanceMoved < stuckThreshold)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= stuckTime)
            {
                if ((isPlayerInRange && player != null) || (targetingMainBase && mainBase != null))
                {
                    Vector2 randomDirection = Random.insideUnitCircle.normalized;
                    currentTarget = (Vector2)transform.position + randomDirection * 2f;
                    Debug.Log($"{gameObject.name} 检测到卡住，尝试脱困移动");
                }
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }
        lastPosition = transform.position;
    }
    private void UpdateMovementAnimation(Vector2 direction, bool isMoving)
    {
        if (animator == null)
            return;
        animator.SetBool(ANIM_IS_MOVING, isMoving);
        if (isMoving)
        {
            animator.SetFloat(ANIM_MOVE_X, direction.x);
            animator.SetFloat(ANIM_MOVE_Y, direction.y);
            lastMoveDirection = direction;
        }
        else
        {
            animator.SetFloat(ANIM_MOVE_X, 0f);
            animator.SetFloat(ANIM_MOVE_Y, 0f);
        }
    }
    private void UpdateFacing(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) > 0.1f)
        {
            bool shouldFaceRight = direction.x > 0;
            if (shouldFaceRight != isFacingRight)
            {
                isFacingRight = shouldFaceRight;
                FlipSprite();
                if (animator != null)
                {
                    animator.SetBool(ANIM_FACING_RIGHT, isFacingRight);
                }
            }
        }
    }
    private void FlipSprite()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = !isFacingRight;
        }
    }
    public void SetFacing(bool faceRight)
    {
        if (faceRight != isFacingRight)
        {
            isFacingRight = faceRight;
            FlipSprite();
            if (animator != null)
            {
                animator.SetBool(ANIM_FACING_RIGHT, isFacingRight);
            }
        }
    }
    public bool IsFacingRight()
    {
        return isFacingRight;
    }
    public void TriggerAttackAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(ANIM_ATTACK);
        }
    }
    public void TriggerDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(ANIM_DEATH);
            animator.SetBool(ANIM_IS_MOVING, false);
        }
    }
    public float GetDeathAnimationLength()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            foreach (AnimationClip clip in clips)
            {
                if (clip.name.ToLower().Contains("death") || clip.name.ToLower().Contains("die"))
                {
                    return clip.length;
                }
            }
        }
        return deathAnimationDuration;
    }
    public void AutoSetDeathAnimationDuration()
    {
        float actualLength = GetDeathAnimationLength();
        if (actualLength > 0)
        {
            deathAnimationDuration = actualLength;
            Debug.Log($"{gameObject.name} 自动设置死亡动画时长为: {deathAnimationDuration}秒");
        }
    }
    public void ForceDestroy()
    {
        CancelInvoke();
        DestroyEnemy();
    }
    private void HandleSwarmBehavior()
    {
        if (Time.time - lastSwarmCheckTime < SWARM_CHECK_INTERVAL)
            return;
        lastSwarmCheckTime = Time.time;
        if (isPlayerInRange)
            return;
        CheckNearbyAlertedEnemies();
    }
    private void CheckNearbyAlertedEnemies()
    {
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, swarmRadius, enemyLayerMask);
        foreach (Collider2D collider in nearbyColliders)
        {
            if (collider.gameObject == gameObject)
                continue;
            Enemy nearbyEnemy = collider.GetComponent<Enemy>();
            if (nearbyEnemy != null && nearbyEnemy.isMovingEnemy && nearbyEnemy.isAlerted)
            {
                if (nearbyEnemy.player != null && !isAlerted)
                {
                    JoinSwarmChase(nearbyEnemy.player, nearbyEnemy.currentPropagationLevel + 1);
                    break;
                }
            }
        }
    }
    public void TriggerSwarmAlert(int propagationLevel)
    {
        if (propagationLevel > maxSwarmPropagation || isAlerted)
            return;
        isAlerted = true;
        currentPropagationLevel = propagationLevel;
        Debug.Log($"{gameObject.name} 收到集群警报，传播层级: {propagationLevel}");
        PropagateSwarmAlert(propagationLevel + 1);
    }
    private void PropagateSwarmAlert(int nextLevel)
    {
        if (nextLevel > maxSwarmPropagation)
            return;
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, swarmRadius, enemyLayerMask);
        foreach (Collider2D collider in nearbyColliders)
        {
            if (collider.gameObject == gameObject)
                continue;
            Enemy nearbyEnemy = collider.GetComponent<Enemy>();
            if (nearbyEnemy != null && nearbyEnemy.isMovingEnemy && nearbyEnemy.enableSwarmBehavior)
            {
                nearbyEnemy.TriggerSwarmAlert(nextLevel);
            }
        }
    }
    private void JoinSwarmChase(Transform targetPlayer, int propagationLevel)
    {
        if (propagationLevel > maxSwarmPropagation || isPlayerInRange)
            return;
        player = targetPlayer;
        isAlerted = true;
        currentPropagationLevel = propagationLevel;
        hasTarget = true;
        currentTarget = player.position;
        Debug.Log($"{gameObject.name} 加入集群追击，传播层级: {propagationLevel}");
        PropagateSwarmAlert(propagationLevel + 1);
    }
    public string GetSwarmStatus()
    {
        if (!enableSwarmBehavior)
            return "集群行为已禁用";
        if (isAlerted)
            return $"警戒状态 - 传播层级: {currentPropagationLevel}";
        else
            return "正常状态";
    }
    #endregion
    #region 碰撞检测
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isMovingEnemy && other.CompareTag("Player"))
        {
            PreventPlayerEntry(other);
        }
    }
    private void OnTriggerStay2D(Collider2D other)
    {
        if (!isMovingEnemy && other.CompareTag("Player"))
        {
            PreventPlayerEntry(other);
        }
    }
    private void PreventPlayerEntry(Collider2D playerCollider)
    {
        if (boxCollider2D == null || playerCollider == null)
            return;
        Rigidbody2D playerRb = playerCollider.GetComponent<Rigidbody2D>();
        if (playerRb == null)
            return;
        Vector2 directionToPlayer = (playerCollider.transform.position - transform.position).normalized;
        Bounds enemyBounds = boxCollider2D.bounds;
        Bounds playerBounds = playerCollider.bounds;
        Vector2 pushDirection = Vector2.zero;
        float pushDistance = 0f;
        float overlapX = Mathf.Min(enemyBounds.max.x, playerBounds.max.x) - Mathf.Max(enemyBounds.min.x, playerBounds.min.x);
        float overlapY = Mathf.Min(enemyBounds.max.y, playerBounds.max.y) - Mathf.Max(enemyBounds.min.y, playerBounds.min.y);
        if (overlapX < overlapY)
        {
            pushDirection = new Vector2(Mathf.Sign(directionToPlayer.x), 0);
            pushDistance = overlapX + 0.1f; 
        }
        else
        {
            pushDirection = new Vector2(0, Mathf.Sign(directionToPlayer.y));
            pushDistance = overlapY + 0.1f; 
        }
        Vector2 targetPosition = (Vector2)playerCollider.transform.position + pushDirection * pushDistance;
        playerRb.MovePosition(Vector2.Lerp(playerCollider.transform.position, targetPosition, Time.fixedDeltaTime * 10f));
        Debug.Log($"静止敌人 {gameObject.name} 阻止玩家进入碰撞箱");
    }
    #endregion
     private void ShowDamageEffect()
    {
        if (spriteRenderer != null)
        {
            CancelInvoke(nameof(ResetColor));
            spriteRenderer.color = damageColor;
            Invoke(nameof(ResetColor), damageFlashDuration);
        }
    }
    private void ResetColor()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
    private void Die()
    {
        if (isDead)
            return;
        isDead = true;
        if (pathfindingCoroutine != null)
        {
            StopCoroutine(pathfindingCoroutine);
            pathfindingCoroutine = null;
        }
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
        if (animator != null)
        {
            animator.SetTrigger(ANIM_DEATH);
            animator.SetBool(ANIM_IS_MOVING, false);
        }
        PlaySound(deathSound);
        CreateDeathEffect();
        DropItems();
        OnDeath?.Invoke(this);
        Debug.Log($"{gameObject.name} 死亡");
        float destroyDelay = CalculateDestroyDelay();
        if (destroyDelay > 0)
        {
            Invoke(nameof(DestroyEnemy), destroyDelay);
        }
        else
        {
            DestroyEnemy();
        }
    }
    private float CalculateDestroyDelay()
    {
        float delay = 0f;
        if (waitForDeathAnimation)
        {
            delay = Mathf.Max(delay, deathAnimationDuration);
        }
        if (deathDelay > 0)
        {
            delay = Mathf.Max(delay, deathDelay);
        }
        if (delay <= 0 && waitForDeathAnimation)
        {
            delay = deathAnimationDuration;
        }
        return delay;
    }
    private void CreateDeathEffect()
    {
        if (deathEffectPrefab != null)
        {
            GameObject effect = Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
            ParticleSystem particles = effect.GetComponent<ParticleSystem>();
            if (particles != null)
            {
                Destroy(effect, particles.main.duration + particles.main.startLifetime.constantMax);
            }
            else
            {
                Destroy(effect, 3f); 
            }
        }
    }
    private void DestroyEnemy()
    {
        Destroy(gameObject);
    }
    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }
    public void Heal(float healAmount)
    {
        if (isDead || healAmount <= 0)
            return;
        currentHealth += healAmount;
        currentHealth = Mathf.Min(maxHealth, currentHealth);
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        Debug.Log($"{gameObject.name} 恢复 {healAmount} 点血量，当前血量: {currentHealth}/{maxHealth}");
    }
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = newMaxHealth;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
    }
    public void FullHeal()
    {
        if (!isDead)
        {
            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        }
    }
    private void DropItems()
    {
        if (dropItems.Count == 0)
        {
            Debug.Log($"{gameObject.name} 没有配置掉落物品");
            return;
        }
        if (Random.Range(0f, 1f) > dropChance)
        {
            Debug.Log($"{gameObject.name} 掉落概率检查失败，不掉落物品");
            return;
        }
        Item selectedItem = SelectRandomItem();
        if (selectedItem == null)
        {
            Debug.LogWarning($"{gameObject.name} 选择掉落物品失败");
            return;
        }
        int dropAmount = Random.Range(minDropAmount, maxDropAmount + 1);
        if (selectedItem.isStackable)
        {
            dropAmount = Mathf.Min(dropAmount, selectedItem.maxStackSize);
        }
        else
        {
            dropAmount = 1;
        }
        Vector3 dropPosition = transform.position + Vector3.up * 0.5f; 
        GameObject droppedItem = ItemPickup.CreateItemPickup(selectedItem, dropPosition, dropAmount);
        if (droppedItem != null)
        {
            Debug.Log($"{gameObject.name} 掉落了 {dropAmount}x {selectedItem.itemName}");
        }
        else
        {
            Debug.LogError($"{gameObject.name} 创建掉落物品失败");
        }
    }
    private Item SelectRandomItem()
    {
        if (dropItems.Count == 0) return null;
        List<float> weights = new List<float>();
        foreach (Item item in dropItems)
        {
            float weight = GetItemWeight(item);
            weights.Add(weight);
        }
        int selectedIndex = GetWeightedRandomIndex(weights);
        return dropItems[selectedIndex];
    }
    private float GetItemWeight(Item item)
    {
        if (item.itemLevel >= 50)
            return legendaryWeight;
        else if (item.itemLevel >= 30)
            return epicWeight;
        else if (item.itemLevel >= 20)
            return rareWeight;
        else
            return commonWeight;
    }
    private int GetWeightedRandomIndex(List<float> weights)
    {
        float totalWeight = 0f;
        foreach (float weight in weights)
        {
            totalWeight += weight;
        }
        if (totalWeight <= 0f)
        {
            return Random.Range(0, weights.Count);
        }
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            currentWeight += weights[i];
            if (randomValue <= currentWeight)
            {
                return i;
            }
        }
        return weights.Count - 1; 
    }
    private void OnDrawGizmosSelected()
    {
        if (maxHealth > 0)
        {
            Vector3 healthBarPos = transform.position + Vector3.up * 1.5f;
            float healthPercentage = currentHealth / maxHealth;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(healthBarPos - Vector3.right * 0.5f, healthBarPos + Vector3.right * 0.5f);
            Gizmos.color = Color.green;
            Vector3 healthEnd = healthBarPos - Vector3.right * 0.5f + Vector3.right * healthPercentage;
            Gizmos.DrawLine(healthBarPos - Vector3.right * 0.5f, healthEnd);
        }
        if (isMovingEnemy)
        {
            Gizmos.color = isPlayerInRange ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            if (enableSwarmBehavior)
            {
                Gizmos.color = isAlerted ? new Color(1f, 0.5f, 0f, 1f) : new Color(0.5f, 0.5f, 1f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, swarmRadius);
            }
            if (player != null && isPlayerInRange)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, player.position);
            }
            if (hasTarget)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(currentTarget, 0.3f);
                Gizmos.DrawLine(transform.position, currentTarget);
            }
            if (enableSwarmBehavior && isAlerted)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 1f); 
                Vector3 alertPos = transform.position + Vector3.up * 2f;
                Gizmos.DrawWireCube(alertPos, Vector3.one * 0.5f);
            }
        }
    }
    public float GetAttackDamage() => attackDamage;
    public void SetAttackDamage(float damage) => attackDamage = damage;
    public bool IsMovingEnemy() => isMovingEnemy;
    public void SetMovingEnemy(bool moving)
    {
        isMovingEnemy = moving;
        if (!moving && pathfindingCoroutine != null)
        {
            StopCoroutine(pathfindingCoroutine);
            pathfindingCoroutine = null;
            if (rb != null)
            {
                rb.velocity = Vector2.zero;
            }
        }
        else if (moving && !isDead && player != null && pathfindingCoroutine == null)
        {
            pathfindingCoroutine = StartCoroutine(PathfindingUpdate());
        }
    }
}
