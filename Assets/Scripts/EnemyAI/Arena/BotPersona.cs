using UnityEngine;

/// <summary>
/// 机器人玩家的性格资产（菜单：Create > Enemy AI > Bot Persona）。
/// 训练场每个回合从中采样一组具体参数（BotPersonaSpec.Sample），由 BotBrain 执行。
/// </summary>
[CreateAssetMenu(fileName = "NewBotPersona", menuName = "Enemy AI/Bot Persona")]
public class BotPersona : ScriptableObject
{
    [TextArea(2, 5)] public string description;
    public BotPersonaSpec spec = new BotPersonaSpec();

    public BotParams Sample(System.Random rng)
    {
        return spec.Sample(rng, name);
    }
}
