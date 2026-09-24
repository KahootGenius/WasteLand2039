using UnityEngine;

/// <summary>
/// 游戏数据管理器 - 负责保存和读取游戏数据
/// </summary>
public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }
    
    [Header("游戏数据")]
    [SerializeField] private int currentDay = 1;
    [SerializeField] private int highestDay = 1;
    [SerializeField] private bool hasExistingSave = false;
    
    // 数据保存键名
    private const string HIGHEST_DAY_KEY = "HighestDay";
    private const string CURRENT_DAY_KEY = "CurrentDay";
    private const string HAS_SAVE_KEY = "HasExistingSave";
    
    // 事件
    public System.Action<int> OnDayChanged;
    public System.Action<int> OnHighestDayUpdated;
    
    private void Awake()
    {
        // 单例模式
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadGameData();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// 加载游戏数据
    /// </summary>
    private void LoadGameData()
    {
        highestDay = PlayerPrefs.GetInt(HIGHEST_DAY_KEY, 1);
        currentDay = PlayerPrefs.GetInt(CURRENT_DAY_KEY, 1);
        hasExistingSave = PlayerPrefs.GetInt(HAS_SAVE_KEY, 0) == 1;
        
        Debug.Log($"[GameDataManager] 加载游戏数据 - 最高天数: {highestDay}, 当前天数: {currentDay}, 存在存档: {hasExistingSave}");
    }
    
    /// <summary>
    /// 保存游戏数据
    /// </summary>
    public void SaveGameData()
    {
        PlayerPrefs.SetInt(HIGHEST_DAY_KEY, highestDay);
        PlayerPrefs.SetInt(CURRENT_DAY_KEY, currentDay);
        PlayerPrefs.SetInt(HAS_SAVE_KEY, hasExistingSave ? 1 : 0);
        PlayerPrefs.Save();
        
        Debug.Log($"[GameDataManager] 保存游戏数据 - 最高天数: {highestDay}, 当前天数: {currentDay}");
    }
    
    /// <summary>
    /// 获取最高天数记录
    /// </summary>
    public int GetHighestDay()
    {
        return highestDay;
    }
    
    /// <summary>
    /// 获取当前天数
    /// </summary>
    public int GetCurrentDay()
    {
        return currentDay;
    }
    
    /// <summary>
    /// 设置当前天数
    /// </summary>
    public void SetCurrentDay(int day)
    {
        currentDay = day;
        hasExistingSave = true;
        
        // 更新最高记录
        if (currentDay > highestDay)
        {
            highestDay = currentDay;
            OnHighestDayUpdated?.Invoke(highestDay);
            Debug.Log($"[GameDataManager] 新的最高记录! 天数: {highestDay}");
        }
        
        OnDayChanged?.Invoke(currentDay);
        SaveGameData();
    }
    
    /// <summary>
    /// 检查是否有存档
    /// </summary>
    public bool HasExistingSave()
    {
        return hasExistingSave;
    }
    
    /// <summary>
    /// 开始新游戏
    /// </summary>
    public void StartNewGame()
    {
        currentDay = 1;
        hasExistingSave = true;
        SaveGameData();
        
        Debug.Log($"[GameDataManager] 开始新游戏");
    }
    
    /// <summary>
    /// 重置游戏数据（用于重新开始）
    /// </summary>
    public void ResetGameData()
    {
        currentDay = 1;
        hasExistingSave = false;
        SaveGameData();
        
        Debug.Log($"[GameDataManager] 重置游戏数据");
    }
    
    /// <summary>
    /// 清除所有数据（包括最高记录）
    /// </summary>
    public void ClearAllData()
    {
        PlayerPrefs.DeleteKey(HIGHEST_DAY_KEY);
        PlayerPrefs.DeleteKey(CURRENT_DAY_KEY);
        PlayerPrefs.DeleteKey(HAS_SAVE_KEY);
        PlayerPrefs.Save();
        
        highestDay = 1;
        currentDay = 1;
        hasExistingSave = false;
        
        Debug.Log($"[GameDataManager] 清除所有数据");
    }
    
    /// <summary>
    /// 游戏结束时调用
    /// </summary>
    public void OnGameOver()
    {
        // 保存当前进度作为最终记录
        SaveGameData();
        Debug.Log($"[GameDataManager] 游戏结束 - 最终天数: {currentDay}");
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveGameData();
        }
    }
    
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SaveGameData();
        }
    }
    
    private void OnDestroy()
    {
        SaveGameData();
    }
}