using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ML 训练场场景（Assets/ML/Arena/MLArena.unity）的总控：把训练场预制体复制 arenaCount 份
/// （网格排列、相距 spacing，互不干扰），给每份分配一个机器人性格，并控制镜头和游戏速度。
///
/// 按键：[ / ] 切换关注的训练场   O 全景 / 跟随   ` 调试面板   H 真人接管当前训练场   - / = 速度减半 / 加倍
///
/// 命令行（独立运行的玩家程序，例如无图形评估）：
///   -arenaScene 场景名    先切换到该场景（一个评估程序包含多个场景时使用）
///   -arenaMinutes N      运行 N 分钟游戏时间后退出（评估运行有固定长度）
///   -stochastic          RL 指挥官按策略概率采样动作（默认取最可能的动作；训练早期策略接近均匀分布时，
///                        确定性推理会退化成单一动作，不能代表训练中的表现）。须在实例化训练场之前设置：
///                        ML-Agents 按模型缓存推理器（Academy.GetOrCreateModelRunner 不区分确定性），
///                        之后再改 BehaviorParameters.DeterministicInference 不起作用
/// </summary>
public class ArenaManager : MonoBehaviour
{
    [Header("训练场")]
    [SerializeField] private ArenaEnvironment arenaPrefab;
    [SerializeField, Range(1, 16)] private int arenaCount = 8;
    [SerializeField, Min(1)] private int columns = 4;
    [Tooltip("训练场之间的距离。须远大于敌人的发现 / 集群半径和监测的威胁半径（15）")]
    [SerializeField] private float spacing = 150f;

    [Header("机器人")]
    [Tooltip("第 i 个训练场使用 personas[i % 数量]；列表为空（且没有性格池）时全部由真人控制")]
    [SerializeField] private List<BotPersona> personas = new List<BotPersona>();
    [Tooltip("训练用性格池：不为空时，每个训练场每回合从池中随机抽取性格（按课程等级 ArenaCurriculum.Level 过滤），代替上面的固定分配")]
    [SerializeField] private List<PersonaPoolEntry> personaPool = new List<PersonaPoolEntry>();
    [Tooltip("每回合打乱性格的路线偏好顺序（RunnerA → 随机的 Runner-A/B/C），迫使指挥官读取玩家画像")]
    [SerializeField] private bool shuffleRoutes = false;
    [SerializeField] private int seed = 20260925;
    [Tooltip("遥测记录量；长时间训练用 Off 或 EventsOnly 节省磁盘")]
    [SerializeField] private ArenaTelemetry telemetry = ArenaTelemetry.Full;
    [Tooltip("每回合的波数；0 = 使用训练场预制体的设置。评估时让不同指挥官的回合结构一致")]
    [SerializeField, Min(0)] private int wavesPerEpisode = 0;

    [Header("运行")]
    [Tooltip("游戏速度（Time.timeScale），运行中按 - / = 调整。0 = 不修改（由 ML-Agents 训练器设置）")]
    [SerializeField] private float timeScale = 1f;
    [SerializeField] private float maxTimeScale = 20f;
    [Tooltip("Unity 窗口不在前台时继续运行（只影响本次运行，不修改项目设置）")]
    [SerializeField] private bool runInBackground = true;
    [Tooltip("每隔多少秒（真实时间）在日志中记录帧率和每帧游戏时间（训练时检查仿真精度）；0 = 不记录")]
    [SerializeField] private float statsLogInterval = 60f;
    [Tooltip("锁步：每帧固定前进这么多秒游戏时间，与真实帧率无关，并尽可能快地运行（评估用，0.02 = 每帧一次物理步）。" +
             "设置后忽略 timeScale。0 = 关闭（按真实时间 × timeScale）")]
    [SerializeField] private float lockstepFrameTime = 0f;
    [Tooltip("运行这么多分钟游戏时间后退出（0 = 一直运行）。命令行 -arenaMinutes 优先")]
    [SerializeField] private float runForGameMinutes = 0f;

    [Header("镜头")]
    [SerializeField] private CameraController cameraController;
    [Tooltip("跟随玩家时的正交大小（与 MainGame 相同）")]
    [SerializeField] private float followSize = 5f;
    [Tooltip("全景时的正交大小")]
    [SerializeField] private float overviewSize = 30f;
    [SerializeField] private bool startInOverview = true;

    private readonly List<ArenaEnvironment> arenas = new List<ArenaEnvironment>();
    private int focused;
    private bool overview;
    private bool showOverlay;
    private Camera cameraComponent;
    private GUIStyle statusStyle;
    private float statsRealtime;
    private int statsFrame;
    private float statsGameTime;

    public IReadOnlyList<ArenaEnvironment> Arenas => arenas;
    public int Focused => focused;

    private void Start()
    {
        string sceneArgument = CommandLineValue("-arenaScene");
        if (!string.IsNullOrEmpty(sceneArgument) && sceneArgument != gameObject.scene.name)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneArgument);
            return;
        }
        if (float.TryParse(CommandLineValue("-arenaMinutes"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float minutes))
            runForGameMinutes = minutes;

        if (arenaPrefab == null)
        {
            Debug.LogError("[ArenaManager] 未指定训练场预制体", this);
            enabled = false;
            return;
        }

        bool stochastic = HasCommandLineFlag("-stochastic");
        var prefabBehaviors = arenaPrefab.GetComponentsInChildren<Unity.MLAgents.Policies.BehaviorParameters>(true);
        var deterministic = new bool[prefabBehaviors.Length];
        for (int i = 0; i < prefabBehaviors.Length; i++)
        {
            deterministic[i] = prefabBehaviors[i].DeterministicInference;
            if (stochastic)
                prefabBehaviors[i].DeterministicInference = false;
        }

        for (int i = 0; i < arenaCount; i++)
        {
            Vector3 position = transform.position + new Vector3((i % columns) * spacing, -(i / columns) * spacing, 0f);
            ArenaEnvironment arena = Instantiate(arenaPrefab, position, Quaternion.identity, transform);
            BotPersona persona = personas.Count > 0 ? personas[i % personas.Count] : null;
            arena.Configure(i, persona, seed + i * 7919);
            if (personaPool.Count > 0)
                arena.SetPersonaPool(personaPool, shuffleRoutes);
            arena.SetTelemetry(telemetry);
            arena.SetWavesPerEpisode(wavesPerEpisode);
            arenas.Add(arena);
        }

        // 在实例化之后恢复预制体（实例已复制该设置；推理器按模型缓存，第一个实例决定采样方式）
        for (int i = 0; i < prefabBehaviors.Length; i++)
            prefabBehaviors[i].DeterministicInference = deterministic[i];
        if (stochastic)
            Debug.Log($"[ArenaManager] -stochastic：RL 指挥官按概率采样动作（{prefabBehaviors.Length} 个行为参数/训练场）");

        if (cameraController == null)
            cameraController = FindObjectOfType<CameraController>();
        cameraComponent = cameraController != null ? cameraController.GetComponent<Camera>() : Camera.main;

        if (runInBackground)
            Application.runInBackground = true;
        if (lockstepFrameTime > 0f)
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = lockstepFrameTime;
        }
        else if (timeScale > 0f)
        {
            SetTimeScale(timeScale);
        }
        overview = startInOverview;
        Focus(0, snap: true);
    }

    private void OnDestroy()
    {
        if (lockstepFrameTime > 0f)
            Time.captureDeltaTime = 0f;
        else if (timeScale > 0f)
            Time.timeScale = 1f;
    }

    private static bool HasCommandLineFlag(string name)
    {
        return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;
    }

    private static string CommandLineValue(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }

    private void Update()
    {
        if (arenas.Count == 0)
            return;
        LogStats();

        if (runForGameMinutes > 0f && Time.time >= runForGameMinutes * 60f)
        {
            Debug.Log($"[ArenaManager] 已运行 {runForGameMinutes} 分钟游戏时间，退出");
            runForGameMinutes = 0f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            return;
        }

        if (Input.GetKeyDown(KeyCode.RightBracket))
            Focus((focused + 1) % arenas.Count, snap: true);
        if (Input.GetKeyDown(KeyCode.LeftBracket))
            Focus((focused - 1 + arenas.Count) % arenas.Count, snap: true);
        if (Input.GetKeyDown(KeyCode.O))
        {
            overview = !overview;
            Focus(focused, snap: false);
        }
        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            showOverlay = !showOverlay;
            ApplyOverlay();
        }
        if (Input.GetKeyDown(KeyCode.H))
        {
            ArenaEnvironment arena = arenas[focused];
            arena.HumanControl = !arena.HumanControl;
            if (arena.HumanControl)
            {
                overview = false;
                Focus(focused, snap: false);
            }
        }
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            SetTimeScale(Time.timeScale * 0.5f);
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            SetTimeScale(Time.timeScale * 2f);
    }

    /// <summary>
    /// 定期记录帧率。机器人输入、敌人目标选择和开火都在 Update 中，每帧的游戏时间过长会降低仿真精度
    /// （建议 ≤ 0.02 秒，对应 50 Hz）
    /// </summary>
    private void LogStats()
    {
        if (statsLogInterval <= 0f)
            return;
        float now = Time.realtimeSinceStartup;
        if (statsFrame == 0)
        {
            statsRealtime = now;
            statsFrame = Time.frameCount;
            statsGameTime = Time.time;
            return;
        }
        if (now - statsRealtime < statsLogInterval)
            return;

        int frames = Mathf.Max(1, Time.frameCount - statsFrame);
        float gameSeconds = Time.time - statsGameTime;
        Debug.Log($"[ArenaManager] {frames / (now - statsRealtime):0} fps，每帧游戏时间 {gameSeconds / frames:0.0000} 秒，" +
                  $"游戏速度 ×{gameSeconds / (now - statsRealtime):0.0}（timeScale {Time.timeScale:0.#}）");
        statsRealtime = now;
        statsFrame = Time.frameCount;
        statsGameTime = Time.time;
    }

    /// <summary>关注第 index 个训练场：镜头对准它，调试面板显示它</summary>
    public void Focus(int index, bool snap)
    {
        if (index < 0 || index >= arenas.Count)
            return;

        // 离开的训练场交还给机器人（真人只能操作镜头所在的训练场）
        if (index != focused && arenas[focused].HumanControl && arenas[focused].Persona != null)
            arenas[focused].HumanControl = false;

        focused = index;
        ArenaEnvironment arena = arenas[focused];
        Transform target = overview || arena.Player == null ? arena.transform : arena.Player.transform;

        if (cameraController != null)
        {
            cameraController.SetTarget(target);
            if (snap)
                cameraController.transform.position = new Vector3(target.position.x, target.position.y, cameraController.transform.position.z);
        }
        if (cameraComponent != null)
            cameraComponent.orthographicSize = overview ? overviewSize : followSize;

        ApplyOverlay();
    }

    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Clamp(scale, 0.25f, maxTimeScale);
        Time.timeScale = timeScale;
    }

    private void ApplyOverlay()
    {
        for (int i = 0; i < arenas.Count; i++)
        {
            if (arenas[i].Monitor != null)
                arenas[i].Monitor.ShowOverlay = showOverlay && i == focused;
        }
    }

    private void OnGUI()
    {
        if (arenas.Count == 0)
            return;

        if (statusStyle == null)
        {
            statusStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 13 };
            statusStyle.normal.textColor = Color.white;
        }

        string text = $"{arenas[focused].StatusLine()}   ×{Time.timeScale:0.##}   " +
                      "[ ] 切换  O 全景  ` 面板  H 真人接管  -/= 速度";
        GUI.Box(new Rect(10f, Screen.height - 34f, Mathf.Min(Screen.width - 20f, 900f), 24f), text, statusStyle);
    }
}
