using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ML 训练场场景（Assets/ML/Arena/MLArena.unity）的总控：把训练场预制体复制 arenaCount 份
/// （网格排列、相距 spacing，互不干扰），给每份分配一个机器人性格，并控制镜头和游戏速度。
///
/// 按键：[ / ] 切换关注的训练场   O 全景 / 跟随   ` 调试面板   H 真人接管当前训练场   - / = 速度减半 / 加倍
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
    [Tooltip("第 i 个训练场使用 personas[i % 数量]；列表为空时全部由真人控制")]
    [SerializeField] private List<BotPersona> personas = new List<BotPersona>();
    [SerializeField] private int seed = 20260925;

    [Header("运行")]
    [Tooltip("游戏速度（Time.timeScale），运行中按 - / = 调整")]
    [SerializeField] private float timeScale = 1f;
    [SerializeField] private float maxTimeScale = 20f;
    [Tooltip("Unity 窗口不在前台时继续运行（只影响本次运行，不修改项目设置）")]
    [SerializeField] private bool runInBackground = true;

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

    public IReadOnlyList<ArenaEnvironment> Arenas => arenas;
    public int Focused => focused;

    private void Start()
    {
        if (arenaPrefab == null)
        {
            Debug.LogError("[ArenaManager] 未指定训练场预制体", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < arenaCount; i++)
        {
            Vector3 position = transform.position + new Vector3((i % columns) * spacing, -(i / columns) * spacing, 0f);
            ArenaEnvironment arena = Instantiate(arenaPrefab, position, Quaternion.identity, transform);
            BotPersona persona = personas.Count > 0 ? personas[i % personas.Count] : null;
            arena.Configure(i, persona, seed + i * 7919);
            arenas.Add(arena);
        }

        if (cameraController == null)
            cameraController = FindObjectOfType<CameraController>();
        cameraComponent = cameraController != null ? cameraController.GetComponent<Camera>() : Camera.main;

        if (runInBackground)
            Application.runInBackground = true;
        SetTimeScale(timeScale);
        overview = startInOverview;
        Focus(0, snap: true);
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (arenas.Count == 0)
            return;

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
