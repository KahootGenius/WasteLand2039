using System.Collections.Generic;

/// <summary>
/// 内置的机器人性格（计划 §4 的训练对手）。
/// 编辑器菜单 Tools > Enemy AI > Build ML Arena 用它们创建 Assets/ML/Personas 下的资产（已存在的不覆盖），
/// 逻辑测试也使用同样的定义。之后在 Inspector 中调整资产不会改变这里的默认值。
/// 路线顺序与训练场一致：A = 东，B = 西北，C = 西南。
/// </summary>
public static class BotPersonaPresets
{
    public struct Preset
    {
        public string Name;
        public string Description;
        public BotPersonaSpec Spec;
    }

    public static IReadOnlyList<Preset> All => new[]
    {
        new Preset { Name = "Fighter", Spec = Fighter(),
            Description = "正面作战：守在基地旁，敌人进入射程就停下射击，从不逃跑" },
        new Preset { Name = "RunnerA", Spec = RunnerA(),
            Description = "逃跑型：敌人靠近就沿路线 A（东）逃到避难点，安全后返回基地" },
        new Preset { Name = "Mixed", Spec = Mixed(),
            Description = "混合型：每次交战 60% 逃跑、40% 作战；逃跑时偏好路线 B（70%）胜过 C（30%）" },
        new Preset { Name = "Adaptive", Spec = Adaptive(),
            Description = "适应型：习惯逃往 A，但逃跑途中被拦截后会降低该路线的权重、改走其他路线（测试指挥官是否过度押注）" },
        new Preset { Name = "Kiter", Spec = Kiter(),
            Description = "风筝型：沿路线边退边打（走一段、停下射击、再走），偏好路线 C" },
        new Preset { Name = "Random", Spec = RandomPlayer(),
            Description = "随机型：每回合随机的作战 / 逃跑 / 风筝比例和路线偏好，参数范围更宽" },
        new Preset { Name = "RunnerB80", Spec = RunnerB80(),
            Description = "仅用于评估（计划 §4 的测试用例）：总是逃跑，80% 走路线 B（西北），A / C 各 10%。不在训练性格池中" },
    };

    public static BotPersonaSpec Fighter()
    {
        return new BotPersonaSpec { fightWeight = 1f, fleeWeight = 0f, kiteWeight = 0f };
    }

    public static BotPersonaSpec RunnerA()
    {
        return new BotPersonaSpec { fightWeight = 0f, fleeWeight = 1f, kiteWeight = 0f, routeWeights = new[] { 1f, 0f, 0f } };
    }

    public static BotPersonaSpec RunnerB80()
    {
        return new BotPersonaSpec { fightWeight = 0f, fleeWeight = 1f, kiteWeight = 0f, routeWeights = new[] { 0.1f, 0.8f, 0.1f }, weightJitter = 0f };
    }

    public static BotPersonaSpec Mixed()
    {
        return new BotPersonaSpec { fightWeight = 0.4f, fleeWeight = 0.6f, kiteWeight = 0f, routeWeights = new[] { 0f, 0.7f, 0.3f } };
    }

    public static BotPersonaSpec Adaptive()
    {
        return new BotPersonaSpec
        {
            fightWeight = 0f, fleeWeight = 1f, kiteWeight = 0f,
            routeWeights = new[] { 0.8f, 0.1f, 0.1f },
            adaptRoutes = true, ambushPenalty = 0.2f, ambushRecovery = 0.05f
        };
    }

    public static BotPersonaSpec Kiter()
    {
        return new BotPersonaSpec { fightWeight = 0f, fleeWeight = 0f, kiteWeight = 1f, routeWeights = new[] { 0.15f, 0.15f, 0.7f } };
    }

    public static BotPersonaSpec RandomPlayer()
    {
        return new BotPersonaSpec
        {
            fightWeight = 1f, fleeWeight = 1f, kiteWeight = 1f,
            routeWeights = new[] { 1f, 1f, 1f },
            weightJitter = 1f,
            decisionDistance = new FloatRange(3.5f, 6f),
            reactionTime = new FloatRange(0.1f, 0.6f),
            lingerTime = new FloatRange(0.5f, 6f),
            aimError = new FloatRange(1f, 10f),
            clickRate = new FloatRange(2f, 5f)
        };
    }
}
