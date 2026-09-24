using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 暂停菜单UI控制器
/// 处理暂停菜单的UI交互和视觉效果
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    [Header("UI组件")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button survivalManualButton;
    [SerializeField] private Button saveAndQuitButton;
    
    [Header("UI Settings")]
    [SerializeField] private string titleString = "Game Paused";
    [SerializeField] private string continueButtonText = "Continue";
    [SerializeField] private string survivalManualButtonText = "Survival Manual";
    [SerializeField] private string saveAndQuitButtonText = "Save & Quit";
    
    [Header("动画设置")]
    [SerializeField] private bool useAnimation = true;
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private float fadeOutDuration = 0.2f;
    
    private CanvasGroup canvasGroup;
    private bool isAnimating = false;
    
    private void Awake()
    {
        InitializeUI();
    }
    
    private void InitializeUI()
    {
        // 获取或添加CanvasGroup组件
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        // 设置初始状态
        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }
        
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        
        // 设置文本内容
        SetupTexts();
        
        Debug.Log("[PauseMenuUI] 暂停菜单UI初始化完成");
    }
    
    private void SetupTexts()
    {
        if (titleText != null)
        {
            titleText.text = titleString;
        }
        
        if (continueButton != null && continueButton.GetComponentInChildren<TextMeshProUGUI>() != null)
        {
            continueButton.GetComponentInChildren<TextMeshProUGUI>().text = continueButtonText;
        }
        
        if (survivalManualButton != null && survivalManualButton.GetComponentInChildren<TextMeshProUGUI>() != null)
        {
            survivalManualButton.GetComponentInChildren<TextMeshProUGUI>().text = survivalManualButtonText;
        }
        
        if (saveAndQuitButton != null && saveAndQuitButton.GetComponentInChildren<TextMeshProUGUI>() != null)
        {
            saveAndQuitButton.GetComponentInChildren<TextMeshProUGUI>().text = saveAndQuitButtonText;
        }
    }
    
    /// <summary>
    /// 显示暂停菜单
    /// </summary>
    public void ShowPauseMenu()
    {
        if (isAnimating) return;
        
        if (pausePanel != null)
        {
            pausePanel.SetActive(true);
        }
        
        if (useAnimation)
        {
            StartCoroutine(FadeIn());
        }
        else
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        
        Debug.Log("[PauseMenuUI] 显示暂停菜单");
    }
    
    /// <summary>
    /// 隐藏暂停菜单
    /// </summary>
    public void HidePauseMenu()
    {
        if (isAnimating) return;
        
        if (useAnimation)
        {
            StartCoroutine(FadeOut());
        }
        else
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            
            if (pausePanel != null)
            {
                pausePanel.SetActive(false);
            }
        }
        
        Debug.Log("[PauseMenuUI] 隐藏暂停菜单");
    }
    
    private System.Collections.IEnumerator FadeIn()
    {
        isAnimating = true;
        
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;
        
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;
        
        while (elapsedTime < fadeInDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / fadeInDuration;
            
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, progress);
            
            yield return null;
        }
        
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        isAnimating = false;
    }
    
    private System.Collections.IEnumerator FadeOut()
    {
        isAnimating = true;
        
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;
        
        canvasGroup.interactable = false;
        
        while (elapsedTime < fadeOutDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / fadeOutDuration;
            
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
            
            yield return null;
        }
        
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        
        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }
        
        isAnimating = false;
    }
    
    /// <summary>
    /// 设置按钮交互状态
    /// </summary>
    public void SetButtonsInteractable(bool interactable)
    {
        if (continueButton != null)
        {
            continueButton.interactable = interactable;
        }
        
        if (survivalManualButton != null)
        {
            survivalManualButton.interactable = interactable;
        }
        
        if (saveAndQuitButton != null)
        {
            saveAndQuitButton.interactable = interactable;
        }
    }
    
    /// <summary>
    /// 获取是否正在播放动画
    /// </summary>
    public bool IsAnimating => isAnimating;
    
    /// <summary>
    /// 获取是否可见
    /// </summary>
    public bool IsVisible => canvasGroup.alpha > 0f;
}