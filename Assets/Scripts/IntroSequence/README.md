# 游戏开场动画系统使用指南

本系统用于创建一个展示两个地球GIF（正常绿色地球和被病毒侵蚀的红色地球）的开场动画，并配有文字剧情描述。

## 设置步骤

### 1. 准备GIF素材

由于Unity不直接支持GIF格式，我们需要将GIF转换为序列帧：

1. 使用在线工具（如 [ezgif.com](https://ezgif.com/split) 或 [GIF Explode](https://gif-explode.com/)）将GIF分解为PNG序列帧
2. 将所有帧导入Unity项目中
3. 确保帧按正确顺序命名（例如：earth_normal_001.png, earth_normal_002.png 等）

### 2. 创建UI界面

1. 创建一个新的场景，命名为 "IntroScene"
2. 创建UI Canvas（右键点击Hierarchy > UI > Canvas）
3. 在Canvas下创建以下UI元素：
   - 两个RawImage组件用于显示地球GIF
   - 一个TextMeshPro - Text组件用于显示剧情文本

### 3. 设置GIF播放器

1. 选择第一个RawImage（正常地球），添加 `GifPlayer` 组件
2. 在Inspector中设置 `GifPlayer` 组件：
   - 设置Frames数组大小为GIF帧数
   - 将每一帧的PNG图像拖拽到Frames数组中
   - 调整Frames Per Second到合适的值（通常为24或30）
3. 对第二个RawImage（被感染地球）重复上述步骤

### 4. 设置IntroController

1. 创建一个空游戏对象，命名为 "IntroController"
2. 添加 `IntroController` 脚本到该对象
3. 在Inspector中设置 `IntroController` 组件：
   - 将两个RawImage拖拽到相应的字段中
   - 将TextMeshPro组件拖拽到Story Text字段
   - 设置过渡和文字显示的时间参数
   - 在Story Lines中添加剧情文本（可以添加多行）

## 示例剧情文本

以下是一些示例剧情文本，您可以根据游戏背景进行修改：

1. "2023年，地球依然美丽，人类文明繁荣发展..."
2. "2024年，一种未知病毒开始在全球蔓延，世界陷入混乱..."
3. "你是最后的幸存者之一，肩负着拯救人类的使命..."

## 高级定制

### 添加音效

1. 创建一个Audio Source组件
2. 添加背景音乐或过渡音效
3. 在 `IntroController` 脚本中添加代码控制音频播放

### 添加场景转换

在 `IntroController.cs` 中取消注释以下代码并设置您的主游戏场景名称：

```csharp
// 在PlayIntroSequence方法的末尾
SceneManager.LoadScene("MainGame");
```

确保添加 `using UnityEngine.SceneManagement;` 到文件顶部。

### 添加跳过功能

您可以添加以下代码到 `IntroController.cs` 的Update方法中，允许玩家按任意键跳过开场动画：

```csharp
void Update()
{
    // 检测任意键或鼠标点击
    if (Input.anyKeyDown || Input.GetMouseButtonDown(0))
    {
        // 停止所有协程
        StopAllCoroutines();
        // 直接加载主游戏场景
        SceneManager.LoadScene("MainGame");
    }
}
```

## 故障排除

1. **GIF不播放**：确保所有帧都正确导入，并且GifPlayer组件已正确设置
2. **文字不显示**：检查TextMeshPro组件是否正确引用，以及字体设置是否正确
3. **过渡效果不流畅**：尝试调整transitionDuration参数

## 注意事项

- 确保在Build Settings中包含IntroScene
- 如果使用TextMeshPro，请确保已导入TextMeshPro Essential Resources
- 对于较大的GIF，考虑降低分辨率以提高性能