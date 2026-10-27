using UnityEngine;

/// <summary>
/// 尸潮指挥官（敌人AI的"大脑"）。HordeEventSpawner 在尸潮的各个阶段调用它：
/// 决定每个敌人的生成位置，并通过 Enemy.SetOrder / Squad.Issue 指挥敌人。
///
/// 实现：
/// - BaselineCommander：原版行为（对照组）
/// - DebugOrdersCommander：测试命令系统用
/// - （后续）V1 强化学习指挥官、V2 预测+规则指挥官
///
/// 所有实现使用相同的输入（HordeContext）和相同的命令接口，保证对比公平。
/// </summary>
public interface IHordeCommander
{
    /// <summary>用于日志和数据记录的名称</summary>
    string DisplayName { get; }

    /// <summary>一波尸潮开始</summary>
    void OnWaveStarted(HordeContext context);

    /// <summary>决定下一个敌人的生成位置（在选定敌人类型之后调用）</summary>
    Vector3 ChooseSpawnPosition(HordeContext context);

    /// <summary>敌人已生成（可在此分配小队 / 下达命令）。预制体没有 Enemy 组件时不会调用</summary>
    void OnEnemySpawned(HordeContext context, Enemy enemy);

    /// <summary>尸潮进行中每帧调用（实现自行控制决策频率）</summary>
    void Tick(HordeContext context);

    /// <summary>一波尸潮结束（全部生成且全部死亡）</summary>
    void OnWaveCompleted(HordeContext context);
}
