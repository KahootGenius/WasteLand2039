using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 主菜单UI控制器 - 管理游戏开始页面的所有UI元素和交互
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("UI按钮")]
    public Button startGameButton;
    public Button restartGameButton;
    public Button quitGameButton;
    
    [Header("信息显示")]
    public TextMeshProUGUI highestDayText;
    public TextMeshProUGUI gameVersionText;
    
    [Header("场景设置")]
    public string gameSceneName = "GameScene";
    
    [Header("动画设置")]
    public CanvasGroup canvasGroup;
    public bool useAnimation = true;
    public float animationDuration = 0.5f;
    
    [Header("确认对话框")]
    public GameObject confirmDialog;  // 确认对话框Panel
    public Button yesButton;          // Yes按钮
    public Button noButton;           // No按钮
    public TextMeshProUGUI confirmMessageText;
    
    private bool isAnimating = false;
    
    private void Start()
    {
        InitializeUI();
        SetupButtons();
        UpdateHighestDayDisplay();
        
        // 启动时播放淡入动画
        if (useAnimation)
        {
            PlayFadeInAnimation();
        }
    }
    
    /// <summary>
    /// 初始化UI
    /// </summary>
    private void InitializeUI()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        
        // 设置游戏版本信息
        if (gameVersionText != null)
        {
            gameVersionText.text = $"Version {Application.version}";
        }
        
        // 隐藏确认对话框
        if (confirmDialog != null)
        {
            confirmDialog.SetActive(false);
        }
        
        // 根据是否有存档来设置按钮状态
        UpdateButtonStates();
    }
    
    /// <summary>
    /// 设置按钮事件
    /// </summary>
    private void SetupButtons()
    {
        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(OnStartGameClicked);
        }
        
        if (restartGameButton != null)
        {
            restartGameButton.onClick.RemoveAllListeners();
            restartGameButton.onClick.AddListener(OnRestartGameClicked);
        }
        
        if (quitGameButton != null)
        {
            quitGameButton.onClick.RemoveAllListeners();
            quitGameButton.onClick.AddListener(OnQuitGameClicked);
        }
        
        // 设置确认对话框按钮
        if (yesButton != null)
        {
            yesButton.onClick.RemoveAllListeners();
            yesButton.onClick.AddListener(OnYesButtonClicked);
        }
        
        if (noButton != null)
        {
            noButton.onClick.RemoveAllListeners();
            noButton.onClick.AddListener(OnNoButtonClicked);
        }
    }
    
    /// <summary>
    /// 更新按钮状态
    /// </summary>
    private void UpdateButtonStates()
    {
        bool hasExistingSave = GameDataManager.Instance != null && GameDataManager.Instance.HasExistingSave();
        
        // 如果有存档，开始游戏按钮显示"继续游戏"
        if (startGameButton != null)
        {
            var buttonText = startGameButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = hasExistingSave ? "Continue Game" : "Start Game";
            }
        }
        
        // 重新开始按钮只在有存档时可用
        if (restartGameButton != null)
        {
            restartGameButton.interactable = hasExistingSave;
        }
    }
    
    /// <summary>
    /// 更新最高天数显示
    /// </summary>
    private void UpdateHighestDayDisplay()
    {
        if (highestDayText != null && GameDataManager.Instance != null)
        {
            int highestDay = GameDataManager.Instance.GetHighestDay();
            highestDayText.text = $"Best Record: Day {highestDay}";
        }
    }
    
    /// <summary>
    /// 开始游戏按钮点击
    /// </summary>
    private void OnStartGameClicked()
    {
        if (isAnimating) return;
        
        Debug.Log("[MainMenuUI] 开始游戏按钮被点击");
        
        // 如果没有存档，创建新游戏
        if (GameDataManager.Instance != null && !GameDataManager.Instance.HasExistingSave())
        {
            GameDataManager.Instance.StartNewGame();
        }
        
        LoadGameScene();
    }
    
    /// <summary>
    /// 重新开始游戏按钮点击
    /// </summary>
    private void OnRestartGameClicked()
    {
        if (isAnimating) return;
        
        Debug.Log("[MainMenuUI] 重新开始游戏按钮被点击");
        
        // 显示确认对话框
        ShowConfirmDialog("Are you sure you want to restart the game?\nCurrent progress will be lost!", () => {
            if (GameDataManager.Instance != null)
            {
                GameDataManager.Instance.ResetGameData();
                GameDataManager.Instance.StartNewGame();
            }
            LoadGameScene();
        });
    }
    
    /// <summary>
    /// 退出游戏按钮点击
    /// </summary>
    private void OnQuitGameClicked()
    {
        if (isAnimating) return;
        
        Debug.Log("[MainMenuUI] 退出游戏按钮被点击");
        
        // 显示确认对话框
        ShowConfirmDialog("Are you sure you want to quit the game?", () => {
            QuitGame();
        });
    }
    
    /// <summary>
    /// 显示确认对话框
    /// </summary>
    private void ShowConfirmDialog(string message, System.Action onConfirm)
    {
        if (confirmDialog == null) return;
        
        if (confirmMessageText != null)
        {
            confirmMessageText.text = message;
        }
        
        confirmDialog.SetActive(true);
        
        // 临时存储确认回调
        yesButton.onClick.RemoveAllListeners();
        yesButton.onClick.AddListener(() => {
            onConfirm?.Invoke();
            HideConfirmDialog();
        });
        
        // 设置No按钮
        noButton.onClick.RemoveAllListeners();
        noButton.onClick.AddListener(() => {
            HideConfirmDialog();
        });
    }
    
    /// <summary>
    /// 隐藏确认对话框
    /// </summary>
    private void HideConfirmDialog()
    {
        if (confirmDialog != null)
        {
            confirmDialog.SetActive(false);
        }
        
        // 重新设置按钮事件
        SetupButtons();
    }
    
    /// <summary>
    /// Yes按钮点击
    /// </summary>
    private void OnYesButtonClicked()
    {
        // 这个方法会被动态设置的回调覆盖
        HideConfirmDialog();
    }
    
    /// <summary>
    /// No按钮点击
    /// </summary>
    private void OnNoButtonClicked()
    {
        HideConfirmDialog();
    }
    
    /// <summary>
    /// 加载游戏场景
    /// </summary>
    private void LoadGameScene()
    {
        if (useAnimation)
        {
            PlayFadeOutAnimation(() => {
                SceneManager.LoadScene(gameSceneName);
            });
        }
        else
        {
            SceneManager.LoadScene(gameSceneName);
        }
    }
    
    /// <summary>
    /// 退出游戏
    /// </summary>
    private void QuitGame()
    {
        if (useAnimation)
        {
            PlayFadeOutAnimation(() => {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
        }
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
    
    /// <summary>
    /// 播放淡入动画
    /// </summary>
    private void PlayFadeInAnimation()
    {
        if (canvasGroup == null) return;
        
        isAnimating = true;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        
        StartCoroutine(FadeInCoroutine());
    }
    
    /// <summary>
    /// 播放淡出动画
    /// </summary>
    private void PlayFadeOutAnimation(System.Action onComplete = null)
    {
        if (canvasGroup == null)
        {
            onComplete?.Invoke();
            return;
        }
        
        isAnimating = true;
        canvasGroup.interactable = false;
        
        StartCoroutine(FadeOutCoroutine(onComplete));
    }
    
    /// <summary>
    /// 淡入协程
    /// </summary>
    private System.Collections.IEnumerator FadeInCoroutine()
    {
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            
            // 使用平滑曲线
            progress = Mathf.SmoothStep(0f, 1f, progress);
            
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, progress);
            yield return null;
        }
        
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        isAnimating = false;
    }
    
    /// <summary>
    /// 淡出协程
    /// </summary>
    private System.Collections.IEnumerator FadeOutCoroutine(System.Action onComplete = null)
    {
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            
            // 使用平滑曲线
            progress = Mathf.SmoothStep(0f, 1f, progress);
            
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
            yield return null;
        }
        
        canvasGroup.alpha = 0f;
        isAnimating = false;
        onComplete?.Invoke();
    }
    
    private void OnEnable()
    {
        // 订阅数据更新事件
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.OnHighestDayUpdated += OnHighestDayUpdated;
        }
    }
    
    private void OnDisable()
    {
        // 取消订阅事件
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.OnHighestDayUpdated -= OnHighestDayUpdated;
        }
    }
    
    /// <summary>
    /// 最高天数更新回调
    /// </summary>
    private void OnHighestDayUpdated(int newHighestDay)
    {
        UpdateHighestDayDisplay();
    }
}