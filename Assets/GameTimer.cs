using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 天数-尸潮事件绑定类
/// </summary>
[System.Serializable]
public class DayHordeEvent
{
    [Tooltip("天数")]
    public int day;
    
    [Tooltip("对应的尸潮事件")]
    public HordeEvent hordeEvent;
}

public class GameTimer : MonoBehaviour
{
    // 移除计时器相关字段
    
    [Header("UI组件")]
    public Text TimerText;
    public Text DieText;
    public Text DayText;
    public GameObject WinPanel;
    
    [Header("游戏组件")]
    public PlayerController PC;
    public float SurveyTimer;
    
    [Header("天数系统")]
    public int DayNum = 1;
    public int MaxDays = 30; // 最大天数
    
    [Header("尸潮事件")]
    public List<DayHordeEvent> dayHordeEvents = new List<DayHordeEvent>(); // 天数-尸潮事件绑定列表
    private HordeEvent currentHordeEvent; // 当前天的尸潮事件
    private bool hordeEventTriggered = false;
    
    // 事件委托
    public delegate void DayChangedEvent(int newDay);
    public event DayChangedEvent OnDayChanged;
    
    public delegate void HordeEventTriggeredEvent(HordeEvent hordeEvent);
    public event HordeEventTriggeredEvent OnHordeEventTriggered;

    void Awake()
    {
        // 防御性代码，确保天数从至少1开始
        if (DayNum <= 0)
        {
            Debug.LogWarning($"[GameTimer] 天数(DayNum)初始值为 {DayNum}，自动重置为 1。请检查Inspector面板中的值。");
            DayNum = 1;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        InitializeDaySystem();
    }

    // Update is called once per frame
    void Update()
    {
        UpdateDayDisplay();
        UpdateTimer();
        HandleDayTransition();
    }
    
    /// <summary>
    /// 初始化天数系统
    /// </summary>
    private void InitializeDaySystem()
    {
        UpdateDayDisplay();
        LoadHordeEventForDay(DayNum);
        hordeEventTriggered = false;
    }
    
    /// <summary>
    /// 更新天数显示
    /// </summary>
    private void UpdateDayDisplay()
    {
        if (DayText != null)
        {
            DayText.text = $"Day {DayNum}";
        }
        
        // 清空计时器显示
        if (TimerText != null)
        {
            TimerText.text = "";
        }
    }
    
    /// <summary>
    /// 更新计时器
    /// </summary>
    private void UpdateTimer()
    {
        if (PC != null && PC.currentHealth > 0)
        {
            // 更新生存计时器
            SurveyTimer += Time.deltaTime;
            
            if (DieText != null)
            {
                DieText.text = $"{SurveyTimer:F1}S";
            }
        }
    }
    
    /// <summary>
    /// 处理天数转换
    /// </summary>
    private void HandleDayTransition()
    {
        // 若当前天配置了事件且尚未触发，则直接触发
        if (PC != null && PC.currentHealth > 0 && !hordeEventTriggered && currentHordeEvent != null)
        {
            TriggerHordeEvent();
        }
    }
    
    /// <summary>
    /// 进入下一天（由NPC对话触发）
    /// </summary>
    public void AdvanceToNextDay()
    {
        Debug.LogError($"[{gameObject.name}.GameTimer.AdvanceToNextDay] 准备进入下一天！当前DayNum: {DayNum}");
        
        DayNum++;
        hordeEventTriggered = false;

        Debug.LogError($"[{gameObject.name}.GameTimer.AdvanceToNextDay] DayNum已更新为: {DayNum}，现在调用UpdateDayDisplay()");
        
        // 更新UI显示
        UpdateDayDisplay();
        
        // 触发天数改变事件
        OnDayChanged?.Invoke(DayNum);
        
        // 加载新的尸潮事件
        Debug.LogError($"[{gameObject.name}.GameTimer.AdvanceToNextDay] 现在为第 {DayNum} 天加载尸潮事件");
        LoadHordeEventForDay(DayNum);
        Debug.LogError($"[{gameObject.name}.GameTimer.AdvanceToNextDay] 加载完成，currentHordeEvent: {(currentHordeEvent != null ? currentHordeEvent.hordeName : "null")}");
    }
    
    /// <summary>
    /// 加载指定天数的尸潮事件
    /// </summary>
    /// <param name="day">天数</param>
    private void LoadHordeEventForDay(int day)
    {
        currentHordeEvent = null;
        
        // 在列表中查找对应天数的尸潮事件
        foreach (var dayHordeEvent in dayHordeEvents)
        {
            if (dayHordeEvent.day == day && dayHordeEvent.hordeEvent != null)
            {
                currentHordeEvent = dayHordeEvent.hordeEvent;
                Debug.Log($"[GameTimer] 已为第 {day} 天加载事件: {currentHordeEvent.hordeName}");
                break;
            }
        }

        if (currentHordeEvent == null)
        {
            Debug.Log($"[GameTimer] 第 {day} 天没有找到对应的尸潮事件。");
        }
        

    }
    
    /// <summary>
    /// 触发尸潮事件
    /// </summary>
    private void TriggerHordeEvent()
    {
        Debug.LogError($"[{gameObject.name}.GameTimer.TriggerHordeEvent] 尝试触发尸潮事件 - DayNum: {DayNum}, currentHordeEvent: {(currentHordeEvent != null ? currentHordeEvent.hordeName : "null")}, hordeEventTriggered: {hordeEventTriggered}");
        
        if (currentHordeEvent != null && !hordeEventTriggered)
        {
            Debug.LogError($"[{gameObject.name}.GameTimer.TriggerHordeEvent] 正在为第 {DayNum} 天触发事件: {currentHordeEvent.hordeName}");
            Debug.LogError($"[{gameObject.name}.GameTimer.TriggerHordeEvent] 事件参数 - 总敌人数: {currentHordeEvent.totalEnemyCount}, 最大活跃数: {currentHordeEvent.maxActiveEnemies}, 生成间隔: {currentHordeEvent.spawnInterval}");
            
            hordeEventTriggered = true;
            OnHordeEventTriggered?.Invoke(currentHordeEvent);
            
            Debug.LogError($"[{gameObject.name}.GameTimer.TriggerHordeEvent] 事件已触发，OnHordeEventTriggered事件已调用");
        }
        else
        {
            Debug.LogError($"[{gameObject.name}.GameTimer.TriggerHordeEvent] 无法触发事件 - currentHordeEvent为null: {currentHordeEvent == null}, 已触发: {hordeEventTriggered}");
        }
    }
    
    /// <summary>
    /// 获取当前天的尸潮事件
    /// </summary>
    public HordeEvent GetCurrentHordeEvent()
    {
        return currentHordeEvent;
    }
    
    /// <summary>
    /// 手动设置天数（调试用）
    /// </summary>
    public void SetDay(int newDay)
    {
        if (newDay > 0 && newDay <= MaxDays)
        {
            DayNum = newDay;
            hordeEventTriggered = false;
            UpdateDayDisplay();
            LoadHordeEventForDay(DayNum);
            OnDayChanged?.Invoke(DayNum);
        }
        else
        {
            Debug.LogWarning($"[GameTimer] 无法设置天数 {newDay}，超出有效范围 (1-{MaxDays})");
        }
    }
    
    /// <summary>
    /// 手动触发当前天的尸潮事件
    /// </summary>
    public void TriggerCurrentHordeEvent()
    {
        if (currentHordeEvent != null && !hordeEventTriggered)
        {
            TriggerHordeEvent();
        }
    }
    
    /// <summary>
    /// 获取指定天数的尸潮事件
    /// </summary>
    public HordeEvent GetHordeEventForDay(int day)
    {
        foreach (var dayHordeEvent in dayHordeEvents)
        {
            if (dayHordeEvent.day == day)
            {
                return dayHordeEvent.hordeEvent;
            }
        }
        return null;
    }
    
    /// <summary>
    /// 测试事件系统（调试用）
    /// </summary>
    public void TestHordeEventSystem()
    {
        Debug.Log($"[GameTimer] 测试 - 天数: {DayNum}, 事件: {(currentHordeEvent != null ? currentHordeEvent.hordeName : "无")}, 已触发: {hordeEventTriggered}");
        
        // 尝试手动触发当前事件
        if (currentHordeEvent != null && !hordeEventTriggered)
        {
            TriggerHordeEvent();
        }
    }
}
