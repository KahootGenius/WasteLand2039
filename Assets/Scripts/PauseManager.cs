using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏暂停管理器
/// 处理ESC键暂停、UI显示和游戏状态管理
/// </summary>
public class PauseManager : MonoBehaviour
{
    [Header("暂停UI设置")]
    [SerializeField] private GameObject pauseMenuUI;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button survivalManualButton;
    [SerializeField] private Button saveAndQuitButton;
    
    [Header("生存手册设置")]
    [SerializeField] private GameObject survivalManualUI;
    [SerializeField] private SurvivalManualData manualData;
    
    [Header("暂停设置")]
    [SerializeField] private bool canPause = true;
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;
    
    private static PauseManager instance;
    public static PauseManager Instance => instance;
    
    private bool isPaused = false;
    private float originalTimeScale = 1f;
    
    /// <summary>
    /// 获取当前暂停状态
    /// </summary>
    public bool IsPaused => isPaused;
    
    private void Awake()
    {
        // 单例模式。随 MainGame 场景创建和销毁，不用 DontDestroyOnLoad：
        // 暂停菜单在场景里，跨场景保留后引用失效，回到主菜单按 ESC 也会把时间冻结
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // 初始化
        Initialize();
    }
    
    private void Initialize()
    {
        // 记录原始时间缩放
        originalTimeScale = Time.timeScale;
        
        // 确保暂停菜单初始状态为隐藏
        if (pauseMenuUI != null)
        {
            pauseMenuUI.SetActive(false);
        }
        
        // 绑定按钮事件
        SetupButtonEvents();
        
        Debug.Log("[PauseManager] 暂停管理器初始化完成");
    }
    
    private void SetupButtonEvents()
    {
        if (continueButton != null)
        {
            continueButton.onClick.AddListener(ResumeGame);
        }
        
        if (survivalManualButton != null)
        {
            survivalManualButton.onClick.AddListener(OpenSurvivalManual);
        }
        
        if (saveAndQuitButton != null)
        {
            saveAndQuitButton.onClick.AddListener(SaveAndQuit);
        }
    }
    
    private void Update()
    {
        // 检测ESC键输入
        if (canPause && Input.GetKeyDown(pauseKey))
        {
            if (isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }
    }
    
    /// <summary>
    /// 暂停游戏
    /// </summary>
    public void PauseGame()
    {
        if (!canPause || isPaused) return;
        
        isPaused = true;
        Time.timeScale = 0f;
        
        // 显示暂停菜单
        if (pauseMenuUI != null)
        {
            pauseMenuUI.SetActive(true);
            
            // 获取PauseMenuUI组件并调用ShowPauseMenu方法
            PauseMenuUI pauseMenuUIComponent = pauseMenuUI.GetComponent<PauseMenuUI>();
            if (pauseMenuUIComponent != null)
            {
                pauseMenuUIComponent.ShowPauseMenu();
            }
            else
            {
                Debug.LogWarning("[PauseManager] 未找到PauseMenuUI组件");
            }
        }
        
        // 显示鼠标光标
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ShowCursor();
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        
        Debug.Log("[PauseManager] 游戏已暂停");
    }
    
    /// <summary>
    /// 恢复游戏
    /// </summary>
    public void ResumeGame()
    {
        if (!isPaused) return;
        
        isPaused = false;
        Time.timeScale = originalTimeScale;
        
        // 隐藏暂停菜单
        if (pauseMenuUI != null)
        {
            // 获取PauseMenuUI组件并调用HidePauseMenu方法
            PauseMenuUI pauseMenuUIComponent = pauseMenuUI.GetComponent<PauseMenuUI>();
            if (pauseMenuUIComponent != null)
            {
                pauseMenuUIComponent.HidePauseMenu();
            }
            else
            {
                pauseMenuUI.SetActive(false);
                Debug.LogWarning("[PauseManager] 未找到PauseMenuUI组件，直接设置GameObject为非激活状态");
            }
        }
        
        // 隐藏鼠标光标（根据GameManager设置）
        if (GameManager.Instance != null)
        {
            GameManager.Instance.HideCursor();
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        
        Debug.Log("[PauseManager] 游戏已恢复");
    }
    
    /// <summary>
    /// 打开生存手册
    /// </summary>
    public void OpenSurvivalManual()
    {
        Debug.Log("[PauseManager] 打开生存手册");
        
        if (survivalManualUI != null)
        {
            // 隐藏暂停菜单
            if (pauseMenuUI != null)
            {
                PauseMenuUI pauseMenuUIComponent = pauseMenuUI.GetComponent<PauseMenuUI>();
                if (pauseMenuUIComponent != null)
                {
                    pauseMenuUIComponent.HidePauseMenu();
                }
                else
                {
                    pauseMenuUI.SetActive(false);
                }
            }
            
            // 显示生存手册
            survivalManualUI.SetActive(true);
            
            // 获取SurvivalManualUI组件并显示手册
            SurvivalManualUI manualUIComponent = survivalManualUI.GetComponent<SurvivalManualUI>();
            if (manualUIComponent != null)
            {
                // 如果有配置数据，设置给UI组件
                if (manualData != null)
                {
                    manualUIComponent.SetManualData(manualData);
                }
                
                manualUIComponent.ShowManual();
                Debug.Log("[PauseManager] 生存手册已显示");
            }
            else
            {
                Debug.LogWarning("[PauseManager] 未找到SurvivalManualUI组件");
            }
        }
        else
        {
            Debug.LogWarning("[PauseManager] 生存手册UI未配置，请在Inspector中设置survivalManualUI");
        }
    }
    
    /// <summary>
    /// 关闭生存手册，返回暂停菜单
    /// </summary>
    public void CloseSurvivalManual()
    {
        Debug.Log("[PauseManager] 关闭生存手册，返回暂停菜单");
        
        if (survivalManualUI != null)
        {
            // 隐藏生存手册
            SurvivalManualUI manualUIComponent = survivalManualUI.GetComponent<SurvivalManualUI>();
            if (manualUIComponent != null)
            {
                manualUIComponent.HideManual();
            }
            else
            {
                survivalManualUI.SetActive(false);
            }
        }
        
        // 显示暂停菜单
        if (pauseMenuUI != null)
        {
            pauseMenuUI.SetActive(true);
            
            PauseMenuUI pauseMenuUIComponent = pauseMenuUI.GetComponent<PauseMenuUI>();
            if (pauseMenuUIComponent != null)
            {
                pauseMenuUIComponent.ShowPauseMenu();
            }
        }
    }
    
    /// <summary>
    /// 保存并退出游戏
    /// </summary>
    public void SaveAndQuit()
    {
        Debug.Log("[PauseManager] 保存并退出游戏");
        
        // 恢复时间缩放
        Time.timeScale = originalTimeScale;
        
        // TODO: 在这里添加保存游戏数据的逻辑
        // 例如：
        // - 保存玩家进度
        // - 保存背包物品
        // - 保存游戏设置
        
        // 退出到主菜单或关闭游戏
        // 这里假设有一个主菜单场景，如果没有则直接退出应用
        try
        {
            // 尝试加载主菜单场景
            SceneManager.LoadScene("MainMenu");
        }
        catch
        {
            // 如果没有主菜单场景，直接退出应用
            Debug.Log("[PauseManager] 退出应用程序");
            Application.Quit();
            
            // 在编辑器中停止播放
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
        }
    }
    
    /// <summary>
    /// 设置是否可以暂停
    /// </summary>
    public void SetCanPause(bool canPause)
    {
        this.canPause = canPause;
        Debug.Log($"[PauseManager] 暂停功能已{(canPause ? "启用" : "禁用")}");
    }
    
    /// <summary>
    /// 强制恢复游戏（用于其他系统调用）
    /// </summary>
    public void ForceResume()
    {
        if (isPaused)
        {
            ResumeGame();
        }
    }
    
    private void OnDestroy()
    {
        if (instance == this)
        {
            // 暂停中离开场景时恢复时间缩放，避免下一个场景被冻结
            if (isPaused)
            {
                Time.timeScale = originalTimeScale;
            }
            instance = null;
        }
        
        // 清理按钮事件
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(ResumeGame);
        }
        
        if (survivalManualButton != null)
        {
            survivalManualButton.onClick.RemoveListener(OpenSurvivalManual);
        }
        
        if (saveAndQuitButton != null)
        {
            saveAndQuitButton.onClick.RemoveListener(SaveAndQuit);
        }
    }
}