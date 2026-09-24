using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// 生存手册UI控制器
/// 处理手册的显示、换页和交互
/// </summary>
public class SurvivalManualUI : MonoBehaviour
{
    [Header("UI组件")]
    [SerializeField] private GameObject manualPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI contentText;
    [SerializeField] private TextMeshProUGUI pageInfoText;
    [SerializeField] private Image illustrationImage;
    
    [Header("导航按钮")]
    [SerializeField] private Button previousPageButton;
    [SerializeField] private Button nextPageButton;
    [SerializeField] private Button closeButton;
    
    [Header("动画设置")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool useAnimation = true;
    [SerializeField] private float animationDuration = 0.3f;
    
    [Header("数据配置")]
    [SerializeField] private SurvivalManualData manualData;
    
    // 当前状态
    private int currentPageIndex = 0;
    private bool isAnimating = false;
    private bool isVisible = false;
    private System.Collections.Generic.List<ManualPage> allPages = new System.Collections.Generic.List<ManualPage>();
    
    // 文本配置
    [Header("UI Text Configuration")]
    [SerializeField] private string closeButtonText = "Close";
    [SerializeField] private string previousPageText = "Previous";
    [SerializeField] private string nextPageText = "Next";
    
    private void Awake()
    {
        InitializeComponents();
        SetupButtons();
        
        // 初始状态为隐藏
        if (manualPanel != null)
        {
            manualPanel.SetActive(false);
        }
    }
    
    private void Start()
    {
        // 如果没有配置数据，尝试加载默认数据
        if (manualData == null)
        {
            manualData = Resources.Load<SurvivalManualData>("SurvivalManualData");
            if (manualData == null)
            {
                Debug.LogWarning("[SurvivalManualUI] 未找到SurvivalManualData，请在Resources文件夹中创建");
            }
        }
        
        InitializeUI();
        InitializeAllPages();
        UpdateContent();
    }
    
    /// <summary>
    /// 初始化UI组件
    /// </summary>
    private void InitializeComponents()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
    }
    
    /// <summary>
    /// 设置按钮事件
    /// </summary>
    private void SetupButtons()
    {
        if (previousPageButton != null)
        {
            previousPageButton.onClick.AddListener(PreviousPage);
        }
        
        if (nextPageButton != null)
        {
            nextPageButton.onClick.AddListener(NextPage);
        }
        
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseManual);
        }
    }
    
    /// <summary>
    /// 初始化UI文本
    /// </summary>
    private void InitializeUI()
    {
        // 设置按钮文本
        if (closeButton != null && closeButton.GetComponentInChildren<TextMeshProUGUI>() != null)
        {
            closeButton.GetComponentInChildren<TextMeshProUGUI>().text = closeButtonText;
        }
        
        if (previousPageButton != null && previousPageButton.GetComponentInChildren<TextMeshProUGUI>() != null)
        {
            previousPageButton.GetComponentInChildren<TextMeshProUGUI>().text = previousPageText;
        }
        
        if (nextPageButton != null && nextPageButton.GetComponentInChildren<TextMeshProUGUI>() != null)
        {
            nextPageButton.GetComponentInChildren<TextMeshProUGUI>().text = nextPageText;
        }
    }
    
    /// <summary>
    /// 初始化所有页面数据
    /// </summary>
    private void InitializeAllPages()
    {
        allPages.Clear();
        if (manualData != null && manualData.topics != null)
        {
            foreach (var topic in manualData.topics)
            {
                if (topic.pages != null)
                {
                    allPages.AddRange(topic.pages);
                }
            }
        }
    }
    
    /// <summary>
    /// 显示生存手册
    /// </summary>
    public void ShowManual()
    {
        if (isAnimating || isVisible) return;
        
        if (manualPanel != null)
        {
            manualPanel.SetActive(true);
        }
        
        UpdateContent();
        
        if (useAnimation)
        {
            StartCoroutine(FadeIn());
        }
        else
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            isVisible = true;
        }
        
        Debug.Log("[SurvivalManualUI] 显示生存手册");
    }
    
    /// <summary>
    /// 隐藏生存手册
    /// </summary>
    public void HideManual()
    {
        if (isAnimating || !isVisible) return;
        
        if (useAnimation)
        {
            StartCoroutine(FadeOut());
        }
        else
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            isVisible = false;
            
            if (manualPanel != null)
            {
                manualPanel.SetActive(false);
            }
        }
        
        Debug.Log("[SurvivalManualUI] 隐藏生存手册");
    }
    
    /// <summary>
    /// 关闭手册
    /// </summary>
    public void CloseManual()
    {
        HideManual();
        
        // 通知PauseManager手册已关闭，返回暂停菜单
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.CloseSurvivalManual();
            Debug.Log("[SurvivalManualUI] 手册已关闭，返回暂停菜单");
        }
    }
    
    /// <summary>
    /// 上一页
    /// </summary>
    public void PreviousPage()
    {
        if (allPages.Count == 0 || isAnimating) return;
        
        currentPageIndex = (currentPageIndex - 1 + allPages.Count) % allPages.Count;
        UpdateContent();
        Debug.Log($"[SurvivalManualUI] 切换到上一页: {currentPageIndex + 1}");
    }
    
    /// <summary>
    /// 下一页
    /// </summary>
    public void NextPage()
    {
        if (allPages.Count == 0 || isAnimating) return;
        
        currentPageIndex = (currentPageIndex + 1) % allPages.Count;
        UpdateContent();
        Debug.Log($"[SurvivalManualUI] 切换到下一页: {currentPageIndex + 1}");
    }
    

    
    /// <summary>
    /// 更新内容显示
    /// </summary>
    private void UpdateContent()
    {
        if (allPages.Count == 0)
        {
            DisplayErrorMessage();
            return;
        }
        
        if (currentPageIndex >= allPages.Count)
        {
            currentPageIndex = 0;
        }
        
        // 获取当前页面数据
        var currentPage = allPages[currentPageIndex];
        if (currentPage == null)
        {
            DisplayErrorMessage();
            return;
        }
        
        // 更新标题
        if (titleText != null)
        {
            titleText.text = currentPage.title;
        }
        
        // 更新内容
        if (contentText != null)
        {
            contentText.text = currentPage.content;
        }
        
        // 更新页面信息
        if (pageInfoText != null)
        {
            pageInfoText.text = $"{currentPageIndex + 1} / {allPages.Count}";
        }
        
        // 更新插图
        if (illustrationImage != null)
        {
            if (currentPage.illustration != null)
            {
                illustrationImage.sprite = currentPage.illustration;
                illustrationImage.gameObject.SetActive(true);
            }
            else
            {
                illustrationImage.gameObject.SetActive(false);
            }
        }
        
        // 更新按钮状态
        UpdateButtonStates();
    }
    
    /// <summary>
    /// 更新按钮状态
    /// </summary>
    private void UpdateButtonStates()
    {
        // 页面导航按钮
        if (previousPageButton != null)
        {
            previousPageButton.interactable = allPages.Count > 1;
        }
        
        if (nextPageButton != null)
        {
            nextPageButton.interactable = allPages.Count > 1;
        }
    }
    
    /// <summary>
    /// 显示错误信息
    /// </summary>
    private void DisplayErrorMessage()
    {
        if (titleText != null)
        {
            titleText.text = "Error";
        }
        
        if (contentText != null)
        {
            contentText.text = "Unable to load manual content. Please check data configuration.";
        }
        
        if (pageInfoText != null)
        {
            pageInfoText.text = "0 / 0";
        }
        
        Debug.LogError("[SurvivalManualUI] 无法加载手册内容");
    }
    
    /// <summary>
    /// 淡入动画
    /// </summary>
    private IEnumerator FadeIn()
    {
        isAnimating = true;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;
        
        float elapsedTime = 0f;
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsedTime / animationDuration);
            yield return null;
        }
        
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        isAnimating = false;
        isVisible = true;
    }
    
    /// <summary>
    /// 淡出动画
    /// </summary>
    private IEnumerator FadeOut()
    {
        isAnimating = true;
        canvasGroup.interactable = false;
        
        float elapsedTime = 0f;
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsedTime / animationDuration);
            yield return null;
        }
        
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        isAnimating = false;
        isVisible = false;
        
        if (manualPanel != null)
        {
            manualPanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// 获取当前可见状态
    /// </summary>
    public bool IsVisible => isVisible;
    
    /// <summary>
    /// 获取当前动画状态
    /// </summary>
    public bool IsAnimating => isAnimating;
    
    /// <summary>
    /// 设置手册数据
    /// </summary>
    public void SetManualData(SurvivalManualData data)
    {
        manualData = data;
        InitializeAllPages();
        UpdateContent();
    }
    
    /// <summary>
    /// 跳转到指定页面
    /// </summary>
    public void NavigateToPage(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex < allPages.Count)
        {
            currentPageIndex = pageIndex;
            UpdateContent();
        }
    }
}