using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

/// <summary>
/// 先决条件管理器 - 管理游戏中的各种条件和事件
/// </summary>
public class PrerequisiteManager : MonoBehaviour
{
    public static PrerequisiteManager Instance { get; private set; }
    
    [Header("调试设置")]
    [SerializeField] private bool enableDebugMode = true;
    
    // 存储所有条件状态
    private Dictionary<string, bool> conditions = new Dictionary<string, bool>();
    
    // 事件系统
    public static event Action<string> OnConditionChanged;
    public static event Action<string> OnConditionMet;
    
    private void Awake()
    {
        // 单例模式。随 MainGame 场景创建和销毁，不用 DontDestroyOnLoad：
        // 条件（对话完成、刷怪开启等）属于一局游戏，新的一局从默认条件开始
        if (Instance == null)
        {
            Instance = this;
            InitializeConditions();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    
    /// <summary>
    /// 初始化默认条件
    /// </summary>
    private void InitializeConditions()
    {
        // 添加一些常用的游戏条件（不触发事件）
        SetConditionSilent("tutorial_started", false);
        SetConditionSilent("tutorial_completed", false);
        SetConditionSilent("first_dialogue_completed", false);
        SetConditionSilent("weapon_tutorial_completed", false);
        SetConditionSilent("inventory_tutorial_completed", false);
        SetConditionSilent("enemy_spawning_enabled", false);
        
        if (enableDebugMode)
        {
            Debug.Log("[PrerequisiteManager] 初始化完成，已设置默认条件");
        }
    }
    
    /// <summary>
    /// 静默设置条件状态（不触发事件，用于初始化）
    /// </summary>
    /// <param name="conditionName">条件名称</param>
    /// <param name="value">条件值</param>
    private void SetConditionSilent(string conditionName, bool value)
    {
        conditions[conditionName] = value;
        
        if (enableDebugMode)
        {
            Debug.Log($"[PrerequisiteManager] 静默设置条件 '{conditionName}' = {value}");
        }
    }
    
    /// <summary>
    /// 设置条件状态
    /// </summary>
    /// <param name="conditionName">条件名称</param>
    /// <param name="value">条件值</param>
    public void SetCondition(string conditionName, bool value)
    {
        bool previousValue = conditions.ContainsKey(conditionName) ? conditions[conditionName] : false;
        conditions[conditionName] = value;
        
        if (enableDebugMode)
        {
            Debug.Log($"[PrerequisiteManager] 条件 '{conditionName}' 设置为: {value}");
        }
        
        // 触发条件改变事件
        OnConditionChanged?.Invoke(conditionName);
        
        // 如果条件从false变为true，触发条件满足事件
        if (!previousValue && value)
        {
            OnConditionMet?.Invoke(conditionName);
            
            if (enableDebugMode)
            {
                Debug.Log($"[PrerequisiteManager] 条件 '{conditionName}' 已满足！");
            }
        }
    }
    
    /// <summary>
    /// 获取条件状态
    /// </summary>
    /// <param name="conditionName">条件名称</param>
    /// <returns>条件状态</returns>
    public bool GetCondition(string conditionName)
    {
        return conditions.ContainsKey(conditionName) ? conditions[conditionName] : false;
    }
    
    /// <summary>
    /// 检查多个条件是否都满足
    /// </summary>
    /// <param name="conditionNames">条件名称数组</param>
    /// <returns>是否所有条件都满足</returns>
    public bool CheckAllConditions(params string[] conditionNames)
    {
        foreach (string condition in conditionNames)
        {
            if (!GetCondition(condition))
            {
                return false;
            }
        }
        return true;
    }
    
    /// <summary>
    /// 检查任意一个条件是否满足
    /// </summary>
    /// <param name="conditionNames">条件名称数组</param>
    /// <returns>是否有任意条件满足</returns>
    public bool CheckAnyCondition(params string[] conditionNames)
    {
        foreach (string condition in conditionNames)
        {
            if (GetCondition(condition))
            {
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// 重置所有条件
    /// </summary>
    public void ResetAllConditions()
    {
        conditions.Clear();
        InitializeConditions();
        
        if (enableDebugMode)
        {
            Debug.Log("[PrerequisiteManager] 所有条件已重置");
        }
    }
    
    /// <summary>
    /// 获取所有条件的调试信息
    /// </summary>
    /// <returns>条件状态字符串</returns>
    public string GetDebugInfo()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 先决条件状态 ===");
        
        foreach (var kvp in conditions)
        {
            sb.AppendLine($"{kvp.Key}: {kvp.Value}");
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// 便捷方法：标记新手教程完成
    /// </summary>
    public void CompleteTutorial()
    {
        SetCondition("tutorial_completed", true);
        SetCondition("enemy_spawning_enabled", true);
    }
    
    /// <summary>
    /// 便捷方法：标记对话完成
    /// </summary>
    /// <param name="dialogueId">对话ID</param>
    public void CompleteDialogue(string dialogueId)
    {
        SetCondition($"dialogue_{dialogueId}_completed", true);
        
        // 如果是第一个对话，设置通用标记
        if (dialogueId == "first" || dialogueId == "intro")
        {
            SetCondition("first_dialogue_completed", true);
        }
    }
    
    /// <summary>
    /// 便捷方法：启用敌人生成
    /// </summary>
    public void EnableEnemySpawning()
    {
        SetCondition("enemy_spawning_enabled", true);
    }
    
    /// <summary>
    /// 便捷方法：禁用敌人生成
    /// </summary>
    public void DisableEnemySpawning()
    {
        SetCondition("enemy_spawning_enabled", false);
    }
    
    // Unity编辑器中的调试功能
    #if UNITY_EDITOR
    [ContextMenu("显示所有条件状态")]
    private void ShowAllConditions()
    {
        Debug.Log(GetDebugInfo());
    }
    
    [ContextMenu("重置所有条件")]
    private void ResetConditionsFromMenu()
    {
        ResetAllConditions();
    }
    
    [ContextMenu("完成新手教程")]
    private void CompleteTutorialFromMenu()
    {
        CompleteTutorial();
    }
    #endif
}