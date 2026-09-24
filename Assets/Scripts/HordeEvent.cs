using UnityEngine;
using System;

/// <summary>
/// 尸潮事件配置类 - 定义尸潮的各种参数和属性
/// </summary>
[CreateAssetMenu(fileName = "NewHordeEvent", menuName = "Game/Horde Event")]
public class HordeEvent : ScriptableObject
{
    [Header("基本设置")]
    [Tooltip("尸潮名称")]
    public string hordeName = "尸潮事件";
    
    [Tooltip("尸潮描述")]
    [TextArea(3, 5)]
    public string description = "一波尸潮正在接近...";
    
    [Header("生成设置")]
    [Tooltip("敌人总数")]
    public int totalEnemyCount = 20;
    
    [Tooltip("同时存在的最大敌人数量")]
    public int maxActiveEnemies = 5;
    
    [Tooltip("生成间隔时间（秒）")]
    public float spawnInterval = 2f;
    
    [Tooltip("生成半径（米）")]
    public float spawnRadius = 15f;
    
    [Tooltip("最小生成距离（米）")]
    public float minSpawnDistance = 5f;
    
    [Header("敌人类型设置")]
    [Tooltip("敌人类型列表 - 可以设置多种类型及其权重")]
    public EnemyTypeWeight[] enemyTypes;
    
    [Header("时间设置")]
    [Tooltip("尸潮持续时间（秒）")]
    public float hordeDuration = 60f;
    
    [Tooltip("是否使用持续时间限制")]
    public bool useDurationLimit = true;
    
    [Tooltip("是否要求消灭所有敌人")]
    public bool requireAllEnemiesKilled = false;
    
    [Header("奖励设置")]
    [Tooltip("完成尸潮的经验奖励")]
    public int experienceReward = 100;
    
    [Tooltip("完成尸潮的金币奖励")]
    public int goldReward = 50;
    
    [Tooltip("额外物品奖励")]
    public GameObject[] itemRewards;
    
    [Header("特殊效果")]
    [Tooltip("尸潮开始时的音效")]
    public AudioClip startSound;
    
    [Tooltip("尸潮结束时的音效")]
    public AudioClip endSound;
    
    [Tooltip("尸潮警告音效")]
    public AudioClip warningSound;
    
    [Tooltip("警告提前时间（秒）")]
    public float warningTime = 3f;
    
    [Header("视觉效果")]
    [Tooltip("尸潮开始时的特效")]
    public GameObject startEffect;
    
    [Tooltip("尸潮结束时的特效")]
    public GameObject endEffect;
    
    [Tooltip("警告特效")]
    public GameObject warningEffect;
    
    [Header("敌人属性倍率")]
    [Tooltip("敌人生命值倍率")]
    public float healthMultiplier = 1f;
    
    [Tooltip("敌人移动速度倍率")]
    public float speedMultiplier = 1f;
    
    [Tooltip("敌人攻击力倍率")]
    public float damageMultiplier = 1f;
    
    [Header("特殊效果")]
    [Tooltip("是否启用特殊效果")]
    public bool hasSpecialEffects = false;
    
    /// <summary>
    /// 获取随机敌人类型
    /// </summary>
    public GameObject GetRandomEnemyType()
    {
        if (enemyTypes == null || enemyTypes.Length == 0)
        {
            Debug.LogWarning("[HordeEvent] 没有配置敌人类型");
            return null;
        }
        
        // 计算总权重
        float totalWeight = 0f;
        foreach (var enemyType in enemyTypes)
        {
            totalWeight += enemyType.weight;
        }
        
        // 随机选择
        float randomValue = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        
        foreach (var enemyType in enemyTypes)
        {
            currentWeight += enemyType.weight;
            if (randomValue <= currentWeight)
            {
                return enemyType.enemyPrefab;
            }
        }
        
        // 默认返回第一个
        return enemyTypes[0].enemyPrefab;
    }
    
    /// <summary>
    /// 获取敌人类型的生成概率
    /// </summary>
    public float[] GetEnemyTypeProbabilities()
    {
        if (enemyTypes == null || enemyTypes.Length == 0)
        {
            return new float[0];
        }
        
        float totalWeight = 0f;
        foreach (var enemyType in enemyTypes)
        {
            totalWeight += enemyType.weight;
        }
        
        float[] probabilities = new float[enemyTypes.Length];
        for (int i = 0; i < enemyTypes.Length; i++)
        {
            probabilities[i] = enemyTypes[i].weight / totalWeight;
        }
        
        return probabilities;
    }
    
    /// <summary>
    /// 获取敌人预制体数组
    /// </summary>
    public GameObject[] GetEnemyPrefabs()
    {
        if (enemyTypes == null || enemyTypes.Length == 0)
            return new GameObject[0];
        
        GameObject[] prefabs = new GameObject[enemyTypes.Length];
        for (int i = 0; i < enemyTypes.Length; i++)
        {
            prefabs[i] = enemyTypes[i].enemyPrefab;
        }
        return prefabs;
    }
    
    /// <summary>
    /// 获取敌人权重数组
    /// </summary>
    public int[] GetEnemyWeights()
    {
        if (enemyTypes == null || enemyTypes.Length == 0)
            return new int[0];
        
        int[] weights = new int[enemyTypes.Length];
        for (int i = 0; i < enemyTypes.Length; i++)
        {
            weights[i] = Mathf.RoundToInt(enemyTypes[i].weight);
        }
        return weights;
    }
    
    /// <summary>
    /// 总敌人数量
    /// </summary>
    public int totalEnemies => totalEnemyCount;
    
    /// <summary>
    /// 最大同时存在敌人数量
    /// </summary>
    public int maxSimultaneousEnemies => maxActiveEnemies;
    
    /// <summary>
    /// 生成间隔时间
    /// </summary>
    public float spawnIntervalTime => spawnInterval;
    
    /// <summary>
    /// 生成半径大小
    /// </summary>
    public float spawnRadiusSize => spawnRadius;
    
    /// <summary>
    /// 尸潮强度
    /// </summary>
    public float intensity => 1f; // 默认强度，可以根据需要调整
    
    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public bool ValidateConfiguration()
    {
        if (totalEnemyCount <= 0)
        {
            Debug.LogError($"[HordeEvent] {name}: 敌人总数必须大于0");
            return false;
        }
        
        if (maxActiveEnemies <= 0)
        {
            Debug.LogError($"[HordeEvent] {name}: 最大活跃敌人数量必须大于0");
            return false;
        }
        
        if (spawnInterval <= 0f)
        {
            Debug.LogError($"[HordeEvent] {name}: 生成间隔必须大于0");
            return false;
        }
        
        if (spawnRadius <= 0f)
        {
            Debug.LogError($"[HordeEvent] {name}: 生成半径必须大于0");
            return false;
        }
        
        if (minSpawnDistance < 0f)
        {
            Debug.LogError($"[HordeEvent] {name}: 最小生成距离不能为负数");
            return false;
        }
        
        if (minSpawnDistance >= spawnRadius)
        {
            Debug.LogError($"[HordeEvent] {name}: 最小生成距离必须小于生成半径");
            return false;
        }
        
        if (enemyTypes == null || enemyTypes.Length == 0)
        {
            Debug.LogError($"[HordeEvent] {name}: 必须配置至少一种敌人类型");
            return false;
        }
        
        foreach (var enemyType in enemyTypes)
        {
            if (enemyType.enemyPrefab == null)
            {
                Debug.LogError($"[HordeEvent] {name}: 敌人类型配置中缺少预制体");
                return false;
            }
            
            if (enemyType.weight <= 0f)
            {
                Debug.LogError($"[HordeEvent] {name}: 敌人类型权重必须大于0");
                return false;
            }
        }
        
        if (useDurationLimit && hordeDuration <= 0f)
        {
            Debug.LogError($"[HordeEvent] {name}: 尸潮持续时间必须大于0");
            return false;
        }
        
        return true;
    }
}

/// <summary>
/// 敌人类型权重配置
/// </summary>
[Serializable]
public class EnemyTypeWeight
{
    [Tooltip("敌人预制体")]
    public GameObject enemyPrefab;
    
    [Tooltip("生成权重 - 值越大，生成概率越高")]
    public float weight = 1f;
    
    [Tooltip("最小生成数量")]
    public int minCount = 0;
    
    [Tooltip("最大生成数量（0表示无限制）")]
    public int maxCount = 0;
}