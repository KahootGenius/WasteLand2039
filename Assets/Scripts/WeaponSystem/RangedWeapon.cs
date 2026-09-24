using UnityEngine;

[System.Serializable]
public class WeaponStats
{
    [Header("基础属性")]
    public string weaponName = "远程武器";
    public float damage = 10f;
    public float fireRate = 5f; // 每秒射击次数
    public float range = 10f;
    public int magazineSize = 30;
    public float reloadTime = 2f;
    
    [Header("子弹属性")]
    public float bulletSpeed = 20f;
    public float bulletLifetime = 3f;
    
    [Header("精度")]
    public float accuracy = 0.95f; // 1.0 = 完全精确
    public float maxSpread = 5f; // 最大散布角度
}

public class RangedWeapon : MonoBehaviour
{
    [Header("武器配置")]
    [SerializeField] private WeaponStats weaponStats;
    
    [Header("射击点")]
    [SerializeField] private Transform firePoint;
    
    [Header("子弹预制体")]
    [SerializeField] private GameObject bulletPrefab;
    
    [Header("音效")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField] private AudioClip emptySound;
    
    [Header("子弹消耗系统")]
    [SerializeField] private Item bulletItem; // 子弹物品引用
    [SerializeField] private int bulletItemToAmmoRatio = 1; // 1个物品等于多少发子弹
    [SerializeField] private bool requireAmmoFromInventory = true; // 是否需要从背包消耗子弹
    
    // 私有变量
    private float nextFireTime;
    private int currentAmmo;
    private bool isReloading;
    private AudioSource audioSource;
    private InventoryManager inventoryManager; // 背包管理器引用
    
    // 事件
    public System.Action<int, int> OnAmmoChanged; // 当前弹药, 最大弹药
    public System.Action OnReloadStart;
    public System.Action OnReloadComplete;
    public System.Action OnWeaponFired;
    
    // 公共方法用于设置武器属性
    public void SetFirePoint(Transform point)
    {
        firePoint = point;
    }
    
    public void SetBulletPrefab(GameObject prefab)
    {
        bulletPrefab = prefab;
    }
    
    // 属性
    public Transform FirePoint => firePoint;
    public WeaponStats Stats => weaponStats;
    public int CurrentAmmo => currentAmmo;
    public int MaxAmmo => weaponStats.magazineSize;
    public bool IsReloading => isReloading;
    public bool CanFire => !isReloading && currentAmmo > 0 && Time.time >= nextFireTime;
    
    private void Awake()
    {
        // 初始化组件
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // 初始化武器统计数据（如果为空）
        if (weaponStats == null)
        {
            weaponStats = new WeaponStats();
        }
        
        // 获取背包管理器引用
        inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null && requireAmmoFromInventory)
        {
            Debug.LogWarning("[RangedWeapon] InventoryManager instance not found, ammo consumption disabled");
        }
        
        // 初始化弹药
        currentAmmo = weaponStats.magazineSize;
        
        // 初始化射击冷却时间为0，避免游戏开始时的长时间冷却
        nextFireTime = 0f;
        
        // 如果没有设置射击点，创建一个
        if (firePoint == null)
        {
            GameObject firePointObj = new GameObject("FirePoint");
            firePointObj.transform.SetParent(transform);
            firePointObj.transform.localPosition = Vector3.right; // 默认在右侧
            firePoint = firePointObj.transform;
        }
    }
    
    private void Start()
    {
        // 通知UI更新弹药显示
        OnAmmoChanged?.Invoke(currentAmmo, weaponStats.magazineSize);
    }
    
    private void OnEnable()
    {
        // 确保武器激活时射击冷却时间被重置，避免长时间冷却
        nextFireTime = 0f;
        
        // 重置重装状态，防止卡在重装阶段
        if (isReloading)
        {
            Debug.LogWarning("[RangedWeapon] OnEnable时检测到重装状态，强制重置重装状态");
            CancelInvoke(nameof(CompleteReload));
            isReloading = false;
        }
        
        // 额外的安全检查：如果Time.time异常大，强制重置
        if (Time.time > 1000f)
        {
            Debug.LogWarning($"[RangedWeapon] 检测到异常的Time.time值: {Time.time}, 强制重置nextFireTime");
            nextFireTime = 0f;
        }
        
        VerboseLog.Log($"[RangedWeapon] OnEnable - 重置射击冷却时间: {nextFireTime}, 重装状态: {isReloading}, 当前Time.time: {Time.time}");
    }
    
    private void OnDisable()
    {
        // 清理重装状态，防止GameObject被禁用时重装卡死
        if (isReloading)
        {
            Debug.LogWarning("[RangedWeapon] OnDisable时检测到重装状态，清理重装状态");
            CancelInvoke(nameof(CompleteReload));
            CancelInvoke(nameof(ForceCompleteReload));
            isReloading = false;
        }
        
        VerboseLog.Log($"[RangedWeapon] OnDisable - 清理重装状态: {isReloading}");
    }
    
    /// <summary>
    /// 尝试射击
    /// </summary>
    /// <param name="targetDirection">射击方向</param>
    /// <returns>是否成功射击</returns>
    public bool TryFire(Vector2 targetDirection)
    {
        VerboseLog.Log($"[RangedWeapon] TryFire开始 - 目标方向: {targetDirection}, 当前时间: {Time.time}, 下次射击时间: {nextFireTime}, 可以射击: {CanFire}");
        VerboseLog.Log($"[RangedWeapon] 武器状态检查 - fireRate: {weaponStats.fireRate}, 射击间隔: {(1f/weaponStats.fireRate):F6}秒, 弹药: {currentAmmo}, 重装中: {isReloading}");
        
        // 额外的调试信息：检查是否存在异常的nextFireTime值
        if (nextFireTime > Time.time + 10f)
        {
            Debug.LogError($"[RangedWeapon] 检测到异常的nextFireTime值: {nextFireTime}, 当前时间: {Time.time}, 差值: {nextFireTime - Time.time}秒, 武器名: {weaponStats.weaponName}");
            Debug.LogError($"[RangedWeapon] 武器详细信息 - fireRate: {weaponStats.fireRate}, reloadTime: {weaponStats.reloadTime}, 重装状态: {isReloading}");
        }
        
        if (!CanFire)
        {
            if (isReloading)
            {
                VerboseLog.Log("[RangedWeapon] 无法射击：正在重装弹药");
            }
            else if (currentAmmo <= 0)
            {
                VerboseLog.Log("[RangedWeapon] 无法射击：弹药不足");
                PlaySound(emptySound);
            }
            else if (Time.time < nextFireTime)
            {
                float remainingCooldown = nextFireTime - Time.time;
                
                // 安全检查：如果剩余冷却时间超过5秒，说明有问题，立即重置
                if (remainingCooldown > 5f)
                {
                    Debug.LogWarning($"[RangedWeapon] 检测到异常冷却时间: {remainingCooldown}秒, 立即重置为可射击状态");
                    nextFireTime = 0f;
                    return TryFire(targetDirection); // 重新尝试射击
                }
                
                VerboseLog.Log($"[RangedWeapon] 无法射击：冷却中，剩余时间: {remainingCooldown:F6}秒, fireRate: {weaponStats.fireRate}");
            }
            return false;
        }
        
        VerboseLog.Log("[RangedWeapon] 射击条件满足，执行射击");
        Fire(targetDirection);
        return true;
    }
    
    /// <summary>
    /// 执行射击
    /// </summary>
    /// <param name="direction">射击方向</param>
    private void Fire(Vector2 direction)
    {
        VerboseLog.Log($"[RangedWeapon] Fire开始 - 射击前弹药: {currentAmmo}, 射击方向: {direction}");
        
        // 消耗弹药
        currentAmmo--;
        VerboseLog.Log($"[RangedWeapon] 弹药消耗后: {currentAmmo}");
        
        // 设置下次射击时间
        float cooldownTime = 1f / Mathf.Max(weaponStats.fireRate, 0.1f); // 防止除零和异常小的值
        nextFireTime = Time.time + cooldownTime;
        
        // 额外的安全检查：如果计算出的冷却时间超过5秒，重置为5秒
        if (cooldownTime > 5f)
        {
            Debug.LogWarning($"[RangedWeapon] 检测到异常冷却时间: {cooldownTime}秒, fireRate: {weaponStats.fireRate}, 重置为5秒");
            cooldownTime = 5f;
            nextFireTime = Time.time + cooldownTime;
        }
        
        VerboseLog.Log($"[RangedWeapon] 射击冷却计算 - fireRate: {weaponStats.fireRate}, 冷却时间: {cooldownTime:F3}秒, 下次射击时间: {nextFireTime} (当前时间: {Time.time})");
        
        // 计算射击方向（加入精度影响）
        Vector2 fireDirection = CalculateFireDirection(direction);
        VerboseLog.Log($"[RangedWeapon] 计算后的射击方向: {fireDirection}");
        
        // 创建子弹
        CreateBullet(firePoint.position, fireDirection);
        
        // 播放射击音效
        PlaySound(fireSound);
        
        // 触发事件
        OnWeaponFired?.Invoke();
        OnAmmoChanged?.Invoke(currentAmmo, weaponStats.magazineSize);
        
        VerboseLog.Log($"[RangedWeapon] Fire完成 - 剩余弹药: {currentAmmo}");
        
        // 如果弹药用完，自动重装
        if (currentAmmo <= 0)
        {
            VerboseLog.Log("[RangedWeapon] 弹药用完，开始自动重装");
            StartReload();
        }
    }
    
    /// <summary>
    /// 计算射击方向（考虑精度）
    /// </summary>
    /// <param name="baseDirection">基础方向</param>
    /// <returns>最终射击方向</returns>
    private Vector2 CalculateFireDirection(Vector2 baseDirection)
    {
        // 如果精度为1，直接返回基础方向
        if (weaponStats.accuracy >= 1f)
        {
            return baseDirection.normalized;
        }
        
        // 计算散布角度
        float spreadAngle = weaponStats.maxSpread * (1f - weaponStats.accuracy);
        float randomAngle = Random.Range(-spreadAngle, spreadAngle);
        
        // 应用散布
        float angle = Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg;
        angle += randomAngle;
        
        return new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
    }
    
    /// <summary>
    /// 创建子弹
    /// </summary>
    /// <param name="position">发射位置</param>
    /// <param name="direction">发射方向</param>
    private void CreateBullet(Vector3 position, Vector2 direction)
    {
        VerboseLog.Log($"[RangedWeapon] CreateBullet - 位置: {position}, 方向: {direction}");
        
        if (bulletPrefab == null)
        {
            Debug.LogError("[RangedWeapon] 子弹预制体未设置！");
            return;
        }
        
        VerboseLog.Log($"[RangedWeapon] 使用子弹预制体: {bulletPrefab.name}");
        
        GameObject bullet = Instantiate(bulletPrefab, position, Quaternion.identity);
        VerboseLog.Log($"[RangedWeapon] 子弹实例化成功: {bullet.name}");
        
        Bullet bulletComponent = bullet.GetComponent<Bullet>();
        
        if (bulletComponent != null)
        {
            VerboseLog.Log($"[RangedWeapon] 初始化子弹 - 速度: {weaponStats.bulletSpeed}, 伤害: {weaponStats.damage}, 生命时间: {weaponStats.bulletLifetime}");
            bulletComponent.Initialize(direction, weaponStats.bulletSpeed, weaponStats.damage, weaponStats.bulletLifetime, weaponStats.range);
            VerboseLog.Log("[RangedWeapon] 子弹初始化完成");
        }
        else
        {
            Debug.LogError("[RangedWeapon] 子弹预制体缺少Bullet组件！");
        }
    }
    
    /// <summary>
    /// 开始重装弹药
    /// </summary>
    public void StartReload()
    {
        VerboseLog.Log($"[RangedWeapon] StartReload - 当前弹药: {currentAmmo}, 最大弹药: {weaponStats.magazineSize}, 重装中: {isReloading}");
        
        if (isReloading)
        {
            VerboseLog.Log("[RangedWeapon] 已经在重装中，强制停止当前重装并重新开始");
            // 强制停止当前重装
            CancelInvoke(nameof(CompleteReload));
            CancelInvoke(nameof(ForceCompleteReload));
            isReloading = false;
        }
        
        if (currentAmmo >= weaponStats.magazineSize)
        {
            VerboseLog.Log("[RangedWeapon] 弹匣已满，无需重装");
            return;
        }
        
        // 检查是否需要消耗背包中的子弹
        if (requireAmmoFromInventory && !CanReloadFromInventory())
        {
            VerboseLog.Log("[RangedWeapon] 背包中没有足够的子弹，无法重装");
            PlaySound(emptySound);
            return;
        }
        
        VerboseLog.Log($"[RangedWeapon] 开始重装，重装时间: {weaponStats.reloadTime}秒");
        isReloading = true;
        PlaySound(reloadSound);
        OnReloadStart?.Invoke();
        
        // 取消之前的重装Invoke（如果有的话）
        CancelInvoke(nameof(CompleteReload));
        CancelInvoke(nameof(ForceCompleteReload));
        
        // 延迟完成重装
        float reloadTime = Mathf.Max(weaponStats.reloadTime, 0.1f); // 确保重装时间至少0.1秒
        Invoke(nameof(CompleteReload), reloadTime);
        
        // 添加安全超时检查，防止Invoke失效
        float safetyTimeout = reloadTime + 1f; // 比正常重装时间多1秒
        Invoke(nameof(ForceCompleteReload), safetyTimeout);
    }
    
    /// <summary>
    /// 完成重装
    /// </summary>
    private void CompleteReload()
    {
        VerboseLog.Log($"[RangedWeapon] 重装完成 - 弹药从 {currentAmmo} 恢复到 {weaponStats.magazineSize}");
        
        // 取消安全超时检查
        CancelInvoke(nameof(ForceCompleteReload));
        
        // 如果需要消耗背包中的子弹，执行消耗
        if (requireAmmoFromInventory)
        {
            ConsumeAmmoFromInventory();
        }
        
        isReloading = false;
        currentAmmo = weaponStats.magazineSize;
        
        OnReloadComplete?.Invoke();
        OnAmmoChanged?.Invoke(currentAmmo, weaponStats.magazineSize);
        
        VerboseLog.Log("[RangedWeapon] 重装完成，可以继续射击");
    }
    
    /// <summary>
    /// 强制完成重装（安全超时机制）
    /// </summary>
    private void ForceCompleteReload()
    {
        if (isReloading)
        {
            Debug.LogWarning("[RangedWeapon] 检测到重装超时，强制完成重装");
            
            // 取消正常的重装完成调用
            CancelInvoke(nameof(CompleteReload));
            
            // 如果需要消耗背包中的子弹，执行消耗
            if (requireAmmoFromInventory)
            {
                ConsumeAmmoFromInventory();
            }
            
            isReloading = false;
            currentAmmo = weaponStats.magazineSize;
            
            OnReloadComplete?.Invoke();
            OnAmmoChanged?.Invoke(currentAmmo, weaponStats.magazineSize);
            
            VerboseLog.Log("[RangedWeapon] 强制重装完成，可以继续射击");
        }
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
    /// 设置武器统计数据
    /// </summary>
    /// <param name="newStats">新的武器数据</param>
    public void SetWeaponStats(WeaponStats newStats)
    {
        if (newStats == null)
        {
            Debug.LogError("[RangedWeapon] SetWeaponStats: newStats不能为null");
            return;
        }
        
        // 如果正在重装，取消重装
        if (isReloading)
        {
            Debug.LogWarning("[RangedWeapon] SetWeaponStats时检测到重装状态，取消重装");
            CancelInvoke(nameof(CompleteReload));
            isReloading = false;
        }
        
        weaponStats = newStats;
        // 重新初始化弹药为满弹匣
        currentAmmo = weaponStats.magazineSize;
        // 重置射击冷却时间，确保武器设置后立即可以射击
        nextFireTime = 0f;
        OnAmmoChanged?.Invoke(currentAmmo, weaponStats.magazineSize);
        
        VerboseLog.Log($"[RangedWeapon] 武器统计数据已设置: {weaponStats.weaponName}, 弹匣大小: {weaponStats.magazineSize}, fireRate: {weaponStats.fireRate}, 射击间隔: {(1f/weaponStats.fireRate):F3}秒, 射击冷却重置");
    }
    
    #region 子弹消耗系统
    
    /// <summary>
    /// 检查是否可以从背包重装
    /// </summary>
    /// <returns>是否有足够的子弹</returns>
    private bool CanReloadFromInventory()
    {
        if (!requireAmmoFromInventory || bulletItem == null || inventoryManager == null)
        {
            return true; // 如果不需要消耗子弹或没有设置，总是允许重装
        }
        
        // 计算需要多少个子弹物品
        int neededAmmo = weaponStats.magazineSize - currentAmmo;
        int neededItems = Mathf.CeilToInt((float)neededAmmo / bulletItemToAmmoRatio);
        
        bool hasEnough = inventoryManager.HasItem(bulletItem, neededItems);
        VerboseLog.Log($"[RangedWeapon] 检查子弹 - 需要弹药: {neededAmmo}, 需要物品: {neededItems}, 背包中有足够: {hasEnough}");
        
        return hasEnough;
    }
    
    /// <summary>
    /// 从背包消耗子弹
    /// </summary>
    private void ConsumeAmmoFromInventory()
    {
        if (!requireAmmoFromInventory || bulletItem == null || inventoryManager == null)
        {
            return; // 如果不需要消耗子弹或没有设置，直接返回
        }
        
        // 计算需要消耗多少个子弹物品
        int neededAmmo = weaponStats.magazineSize - currentAmmo;
        int itemsToConsume = Mathf.CeilToInt((float)neededAmmo / bulletItemToAmmoRatio);
        
        if (inventoryManager.RemoveItem(bulletItem, itemsToConsume))
        {
            VerboseLog.Log($"[RangedWeapon] 消耗了 {itemsToConsume}x {bulletItem.itemName} 来重装 {neededAmmo} 发子弹");
        }
        else
        {
            Debug.LogWarning($"[RangedWeapon] 无法消耗 {itemsToConsume}x {bulletItem.itemName}");
        }
    }
    
    /// <summary>
    /// 设置子弹物品
    /// </summary>
    /// <param name="item">子弹物品</param>
    /// <param name="ratio">1个物品等于多少发子弹</param>
    public void SetBulletItem(Item item, int ratio = 1)
    {
        bulletItem = item;
        bulletItemToAmmoRatio = Mathf.Max(1, ratio);
        VerboseLog.Log($"[RangedWeapon] 设置子弹物品: {(item != null ? item.itemName : "null")}, 比例: 1:{bulletItemToAmmoRatio}");
    }
    
    /// <summary>
    /// 设置是否需要从背包消耗子弹
    /// </summary>
    /// <param name="require">是否需要</param>
    public void SetRequireAmmoFromInventory(bool require)
    {
        requireAmmoFromInventory = require;
        VerboseLog.Log($"[RangedWeapon] 设置子弹消耗需求: {requireAmmoFromInventory}");
    }
    
    /// <summary>
    /// 获取子弹物品信息
    /// </summary>
    /// <returns>子弹物品信息字符串</returns>
    public string GetBulletItemInfo()
    {
        if (!requireAmmoFromInventory)
        {
            return "Ammo consumption disabled";
        }
        
        if (bulletItem == null)
        {
            return "No bullet item set";
        }
        
        int availableItems = inventoryManager != null ? inventoryManager.GetItemAmount(bulletItem) : 0;
        int availableAmmo = availableItems * bulletItemToAmmoRatio;
        
        return $"Bullet: {bulletItem.itemName} (1:{bulletItemToAmmoRatio}) - Available: {availableItems} items ({availableAmmo} rounds)";
    }
    
    #endregion
}