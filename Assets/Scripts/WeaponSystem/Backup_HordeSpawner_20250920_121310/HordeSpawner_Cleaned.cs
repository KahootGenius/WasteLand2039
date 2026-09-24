using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 尸潮生成器 - 清理版本（完全移除废弃配置）
/// 
/// ⚠️ 此版本完全移除了所有废弃的Inspector配置字段！
/// 仅在没有HordeEvent配置时提供基本默认设置。
/// </summary>
public class HordeSpawner_Cleaned : EnemySpawner
{
    // 状态变量
    private bool isHordeActive = false;
    private int currentHordeDay = 1;
    private int targetEnemyCount = 0;
    private int spawnedEnemyCount = 0;
    private float hordeIntensity = 1f;
    private float hordeTimer = 0f;
    
    // 尸潮生成协程
    private Coroutine hordeSpawningCoroutine;
    
    /// <summary>
    /// 开始尸潮（完全依赖HordeEvent配置）
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
            // 使用极简默认配置
            StartHordeMinimalDefault(day);
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
    /// 极简默认配置（仅当没有HordeEvent配置时使用）
    /// </summary>
    private void StartHordeMinimalDefault(int day)
    {
        Debug.LogWarning($"[HordeSpawner] 第 {day} 天没有HordeEvent配置，使用极简默认配置。强烈建议创建对应的HordeEvent ScriptableObject！");
        
        // 极简默认设置
        targetEnemyCount = 10 + (day * 2); // 基础10个敌人，每天增加2个
        spawnRadius = 15f; // 固定15米半径
        spawnInterval = 1f; // 固定1秒间隔
        hordeIntensity = 1f + (day * 0.1f); // 基础强度1，每天增加0.1
        
        Debug.Log($"[HordeSpawner] 第 {day} 天尸潮开始 - 使用极简默认配置 - 强度: {hordeIntensity:F2}, 目标敌人: {targetEnemyCount}, 生成半径: {spawnRadius:F1}m");
        
        // 停止普通生成
        StopSpawning();
        
        // 开始尸潮生成
        if (hordeSpawningCoroutine != null)
            StopCoroutine(hordeSpawningCoroutine);
        
        hordeSpawningCoroutine = StartCoroutine(HordeSpawningCoroutineMinimal());
        
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
    /// 极简默认生成协程
    /// </summary>
    private IEnumerator HordeSpawningCoroutineMinimal()
    {
        int maxSimultaneous = 5; // 固定最大同时存在5个敌人
        
        while (isHordeActive && spawnedEnemyCount < targetEnemyCount)
        {
            // 检查当前敌人数量
            if (GetActiveEnemyCount() < maxSimultaneous)
            {
                // 尝试生成敌人（使用基础敌人类型）
                if (TrySpawnBasicEnemy())
                {
                    spawnedEnemyCount++;
                    Debug.Log($"[HordeSpawner] 尸潮生成进度: {spawnedEnemyCount}/{targetEnemyCount}");
                }
            }
            
            yield return new WaitForSeconds(spawnInterval);
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
    /// 尝试生成基础敌人（极简默认版本）
    /// </summary>
    private bool TrySpawnBasicEnemy()
    {
        // 获取生成位置
        Vector3 spawnPosition = GetHordeSpawnPosition();
        
        // 验证生成位置
        if (!IsValidSpawnPosition(spawnPosition))
            return false;
        
        // 获取基础敌人类型（如果没有配置则使用第一个可用的）
        GameObject enemyPrefab = GetBasicEnemyPrefab();
        if (enemyPrefab == null)
        {
            Debug.LogError("[HordeSpawner] 没有找到基础敌人类型！");
            return false;
        }
        
        // 生成敌人
        GameObject enemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
        
        // 设置基础属性
        SetupBasicEnemy(enemy);
        
        return true;
    }
    
    /// <summary>
    /// 获取基础敌人类型
    /// </summary>
    private GameObject GetBasicEnemyPrefab()
    {
        // 使用反射访问基类的受保护字段
        System.Reflection.FieldInfo enemyPrefabsField = typeof(EnemySpawner).GetField("enemyPrefabs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (enemyPrefabsField != null)
        {
            GameObject[] baseEnemyPrefabs = enemyPrefabsField.GetValue(this) as GameObject[];
            if (baseEnemyPrefabs != null && baseEnemyPrefabs.Length > 0 && baseEnemyPrefabs[0] != null)
            {
                return baseEnemyPrefabs[0];
            }
        }
        
        Debug.LogError("[HordeSpawner] 没有配置基础敌人类型！请确保Enemy Prefabs数组至少有一个敌人类型。");
        return null;
    }
    
    /// <summary>
    /// 设置基础敌人属性
    /// </summary>
    private void SetupBasicEnemy(GameObject enemy)
    {
        // 基础设置，不依赖复杂配置
        if (enemy != null)
        {
            // 确保敌人有正确的标签和层
            enemy.tag = "Enemy";
            enemy.layer = LayerMask.NameToLayer("Enemy");
            
            // 基础生命值设置 - 使用Enemy组件的方法
            Enemy enemyComponent = enemy.GetComponent<Enemy>();
            if (enemyComponent != null)
            {
                float baseHealth = 50f + (currentHordeDay * 10f); // 基础50血，每天增加10
                enemyComponent.SetMaxHealth(baseHealth);
                
                // 使用反射来设置当前生命值
                System.Reflection.FieldInfo currentHealthField = typeof(Enemy).GetField("currentHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (currentHealthField != null)
                {
                    currentHealthField.SetValue(enemyComponent, baseHealth);
                    enemyComponent.OnHealthChanged?.Invoke(enemyComponent, baseHealth, baseHealth);
                }
            }
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
    /// 获取尸潮状态信息
    /// </summary>
    public bool IsHordeActive()
    {
        return isHordeActive;
    }

    public int GetSpawnedEnemyCount()
    {
        return spawnedEnemyCount;
    }

    public int GetTargetEnemyCount()
    {
        return targetEnemyCount;
    }

    public float GetHordeProgress()
    {
        if (targetEnemyCount <= 0) return 0f;
        return (float)spawnedEnemyCount / targetEnemyCount;
    }
}