using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class IntroController : MonoBehaviour
{
    [Header("GIF Images")]
    public RawImage normalEarthImage; // 正常的绿色地球GIF
    public RawImage infectedEarthImage; // 被病毒侵蚀的红色地球GIF
    
    [Header("Story Text")]
    public TextMeshProUGUI storyText; // 剧情文本
    public float textFadeInSpeed = 1f; // 文字淡入速度
    public float textDisplayDuration = 5f; // 文字显示持续时间
    public float textFadeOutSpeed = 1f; // 文字淡出速度
    
    [Header("Transition Settings")]
    public float transitionDuration = 2f; // 两个地球之间的过渡时间
    
    [Header("Story Content")]
    [TextArea(3, 10)]
    public string[] storyLines; // 剧情文本数组
    
    #pragma warning disable CS0414 // 字段已分配但从未使用过
    private int currentStoryIndex = 0;
    #pragma warning restore CS0414
    
    void Start()
    {
        // 初始化设置
        if (normalEarthImage != null)
            normalEarthImage.gameObject.SetActive(true);
            
        if (infectedEarthImage != null)
            infectedEarthImage.gameObject.SetActive(false);
            
        if (storyText != null)
        {
            storyText.text = "";
            storyText.alpha = 0f;
        }
        
        // 开始播放序列
        StartCoroutine(PlayIntroSequence());
    }
    
    IEnumerator PlayIntroSequence()
    {
        // 显示正常地球
        yield return StartCoroutine(ShowNormalEarth());
        
        // 显示第一部分剧情
        yield return StartCoroutine(ShowStoryText(0));
        
        // 过渡到被感染的地球
        yield return StartCoroutine(TransitionToInfectedEarth());
        
        // 显示第二部分剧情
        yield return StartCoroutine(ShowStoryText(1));
        
        // 如果有更多剧情，可以继续添加
        for (int i = 2; i < storyLines.Length; i++)
        {
            yield return StartCoroutine(ShowStoryText(i));
        }
        
        // 序列结束后的操作，例如加载主游戏场景
        Debug.Log("Intro sequence completed");
        SceneManager.LoadScene("MainGame");
    }
    
    IEnumerator ShowNormalEarth()
    {
        if (normalEarthImage != null)
        {
            normalEarthImage.gameObject.SetActive(true);
            // 可以添加淡入效果
            yield return new WaitForSeconds(2f); // 给玩家时间观察正常地球
        }
        else
            yield return null;
    }
    
    IEnumerator TransitionToInfectedEarth()
    {
        if (normalEarthImage != null && infectedEarthImage != null)
        {
            // 激活被感染的地球图像
            infectedEarthImage.gameObject.SetActive(true);
            
            float elapsedTime = 0f;
            Color normalColor = normalEarthImage.color;
            Color infectedColor = infectedEarthImage.color;
            
            // 设置初始透明度
            normalColor.a = 1f;
            infectedColor.a = 0f;
            normalEarthImage.color = normalColor;
            infectedEarthImage.color = infectedColor;
            
            // 渐变效果
            while (elapsedTime < transitionDuration)
            {
                elapsedTime += Time.deltaTime;
                float t = elapsedTime / transitionDuration;
                
                normalColor.a = 1 - t;
                infectedColor.a = t;
                
                normalEarthImage.color = normalColor;
                infectedEarthImage.color = infectedColor;
                
                yield return null;
            }
            
            // 确保完全过渡
            normalColor.a = 0f;
            infectedColor.a = 1f;
            normalEarthImage.color = normalColor;
            infectedEarthImage.color = infectedColor;
            
            // 可以选择禁用正常地球图像以节省资源
            normalEarthImage.gameObject.SetActive(false);
            
            yield return new WaitForSeconds(1f); // 给玩家时间观察被感染的地球
        }
        else
            yield return null;
    }
    
    IEnumerator ShowStoryText(int index)
    {
        if (storyText != null && index < storyLines.Length)
        {
            // 设置文本内容
            storyText.text = storyLines[index];
            
            // 文字淡入
            float elapsedTime = 0f;
            while (elapsedTime < 1f / textFadeInSpeed)
            {
                elapsedTime += Time.deltaTime;
                float alpha = Mathf.Clamp01(elapsedTime * textFadeInSpeed);
                storyText.alpha = alpha;
                yield return null;
            }
            storyText.alpha = 1f;
            
            // 显示文字一段时间
            yield return new WaitForSeconds(textDisplayDuration);
            
            // 文字淡出
            elapsedTime = 0f;
            while (elapsedTime < 1f / textFadeOutSpeed)
            {
                elapsedTime += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(elapsedTime * textFadeOutSpeed);
                storyText.alpha = alpha;
                yield return null;
            }
            storyText.alpha = 0f;
            
            // 短暂停顿
            yield return new WaitForSeconds(0.5f);
        }
        else
            yield return null;
    }
}