using UnityEngine;

/// <summary>
/// 训练场的基地：替代 MainBase（MainBase 被摧毁时会把 Time.timeScale 设为 0，暂停所有训练场）。
/// 只记录受到的伤害，血量归零也不会结束游戏；每波开始时由 ArenaEnvironment 重置。
/// 碰撞体尺寸与 MainGame 的基地相同，敌人在基地周围的行为一致。
/// </summary>
[DisallowMultipleComponent]
public class ArenaBase : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 1000f;

    private float currentHealth;

    /// <summary>本波累计受到的伤害</summary>
    public float DamageTaken { get; private set; }
    /// <summary>本波被攻击次数</summary>
    public int Hits { get; private set; }

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f)
            return;

        DamageTaken += damage;
        Hits++;
        currentHealth = Mathf.Max(0f, currentHealth - damage);
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
        return currentHealth > 0f;
    }

    public void ResetBase()
    {
        currentHealth = maxHealth;
        DamageTaken = 0f;
        Hits = 0;
    }
}
