using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 开场指引管理器
/// 使用Unity 2D Light系统实现聚光灯效果
/// </summary>
public class TutorialGuideManager : MonoBehaviour
{
    [Header("指引设置")]
    [SerializeField] private bool enableTutorialOnStart = true;
    [SerializeField] private float spotlightDuration = 3f; // 聚光灯持续时间（秒）
    
    [Header("聚光灯设置")]
    [SerializeField] private float spotlightIntensity = 1.5f; // 聚光灯强度
    [SerializeField] private float spotlightRadius = 3f; // 聚光灯半径
    [SerializeField] private Color spotlightColor = Color.white; // 聚光灯颜色
    [SerializeField] private float fadeInDuration = 0.5f; // 淡入持续时间
    [SerializeField] private float fadeOutDuration = 0.5f; // 淡出持续时间
    
    [Header("目标设置")]
    [SerializeField] private Transform targetNPC; // 目标NPC位置
    [SerializeField] private Vector3 spotlightOffset = Vector3.zero; // 聚光灯偏移
    
    private static TutorialGuideManager instance;
    public static TutorialGuideManager Instance => instance;
    
    private bool isTutorialActive = false;
    private Light2D tutorialSpotlight;
    private List<Light2D> originalLights = new List<Light2D>();
    private List<float> originalIntensities = new List<float>();
    private Coroutine tutorialCoroutine;
    private Coroutine autoHideCoroutine;
    
    private void Awake()
    {
        // 单例模式
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        if (enableTutorialOnStart)
        {
            StartTutorial();
        }
    }
    
    /// <summary>
    /// 开始指引
    /// </summary>
    public void StartTutorial()
    {
        if (isTutorialActive) return;
        
        Debug.Log("[TutorialGuideManager] 开始开场指引");
        isTutorialActive = true;
        
        if (tutorialCoroutine != null)
        {
            StopCoroutine(tutorialCoroutine);
        }
        
        tutorialCoroutine = StartCoroutine(TutorialSequence());
    }
    
    /// <summary>
    /// 指引序列协程
    /// </summary>
    private IEnumerator TutorialSequence()
    {
        // 收集场景中所有的Light2D组件
        CollectSceneLights();
        
        // 创建聚光灯
        CreateTutorialSpotlight();
        
        // 调暗其他光源
        yield return StartCoroutine(DimOtherLights());
        
        // 淡入聚光灯
        yield return StartCoroutine(FadeInSpotlight());
        
        // 启动自动隐藏计时器
        if (autoHideCoroutine != null)
        {
            StopCoroutine(autoHideCoroutine);
        }
        autoHideCoroutine = StartCoroutine(AutoHideSpotlight());
        
        Debug.Log($"[TutorialGuideManager] 指引效果已激活，将在{spotlightDuration}秒后自动消失");
    }
    
    /// <summary>
    /// 收集场景中的所有Light2D组件
    /// </summary>
    private void CollectSceneLights()
    {
        originalLights.Clear();
        originalIntensities.Clear();
        
        Light2D[] allLights = FindObjectsOfType<Light2D>();
        
        foreach (Light2D light in allLights)
        {
            if (light != tutorialSpotlight) // 排除我们自己创建的聚光灯
            {
                originalLights.Add(light);
                originalIntensities.Add(light.intensity);
            }
        }
        
        Debug.Log($"[TutorialGuideManager] 收集到 {originalLights.Count} 个场景光源");
    }
    
    /// <summary>
    /// 创建指引聚光灯
    /// </summary>
    private void CreateTutorialSpotlight()
    {
        if (tutorialSpotlight != null)
        {
            DestroyImmediate(tutorialSpotlight.gameObject);
        }
        
        GameObject spotlightGO = new GameObject("TutorialSpotlight");
        spotlightGO.transform.SetParent(transform);
        
        // 设置位置
        Vector3 targetPosition = targetNPC != null ? targetNPC.position + spotlightOffset : Vector3.zero;
        spotlightGO.transform.position = targetPosition;
        
        // 添加Light2D组件
        tutorialSpotlight = spotlightGO.AddComponent<Light2D>();
        tutorialSpotlight.lightType = Light2D.LightType.Point;
        tutorialSpotlight.intensity = 0f; // 初始强度为0，用于淡入效果
        tutorialSpotlight.pointLightOuterRadius = spotlightRadius;
        tutorialSpotlight.pointLightInnerRadius = spotlightRadius * 0.3f;
        tutorialSpotlight.color = spotlightColor;
        tutorialSpotlight.blendStyleIndex = 0; // 使用默认混合模式
        
        Debug.Log($"[TutorialGuideManager] 在位置 {targetPosition} 创建聚光灯");
    }
    
    /// <summary>
    /// 调暗其他光源
    /// </summary>
    private IEnumerator DimOtherLights()
    {
        float elapsedTime = 0f;
        
        while (elapsedTime < fadeInDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fadeInDuration;
            
            // 逐渐降低其他光源的强度
            for (int i = 0; i < originalLights.Count; i++)
            {
                if (originalLights[i] != null)
                {
                    float targetIntensity = originalIntensities[i] * 0.05f; // 降低到原来的5%
                    originalLights[i].intensity = Mathf.Lerp(originalIntensities[i], targetIntensity, progress);
                }
            }
            
            yield return null;
        }
        
        // 确保最终值正确
        for (int i = 0; i < originalLights.Count; i++)
        {
            if (originalLights[i] != null)
            {
                originalLights[i].intensity = originalIntensities[i] * 0.05f;
            }
        }
    }
    
    /// <summary>
    /// 淡入聚光灯
    /// </summary>
    private IEnumerator FadeInSpotlight()
    {
        if (tutorialSpotlight == null) yield break;
        
        float elapsedTime = 0f;
        
        while (elapsedTime < fadeInDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fadeInDuration;
            
            tutorialSpotlight.intensity = Mathf.Lerp(0f, spotlightIntensity, progress);
            
            yield return null;
        }
        
        tutorialSpotlight.intensity = spotlightIntensity;
    }
    
    /// <summary>
    /// 自动隐藏聚光灯协程
    /// </summary>
    private IEnumerator AutoHideSpotlight()
    {
        yield return new WaitForSeconds(spotlightDuration);
        EndTutorial();
    }
    
    /// <summary>
    /// 结束指引
    /// </summary>
    public void EndTutorial()
    {
        if (!isTutorialActive) return;
        
        Debug.Log("[TutorialGuideManager] 结束开场指引");
        
        // 停止所有协程
        if (tutorialCoroutine != null)
        {
            StopCoroutine(tutorialCoroutine);
            tutorialCoroutine = null;
        }
        
        if (autoHideCoroutine != null)
        {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }
        
        StartCoroutine(FadeOutTutorial());
    }
    
    /// <summary>
    /// 淡出指引效果
    /// </summary>
    private IEnumerator FadeOutTutorial()
    {
        float elapsedTime = 0f;
        
        // 同时淡出聚光灯和恢复其他光源
        while (elapsedTime < fadeOutDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fadeOutDuration;
            
            // 淡出聚光灯
            if (tutorialSpotlight != null)
            {
                tutorialSpotlight.intensity = Mathf.Lerp(spotlightIntensity, 0f, progress);
            }
            
            // 恢复其他光源
            for (int i = 0; i < originalLights.Count; i++)
            {
                if (originalLights[i] != null)
                {
                    float currentIntensity = originalIntensities[i] * 0.05f;
                    originalLights[i].intensity = Mathf.Lerp(currentIntensity, originalIntensities[i], progress);
                }
            }
            
            yield return null;
        }
        
        // 确保最终值正确
        for (int i = 0; i < originalLights.Count; i++)
        {
            if (originalLights[i] != null)
            {
                originalLights[i].intensity = originalIntensities[i];
            }
        }
        
        // 销毁聚光灯
        if (tutorialSpotlight != null)
        {
            DestroyImmediate(tutorialSpotlight.gameObject);
            tutorialSpotlight = null;
        }
        
        isTutorialActive = false;
        
        Debug.Log("[TutorialGuideManager] 指引效果已结束");
    }
    
    /// <summary>
    /// 更新目标NPC
    /// </summary>
    /// <param name="npc">新的目标NPC</param>
    public void SetTargetNPC(Transform npc)
    {
        targetNPC = npc;
        if (isTutorialActive && targetNPC != null && tutorialSpotlight != null)
        {
            tutorialSpotlight.transform.position = targetNPC.position + spotlightOffset;
        }
    }
    
    /// <summary>
    /// 设置聚光灯位置
    /// </summary>
    /// <param name="worldPosition">世界坐标位置</param>
    public void SetSpotlightPosition(Vector3 worldPosition)
    {
        if (tutorialSpotlight != null)
        {
            tutorialSpotlight.transform.position = worldPosition;
        }
    }
    
    /// <summary>
    /// 检查指引是否激活
    /// </summary>
    public bool IsTutorialActive => isTutorialActive;
    
    private void Update()
    {
        // 如果指引激活且有目标NPC，实时更新聚光灯位置
        if (isTutorialActive && targetNPC != null && tutorialSpotlight != null)
        {
            tutorialSpotlight.transform.position = targetNPC.position + spotlightOffset;
        }
    }
    
    private void OnDestroy()
    {
        // 清理资源
        if (tutorialSpotlight != null)
        {
            DestroyImmediate(tutorialSpotlight.gameObject);
        }
    }
}