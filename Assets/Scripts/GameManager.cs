using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("光标设置")]
    [SerializeField] private bool showCursorInGame = true;
    [SerializeField] private CursorLockMode cursorLockMode = CursorLockMode.None;
    
    [Header("开场指引设置")]
    [SerializeField] private bool enableTutorialOnStart = true;
    [SerializeField] private Transform tutorialTargetNPC; // 指引目标NPC
    
    [Header("天数系统设置")]
    [SerializeField] private bool enableDaySystem = true;
    [SerializeField] private bool autoStartFirstDay = true;
   // [SerializeField] private DayManager dayManagerPrefab; // 天数管理器预制体
    
    private static GameManager instance;
    public static GameManager Instance => instance;
    
    private TutorialGuideManager tutorialManager;
   //private DayManager dayManager;
    
    private void Awake()
    {
        // 单例模式。随 MainGame 场景创建和销毁，不用 DontDestroyOnLoad：
        // 指引目标 NPC 等引用在场景里；每次进入 MainGame 都是新的一局，重新初始化
        if (instance == null)
        {
            instance = this;
            InitializeGame();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void InitializeGame()
    {
        // 设置光标状态
        SetCursorState(showCursorInGame, cursorLockMode);
        
        // 初始化开场指引系统
        InitializeTutorialSystem();
        
        // 初始化天数系统
        InitializeDaySystem();
        
        // 与GameDataManager集成
        IntegrateWithGameDataManager();
        
        Debug.Log("[GameManager] 游戏初始化完成 - 光标可见: " + showCursorInGame + ", 锁定模式: " + cursorLockMode);
    }
    
    /// <summary>
    /// 与GameDataManager集成
    /// </summary>
    private void IntegrateWithGameDataManager()
    {
        if (GameDataManager.Instance != null)
        {
            // 订阅天数变化事件
            GameDataManager.Instance.OnDayChanged += OnDayChanged;
            
            // 如果有存档，恢复游戏状态
            if (GameDataManager.Instance.HasExistingSave())
            {
                int currentDay = GameDataManager.Instance.GetCurrentDay();
                Debug.Log($"[GameManager] 恢复游戏状态 - 当前天数: {currentDay}");
                
                // 这里可以根据需要恢复其他游戏状态
                RestoreGameState(currentDay);
            }
            else
            {
                Debug.Log("[GameManager] 新游戏开始");
            }
        }
    }
    
    /// <summary>
    /// 天数变化回调
    /// </summary>
    private void OnDayChanged(int newDay)
    {
        Debug.Log($"[GameManager] 天数变化: {newDay}");
        
        // 这里可以处理天数变化时的逻辑
        // 例如：更新UI、触发事件等
    }
    
    /// <summary>
    /// 恢复游戏状态
    /// </summary>
    private void RestoreGameState(int day)
    {
        // 根据天数恢复游戏状态
        // 这里可以添加具体的恢复逻辑
        Debug.Log($"[GameManager] 恢复到第 {day} 天的游戏状态");
    }
    
    /// <summary>
    /// 返回主菜单
    /// </summary>
    public void ReturnToMainMenu()
    {
        // 保存当前游戏状态
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.SaveGameData();
        }
        
        // 使用SceneController加载主菜单
        if (SceneController.Instance != null)
        {
            SceneController.Instance.LoadMainMenu();
        }
        else
        {
            // 备用方案：直接加载场景
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
        
        Debug.Log("[GameManager] 返回主菜单");
    }
    
    /// <summary>
    /// 初始化开场指引系统
    /// </summary>
    private void InitializeTutorialSystem()
    {
        // 查找或创建TutorialGuideManager
        tutorialManager = FindObjectOfType<TutorialGuideManager>();
        
        if (tutorialManager == null && enableTutorialOnStart)
        {
            GameObject tutorialGO = new GameObject("TutorialGuideManager");
            tutorialManager = tutorialGO.AddComponent<TutorialGuideManager>();
            
            Debug.Log("[GameManager] 已创建TutorialGuideManager");
        }
        
        // 设置目标NPC
        if (tutorialManager != null && tutorialTargetNPC != null)
        {
            tutorialManager.SetTargetNPC(tutorialTargetNPC);
            Debug.Log("[GameManager] 已设置指引目标NPC: " + tutorialTargetNPC.name);
        }
    }
    
    /// <summary>
    /// 设置光标状态
    /// </summary>
    /// <param name="visible">是否显示光标</param>
    /// <param name="lockMode">光标锁定模式</param>
    public void SetCursorState(bool visible, CursorLockMode lockMode)
    {
        Debug.Log("[GameManager] 设置光标状态 - 可见: " + visible + ", 锁定模式: " + lockMode);
        Cursor.visible = visible;
        Cursor.lockState = lockMode;
        Debug.Log("[GameManager] 光标状态已更新 - 当前可见: " + Cursor.visible + ", 当前锁定模式: " + Cursor.lockState);
    }
    
    /// <summary>
    /// 显示光标（用于UI界面）
    /// </summary>
    public void ShowCursor()
    {
        SetCursorState(true, CursorLockMode.None);
    }
    
    /// <summary>
    /// 隐藏光标（用于游戏中）
    /// </summary>
    public void HideCursor()
    {
        SetCursorState(false, CursorLockMode.Locked);
    }
    
    /// <summary>
    /// 切换光标显示状态
    /// </summary>
    public void ToggleCursor()
    {
        if (Cursor.visible)
        {
            SetCursorState(false, CursorLockMode.Locked);
        }
        else
        {
            SetCursorState(true, CursorLockMode.None);
        }
    }
    
    private void Update()
    {
        // ESC键切换光标状态 - 只有在没有PauseManager或PauseManager未暂停时才处理
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 检查是否存在PauseManager并且游戏已暂停
            PauseManager pauseManager = FindObjectOfType<PauseManager>();
            if (pauseManager == null || !pauseManager.IsPaused)
            {
                Debug.Log("[GameManager] ESC键被按下，切换光标状态");
                ToggleCursor();
            }
            else
            {
                Debug.Log("[GameManager] ESC键被按下，但PauseManager正在处理，跳过光标切换");
            }
        }
        
        // 检测鼠标点击事件
        if (Input.GetMouseButtonDown(0))
        {
            Debug.Log("[GameManager] 检测到鼠标左键点击 - 当前光标状态: 可见=" + Cursor.visible + ", 锁定模式=" + Cursor.lockState);
        }
    }
    
    /// <summary>
    /// 开始开场指引
    /// </summary>
    public void StartTutorial()
    {
        if (tutorialManager != null)
        {
            tutorialManager.StartTutorial();
            Debug.Log("[GameManager] 开始开场指引");
        }
        else
        {
            Debug.LogWarning("[GameManager] TutorialGuideManager未找到，无法开始指引");
        }
    }
    
    /// <summary>
    /// 结束开场指引
    /// </summary>
    public void EndTutorial()
    {
        if (tutorialManager != null)
        {
            tutorialManager.EndTutorial();
            Debug.Log("[GameManager] 结束开场指引");
        }
    }
    
    /// <summary>
    /// 设置指引目标NPC
    /// </summary>
    /// <param name="npc">目标NPC的Transform</param>
    public void SetTutorialTarget(Transform npc)
    {
        tutorialTargetNPC = npc;
        if (tutorialManager != null)
        {
            tutorialManager.SetTargetNPC(npc);
            Debug.Log("[GameManager] 更新指引目标NPC: " + (npc != null ? npc.name : "null"));
        }
    }
    
    /// <summary>
    /// 初始化天数系统
    /// </summary>
    private void InitializeDaySystem()
    {
        if (!enableDaySystem)
        {
            Debug.Log("[GameManager] 天数系统已禁用");
            return;
        }
        
        // 查找或创建DayManager
      /*  dayManager = FindObjectOfType<DayManager>();
        
        if (dayManager == null)
        {
            if (dayManagerPrefab != null)
            {
                // 从预制体创建
                GameObject dayManagerGO = Instantiate(dayManagerPrefab.gameObject);
                dayManager = dayManagerGO.GetComponent<DayManager>();
                Debug.Log("[GameManager] 已从预制体创建DayManager");
            }
            else
            {
                // 创建新的DayManager
                GameObject dayManagerGO = new GameObject("DayManager");
                dayManager = dayManagerGO.AddComponent<DayManager>();
                Debug.Log("[GameManager] 已创建新的DayManager");
            }
        }
        
        if (dayManager != null)
        {
            // 配置DayManager
            dayManager.autoStartFirstDay = autoStartFirstDay;
            
            // 自动查找相关组件 (HordeSpawner已移除)
            // if (dayManager.hordeSpawner == null)
            // {
            //     dayManager.hordeSpawner = FindObjectOfType<HordeSpawner>();
            //     if (dayManager.hordeSpawner != null)
            //     {
            //         Debug.Log("[GameManager] 已自动找到HordeSpawner");
            //     }
            // }
            
            if (dayManager.dialogueSystem == null)
            {
                dayManager.dialogueSystem = FindObjectOfType<DialogueSystem>();
                if (dayManager.dialogueSystem != null)
                {
                    Debug.Log("[GameManager] 已自动找到DialogueSystem");
                }
            }
            
            Debug.Log("[GameManager] 天数系统初始化完成");
        }
        else
        {
            Debug.LogError("[GameManager] 无法初始化天数系统 - DayManager创建失败");
        }*/
    }
    
    /// <summary>
    /// 开始新天数
    /// </summary>
   /* public void StartNewDay()
    {
        if (dayManager != null)
        {
            dayManager.StartNewDay();
            Debug.Log("[GameManager] 开始新天数");
        }
    }
    
    /// <summary>
    /// 完成当前天数
    /// </summary>
    public void CompleteCurrentDay()
    {
        if (dayManager != null)
        {
            dayManager.CompleteDay();
            Debug.Log("[GameManager] 完成当前天数");
        }
    }*/
    
    /// <summary>
    /// 开始尸潮
    /// </summary>
    /*public void StartHorde()
    {
        if (dayManager != null)
        {
            dayManager.StartHorde();
            Debug.Log("[GameManager] 开始尸潮");
        }
    }
    
    /// <summary>
    /// 获取当前天数
    /// </summary>
    public int GetCurrentDay()
    {
        return dayManager != null ? dayManager.GetCurrentDay() : 1;
    }
    
    /// <summary>
    /// 检查是否处于尸潮中
    /// </summary>
    public bool IsHordeActive()
    {
        return dayManager != null && dayManager.IsHordeActive();
    }
    
    /// <summary>
    /// 获取DayManager实例
    /// </summary>
    public DayManager GetDayManager()
    {
        return dayManager;
    }*/
    
    /// <summary>
    /// 检查指引是否激活
    /// </summary>
    public bool IsTutorialActive
    {
        get
        {
            return tutorialManager != null && tutorialManager.IsTutorialActive;
        }
    }
    
    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
        
        // 取消订阅事件
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.OnDayChanged -= OnDayChanged;
        }
    }
}