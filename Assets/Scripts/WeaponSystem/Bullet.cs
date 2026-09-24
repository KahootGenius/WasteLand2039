using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Header("子弹设置")]
    [SerializeField] private LayerMask hitLayers = -1; // 可以击中的图层
    [SerializeField] private bool destroyOnHit = true;
    [SerializeField] private GameObject hitEffectPrefab;
    
    // 子弹属性
    private Vector2 direction;
    private float speed;
    private float damage;
    private float lifetime;
    private float maxRange;
    private Vector3 startPosition;
    
    // 组件引用
    private Rigidbody2D rb;
    private Collider2D col;
    private ParticleSystem particles;
    
    // 状态
    private bool hasHit = false;
    
    // 事件
    public System.Action<Bullet, Collider2D> OnHit;
    public System.Action<Bullet> OnDestroy;
    
    private void Awake()
    {
        // 获取组件
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        particles = GetComponent<ParticleSystem>();
        
        // 如果没有刚体，添加一个
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        
        // 配置刚体
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        
        // 如果没有碰撞器，添加一个
        if (col == null)
        {
            col = gameObject.AddComponent<CircleCollider2D>();
            ((CircleCollider2D)col).radius = 0.1f;
        }
        
        // 设置为触发器
        col.isTrigger = true;
    }
    
    /// <summary>
    /// 初始化子弹
    /// </summary>
    /// <param name="fireDirection">发射方向</param>
    /// <param name="bulletSpeed">子弹速度</param>
    /// <param name="bulletDamage">子弹伤害</param>
    /// <param name="bulletLifetime">子弹生存时间</param>
    /// <param name="bulletRange">子弹射程</param>
    public void Initialize(Vector2 fireDirection, float bulletSpeed, float bulletDamage, float bulletLifetime, float bulletRange)
    {
        direction = fireDirection.normalized;
        speed = bulletSpeed;
        damage = bulletDamage;
        lifetime = bulletLifetime;
        maxRange = bulletRange;
        startPosition = transform.position;
        
        // 设置子弹朝向
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        
        // 设置初始速度
        rb.velocity = direction * speed;
        
        // 启动粒子效果
        if (particles != null)
        {
            Debug.Log($"[Bullet] 启动粒子系统 - 粒子系统存在: {particles != null}, 是否启用: {particles.gameObject.activeInHierarchy}");
            particles.Play();
            Debug.Log($"[Bullet] 粒子系统状态 - 正在播放: {particles.isPlaying}, 发射启用: {particles.emission.enabled}, 发射率: {particles.emission.rateOverTime.constant}");
        }
        else
        {
            Debug.LogWarning("[Bullet] 粒子系统组件未找到！");
        }
        
        // 设置自动销毁
        Destroy(gameObject, lifetime);
    }
    
    private void Update()
    {
        // 检查是否超出射程
        if (Vector3.Distance(startPosition, transform.position) >= maxRange)
        {
            DestroyBullet();
        }
    }
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 避免重复碰撞
        if (hasHit)
            return;
            
        // 检查是否在可击中的图层
        if (((1 << other.gameObject.layer) & hitLayers) == 0)
            return;
            
        // 忽略自己的碰撞器
        if (other == col)
            return;
            
        hasHit = true;
        
        // 处理碰撞
        HandleHit(other);
    }
    
    /// <summary>
    /// 处理碰撞
    /// </summary>
    /// <param name="hitCollider">被击中的碰撞器</param>
    private void HandleHit(Collider2D hitCollider)
    {
        // 尝试对目标造成伤害
        IDamageable damageable = hitCollider.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damage);
        }
        
        // 创建击中特效
        CreateHitEffect(transform.position);
        
        // 触发碰撞事件
        OnHit?.Invoke(this, hitCollider);
        
        // 销毁子弹
        if (destroyOnHit)
        {
            DestroyBullet();
        }
    }
    
    /// <summary>
    /// 创建击中特效
    /// </summary>
    /// <param name="position">特效位置</param>
    private void CreateHitEffect(Vector3 position)
    {
        if (hitEffectPrefab != null)
        {
            GameObject effect = Instantiate(hitEffectPrefab, position, Quaternion.identity);
            
            // 自动销毁特效
            ParticleSystem effectParticles = effect.GetComponent<ParticleSystem>();
            if (effectParticles != null)
            {
                Destroy(effect, effectParticles.main.duration + effectParticles.main.startLifetime.constantMax);
            }
            else
            {
                Destroy(effect, 2f); // 默认2秒后销毁
            }
        }
    }
    
    /// <summary>
    /// 销毁子弹
    /// </summary>
    private void DestroyBullet()
    {
        // 停止粒子效果
        if (particles != null)
        {
            particles.Stop();
        }
        
        // 触发销毁事件
        OnDestroy?.Invoke(this);
        
        // 销毁游戏对象
        Destroy(gameObject);
    }
    
    /// <summary>
    /// 设置子弹图层遮罩
    /// </summary>
    /// <param name="layers">图层遮罩</param>
    public void SetHitLayers(LayerMask layers)
    {
        hitLayers = layers;
    }
    
    /// <summary>
    /// 获取子弹伤害
    /// </summary>
    /// <returns>伤害值</returns>
    public float GetDamage()
    {
        return damage;
    }
    
    /// <summary>
    /// 设置子弹伤害
    /// </summary>
    /// <param name="newDamage">新的伤害值</param>
    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }
}

/// <summary>
/// 可受伤害接口
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage);
    float GetCurrentHealth();
    float GetMaxHealth();
    bool IsAlive();
}