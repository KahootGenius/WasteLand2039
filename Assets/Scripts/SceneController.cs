using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景控制器 - 管理场景切换和游戏流程
/// </summary>
public class SceneController : MonoBehaviour
{
    public static SceneController Instance { get; private set; }
    
    [Header("场景名称")]
    public string mainMenuSceneName = "MainMenu";
    public string gameSceneName = "GameScene";
    public string loadingSceneName = "LoadingScene";
    
    [Header("加载设置")]
    public bool useLoadingScreen = true;
    public float minimumLoadingTime = 1.0f;
    
    // 当前场景状态
    private string currentSceneName;
    private bool isLoading = false;
    
    // 事件
    public System.Action<string> OnSceneLoadStarted;
    public System.Action<string> OnSceneLoadCompleted;
    
    private void Awake()
    {
        // 单例模式
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            currentSceneName = SceneManager.GetActiveScene().name;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        // 订阅场景加载事件
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    private void OnDestroy()
    {
        // 取消订阅事件
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    /// <summary>
    /// 场景加载完成回调
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        currentSceneName = scene.name;
        isLoading = false;
        
        Debug.Log($"[SceneController] 场景加载完成: {scene.name}");
        OnSceneLoadCompleted?.Invoke(scene.name);
        
        // 场景加载后的初始化
        InitializeSceneSpecificSystems(scene.name);
    }
    
    /// <summary>
    /// 初始化场景特定的系统
    /// </summary>
    private void InitializeSceneSpecificSystems(string sceneName)
    {
        switch (sceneName)
        {
            case "MainMenu":
                InitializeMainMenu();
                break;
            case "GameScene":
                InitializeGameScene();
                break;
        }
    }
    
    /// <summary>
    /// 初始化主菜单
    /// </summary>
    private void InitializeMainMenu()
    {
        // 确保GameDataManager存在
        if (GameDataManager.Instance == null)
        {
            GameObject gameDataManagerPrefab = Resources.Load<GameObject>("GameDataManager");
            if (gameDataManagerPrefab != null)
            {
                Instantiate(gameDataManagerPrefab);
            }
        }
        
        // 设置光标状态
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        Debug.Log("[SceneController] 主菜单初始化完成");
    }
    
    /// <summary>
    /// 初始化游戏场景
    /// </summary>
    private void InitializeGameScene()
    {
        // 确保必要的管理器存在
        EnsureGameManagerExists();
        
        // 设置游戏状态
        if (GameDataManager.Instance != null)
        {
            // 如果是新游戏，设置初始状态
            if (!GameDataManager.Instance.HasExistingSave())
            {
                GameDataManager.Instance.SetCurrentDay(1);
            }
        }
        
        Debug.Log("[SceneController] 游戏场景初始化完成");
    }
    
    /// <summary>
    /// 确保GameManager存在
    /// </summary>
    private void EnsureGameManagerExists()
    {
        if (GameManager.Instance == null)
        {
            GameObject gameManagerPrefab = Resources.Load<GameObject>("GameManager");
            if (gameManagerPrefab != null)
            {
                Instantiate(gameManagerPrefab);
            }
        }
    }
    
    /// <summary>
    /// 加载主菜单
    /// </summary>
    public void LoadMainMenu()
    {
        if (isLoading) return;
        
        Debug.Log("[SceneController] 加载主菜单");
        LoadScene(mainMenuSceneName);
    }
    
    /// <summary>
    /// 加载游戏场景
    /// </summary>
    public void LoadGameScene()
    {
        if (isLoading) return;
        
        Debug.Log("[SceneController] 加载游戏场景");
        LoadScene(gameSceneName);
    }
    
    /// <summary>
    /// 重新加载当前场景
    /// </summary>
    public void ReloadCurrentScene()
    {
        if (isLoading) return;
        
        Debug.Log($"[SceneController] 重新加载当前场景: {currentSceneName}");
        LoadScene(currentSceneName);
    }
    
    /// <summary>
    /// 加载指定场景
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (isLoading || string.IsNullOrEmpty(sceneName)) return;
        
        isLoading = true;
        OnSceneLoadStarted?.Invoke(sceneName);
        
        Debug.Log($"[SceneController] 开始加载场景: {sceneName}");
        
        if (useLoadingScreen && !string.IsNullOrEmpty(loadingSceneName))
        {
            StartCoroutine(LoadSceneWithLoadingScreen(sceneName));
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }
    
    /// <summary>
    /// 带加载界面的场景加载
    /// </summary>
    private System.Collections.IEnumerator LoadSceneWithLoadingScreen(string targetSceneName)
    {
        // 先加载加载界面
        AsyncOperation loadingOperation = SceneManager.LoadSceneAsync(loadingSceneName);
        yield return loadingOperation;
        
        // 等待最小加载时间
        float startTime = Time.time;
        
        // 异步加载目标场景
        AsyncOperation targetOperation = SceneManager.LoadSceneAsync(targetSceneName);
        targetOperation.allowSceneActivation = false;
        
        // 等待加载完成或达到最小时间
        while (!targetOperation.isDone)
        {
            float progress = Mathf.Clamp01(targetOperation.progress / 0.9f);
            
            // 更新加载进度（如果有加载界面UI的话）
            UpdateLoadingProgress(progress);
            
            // 检查是否可以激活场景
            if (targetOperation.progress >= 0.9f && Time.time - startTime >= minimumLoadingTime)
            {
                targetOperation.allowSceneActivation = true;
            }
            
            yield return null;
        }
    }
    
    /// <summary>
    /// 更新加载进度
    /// </summary>
    private void UpdateLoadingProgress(float progress)
    {
        // 这里可以更新加载界面的进度条
        // 如果有LoadingUI脚本的话
    }
    
    /// <summary>
    /// 获取当前场景名称
    /// </summary>
    public string GetCurrentSceneName()
    {
        return currentSceneName;
    }
    
    /// <summary>
    /// 检查是否正在加载
    /// </summary>
    public bool IsLoading()
    {
        return isLoading;
    }
    
    /// <summary>
    /// 退出游戏
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[SceneController] 退出游戏");
        
        // 保存游戏数据
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.SaveGameData();
        }
        
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}