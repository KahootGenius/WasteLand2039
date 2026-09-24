using System.Diagnostics;
using Object = UnityEngine.Object;

/// <summary>
/// 高频信息日志（战斗循环、生成、伤害等）。
/// 默认不输出：调用及其参数（包括字符串插值）会在编译时被完全移除，零开销，
/// 避免加速运行（如ML训练时 Time.timeScale = 20）时被日志拖慢。
/// 需要查看时：Project Settings > Player > Scripting Define Symbols 中添加 VERBOSE_LOGS。
/// 警告和错误请继续使用 Debug.LogWarning / Debug.LogError。
/// </summary>
public static class VerboseLog
{
    [Conditional("VERBOSE_LOGS")]
    public static void Log(object message)
    {
        UnityEngine.Debug.Log(message);
    }

    [Conditional("VERBOSE_LOGS")]
    public static void Log(object message, Object context)
    {
        UnityEngine.Debug.Log(message, context);
    }
}
