using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 实验分支的自动装配（每次加载场景时，无需修改场景）：
/// 1. 为没有监测组件的 HordeEventSpawner 添加 PlayerBehaviourMonitor，采集玩家行为数据。
///    若场景中已手动放置 PlayerBehaviourMonitor（例如想调整参数或关闭遥测），则不做自动添加。
/// 2. EnemyAISettings 选择 RL（且有模型）或 V2 时，为没有指定指挥官的 HordeEventSpawner 添加
///    RLCommander（只推理）或 PredictiveCommander。训练场（有 ArenaManager 的场景）不受影响。
/// </summary>
public static class EnemyAIBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HordeEventSpawner[] spawners = Object.FindObjectsOfType<HordeEventSpawner>();

        if (Object.FindObjectOfType<PlayerBehaviourMonitor>() == null)
        {
            foreach (HordeEventSpawner spawner in spawners)
                spawner.gameObject.AddComponent<PlayerBehaviourMonitor>();
        }

        if (Object.FindObjectOfType<ArenaManager>() != null)
            return;
        EnemyAISettings settings = EnemyAISettings.Load();
        if (settings == null)
            return;
        if (settings.gameCommander == EnemyAISettings.CommanderChoice.RL)
            InstallLearnedCommander(settings, spawners);
        else if (settings.gameCommander == EnemyAISettings.CommanderChoice.Predictive)
            InstallPredictiveCommander(settings, spawners);
    }

    private static void InstallPredictiveCommander(EnemyAISettings settings, HordeEventSpawner[] spawners)
    {
        foreach (HordeEventSpawner spawner in spawners)
        {
            if (spawner.HasAssignedCommander || spawner.GetComponent<IHordeCommander>() != null)
                continue;
            var commander = spawner.gameObject.AddComponent<PredictiveCommander>();
            commander.Configure(settings.v2Predictor, settings.v2LearnedWeights);
            commander.ConfigureAdaptive(settings.v2Thompson, settings.v2Bandit);
            Debug.Log($"[EnemyAIBootstrap] {spawner.name}: 使用 V2 指挥官（{commander.DisplayName}）");
        }
    }

    private static void InstallLearnedCommander(EnemyAISettings settings, HordeEventSpawner[] spawners)
    {
        if (settings.rlModel == null)
        {
            Debug.LogWarning("[EnemyAIBootstrap] EnemyAISettings 选择了 RL 指挥官，但没有模型；使用基准指挥官");
            return;
        }

        foreach (HordeEventSpawner spawner in spawners)
        {
            if (spawner.HasAssignedCommander || spawner.GetComponent<IHordeCommander>() != null)
                continue;

            CommanderActions.Scheme? scheme = RLCommander.DetectScheme(settings.rlModel);
            if (scheme == null)
            {
                Debug.LogWarning($"[EnemyAIBootstrap] 无法识别模型 {settings.rlModel.name} 的动作方案；使用基准指挥官");
                return;
            }

            // 先配置 BehaviorParameters，再添加 Agent（Agent 启用时读取行为参数）
            var behavior = spawner.gameObject.AddComponent<BehaviorParameters>();
            RLCommander.ConfigureBehavior(behavior, scheme.Value);
            behavior.Model = settings.rlModel;
            behavior.BehaviorType = BehaviorType.InferenceOnly;
            behavior.DeterministicInference = true;
            var commander = spawner.gameObject.AddComponent<RLCommander>();
            commander.ActionScheme = scheme.Value;
            commander.DecisionInterval = RLCommander.TrainedDecisionInterval(scheme.Value);
            Debug.Log($"[EnemyAIBootstrap] {spawner.name}: 使用 RL 指挥官（模型 {settings.rlModel.name}）");
        }
    }
}
