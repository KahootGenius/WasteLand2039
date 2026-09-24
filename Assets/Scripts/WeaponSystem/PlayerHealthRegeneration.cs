using UnityEngine;
using System.Collections;

public class PlayerHealthRegeneration : MonoBehaviour
{
    [Header("恢复效果设置")]
    [SerializeField] private bool isRegenerating = false;
    [SerializeField] private float currentHealRate = 0f;
    [SerializeField] private float remainingTime = 0f;
    
    [Header("视觉效果")]
    [SerializeField] private GameObject healingEffectPrefab;
    [SerializeField] private Color healingColor = Color.green;
    
    [Header("音效")]
    [SerializeField] private AudioClip healingStartSound;
    [SerializeField] private AudioClip healingTickSound;
    [SerializeField] private AudioClip healingEndSound;
    
    // 私有变量
    private IDamageable playerHealth;
    private AudioSource audioSource;
    private Coroutine regenerationCoroutine;
    private GameObject currentHealingEffect;
    private SpriteRenderer playerSpriteRenderer;
    private Color originalColor;
    
    // 事件
    public System.Action<float, float> OnRegenerationStarted; // 恢复速率, 持续时间
    public System.Action<float> OnHealthRegenerated; // 恢复的血量
    public System.Action OnRegenerationEnded;
    
    // 属性
    public bool IsRegenerating => isRegenerating;
    public float RemainingTime => remainingTime;
    public float HealRate => currentHealRate;
    
    private void Awake()
    {
        // 获取玩家血量组件
        playerHealth = GetComponent<IDamageable>();
        if (playerHealth == null)
        {
            Debug.LogWarning("PlayerHealthRegeneration: 未找到IDamageable组件");
        }
        
        // 获取音频源
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // 获取精灵渲染器
        playerSpriteRenderer = GetComponent<SpriteRenderer>();
        if (playerSpriteRenderer != null)
        {
            originalColor = playerSpriteRenderer.color;
        }
    }
    
    /// <summary>
    /// 开始生命恢复
    /// </summary>
    /// <param name="healRate">每秒恢复量</param>
    /// <param name="duration">持续时间</param>
    public void StartRegeneration(float healRate, float duration)
    {
        // 如果已经在恢复中，停止当前恢复
        if (isRegenerating)
        {
            StopRegeneration();
        }
        
        currentHealRate = healRate;
        remainingTime = duration;
        isRegenerating = true;
        
        // 播放开始音效
        PlaySound(healingStartSound);
        
        // 创建视觉效果
        CreateHealingEffect();
        
        // 开始恢复协程
        regenerationCoroutine = StartCoroutine(RegenerationCoroutine());
        
        // 触发事件
        OnRegenerationStarted?.Invoke(healRate, duration);
        
        Debug.Log($"开始生命恢复：每秒 {healRate} 点，持续 {duration} 秒");
    }
    
    /// <summary>
    /// 停止生命恢复
    /// </summary>
    public void StopRegeneration()
    {
        if (!isRegenerating)
            return;
            
        isRegenerating = false;
        remainingTime = 0f;
        currentHealRate = 0f;
        
        // 停止协程
        if (regenerationCoroutine != null)
        {
            StopCoroutine(regenerationCoroutine);
            regenerationCoroutine = null;
        }
        
        // 移除视觉效果
        RemoveHealingEffect();
        
        // 播放结束音效
        PlaySound(healingEndSound);
        
        // 触发事件
        OnRegenerationEnded?.Invoke();
        
        Debug.Log("生命恢复效果结束");
    }
    
    /// <summary>
    /// 生命恢复协程
    /// </summary>
    private IEnumerator RegenerationCoroutine()
    {
        while (remainingTime > 0 && isRegenerating)
        {
            // 等待1秒
            yield return new WaitForSeconds(1f);
            
            // 减少剩余时间
            remainingTime -= 1f;
            remainingTime = Mathf.Max(0f, remainingTime);
            
            // 恢复生命值
            if (playerHealth != null && playerHealth.IsAlive())
            {
                // 检查是否已满血
                if (playerHealth.GetCurrentHealth() < playerHealth.GetMaxHealth())
                {
                    playerHealth.Heal(currentHealRate);
                    
                    // 播放恢复音效
                    PlaySound(healingTickSound);
                    
                    // 触发恢复事件
                    OnHealthRegenerated?.Invoke(currentHealRate);
                    
                    // 显示恢复效果
                    ShowHealingFlash();
                    
                    Debug.Log($"恢复 {currentHealRate} 点生命值，剩余时间: {remainingTime} 秒");
                }
                else
                {
                    Debug.Log("生命值已满，停止恢复");
                    break;
                }
            }
            else
            {
                Debug.Log("玩家已死亡，停止恢复");
                break;
            }
        }
        
        // 恢复结束
        StopRegeneration();
    }
    
    /// <summary>
    /// 创建治疗视觉效果
    /// </summary>
    private void CreateHealingEffect()
    {
        if (healingEffectPrefab != null)
        {
            currentHealingEffect = Instantiate(healingEffectPrefab, transform);
            currentHealingEffect.transform.localPosition = Vector3.zero;
        }
    }
    
    /// <summary>
    /// 移除治疗视觉效果
    /// </summary>
    private void RemoveHealingEffect()
    {
        if (currentHealingEffect != null)
        {
            Destroy(currentHealingEffect);
            currentHealingEffect = null;
        }
    }
    
    /// <summary>
    /// 显示治疗闪光效果
    /// </summary>
    private void ShowHealingFlash()
    {
        if (playerSpriteRenderer != null)
        {
            StartCoroutine(HealingFlashCoroutine());
        }
    }
    
    /// <summary>
    /// 治疗闪光协程
    /// </summary>
    private IEnumerator HealingFlashCoroutine()
    {
        // 改变颜色
        playerSpriteRenderer.color = healingColor;
        
        // 等待短暂时间
        yield return new WaitForSeconds(0.2f);
        
        // 恢复原色
        playerSpriteRenderer.color = originalColor;
    }
    
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
    /// 延长恢复时间
    /// </summary>
    /// <param name="additionalTime">额外时间</param>
    public void ExtendRegeneration(float additionalTime)
    {
        if (isRegenerating)
        {
            remainingTime += additionalTime;
            Debug.Log($"延长生命恢复时间 {additionalTime} 秒，总剩余时间: {remainingTime} 秒");
        }
    }
    
    /// <summary>
    /// 增强恢复速率
    /// </summary>
    /// <param name="multiplier">倍数</param>
    public void BoostHealRate(float multiplier)
    {
        if (isRegenerating)
        {
            currentHealRate *= multiplier;
            Debug.Log($"生命恢复速率提升至: {currentHealRate} 点/秒");
        }
    }
    
    /// <summary>
    /// 获取恢复状态信息
    /// </summary>
    /// <returns>状态描述</returns>
    public string GetRegenerationStatus()
    {
        if (isRegenerating)
        {
            return $"生命恢复中: {currentHealRate}/秒，剩余 {remainingTime:F1} 秒";
        }
        else
        {
            return "无生命恢复效果";
        }
    }
    
    private void Update()
    {
        // 可以在这里添加UI更新逻辑
        // 比如更新恢复状态显示等
    }
    
    private void OnDestroy()
    {
        // 清理资源
        if (regenerationCoroutine != null)
        {
            StopCoroutine(regenerationCoroutine);
        }
        
        if (currentHealingEffect != null)
        {
            Destroy(currentHealingEffect);
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        if (isRegenerating)
        {
            // 绘制恢复状态指示
            Gizmos.color = healingColor;
            Vector3 indicatorPos = transform.position + Vector3.up * 1.5f;
            Gizmos.DrawWireCube(indicatorPos, Vector3.one * 0.3f);
            
            // 绘制剩余时间条
            if (remainingTime > 0)
            {
                Vector3 timeBarPos = transform.position + Vector3.up * 2f;
                float timePercentage = remainingTime / 10f; // 假设最大时间为10秒
                
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(timeBarPos - Vector3.right * 0.5f, timeBarPos + Vector3.right * 0.5f);
                
                Gizmos.color = Color.cyan;
                Vector3 timeEnd = timeBarPos - Vector3.right * 0.5f + Vector3.right * timePercentage;
                Gizmos.DrawLine(timeBarPos - Vector3.right * 0.5f, timeEnd);
            }
        }
    }
}

// 扩展IDamageable接口以支持治疗
public static class IDamageableExtensions
{
    public static void Heal(this IDamageable damageable, float healAmount)
    {
        // 这里需要根据具体的IDamageable实现来调用治疗方法
        // 如果IDamageable接口没有Heal方法，可能需要转换为具体类型
        if (damageable is MonoBehaviour mb)
        {
            // 尝试调用Heal方法
            var healMethod = mb.GetType().GetMethod("Heal");
            if (healMethod != null)
            {
                healMethod.Invoke(mb, new object[] { healAmount });
            }
            else
            {
                Debug.LogWarning($"对象 {mb.name} 没有Heal方法");
            }
        }
    }
}