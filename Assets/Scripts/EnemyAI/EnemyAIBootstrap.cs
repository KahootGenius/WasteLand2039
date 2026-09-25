using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 实验分支的自动装配：每次加载场景时，为没有监测组件的 HordeEventSpawner 添加 PlayerBehaviourMonitor，
/// 这样无需修改场景就能采集玩家行为数据。
/// 若场景中已手动放置 PlayerBehaviourMonitor（例如想调整参数或关闭遥测），则完全不做自动添加。
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
        if (Object.FindObjectOfType<PlayerBehaviourMonitor>() != null)
            return;

        foreach (HordeEventSpawner spawner in Object.FindObjectsOfType<HordeEventSpawner>())
            spawner.gameObject.AddComponent<PlayerBehaviourMonitor>();
    }
}
