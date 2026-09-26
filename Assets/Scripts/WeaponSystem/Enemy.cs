using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Enemy : MonoBehaviour, IDamageable
{
    [Header("敌人类型设置")]
    [SerializeField] private bool isMovingEnemy = true; // 是否为会动的敌人
    
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
    [SerializeField] private List<Item> dropItems = new List<Item>(); // 可掉落的物品列表
    [SerializeField] private float dropChance = 0.8f; // 掉落概率 (0-1)
    [SerializeField] private int minDropAmount = 1; // 最小掉落数量
    [SerializeField] private int maxDropAmount = 3; // 最大掉落数量
    
    [Header("掉落稀有度权重")]
    [SerializeField] private float commonWeight = 50f; // 普通物品权重
    [SerializeField] private float rareWeight = 25f; // 稀有物品权重
    [SerializeField] private float epicWeight = 15f; // 史诗物品权重
    [SerializeField] private float legendaryWeight = 10f; // 传说物品权重
    
    [Header("移动和AI设置")]
    [SerializeField] private float moveSpeed = 3f; // 移动速度
    [SerializeField] private float detectionRadius = 5f; // 玩家检测半径
    [SerializeField] private float attackRange = 1.5f; // 攻击范围
    [SerializeField] private float attackCooldown = 2f; // 攻击冷却时间
    [SerializeField] private float attackDamage = 20f; // 攻击伤害
    [SerializeField] private LayerMask obstacleLayerMask = -1; // 障碍物图层
    [SerializeField] private LayerMask playerLayerMask = 1; // 玩家图层
    
    [Header("寻路设置")]
    [SerializeField] private float pathUpdateInterval = 0.5f; // 路径更新间隔
    [SerializeField] private float stuckThreshold = 0.1f; // 卡住检测阈值
    [SerializeField] private float stuckTime = 2f; // 卡住时间阈值
    
    [Header("死亡动画设置")]
    [SerializeField] private float deathAnimationDuration = 1f; // 死亡动画持续时间
    [SerializeField] private bool waitForDeathAnimation = true; // 是否等待死亡动画播放完毕
    
    [Header("集群意识设置")]
    [SerializeField] private bool enableSwarmBehavior = true; // 是否启用集群行为
    [SerializeField] private float swarmRadius = 8f; // 集群通信范围
    [SerializeField] private int maxSwarmPropagation = 2; // 最大传播次数
    [SerializeField] private LayerMask enemyLayerMask = -1; // 敌人图层遮罩
    
    [Header("音效")]
    [SerializeField] private AudioClip damageSound;
    [SerializeField] private AudioClip deathSound;
    [SerializeField] private AudioClip attackSound;
    
    // 私有变量
    private Color originalColor;
    private AudioSource audioSource;
    private bool isDead = false;
    
    // AI相关变量
    private Transform player;
    private Transform mainBase; // 主基地目标
    private Rigidbody2D rb;
    private Animator animator;
    private bool isPlayerInRange = false;
    private bool isMainBaseInRange = false; // 是否在基地范围内
    private bool isAttacking = false;
    private float lastAttackTime;
    private Vector3 lastPosition;
    private float stuckTimer;
    private Coroutine pathfindingCoroutine;
    private Vector2 currentTarget;
    private bool hasTarget = false;
    private bool targetingMainBase = false; // 是否正在攻击基地
    
    // 动画和朝向相关
    private bool isFacingRight = true;
    private Vector2 lastMoveDirection = Vector2.right;
    
    // 集群行为相关
    private bool isAlerted = false; // 是否处于警戒状态
    private int currentPropagationLevel = 0; // 当前传播层级
    private float lastSwarmCheckTime = 0f; // 上次集群检查时间
    private const float SWARM_CHECK_INTERVAL = 0.5f; // 集群检查间隔
    
    // 动画参数名称常量
    private readonly string ANIM_IS_MOVING = "IsMoving";
    private readonly string ANIM_MOVE_X = "MoveX";
    private readonly string ANIM_MOVE_Y = "MoveY";
    private readonly string ANIM_ATTACK = "Attack";
    private readonly string ANIM_DEATH = "Death";
    private readonly string ANIM_FACING_RIGHT = "FacingRight";
    
    // 事件
    public System.Action<Enemy> OnDeath;
    public System.Action<Enemy, float> OnDamageTaken; // 敌人, 伤害值
    public System.Action<Enemy, float, float> OnHealthChanged; // 敌人, 当前血量, 最大血量
    
    // 属性
    public bool IsDead => isDead;
    
    // 碰撞检测相关
    private BoxCollider2D boxCollider2D;
    
    private void Awake()
    {
        // 获取组件
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // 获取AI相关组件
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        
        // 如果没有Rigidbody2D，添加一个
        if (rb == null && isMovingEnemy)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f; // 2D顶视角游戏不需要重力
            rb.freezeRotation = true; // 防止旋转
        }
        
        // 初始化血量
        currentHealth = maxHealth;
        
        // 保存原始颜色
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        
        // 初始化位置记录
        lastPosition = transform.position;
        
        // 获取Box Collider 2D组件
        boxCollider2D = GetComponent<BoxCollider2D>();
    }
    
    private void Start()
    {
        // 通知血量变化
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        
        // 如果是移动敌人，启动AI
        if (isMovingEnemy)
        {
            // 查找玩家和主基地
            FindPlayer();
            FindMainBase();
            
            // 启动寻路协程
            if (player != null || mainBase != null)
            {
                pathfindingCoroutine = StartCoroutine(PathfindingUpdate());
            }
        }
        
        // 自动设置死亡动画时长
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
            
        // 减少血量
        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);
        
        // 播放受伤音效
        PlaySound(damageSound);
        
        // 显示受伤效果
        ShowDamageEffect();
        
        // 触发事件
        OnDamageTaken?.Invoke(this, damage);
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        
        // 检查是否死亡
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
        // 只有移动敌人才执行AI逻辑
        if (!isMovingEnemy || isDead)
            return;
            
        // 检测目标（玩家优先，然后是基地）
        DetectTargets();
        
        // 处理集群行为
        if (enableSwarmBehavior)
        {
            HandleSwarmBehavior();
        }
        
        // 处理攻击
        HandleAttack();
        
        // 检测是否卡住
        CheckIfStuck();
    }
    
    private void FixedUpdate()
    {
        // 只有移动敌人才移动
        if (!isMovingEnemy || isDead || isAttacking)
            return;
            
        // 移动向目标
        MoveTowardsTarget();
    }
    
    #region AI系统
    
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
        
        Debug.LogWarning($"{gameObject.name}: 未找到玩家对象，请确保玩家对象有'Player'标签或PlayerController组件");
    }
    
    /// <summary>
    /// 查找主基地对象
    /// </summary>
    private void FindMainBase()
    {
        // 首先尝试通过标签查找
        GameObject baseObj = GameObject.FindGameObjectWithTag("MainBase");
        if (baseObj != null)
        {
            mainBase = baseObj.transform;
            return;
        }
        
        // 如果没有找到，尝试查找MainBase组件
        MainBase mainBaseComponent = FindObjectOfType<MainBase>();
        if (mainBaseComponent != null)
        {
            mainBase = mainBaseComponent.transform;
            return;
        }
        
        Debug.LogWarning($"{gameObject.name}: 未找到主基地对象，请确保主基地对象有'MainBase'标签或MainBase组件");
    }
    
    /// <summary>
    /// 检测目标（玩家优先，然后是基地）
    /// </summary>
    private void DetectTargets()
    {
        // 首先检测玩家
        DetectPlayer();
        
        // 如果没有检测到玩家，检测主基地
        if (!isPlayerInRange)
        {
            DetectMainBase();
        }
        else
        {
            // 如果检测到玩家，停止攻击基地
            if (targetingMainBase)
            {
                targetingMainBase = false;
                isMainBaseInRange = false;
                Debug.Log($"{gameObject.name} 发现玩家，停止攻击基地");
            }
        }
    }
    
    /// <summary>
    /// 检测玩家是否在范围内
    /// </summary>
    private void DetectPlayer()
    {
        if (player == null)
        {
            FindPlayer();
            return;
        }
        
        float distanceToPlayer = Vector2.Distance(transform.position, player.position);
        bool wasInRange = isPlayerInRange;
        
        // 检查是否在检测范围内
        isPlayerInRange = distanceToPlayer <= detectionRadius;
        
        // 如果玩家进入或离开范围，更新目标
        if (isPlayerInRange != wasInRange)
        {
            if (isPlayerInRange)
            {
                currentTarget = player.position;
                hasTarget = true;
                targetingMainBase = false; // 停止攻击基地
                
                // 触发集群警报
                if (enableSwarmBehavior && !isAlerted)
                {
                    TriggerSwarmAlert(0); // 从传播层级0开始
                }
                
                Debug.Log($"{gameObject.name} 检测到玩家，开始追击");
            }
            else
            {
                // 玩家离开范围，检查是否应该攻击基地
                if (!CheckForMainBaseTarget())
                {
                    hasTarget = false;
                    isAlerted = false; // 重置警戒状态
                    currentPropagationLevel = 0; // 重置传播层级
                    
                    // 停止移动
                    if (rb != null)
                    {
                        rb.velocity = Vector2.zero;
                    }
                    UpdateMovementAnimation(Vector2.zero, false);
                }
                Debug.Log($"{gameObject.name} 玩家离开检测范围");
            }
        }
        
        // 如果玩家在范围内，持续更新目标位置
        if (isPlayerInRange)
        {
            currentTarget = player.position;
            hasTarget = true;
        }
    }
    
    /// <summary>
    /// 检测主基地是否在范围内
    /// </summary>
    private void DetectMainBase()
    {
        if (mainBase == null)
        {
            FindMainBase();
            return;
        }
        
        // 移动敌人无视检测距离，直接攻击基地
        if (isMovingEnemy && !isPlayerInRange)
        {
            float distanceToBase = Vector2.Distance(transform.position, mainBase.position);
            bool wasInRange = isMainBaseInRange;
            
            // 对于基地，我们使用更大的检测范围或者无限制
            isMainBaseInRange = true; // 移动敌人总是能"看到"基地
            
            if (!wasInRange && isMainBaseInRange)
            {
                currentTarget = mainBase.position;
                hasTarget = true;
                targetingMainBase = true;
                
                Debug.Log($"{gameObject.name} 开始攻击主基地");
            }
            
            // 如果正在攻击基地，持续更新目标位置
            if (targetingMainBase)
            {
                currentTarget = mainBase.position;
                hasTarget = true;
            }
        }
    }
    
    /// <summary>
    /// 检查是否应该攻击主基地
    /// </summary>
    /// <returns>是否开始攻击基地</returns>
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
    
    /// <summary>
    /// 寻路更新协程
    /// </summary>
    private IEnumerator PathfindingUpdate()
    {
        while (!isDead)
        {
            if (hasTarget)
            {
                Vector2 targetPosition;
                Vector2 directionToTarget;
                float distanceToTarget;
                
                // 确定目标位置（玩家优先）
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
                
                // 使用射线检测是否有障碍物
                RaycastHit2D hit = Physics2D.Raycast(transform.position, directionToTarget, distanceToTarget, obstacleLayerMask);
                
                if (hit.collider == null)
                {
                    // 没有障碍物，直接向目标移动
                    currentTarget = targetPosition;
                }
                else
                {
                    // 有障碍物，尝试绕过
                    Vector2 avoidanceTarget = FindAvoidancePath(hit.point, directionToTarget);
                    currentTarget = avoidanceTarget;
                }
            }
            
            yield return new WaitForSeconds(pathUpdateInterval);
        }
    }
    
    /// <summary>
    /// 寻找避障路径
    /// </summary>
    private Vector2 FindAvoidancePath(Vector2 obstaclePoint, Vector2 originalDirection)
    {
        // 尝试左右两个方向绕过障碍物
        Vector2 leftDirection = new Vector2(-originalDirection.y, originalDirection.x);
        Vector2 rightDirection = new Vector2(originalDirection.y, -originalDirection.x);
        
        float checkDistance = 2f;
        
        // 检查左侧路径
        Vector2 leftTarget = (Vector2)transform.position + leftDirection * checkDistance;
        RaycastHit2D leftHit = Physics2D.Raycast(transform.position, leftDirection, checkDistance, obstacleLayerMask);
        
        // 检查右侧路径
        Vector2 rightTarget = (Vector2)transform.position + rightDirection * checkDistance;
        RaycastHit2D rightHit = Physics2D.Raycast(transform.position, rightDirection, checkDistance, obstacleLayerMask);
        
        // 选择没有障碍物的方向
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
            // 两边都没有障碍物，选择更接近玩家的方向
            float leftDistance = Vector2.Distance(leftTarget, player.position);
            float rightDistance = Vector2.Distance(rightTarget, player.position);
            return leftDistance < rightDistance ? leftTarget : rightTarget;
        }
        
        // 如果两边都有障碍物，返回当前位置
        return transform.position;
    }
    
    /// <summary>
    /// 向目标移动
    /// </summary>
    private void MoveTowardsTarget()
    {
        if (!hasTarget || rb == null)
        {
            // 停止移动时更新动画
            UpdateMovementAnimation(Vector2.zero, false);
            return;
        }
            
        Vector2 direction = (currentTarget - (Vector2)transform.position).normalized;
        float distanceToTarget = Vector2.Distance(transform.position, currentTarget);
        
        // 如果距离目标很近，停止移动
        if (distanceToTarget < 0.1f)
        {
            rb.velocity = Vector2.zero;
            UpdateMovementAnimation(Vector2.zero, false);
            return;
        }
        
        // 如果在攻击范围内，停止移动
        if ((isPlayerInRange || targetingMainBase) && distanceToTarget <= attackRange)
        {
            rb.velocity = Vector2.zero;
            UpdateMovementAnimation(Vector2.zero, false);
            return;
        }
        
        // 移动
        rb.velocity = direction * moveSpeed;
        
        // 更新移动动画和朝向
        UpdateMovementAnimation(direction, true);
        UpdateFacing(direction);
    }
    
    /// <summary>
    /// 处理攻击逻辑
    /// </summary>
    private void HandleAttack()
    {
        if (isAttacking)
            return;
            
        // 优先攻击玩家
        if (isPlayerInRange && player != null)
        {
            float distanceToPlayer = Vector2.Distance(transform.position, player.position);
            if (distanceToPlayer <= attackRange && Time.time >= lastAttackTime + attackCooldown)
            {
                StartCoroutine(PerformAttack(player, "玩家"));
            }
        }
        // 如果没有玩家，攻击基地
        else if (targetingMainBase && mainBase != null)
        {
            float distanceToBase = Vector2.Distance(transform.position, mainBase.position);
            if (distanceToBase <= attackRange && Time.time >= lastAttackTime + attackCooldown)
            {
                StartCoroutine(PerformAttack(mainBase, "主基地"));
            }
        }
    }
    
    /// <summary>
    /// 执行攻击
    /// </summary>
    /// <param name="target">攻击目标</param>
    /// <param name="targetName">目标名称</param>
    private IEnumerator PerformAttack(Transform target, string targetName)
    {
        isAttacking = true;
        lastAttackTime = Time.time;
        
        // 停止移动
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
        
        // 播放攻击动画
        if (animator != null)
        {
            animator.SetTrigger(ANIM_ATTACK);
            animator.SetBool(ANIM_IS_MOVING, false);
        }
        
        // 播放攻击音效
        PlaySound(attackSound);
        
        // 等待攻击动画播放（可以根据动画长度调整）
        yield return new WaitForSeconds(0.5f);
        
        // 检查目标是否仍在攻击范围内
        if (target != null)
        {
            float distanceToTarget = Vector2.Distance(transform.position, target.position);
            if (distanceToTarget <= attackRange)
            {
                // 对目标造成伤害
                IDamageable targetDamageable = target.GetComponent<IDamageable>();
                if (targetDamageable != null)
                {
                    targetDamageable.TakeDamage(attackDamage);
                    Debug.Log($"{gameObject.name} 攻击了{targetName}，造成 {attackDamage} 点伤害");
                }
            }
        }
        
        // 攻击结束
        isAttacking = false;
    }
    
    /// <summary>
    /// 检查是否卡住
    /// </summary>
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
                // 尝试脱困移动
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
    
    /// <summary>
    /// 更新移动动画
    /// </summary>
    /// <param name="direction">移动方向</param>
    /// <param name="isMoving">是否在移动</param>
    private void UpdateMovementAnimation(Vector2 direction, bool isMoving)
    {
        if (animator == null)
            return;
            
        // 设置移动状态
        animator.SetBool(ANIM_IS_MOVING, isMoving);
        
        if (isMoving)
        {
            // 设置移动方向参数
            animator.SetFloat(ANIM_MOVE_X, direction.x);
            animator.SetFloat(ANIM_MOVE_Y, direction.y);
            
            // 记录最后的移动方向
            lastMoveDirection = direction;
        }
        else
        {
            // 停止移动时，保持最后的方向参数
            animator.SetFloat(ANIM_MOVE_X, 0f);
            animator.SetFloat(ANIM_MOVE_Y, 0f);
        }
    }
    
    /// <summary>
    /// 更新朝向
    /// </summary>
    /// <param name="direction">移动方向</param>
    private void UpdateFacing(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) > 0.1f)
        {
            bool shouldFaceRight = direction.x > 0;
            
            if (shouldFaceRight != isFacingRight)
            {
                isFacingRight = shouldFaceRight;
                FlipSprite();
                
                // 如果有朝向参数，也可以设置给Animator
                if (animator != null)
                {
                    animator.SetBool(ANIM_FACING_RIGHT, isFacingRight);
                }
            }
        }
    }
    
    /// <summary>
    /// 翻转精灵
    /// </summary>
    private void FlipSprite()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = !isFacingRight;
        }
    }
    
    /// <summary>
    /// 设置朝向（供外部调用）
    /// </summary>
    /// <param name="faceRight">是否朝向右侧</param>
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
    
    /// <summary>
    /// 获取当前朝向
    /// </summary>
    /// <returns>是否朝向右侧</returns>
    public bool IsFacingRight()
    {
        return isFacingRight;
    }
    
    /// <summary>
    /// 手动触发攻击动画（供外部调用）
    /// </summary>
    public void TriggerAttackAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(ANIM_ATTACK);
        }
    }
    
    /// <summary>
    /// 手动触发死亡动画（供外部调用）
    /// </summary>
    public void TriggerDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(ANIM_DEATH);
            animator.SetBool(ANIM_IS_MOVING, false);
        }
    }
    
    /// <summary>
    /// 获取死亡动画的实际长度
    /// </summary>
    /// <returns>动画长度，如果获取失败返回设置的默认值</returns>
    public float GetDeathAnimationLength()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            // 尝试获取死亡动画片段的长度
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            foreach (AnimationClip clip in clips)
            {
                if (clip.name.ToLower().Contains("death") || clip.name.ToLower().Contains("die"))
                {
                    return clip.length;
                }
            }
        }
        
        // 如果无法获取，返回设置的默认值
        return deathAnimationDuration;
    }
    
    /// <summary>
    /// 自动设置死亡动画持续时间
    /// </summary>
    public void AutoSetDeathAnimationDuration()
    {
        float actualLength = GetDeathAnimationLength();
        if (actualLength > 0)
        {
            deathAnimationDuration = actualLength;
            Debug.Log($"{gameObject.name} 自动设置死亡动画时长为: {deathAnimationDuration}秒");
        }
    }
    
    /// <summary>
    /// 立即销毁敌人（跳过死亡动画）
    /// </summary>
    public void ForceDestroy()
    {
        // 取消所有延迟调用
        CancelInvoke();
        
        // 立即销毁
        DestroyEnemy();
    }
    
    /// <summary>
    /// 处理集群行为
    /// </summary>
    private void HandleSwarmBehavior()
    {
        // 限制检查频率
        if (Time.time - lastSwarmCheckTime < SWARM_CHECK_INTERVAL)
            return;
            
        lastSwarmCheckTime = Time.time;
        
        // 如果已经在追击玩家，不需要额外处理
        if (isPlayerInRange)
            return;
            
        // 检查附近是否有警戒状态的敌人
        CheckNearbyAlertedEnemies();
    }
    
    /// <summary>
    /// 检查附近警戒状态的敌人
    /// </summary>
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
                // 如果附近有警戒的敌人且玩家存在，加入追击
                if (nearbyEnemy.player != null && !isAlerted)
                {
                    JoinSwarmChase(nearbyEnemy.player, nearbyEnemy.currentPropagationLevel + 1);
                    break;
                }
            }
        }
    }
    
    /// <summary>
    /// 触发集群警报
    /// </summary>
    /// <param name="propagationLevel">传播层级</param>
    public void TriggerSwarmAlert(int propagationLevel)
    {
        if (propagationLevel > maxSwarmPropagation || isAlerted)
            return;
            
        isAlerted = true;
        currentPropagationLevel = propagationLevel;
        
        Debug.Log($"{gameObject.name} 收到集群警报，传播层级: {propagationLevel}");
        
        // 向附近的敌人传播警报
        PropagateSwarmAlert(propagationLevel + 1);
    }
    
    /// <summary>
    /// 传播集群警报
    /// </summary>
    /// <param name="nextLevel">下一层级</param>
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
    
    /// <summary>
    /// 加入集群追击
    /// </summary>
    /// <param name="targetPlayer">目标玩家</param>
    /// <param name="propagationLevel">传播层级</param>
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
        
        // 继续传播警报
        PropagateSwarmAlert(propagationLevel + 1);
    }
    
    /// <summary>
    /// 获取集群状态信息
    /// </summary>
    /// <returns>集群状态描述</returns>
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
    
    /// <summary>
    /// 当玩家进入触发器时调用
    /// </summary>
    /// <param name="other">进入的碰撞体</param>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 如果是静止敌人且进入的是玩家，阻止玩家进入
        if (!isMovingEnemy && other.CompareTag("Player"))
        {
            PreventPlayerEntry(other);
        }
    }
    
    /// <summary>
    /// 当玩家停留在触发器内时持续调用
    /// </summary>
    /// <param name="other">停留的碰撞体</param>
    private void OnTriggerStay2D(Collider2D other)
    {
        // 如果是静止敌人且停留的是玩家，持续阻止玩家进入
        if (!isMovingEnemy && other.CompareTag("Player"))
        {
            PreventPlayerEntry(other);
        }
    }
    
    /// <summary>
    /// 阻止玩家进入敌人的碰撞箱
    /// </summary>
    /// <param name="playerCollider">玩家的碰撞体</param>
    private void PreventPlayerEntry(Collider2D playerCollider)
    {
        if (boxCollider2D == null || playerCollider == null)
            return;
            
        // 获取玩家的Rigidbody2D
        Rigidbody2D playerRb = playerCollider.GetComponent<Rigidbody2D>();
        if (playerRb == null)
            return;
            
        // 计算从敌人中心到玩家的方向
        Vector2 directionToPlayer = (playerCollider.transform.position - transform.position).normalized;
        
        // 计算敌人碰撞箱的边界
        Bounds enemyBounds = boxCollider2D.bounds;
        Bounds playerBounds = playerCollider.bounds;
        
        // 计算推出玩家的位置
        Vector2 pushDirection = Vector2.zero;
        float pushDistance = 0f;
        
        // 计算重叠距离
        float overlapX = Mathf.Min(enemyBounds.max.x, playerBounds.max.x) - Mathf.Max(enemyBounds.min.x, playerBounds.min.x);
        float overlapY = Mathf.Min(enemyBounds.max.y, playerBounds.max.y) - Mathf.Max(enemyBounds.min.y, playerBounds.min.y);
        
        // 选择重叠较小的轴进行推出
        if (overlapX < overlapY)
        {
            // 水平推出
            pushDirection = new Vector2(Mathf.Sign(directionToPlayer.x), 0);
            pushDistance = overlapX + 0.1f; // 添加小的缓冲距离
        }
        else
        {
            // 垂直推出
            pushDirection = new Vector2(0, Mathf.Sign(directionToPlayer.y));
            pushDistance = overlapY + 0.1f; // 添加小的缓冲距离
        }
        
        // 计算目标位置
        Vector2 targetPosition = (Vector2)playerCollider.transform.position + pushDirection * pushDistance;
        
        // 平滑移动玩家到目标位置
        playerRb.MovePosition(Vector2.Lerp(playerCollider.transform.position, targetPosition, Time.fixedDeltaTime * 10f));
        
        // 可选：添加调试信息
        Debug.Log($"静止敌人 {gameObject.name} 阻止玩家进入碰撞箱");
    }
    
    #endregion
    
    /// <summary>
     /// 显示受伤效果
     /// </summary>
     private void ShowDamageEffect()
    {
        if (spriteRenderer != null)
        {
            // 取消之前的颜色变化
            CancelInvoke(nameof(ResetColor));
            
            // 改变颜色
            spriteRenderer.color = damageColor;
            
            // 延迟恢复原色
            Invoke(nameof(ResetColor), damageFlashDuration);
        }
    }
    
    /// <summary>
    /// 重置颜色
    /// </summary>
    private void ResetColor()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }
    
    /// <summary>
    /// 死亡处理
    /// </summary>
    private void Die()
    {
        if (isDead)
            return;
            
        isDead = true;
        
        // 停止AI协程
        if (pathfindingCoroutine != null)
        {
            StopCoroutine(pathfindingCoroutine);
            pathfindingCoroutine = null;
        }
        
        // 停止移动
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
        
        // 播放死亡动画
        if (animator != null)
        {
            animator.SetTrigger(ANIM_DEATH);
            animator.SetBool(ANIM_IS_MOVING, false);
        }
        
        // 播放死亡音效
        PlaySound(deathSound);
        
        // 创建死亡特效
        CreateDeathEffect();
        
        // 掉落物品
        DropItems();
        
        // 触发死亡事件
        OnDeath?.Invoke(this);
        
        Debug.Log($"{gameObject.name} 死亡");
        
        // 计算销毁延迟时间
        float destroyDelay = CalculateDestroyDelay();
        
        // 延迟销毁
        if (destroyDelay > 0)
        {
            Invoke(nameof(DestroyEnemy), destroyDelay);
        }
        else
        {
            DestroyEnemy();
        }
    }
    
    /// <summary>
    /// 计算销毁延迟时间
    /// </summary>
    /// <returns>延迟时间</returns>
    private float CalculateDestroyDelay()
    {
        float delay = 0f;
        
        // 如果设置了等待死亡动画
        if (waitForDeathAnimation)
        {
            delay = Mathf.Max(delay, deathAnimationDuration);
        }
        
        // 如果设置了死亡延迟
        if (deathDelay > 0)
        {
            delay = Mathf.Max(delay, deathDelay);
        }
        
        // 如果两者都没有设置，使用默认的动画时间
        if (delay <= 0 && waitForDeathAnimation)
        {
            delay = deathAnimationDuration;
        }
        
        return delay;
    }
    
    /// <summary>
    /// 创建死亡特效
    /// </summary>
    private void CreateDeathEffect()
    {
        if (deathEffectPrefab != null)
        {
            GameObject effect = Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
            
            // 自动销毁特效
            ParticleSystem particles = effect.GetComponent<ParticleSystem>();
            if (particles != null)
            {
                Destroy(effect, particles.main.duration + particles.main.startLifetime.constantMax);
            }
            else
            {
                Destroy(effect, 3f); // 默认3秒后销毁
            }
        }
    }
    
    /// <summary>
    /// 销毁敌人
    /// </summary>
    private void DestroyEnemy()
    {
        Destroy(gameObject);
    }

    // ========== 动画事件 ==========
    // 001Z 的动画片段带有下面三个事件，没有接收方法时每次攻击 / 死亡都会在控制台报 "has no receiver"。
    // 命中和攻击结束由 PerformAttack 协程处理，销毁由 Die 按死亡动画时长安排（CalculateDestroyDelay），
    // 所以这些方法不做任何事，行为不变。

    /// <summary>动画事件（001Z_Attack）：攻击命中帧。伤害由 PerformAttack 协程结算</summary>
    private void OnAttackHit() { }

    /// <summary>动画事件（001Z_Attack）：攻击动画结束。攻击状态由 PerformAttack 协程管理</summary>
    private void OnAttackComplete() { }

    /// <summary>动画事件（001Z_Death）：死亡动画结束。销毁已由 Die 按死亡动画时长安排</summary>
    private void OnDeathComplete() { }
    
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
    /// 治疗
    /// </summary>
    /// <param name="healAmount">治疗量</param>
    public void Heal(float healAmount)
    {
        if (isDead || healAmount <= 0)
            return;
            
        currentHealth += healAmount;
        currentHealth = Mathf.Min(maxHealth, currentHealth);
        
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        
        Debug.Log($"{gameObject.name} 恢复 {healAmount} 点血量，当前血量: {currentHealth}/{maxHealth}");
    }
    
    /// <summary>
    /// 设置最大血量
    /// </summary>
    /// <param name="newMaxHealth">新的最大血量</param>
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = newMaxHealth;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
    }
    
    /// <summary>
    /// 完全恢复血量
    /// </summary>
    public void FullHeal()
    {
        if (!isDead)
        {
            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(this, currentHealth, maxHealth);
        }
    }
    
    /// <summary>
    /// 掉落物品
    /// </summary>
    private void DropItems()
    {
        // 检查是否有可掉落的物品
        if (dropItems.Count == 0)
        {
            Debug.Log($"{gameObject.name} 没有配置掉落物品");
            return;
        }
        
        // 根据掉落概率决定是否掉落
        if (Random.Range(0f, 1f) > dropChance)
        {
            Debug.Log($"{gameObject.name} 掉落概率检查失败，不掉落物品");
            return;
        }
        
        // 选择随机物品
        Item selectedItem = SelectRandomItem();
        if (selectedItem == null)
        {
            Debug.LogWarning($"{gameObject.name} 选择掉落物品失败");
            return;
        }
        
        // 确定掉落数量
        int dropAmount = Random.Range(minDropAmount, maxDropAmount + 1);
        if (selectedItem.isStackable)
        {
            dropAmount = Mathf.Min(dropAmount, selectedItem.maxStackSize);
        }
        else
        {
            dropAmount = 1;
        }
        
        // 在敌人位置生成掉落物品
        Vector3 dropPosition = transform.position + Vector3.up * 0.5f; // 稍微向上偏移
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
    
    /// <summary>
    /// 根据稀有度权重选择随机物品
    /// </summary>
    private Item SelectRandomItem()
    {
        if (dropItems.Count == 0) return null;
        
        // 创建权重列表
        List<float> weights = new List<float>();
        
        foreach (Item item in dropItems)
        {
            float weight = GetItemWeight(item);
            weights.Add(weight);
        }
        
        // 根据权重选择物品
        int selectedIndex = GetWeightedRandomIndex(weights);
        return dropItems[selectedIndex];
    }
    
    /// <summary>
    /// 获取物品权重
    /// </summary>
    private float GetItemWeight(Item item)
    {
        // 根据物品等级确定稀有度
        if (item.itemLevel >= 50)
            return legendaryWeight;
        else if (item.itemLevel >= 30)
            return epicWeight;
        else if (item.itemLevel >= 20)
            return rareWeight;
        else
            return commonWeight;
    }
    
    /// <summary>
    /// 根据权重获取随机索引
    /// </summary>
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
        
        return weights.Count - 1; // fallback
    }
    
    private void OnDrawGizmosSelected()
    {
        // 绘制血量条
        if (maxHealth > 0)
        {
            Vector3 healthBarPos = transform.position + Vector3.up * 1.5f;
            float healthPercentage = currentHealth / maxHealth;
            
            // 背景
            Gizmos.color = Color.red;
            Gizmos.DrawLine(healthBarPos - Vector3.right * 0.5f, healthBarPos + Vector3.right * 0.5f);
            
            // 当前血量
            Gizmos.color = Color.green;
            Vector3 healthEnd = healthBarPos - Vector3.right * 0.5f + Vector3.right * healthPercentage;
            Gizmos.DrawLine(healthBarPos - Vector3.right * 0.5f, healthEnd);
        }
        
        // 只有移动敌人才绘制AI相关的Gizmos
        if (isMovingEnemy)
        {
            // 绘制检测范围
            Gizmos.color = isPlayerInRange ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            
            // 绘制攻击范围
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            
            // 绘制集群通信范围
            if (enableSwarmBehavior)
            {
                Gizmos.color = isAlerted ? new Color(1f, 0.5f, 0f, 1f) : new Color(0.5f, 0.5f, 1f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, swarmRadius);
            }
            
            // 绘制到玩家的连线
            if (player != null && isPlayerInRange)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, player.position);
            }
            
            // 绘制当前目标
            if (hasTarget)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(currentTarget, 0.3f);
                Gizmos.DrawLine(transform.position, currentTarget);
            }
            
            // 绘制集群状态指示
            if (enableSwarmBehavior && isAlerted)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 1f); // 橙色
                Vector3 alertPos = transform.position + Vector3.up * 2f;
                Gizmos.DrawWireCube(alertPos, Vector3.one * 0.5f);
            }
        }
    }
    
    
    
    /// <summary>
    /// 获取攻击伤害
    /// </summary>
    public float GetAttackDamage() => attackDamage;
    
    /// <summary>
    /// 设置攻击伤害
    /// </summary>
    public void SetAttackDamage(float damage) => attackDamage = damage;
    
    /// <summary>
    /// 获取敌人是否为移动类型
    /// </summary>
    public bool IsMovingEnemy() => isMovingEnemy;
    
    /// <summary>
    /// 设置敌人移动类型
    /// </summary>
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