using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 尸潮生成器 - 专门处理尸潮事件的敌人生成
/// 
/// ⚠️ 重要说明：此生成器现在完全依赖HordeEvent ScriptableObject配置！
/// 所有生成参数（敌人数量、生成间隔、敌人类型等）都从HordeEvent获取。
/// Inspector中的旧配置字段已被标记为废弃，仅作为备用配置。
/// 
/// 使用方法：
/// 1. 创建HordeEvent ScriptableObject并配置参数
/// 2. 在DayManager中按天数顺序添加HordeEvent配置
/// 3. 调用StartHorde(int day, HordeEvent hordeEvent)开始尸潮
/// </summary>
public class HordeSpawner : EnemySpawner
{
#if UNITY_EDITOR
    [Header("尸潮设置 (已废弃 - 使用HordeEvent配置)")]
    [SerializeField] private AnimationCurve hordeIntensityCurve = AnimationCurve.EaseInOut(0, 5, 30, 50); // 仅作为备用
    [SerializeField] private AnimationCurve hordeRadiusCurve = AnimationCurve.EaseInOut(0, 15, 30, 25); // 仅作为备用
    [SerializeField] private AnimationCurve spawnRateCurve = AnimationCurve.EaseInOut(0, 1, 30, 0.3f); // 仅作为备用
    [SerializeField] private float hordeSpawnInterval = 0.5f; // 仅作为备用
    [SerializeField] private int maxSimultaneousEnemies = 20; // 仅作为备用
#else
    // 运行时不需要这些字段
    private AnimationCurve hordeIntensityCurve;
    private AnimationCurve hordeRadiusCurve;
    private AnimationCurve spawnRateCurve;
    private float hordeSpawnInterval;
    private int maxSimultaneousEnemies;
#endif
    
    [Header("尸潮类型 (已废弃 - 使用HordeEvent配置)")]
    [SerializeField] private HordeType[] hordeTypes; // 仅作为备用配置
    
    // 状态
    private bool isHordeActive = false;
    private int currentHordeDay = 1;
    private int targetEnemyCount = 0;
    private int spawnedEnemyCount = 0;
    private float hordeIntensity = 1f;
    private float hordeTimer = 0f;
    
    // 尸潮生成协程
    private Coroutine hordeSpawningCoroutine;
    
    [System.Serializable]
    public class HordeType
    {
        public string name;
        public GameObject[] enemyPrefabs; // 该类型尸潮的敌人类型
        public int[] enemyWeights; // 敌人类型权重
        public float minDay; // 最小出现天数
        public float maxDay; // 最大出现天数
        public Color hordeColor = Color.red; // UI显示颜色
        [TextArea(3, 5)]
        public string description; // 描述
    }
    
    /// <summary>
    /// 开始尸潮（支持HordeEvent配置）
    /// </summary>
    public void StartHorde(int day, HordeEvent hordeEvent = null)
    {
        if (isHordeActive) return;
        
        currentHordeDay = day;
        isHordeActive = true;
        hordeTimer = 0f;
        spawnedEnemyCount = 0;
        
        if (hordeEvent != null)
        {
            // 使用HordeEvent配置
            StartHordeWithEvent(hordeEvent, day);
        }
        else
        {
            // 使用默认配置
            StartHordeDefault(day);
        }
    }
    
    /// <summary>
    /// 使用HordeEvent配置开始尸潮
    /// </summary>
    private void StartHordeWithEvent(HordeEvent hordeEvent, int day)
    {
        // 从HordeEvent获取配置
        targetEnemyCount = hordeEvent.totalEnemies;
        spawnRadius = hordeEvent.spawnRadiusSize;
        spawnInterval = hordeEvent.spawnIntervalTime;
        hordeIntensity = hordeEvent.intensity;
        
        Debug.Log($"[HordeSpawner] 第 {day} 天尸潮开始 - 使用HordeEvent配置 - 强度: {hordeIntensity:F2}, 目标敌人: {targetEnemyCount}, 生成半径: {spawnRadius:F1}m");
        
        // 停止普通生成
        StopSpawning();
        
        // 开始尸潮生成
        if (hordeSpawningCoroutine != null)
            StopCoroutine(hordeSpawningCoroutine);
        
        hordeSpawningCoroutine = StartCoroutine(HordeSpawningCoroutineWithEvent(hordeEvent));
        
        // 触发尸潮开始事件
        OnHordeStarted();
    }
    
    /// <summary>
    /// 使用默认配置开始尸潮（仅当没有HordeEvent配置时使用）
    /// </summary>
    private void StartHordeDefault(int day)
    {
        Debug.LogWarning($"[HordeSpawner] 第 {day} 天没有HordeEvent配置，使用备用默认配置。建议创建对应的HordeEvent ScriptableObject！");
        
        // 计算尸潮强度参数
        hordeIntensity = hordeIntensityCurve.Evaluate(day);
        targetEnemyCount = Mathf.RoundToInt(hordeIntensity);
        spawnRadius = hordeRadiusCurve.Evaluate(day);
        spawnInterval = spawnRateCurve.Evaluate(day);
        
        Debug.Log($"[HordeSpawner] 第 {day} 天尸潮开始 - 使用默认配置 - 强度: {hordeIntensity:F2}, 目标敌人: {targetEnemyCount}, 生成半径: {spawnRadius:F1}m");
        
        // 停止普通生成
        StopSpawning();
        
        // 开始尸潮生成
        if (hordeSpawningCoroutine != null)
            StopCoroutine(hordeSpawningCoroutine);
        
        hordeSpawningCoroutine = StartCoroutine(HordeSpawningCoroutine());
        
        // 触发尸潮开始事件
        OnHordeStarted();
    }
    
    /// <summary>
    /// 结束尸潮
    /// </summary>
    public void EndHorde()
    {
        if (!isHordeActive) return;
        
        isHordeActive = false;
        
        if (hordeSpawningCoroutine != null)
        {
            StopCoroutine(hordeSpawningCoroutine);
            hordeSpawningCoroutine = null;
        }
        
        Debug.Log($"[HordeSpawner] 第 {currentHordeDay} 天尸潮结束 - 生成了 {spawnedEnemyCount} 个敌人");
        
        // 触发尸潮结束事件
        OnHordeEnded();
        
        // 恢复普通生成（如果有配置）
        if (autoStart && spawnInterval > 0)
        {
            StartSpawning();
        }
    }
    
    /// <summary>
    /// 尸潮生成协程（默认配置）
    /// </summary>
    private IEnumerator HordeSpawningCoroutine()
    {
        while (isHordeActive && spawnedEnemyCount < targetEnemyCount)
        {
            // 检查当前敌人数量
            if (GetActiveEnemyCount() < maxSimultaneousEnemies)
            {
                // 尝试生成敌人
                if (TrySpawnHordeEnemy())
                {
                    spawnedEnemyCount++;
                    Debug.Log($"[HordeSpawner] 尸潮生成进度: {spawnedEnemyCount}/{targetEnemyCount}");
                }
            }
            
            yield return new WaitForSeconds(hordeSpawnInterval);
        }
        
        // 等待所有敌人被消灭或超时
        while (isHordeActive && GetActiveEnemyCount() > 0)
        {
            yield return new WaitForSeconds(1f);
        }
        
        // 自动结束尸潮
        if (isHordeActive)
        {
            EndHorde();
        }
    }
    
    /// <summary>
    /// 尸潮生成协程（使用HordeEvent配置）
    /// </summary>
    private IEnumerator HordeSpawningCoroutineWithEvent(HordeEvent hordeEvent)
    {
        while (isHordeActive && spawnedEnemyCount < targetEnemyCount)
        {
            // 检查当前敌人数量
            if (GetActiveEnemyCount() < hordeEvent.maxSimultaneousEnemies)
            {
                // 尝试生成敌人
                if (TrySpawnHordeEnemyWithEvent(hordeEvent))
                {
                    spawnedEnemyCount++;
                    Debug.Log($"[HordeSpawner] 尸潮生成进度: {spawnedEnemyCount}/{targetEnemyCount}");
                }
            }
            
            yield return new WaitForSeconds(hordeEvent.spawnIntervalTime);
        }
        
        // 等待所有敌人被消灭或超时
        while (isHordeActive && GetActiveEnemyCount() > 0)
        {
            yield return new WaitForSeconds(1f);
        }
        
        // 自动结束尸潮
        if (isHordeActive)
        {
            EndHorde();
        }
    }
    
    /// <summary>
    /// 尝试生成尸潮敌人（默认配置）
    /// </summary>
    private bool TrySpawnHordeEnemy()
    {
        if (player == null) return false;
        
        // 获取适合当前天数的尸潮类型
        HordeType validHordeType = GetValidHordeType();
        if (validHordeType == null || validHordeType.enemyPrefabs.Length == 0)
        {
            Debug.LogWarning("[HordeSpawner] 没有找到有效的尸潮类型");
            return false;
        }
        
        // 生成位置（使用环形生成）
        Vector3 spawnPosition = GetHordeSpawnPosition();
        
        // 验证位置
        if (!IsValidSpawnPosition(spawnPosition))
        {
            return false;
        }
        
        // 选择敌人类型（基于权重）
        GameObject enemyPrefab = SelectEnemyFromHorde(validHordeType);
        if (enemyPrefab == null)
        {
            return false;
        }
        
        // 生成敌人
        GameObject newEnemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
        spawnedEnemies.Add(newEnemy);
        
        // 设置敌人属性（根据天数调整）
        SetupHordeEnemy(newEnemy, currentHordeDay);
        
        return true;
    }
    
    /// <summary>
    /// 尝试生成尸潮敌人（使用HordeEvent配置）
    /// </summary>
    private bool TrySpawnHordeEnemyWithEvent(HordeEvent hordeEvent)
    {
        if (player == null) return false;
        
        // 从HordeEvent获取敌人类型和权重
        GameObject[] enemyPrefabs = hordeEvent.GetEnemyPrefabs();
        int[] enemyWeights = hordeEvent.GetEnemyWeights();
        
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
        {
            Debug.LogWarning("[HordeSpawner] HordeEvent中没有配置敌人类型");
            return false;
        }
        
        // 生成位置（使用HordeEvent配置的距离）
        Vector3 spawnPosition = GetHordeSpawnPositionWithEvent(hordeEvent);
        
        // 验证位置
        if (!IsValidSpawnPosition(spawnPosition))
        {
            return false;
        }
        
        // 选择敌人类型（基于HordeEvent的权重）
        GameObject enemyPrefab = SelectEnemyFromHordeEvent(enemyPrefabs, enemyWeights);
        if (enemyPrefab == null)
        {
            return false;
        }
        
        // 生成敌人
        GameObject newEnemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
        spawnedEnemies.Add(newEnemy);
        
        // 设置敌人属性（根据HordeEvent配置）
        SetupHordeEnemyWithEvent(newEnemy, hordeEvent);
        
        return true;
    }
    
    /// <summary>
    /// 获取适合当前天数的尸潮类型
    /// </summary>
    private HordeType GetValidHordeType()
    {
        List<HordeType> validTypes = new List<HordeType>();
        
        foreach (HordeType hordeType in hordeTypes)
        {
            if (currentHordeDay >= hordeType.minDay && currentHordeDay <= hordeType.maxDay)
            {
                validTypes.Add(hordeType);
            }
        }
        
        if (validTypes.Count == 0)
        {
            return null;
        }
        
        // 随机选择一个有效的尸潮类型
        return validTypes[Random.Range(0, validTypes.Count)];
    }
    
    /// <summary>
    /// 从尸潮类型中选择敌人
    /// </summary>
    private GameObject SelectEnemyFromHorde(HordeType hordeType)
    {
        if (hordeType.enemyWeights == null || hordeType.enemyWeights.Length != hordeType.enemyPrefabs.Length)
        {
            // 如果没有权重配置，随机选择
            return hordeType.enemyPrefabs[Random.Range(0, hordeType.enemyPrefabs.Length)];
        }
        
        // 基于权重选择
        int totalWeight = 0;
        foreach (int weight in hordeType.enemyWeights)
        {
            totalWeight += weight;
        }
        
        int randomWeight = Random.Range(0, totalWeight);
        int currentWeight = 0;
        
        for (int i = 0; i < hordeType.enemyPrefabs.Length; i++)
        {
            currentWeight += hordeType.enemyWeights[i];
            if (randomWeight < currentWeight)
            {
                return hordeType.enemyPrefabs[i];
            }
        }
        
        return hordeType.enemyPrefabs[0];
    }
    
    /// <summary>
    /// 获取尸潮生成位置（环形生成）
    /// </summary>
    private Vector3 GetHordeSpawnPosition()
    {
        if (player == null)
            return transform.position;
        
        // 在玩家周围环形生成
        float angle = Random.Range(0f, 360f);
        float distance = Random.Range(spawnRadius * 0.7f, spawnRadius);
        
        Vector3 direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0);
        Vector3 spawnPosition = player.transform.position + direction * distance;
        
        return spawnPosition;
    }
    
    /// <summary>
    /// 获取尸潮生成位置（使用HordeEvent配置）
    /// </summary>
    private Vector3 GetHordeSpawnPositionWithEvent(HordeEvent hordeEvent)
    {
        if (player == null)
            return transform.position;
        
        // 在玩家周围环形生成（使用HordeEvent配置的距离）
        float angle = Random.Range(0f, 360f);
        float minDistance = hordeEvent.spawnRadius * 0.7f;
        float maxDistance = hordeEvent.spawnRadius;
        float distance = Random.Range(minDistance, maxDistance);
        
        Vector3 direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0);
        Vector3 spawnPosition = player.transform.position + direction * distance;
        
        return spawnPosition;
    }
    
    /// <summary>
    /// 从HordeEvent选择敌人
    /// </summary>
    private GameObject SelectEnemyFromHordeEvent(GameObject[] enemyPrefabs, int[] enemyWeights)
    {
        if (enemyWeights == null || enemyWeights.Length != enemyPrefabs.Length)
        {
            // 如果没有权重配置，随机选择
            return enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
        }
        
        // 基于权重选择
        int totalWeight = 0;
        foreach (int weight in enemyWeights)
        {
            totalWeight += weight;
        }
        
        int randomWeight = Random.Range(0, totalWeight);
        int currentWeight = 0;
        
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            currentWeight += enemyWeights[i];
            if (randomWeight < currentWeight)
            {
                return enemyPrefabs[i];
            }
        }
        
        return enemyPrefabs[0];
    }
    
    /// <summary>
    /// 设置尸潮敌人属性（默认配置）
    /// </summary>
    private void SetupHordeEnemy(GameObject enemy, int day)
    {
        // 这里可以添加敌人属性增强逻辑
        // 例如：增加生命值、攻击力、速度等
        
        Enemy enemyComponent = enemy.GetComponent<Enemy>();
        if (enemyComponent != null)
        {
            // 根据天数增加敌人生命值
            float healthMultiplier = 1f + (day - 1) * 0.1f;
            float originalMaxHealth = enemyComponent.GetMaxHealth();
            float newMaxHealth = originalMaxHealth * healthMultiplier;
            enemyComponent.SetMaxHealth(newMaxHealth);
            // 使用TakeDamage的负数来恢复血量
            float healAmount = newMaxHealth - enemyComponent.GetCurrentHealth();
            if (healAmount > 0)
            {
                // 使用反射来设置私有字段
                System.Reflection.FieldInfo currentHealthField = typeof(Enemy).GetField("currentHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (currentHealthField != null)
                {
                    currentHealthField.SetValue(enemyComponent, newMaxHealth);
                    enemyComponent.OnHealthChanged?.Invoke(enemyComponent, newMaxHealth, newMaxHealth);
                }
            }
        }
        
        // 可以添加更多属性调整...
    }
    
    /// <summary>
    /// 设置尸潮敌人属性（使用HordeEvent配置）
    /// </summary>
    private void SetupHordeEnemyWithEvent(GameObject enemy, HordeEvent hordeEvent)
    {
        Enemy enemyComponent = enemy.GetComponent<Enemy>();
        if (enemyComponent != null)
        {
            // 根据HordeEvent的强度增加敌人生命值
            float healthMultiplier = hordeEvent.healthMultiplier;
            float originalMaxHealth = enemyComponent.GetMaxHealth();
            float newMaxHealth = originalMaxHealth * healthMultiplier;
            enemyComponent.SetMaxHealth(newMaxHealth);
            
            // 恢复血量到最大值
            float healAmount = newMaxHealth - enemyComponent.GetCurrentHealth();
            if (healAmount > 0)
            {
                // 使用反射来设置私有字段
                System.Reflection.FieldInfo currentHealthField = typeof(Enemy).GetField("currentHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (currentHealthField != null)
                {
                    currentHealthField.SetValue(enemyComponent, newMaxHealth);
                    enemyComponent.OnHealthChanged?.Invoke(enemyComponent, newMaxHealth, newMaxHealth);
                }
            }
            
            // 可以添加更多属性调整，如速度、攻击力等
            if (enemyComponent.TryGetComponent(out Rigidbody2D rb))
            {
                // 根据HordeEvent调整移动速度
                rb.velocity = rb.velocity * hordeEvent.speedMultiplier;
            }
        }
        
        // 可以添加特殊效果、奖励等
        if (hordeEvent.hasSpecialEffects)
        {
            // 添加特殊效果逻辑
            Debug.Log($"[HordeSpawner] 为敌人添加了特殊效果");
        }
    }
    
    /// <summary>
    /// 尸潮开始事件
    /// </summary>
    private void OnHordeStarted()
    {
        Debug.Log($"[HordeSpawner] 尸潮开始事件触发");
        
        // 可以添加音效、特效等
    }
    
    /// <summary>
    /// 尸潮结束事件
    /// </summary>
    private void OnHordeEnded()
    {
        Debug.Log($"[HordeSpawner] 尸潮结束事件触发");
        
        // 可以添加奖励、音效等
    }
    
    /// <summary>
    /// 获取活跃敌人数量
    /// </summary>
    public int GetActiveEnemyCount()
    {
        int count = 0;
        
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            if (spawnedEnemies[i] != null)
            {
                count++;
            }
            else
            {
                spawnedEnemies.RemoveAt(i);
            }
        }
        
        return count;
    }
    
    /// <summary>
    /// 获取尸潮进度（0-1）
    /// </summary>
    public float GetHordeProgress()
    {
        if (!isHordeActive) return 0f;
        
        if (targetEnemyCount <= 0) return 1f;
        
        return (float)spawnedEnemyCount / targetEnemyCount;
    }
    
    /// <summary>
    /// 获取当前尸潮信息
    /// </summary>
    public string GetHordeInfo()
    {
        if (!isHordeActive) return "无尸潮";
        
        return $"第{currentHordeDay}天尸潮 - 进度: {spawnedEnemyCount}/{targetEnemyCount}";
    }
    
    /// <summary>
    /// 是否处于尸潮中
    /// </summary>
    public bool IsHordeActive()
    {
        return isHordeActive;
    }
    
    /// <summary>
    /// 获取当前尸潮类型
    /// </summary>
    public HordeType GetCurrentHordeType()
    {
        return GetValidHordeType();
    }
}