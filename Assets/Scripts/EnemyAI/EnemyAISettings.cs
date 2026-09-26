using UnityEngine;

/// <summary>
/// 实验分支的敌人 AI 设置（Assets/ML/Resources/EnemyAISettings.asset，按名称从 Resources 加载）。
/// 决定正常游戏场景（MainGame 等）使用哪个指挥官；训练场场景不受影响（它们在预制体里指定指挥官）。
/// 找不到此资产时一律使用基准指挥官（原版行为）。
/// </summary>
[CreateAssetMenu(fileName = "EnemyAISettings", menuName = "Enemy AI/Enemy AI Settings")]
public class EnemyAISettings : ScriptableObject
{
    public enum CommanderChoice
    {
        /// <summary>原版行为（对照组）</summary>
        Baseline,
        /// <summary>V1 强化学习指挥官（需要 rlModel）</summary>
        RL
    }

    public const string ResourcePath = "EnemyAISettings";

    [Tooltip("正常游戏场景使用的指挥官。HordeEventSpawner 在 Inspector 中已指定指挥官、或物体上已有指挥官组件时不替换")]
    public CommanderChoice gameCommander = CommanderChoice.Baseline;

    [Tooltip("RL 指挥官使用的模型（Tools > Enemy AI > Install Latest Commander Model 会自动设置）")]
    public Unity.Sentis.ModelAsset rlModel;

    public static EnemyAISettings Load()
    {
        return Resources.Load<EnemyAISettings>(ResourcePath);
    }
}
