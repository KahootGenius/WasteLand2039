using System;
using UnityEngine;

/// <summary>
/// 训练场回合的开始 / 结束通知。指挥官（HordeEventSpawner 使用的 IHordeCommander）实现此接口时，
/// ArenaEnvironment 在每个回合开始前和结束后调用它（例如强化学习指挥官据此开始 / 结束训练回合、读取课程等级）。
/// </summary>
public interface IArenaEpisodeListener
{
    /// <summary>回合开始前（选定机器人性格之前）</summary>
    void OnArenaEpisodeStarting(ArenaEnvironment arena);

    /// <summary>回合结束（最后一波结束之后）</summary>
    void OnArenaEpisodeEnded(ArenaEnvironment arena);
}

/// <summary>
/// 训练课程等级（全局）：性格池中 minLevel ≤ Level 的性格才会被抽到。
/// 训练时由学习型指挥官从训练器的环境参数更新；不训练时为最大值（所有性格都可用）。
/// </summary>
public static class ArenaCurriculum
{
    public const int AllPersonas = 99;

    public static int Level { get; set; } = AllPersonas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Level = AllPersonas;
    }
}

/// <summary>性格池中的一项：从课程等级 minLevel 起可被抽到</summary>
[Serializable]
public class PersonaPoolEntry
{
    public BotPersona persona;
    [Min(0)] public int minLevel;
}

/// <summary>训练场的遥测记录量（长时间训练时关闭以节省磁盘）</summary>
public enum ArenaTelemetry
{
    /// <summary>事件 + 每次采样（评估、调试）</summary>
    Full,
    /// <summary>只写事件，不写 samples.csv</summary>
    EventsOnly,
    /// <summary>不写任何文件</summary>
    Off
}
