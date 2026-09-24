using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class GifPlayer : MonoBehaviour
{
    [Header("GIF Settings")]
    public Texture2D[] frames; // GIF的所有帧
    public float framesPerSecond = 24f; // 播放速度
    
    private RawImage rawImage;
    private int currentFrame = 0;
    private float frameTimer = 0f;
    private bool isPlaying = false;
    
    void Awake()
    {
        rawImage = GetComponent<RawImage>();
        if (rawImage == null)
        {
            Debug.LogError("GifPlayer requires a RawImage component");
            enabled = false;
            return;
        }
        
        if (frames == null || frames.Length == 0)
        {
            Debug.LogWarning("No frames assigned to GifPlayer");
            enabled = false;
            return;
        }
    }
    
    void OnEnable()
    {
        // 当对象启用时开始播放
        StartPlaying();
    }
    
    void OnDisable()
    {
        // 当对象禁用时停止播放
        StopPlaying();
    }
    
    public void StartPlaying()
    {
        if (frames != null && frames.Length > 0)
        {
            isPlaying = true;
            currentFrame = 0;
            frameTimer = 0f;
            UpdateFrame();
        }
    }
    
    public void StopPlaying()
    {
        isPlaying = false;
    }
    
    void Update()
    {
        if (!isPlaying || frames == null || frames.Length == 0)
            return;
            
        frameTimer += Time.deltaTime;
        float frameDuration = 1f / framesPerSecond;
        
        if (frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            currentFrame = (currentFrame + 1) % frames.Length;
            UpdateFrame();
        }
    }
    
    void UpdateFrame()
    {
        if (rawImage != null && currentFrame < frames.Length && frames[currentFrame] != null)
        {
            rawImage.texture = frames[currentFrame];
        }
    }
    
    // 辅助方法：从文件夹加载序列帧
    public void LoadFramesFromFolder(string folderPath)
    {
        // 注意：此方法仅在编辑器中工作，不适用于构建后的游戏
        // 在实际项目中，应该在编辑器中预先设置好frames数组
#if UNITY_EDITOR
        string[] filePaths = System.IO.Directory.GetFiles(folderPath, "*.png");
        System.Array.Sort(filePaths); // 确保按名称排序
        
        frames = new Texture2D[filePaths.Length];
        
        for (int i = 0; i < filePaths.Length; i++)
        {
            Texture2D texture = new Texture2D(2, 2);
            byte[] fileData = System.IO.File.ReadAllBytes(filePaths[i]);
            texture.LoadImage(fileData);
            frames[i] = texture;
        }
        
        if (frames.Length > 0 && rawImage != null)
        {
            rawImage.texture = frames[0];
        }
#endif
    }
}