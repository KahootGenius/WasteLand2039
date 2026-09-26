/// <summary>
/// 学习型指挥官（V1 强化学习 / 之后的 V2）共用的动作空间（纯逻辑，可在 Unity 外测试）。两种方案：
///
/// PerSquad（v1_ppo_01）：Squads + 1 个分支
///   分支 0 .. Squads-1：各小队的命令  0 = 保持，1 = 自主，2 = 追击，3 + k = 驻守区域 k
///   分支 Squads：之后的增援加入哪个小队
///   缺点：随机策略每次决策几乎都会改派每个小队（"保持"只占 1/20），小队在区域间来回奔走、
///   永远到不了伏击点，很难发现"长时间驻守"的好处。
///
/// RetaskOne（默认）：3 个分支，每次决策最多改派一个小队
///   分支 0：改派哪个小队  0 = 不改派，1 + i = 小队 i
///   分支 1：给它的命令    0 = 自主，1 = 追击，2 + k = 驻守区域 k
///   分支 2：之后的增援加入哪个小队
///
/// 两种方案中，增援加入驻守小队时生成在其驻守区域（镜头外），否则按原版规则生成在玩家周围。
/// </summary>
public static class CommanderActions
{
    public enum Scheme
    {
        PerSquad,
        RetaskOne
    }

    public const int Squads = 3;

    // PerSquad 的小队命令编码
    public const int Keep = 0;
    public const int Autonomous = 1;
    public const int Chase = 2;
    public const int FirstHoldZone = 3;

    // RetaskOne 的命令编码（没有"保持"：不改派用分支 0 表示）
    public const int RetaskAutonomous = 0;
    public const int RetaskChase = 1;
    public const int RetaskFirstHoldZone = 2;

    public enum Kind
    {
        Keep,
        Autonomous,
        Chase,
        Hold
    }

    /// <summary>PerSquad：小队命令分支的选项数</summary>
    public static int SquadBranchSize(int zoneCount)
    {
        return FirstHoldZone + zoneCount;
    }

    /// <summary>全部分支的大小（传给 ActionSpec.MakeDiscrete）</summary>
    public static int[] BranchSizes(int zoneCount, Scheme scheme = Scheme.PerSquad)
    {
        if (scheme == Scheme.RetaskOne)
            return new[] { 1 + Squads, RetaskFirstHoldZone + zoneCount, Squads };

        var sizes = new int[Squads + 1];
        for (int i = 0; i < Squads; i++)
            sizes[i] = SquadBranchSize(zoneCount);
        sizes[Squads] = Squads;
        return sizes;
    }

    /// <summary>由动作屏蔽输入的长度（各分支大小之和）判断模型使用的方案；无法判断时返回 null</summary>
    public static Scheme? SchemeFromMaskSize(int maskSize, int zoneCount)
    {
        foreach (Scheme scheme in new[] { Scheme.PerSquad, Scheme.RetaskOne })
        {
            int total = 0;
            foreach (int size in BranchSizes(zoneCount, scheme))
                total += size;
            if (total == maskSize)
                return scheme;
        }
        return null;
    }

    /// <summary>PerSquad：解析小队命令；Hold 时 zone 为区域编号，否则为 -1</summary>
    public static Kind DecodeSquad(int action, int zoneCount, out int zone)
    {
        zone = -1;
        if (action == Autonomous)
            return Kind.Autonomous;
        if (action == Chase)
            return Kind.Chase;
        if (action >= FirstHoldZone && action < FirstHoldZone + zoneCount)
        {
            zone = action - FirstHoldZone;
            return Kind.Hold;
        }
        return Kind.Keep;
    }

    /// <summary>RetaskOne：解析分支 0（改派哪个小队）；不改派时返回 -1</summary>
    public static int DecodeRetaskSquad(int action)
    {
        return action >= 1 && action <= Squads ? action - 1 : -1;
    }

    /// <summary>RetaskOne：解析分支 1（命令）；Hold 时 zone 为区域编号，否则为 -1</summary>
    public static Kind DecodeRetaskOrder(int action, int zoneCount, out int zone)
    {
        zone = -1;
        if (action == RetaskAutonomous)
            return Kind.Autonomous;
        if (action == RetaskChase)
            return Kind.Chase;
        if (action >= RetaskFirstHoldZone && action < RetaskFirstHoldZone + zoneCount)
        {
            zone = action - RetaskFirstHoldZone;
            return Kind.Hold;
        }
        return Kind.Keep;
    }

    public static int EncodeHold(int zone)
    {
        return FirstHoldZone + zone;
    }
}
