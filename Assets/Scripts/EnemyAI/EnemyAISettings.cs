using UnityEngine;

/// <summary>
/// 实验分支的敌人 AI 设置（Assets/ML/Resources/EnemyAISettings.asset，按名称从 Resources 加载）。
/// 决定正常游戏场景（MainGame 等）使用哪个指挥官，以及玩家画像是否跨会话保存；
/// 训练场场景不受影响（它们在预制体里指定指挥官，画像每回合重置）。
/// 找不到此资产时使用基准指挥官（原版行为），画像跨会话保存。
/// </summary>
[CreateAssetMenu(fileName = "EnemyAISettings", menuName = "Enemy AI/Enemy AI Settings")]
public class EnemyAISettings : ScriptableObject
{
    public enum CommanderChoice
    {
        /// <summary>原版行为（对照组）</summary>
        Baseline,
        /// <summary>V1 强化学习指挥官（需要 rlModel）</summary>
        RL,
        /// <summary>V2 预测 + 规则指挥官（PredictiveCommander）</summary>
        Predictive,
        /// <summary>
        /// V2 Full：V2 + 按玩家风格分配 + 截击（PredictiveCommander 的 styleAware 和 interceptChase，评估中的 Full，
        /// 命令行 -v2Style -v2Intercept）。固定用评估过的配置：统计预测器、取最可能的方向、固定圈层，不读 v2Predictor /
        /// v2Thompson / v2Bandit。2026-10-22 的评估（20 轮，类玩家机器人）：8 个训练场平均每波伤害比 V2 多 4.4 [+3.0, +5.8]，
        /// 没有哪个性格比 V2 少
        /// </summary>
        PredictiveFull
    }

    public const string ResourcePath = "EnemyAISettings";

    [Tooltip("正常游戏场景使用的指挥官：Baseline = 原版，RL = V1，Predictive = V2，PredictiveFull = V2 Full（V2 + 按玩家风格分配 + 截击）。" +
             "HordeEventSpawner 在 Inspector 中已指定指挥官、或物体上已有指挥官组件时不替换")]
    public CommanderChoice gameCommander = CommanderChoice.Baseline;

    [Tooltip("RL 指挥官使用的模型（Tools > Enemy AI > Install Latest Commander Model 会自动设置）")]
    public Unity.Sentis.ModelAsset rlModel;

    [Tooltip("V2 指挥官使用的预测器（统计 = 按画像计数；学习型 = 离线训练的模型，需要 v2LearnedWeights）。V2 Full 固定用统计预测器")]
    public PredictiveCommander.PredictorChoice v2Predictor = PredictiveCommander.PredictorChoice.Frequency;

    [Tooltip("V2 学习型预测器的参数（Assets/ML/Predictors/*.json）")]
    public TextAsset v2LearnedWeights;

    [Tooltip("自适应尸潮 A 部分：V2 的伏击方向按预测分布做 Thompson 采样。关闭 = 总取最可能的方向（V2）。V2 Full 不用")]
    public bool v2Thompson = false;

    [Tooltip("自适应尸潮 B 部分：V2 的伏击位置由伏击老虎机学习（扇区 × 离基地的距离）。关闭 = 固定圈层（V2）。" +
             "玩家画像跨会话保存时，老虎机也一起保存（EnemyAIProfiles/场景名.bandit.json）。V2 Full 不用")]
    public bool v2Bandit = false;

    [Tooltip("玩家画像跨会话保存（每个场景一个文件，见 PlayerProfileStore）。关闭 = 每次进入游戏从零开始学习玩家")]
    public bool persistPlayerProfile = true;

    public static EnemyAISettings Load()
    {
        return Resources.Load<EnemyAISettings>(ResourcePath);
    }

    /// <summary>当前设置是否跨会话保存玩家画像（找不到设置资产时为 true）</summary>
    public static bool PersistPlayerProfile
    {
        get
        {
            EnemyAISettings settings = Load();
            return settings == null || settings.persistPlayerProfile;
        }
    }
}
