using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// 录制用 HUD（作品集演示视频）：用英文实时显示敌人指挥官在"想"什么，不改变任何游戏行为。
/// 默认关闭，按 V 打开 / 关闭；命令行 -recordingHud 启动时就打开（独立运行的评估程序）。批处理模式（-batchmode）不安装。
///
/// - 左上面板：指挥官、玩家（测试机器人的性格）、预热进度（几次逃跑后才开始伏击）、预测的逃跑方向（前三个扇区）、
///   伏击点（方向、离基地的距离、到位人数）、老虎机对当前伏击扇区各距离档的估计、最近一次逃跑的试验结果、
///   机器人的拦截次数和（对指挥官隐藏的）路线偏好、游戏时间和速度。
/// - 场景中（LineRenderer：比 Gizmos 粗，独立运行的程序中也可见）：8 个扇区的边界、预测方向的扇形（证据足够之后）、
///   驻守点的交战圈、驻守者到驻守点的连线、进行中的逃跑（起点到玩家）。
/// - 机器人逃跑途中被拦截时，屏幕下方提示拦截者是驻守的伏击者还是追兵（与遥测 bot_ambushed.by_ambusher 相同的判断：
///   PlayerBot.LastAmbushEnemy 的命令是否为 HoldAt）。
/// 训练场中只显示当前关注的训练场；打开时 ArenaManager 不显示中文状态栏。
/// 录制工具，不属于实验本身（由 Claude Code 编写）。
/// </summary>
public class RecordingHud : MonoBehaviour
{
    /// <summary>HUD 是否显示（ArenaManager 据此隐藏中文状态栏）</summary>
    public static bool Visible { get; private set; }

    private const KeyCode ToggleKey = KeyCode.V;
    private const float OuterRadius = 31f;     // 与 RadialZoneMap 的最外圈边界一致（6 / 20 / 31）
    private const float CoreRadius = 6f;
    private const float WaitingDistance = 2.5f; // 离驻守点这么近算"到位"
    private const float BannerSeconds = 2.5f;   // 拦截提示显示的真实时间
    private const float ArenaRange = 70f;       // 训练场相距 150：只画关注的训练场附近的敌人

    private static readonly string[] Compass = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

    private static readonly Color GuideColor = new Color(1f, 1f, 1f, 0.16f);
    private static readonly Color PredictColor = new Color(1f, 0.78f, 0.2f, 0.95f);
    private static readonly Color AmbushColor = new Color(1f, 0.25f, 0.85f, 0.95f);
    private static readonly Color EscapeColor = new Color(1f, 0.3f, 0.25f, 0.9f);

    private class CatchInfo
    {
        public int Episode = -1;
        public int Count;
        public float GameTime = -1f;
        public float RealTime = -1f;
        public string Route;
        public bool ByAmbusher;
        /// <summary>知道是哪个敌人（受伤时 2.5 以内没有敌人则不知道，遥测记为空）</summary>
        public bool KnownAttacker;
    }

    /// <summary>每个训练场当前的回合和它开始的游戏时间（新回合 = 新玩家、画像重置，旧的试验结果不再显示）</summary>
    private readonly Dictionary<ArenaEnvironment, KeyValuePair<int, float>> episodes = new Dictionary<ArenaEnvironment, KeyValuePair<int, float>>();

    private ArenaManager arenaManager;
    private HordeEventSpawner spawner;
    private PlayerBehaviourMonitor monitor;
    private ArenaEnvironment arena;
    private float nextFindTime;

    private readonly Dictionary<PlayerBot, ArenaEnvironment> subscribedBots = new Dictionary<PlayerBot, ArenaEnvironment>();
    private readonly Dictionary<ArenaEnvironment, CatchInfo> catches = new Dictionary<ArenaEnvironment, CatchInfo>();

    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private int usedLines;
    private Material lineMaterial;
    private bool materialMissing;
    private readonly List<Vector2> posts = new List<Vector2>();
    private readonly List<WorldLabel> worldLabels = new List<WorldLabel>();

    /// <summary>场景中的文字：Push ≠ 0 时文字整体放在锚点的这一侧（例如扇形弧线之外），否则以锚点为中心</summary>
    private struct WorldLabel
    {
        public Vector2 Position;
        public Vector2 Push;
        public string Text;
    }

    /// <summary>每个指挥官最近一次"被试过"的伏击（玩家跑进了有伏击点的扇区）：只显示这种试验，追兵把玩家从避难点赶出来的逃跑不显示</summary>
    private class TestedAmbush
    {
        public float SeenTrialTime = -1f;
        public float Time = -1f;
        public int Sector = -1;
        public bool Contact;
    }

    private readonly Dictionary<PredictiveCommander, TestedAmbush> testedAmbushes = new Dictionary<PredictiveCommander, TestedAmbush>();

    private GUIStyle panelStyle;
    private GUIStyle labelStyle;
    private GUIStyle bannerStyle;
    private Texture2D panelBackground;
    private int styledFontSize;
    private readonly StringBuilder text = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        Visible = false; // 关闭"域重载"时静态数据会保留到下一次运行
        if (Application.isBatchMode || FindObjectOfType<RecordingHud>() != null)
            return;
        foreach (string arg in System.Environment.GetCommandLineArgs())
        {
            if (arg == "-recordingHud")
                Visible = true;
        }
        var hud = new GameObject("RecordingHud");
        DontDestroyOnLoad(hud);
        hud.AddComponent<RecordingHud>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            Visible = !Visible;
        // 关闭时也订阅机器人的拦截事件，打开 HUD 时本回合的拦截次数已经齐全
        Resolve();
        SubscribeBots();
        TrackTestedAmbushes();
    }

    private void TrackTestedAmbushes()
    {
        if (arenaManager != null)
        {
            foreach (ArenaEnvironment environment in arenaManager.Arenas)
            {
                if (environment != null && environment.Spawner != null)
                    TrackTestedAmbush(environment.Spawner.Commander as PredictiveCommander);
            }
        }
        else if (spawner != null)
        {
            TrackTestedAmbush(spawner.Commander as PredictiveCommander);
        }
    }

    private void TrackTestedAmbush(PredictiveCommander commander)
    {
        if (commander == null || commander.LastTrialTime < 0f)
            return;
        if (!testedAmbushes.TryGetValue(commander, out TestedAmbush tested))
        {
            tested = new TestedAmbush();
            testedAmbushes[commander] = tested;
        }
        if (Mathf.Approximately(tested.SeenTrialTime, commander.LastTrialTime))
            return;
        tested.SeenTrialTime = commander.LastTrialTime;
        if (!commander.LastTrialTested)
            return;
        tested.Time = commander.LastTrialTime;
        tested.Sector = commander.LastTrialRanSector;
        tested.Contact = commander.LastTrialContact;
    }

    private void LateUpdate()
    {
        usedLines = 0;
        worldLabels.Clear();
        if (Visible && monitor != null && monitor.Zones != null)
            DrawWorld();
        for (int i = usedLines; i < lines.Count; i++)
        {
            if (lines[i] != null)
                lines[i].enabled = false;
        }
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<PlayerBot, ArenaEnvironment> pair in subscribedBots)
        {
            if (pair.Key != null)
                pair.Key.Ambushed -= CatchHandler(pair.Value);
        }
        if (lineMaterial != null)
            Destroy(lineMaterial);
        if (panelBackground != null)
            Destroy(panelBackground);
    }

    // ---------- 找到要显示的指挥官 ----------

    /// <summary>找到要显示的训练场 / 刷怪器 / 监测组件；场景切换后引用失效时重新查找（每秒最多一次）</summary>
    private void Resolve()
    {
        bool refresh = Time.unscaledTime >= nextFindTime;
        if (refresh)
        {
            nextFindTime = Time.unscaledTime + 1f;
            if (arenaManager == null)
                arenaManager = FindObjectOfType<ArenaManager>();
            PruneDestroyed();
        }

        if (arenaManager != null && arenaManager.Arenas.Count > 0)
        {
            int index = Mathf.Clamp(arenaManager.Focused, 0, arenaManager.Arenas.Count - 1);
            arena = arenaManager.Arenas[index];
            spawner = arena != null ? arena.Spawner : null;
            monitor = arena != null ? arena.Monitor : null;
            return;
        }

        arena = null;
        if (refresh && spawner == null)
            spawner = FindObjectOfType<HordeEventSpawner>();
        if (spawner != null && (monitor == null || monitor.gameObject != spawner.gameObject))
            monitor = spawner.GetComponent<PlayerBehaviourMonitor>();
        if (refresh && monitor == null)
            monitor = FindObjectOfType<PlayerBehaviourMonitor>();
    }

    // ---------- 机器人被拦截 ----------

    private readonly Dictionary<ArenaEnvironment, System.Action<int>> handlers = new Dictionary<ArenaEnvironment, System.Action<int>>();

    private System.Action<int> CatchHandler(ArenaEnvironment environment)
    {
        if (!handlers.TryGetValue(environment, out System.Action<int> handler))
        {
            handler = route => RecordCatch(environment, route);
            handlers[environment] = handler;
        }
        return handler;
    }

    private void SubscribeBots()
    {
        if (arenaManager == null)
            return;
        foreach (ArenaEnvironment environment in arenaManager.Arenas)
        {
            if (environment == null)
                continue;
            if (!episodes.TryGetValue(environment, out KeyValuePair<int, float> seen) || seen.Key != environment.Episode)
                episodes[environment] = new KeyValuePair<int, float>(environment.Episode, Time.time);
            PlayerBot bot = environment.Bot;
            if (bot == null || subscribedBots.ContainsKey(bot))
                continue;
            bot.Ambushed += CatchHandler(environment);
            subscribedBots[bot] = environment;
        }
    }

    /// <summary>场景重新载入后去掉已销毁的机器人和训练场</summary>
    private void PruneDestroyed()
    {
        var deadBots = new List<PlayerBot>();
        foreach (PlayerBot bot in subscribedBots.Keys)
        {
            if (bot == null)
                deadBots.Add(bot);
        }
        foreach (PlayerBot bot in deadBots)
            subscribedBots.Remove(bot);

        var deadArenas = new List<ArenaEnvironment>();
        foreach (ArenaEnvironment environment in episodes.Keys)
        {
            if (environment == null)
                deadArenas.Add(environment);
        }
        foreach (ArenaEnvironment environment in deadArenas)
        {
            episodes.Remove(environment);
            catches.Remove(environment);
            handlers.Remove(environment);
        }

        var deadCommanders = new List<PredictiveCommander>();
        foreach (PredictiveCommander commander in testedAmbushes.Keys)
        {
            if (commander == null)
                deadCommanders.Add(commander);
        }
        foreach (PredictiveCommander commander in deadCommanders)
            testedAmbushes.Remove(commander);
    }

    /// <summary>当前训练场本回合开始的游戏时间；不在训练场时为 0</summary>
    private float EpisodeStartTime()
    {
        return arena != null && episodes.TryGetValue(arena, out KeyValuePair<int, float> seen) ? seen.Value : 0f;
    }

    private void RecordCatch(ArenaEnvironment environment, int route)
    {
        if (environment == null || environment.Bot == null)
            return;
        if (!catches.TryGetValue(environment, out CatchInfo info))
        {
            info = new CatchInfo();
            catches[environment] = info;
        }
        Enemy enemy = environment.Bot.LastAmbushEnemy;
        if (info.Episode != environment.Episode)
        {
            info.Episode = environment.Episode; // 每回合重新计数（画像也每回合重置）
            info.Count = 0;
        }
        info.Count++;
        info.GameTime = Time.time;
        info.RealTime = Time.unscaledTime;
        info.Route = environment.Bot.Brain != null ? environment.Bot.Brain.RouteName(route) : "?";
        info.ByAmbusher = enemy != null && enemy.CurrentOrder.Type == EnemyOrderType.HoldAt;
        info.KnownAttacker = enemy != null;
    }

    // ---------- 场景中的线 ----------

    private void DrawWorld()
    {
        Camera cam = Camera.main;
        if (cam == null || !EnsureMaterial())
            return;
        float width = Mathf.Max(0.04f, (cam.orthographic ? cam.orthographicSize : 10f) * 0.01f);
        Vector2 center = monitor.Zones.GetZoneCenter(0);

        // 8 个扇区的边界（扇区以正东为中心，边界在 ±22.5°）和中心区
        for (int s = 0; s < Compass.Length; s++)
        {
            float edge = (s * 45f + 22.5f) * Mathf.Deg2Rad;
            Line(GuideColor, width * 0.6f, false, center + Dir(edge) * CoreRadius, center + Dir(edge) * OuterRadius);
        }
        Circle(GuideColor, width * 0.6f, center, CoreRadius);

        // 预测方向的扇形：只在波次进行中、指挥官上一次决策已经足够确定（会据此伏击）时画。
        // 扇形是最可能的方向；伏击点可能在别处（Argmax 的换位门槛、Thompson 抽样），面板的 Ambush 行会说明
        PredictiveCommander predictive = spawner != null ? spawner.Commander as PredictiveCommander : null;
        bool waveActive = spawner != null && spawner.IsWaveActive;
        if (predictive != null && waveActive && predictive.HasPrediction && predictive.PredictionConfident)
        {
            int top = TopSector(predictive.SectorProbabilities, 0, -1, -1);
            if (top >= 0)
            {
                float mid = top * 45f * Mathf.Deg2Rad;
                float half = 22.5f * Mathf.Deg2Rad;
                var wedge = new List<Vector2> { center + Dir(mid - half) * CoreRadius };
                for (int k = 0; k <= 12; k++)
                    wedge.Add(center + Dir(mid - half + 2f * half * k / 12f) * OuterRadius);
                wedge.Add(center + Dir(mid + half) * CoreRadius);
                Polyline(PredictColor, width * 1.4f, false, wedge);
                worldLabels.Add(new WorldLabel
                {
                    Position = center + Dir(mid) * (OuterRadius + 1.5f),
                    Push = Dir(mid),
                    Text = $"<color=#FFC733>PREDICTED  {Compass[top]} {Percent(predictive.SectorProbabilities[top])}</color>"
                });
            }
        }

        // 驻守点（V1 和 V2 的 HoldAt 命令）：交战圈、驻守者到驻守点的连线
        posts.Clear();
        foreach (Enemy enemy in Enemy.AllActive)
        {
            if (enemy == null || enemy.IsDead || enemy.CurrentOrder.Type != EnemyOrderType.HoldAt)
                continue;
            Vector2 position = enemy.transform.position;
            if (Vector2.Distance(position, center) > ArenaRange)
                continue;
            Vector2 post = enemy.CurrentOrder.Position;
            Line(AmbushColor, width * 0.8f, false, position, post);
            bool known = false;
            foreach (Vector2 existing in posts)
                known |= Vector2.Distance(existing, post) < 1f;
            if (known)
                continue;
            posts.Add(post);
            float radius = enemy.CurrentOrder.EngageRadius > 0f ? enemy.CurrentOrder.EngageRadius : 6f;
            Circle(AmbushColor, width * 1.2f, post, radius);
        }

        // V2 的伏击点：标注到位人数（波次之间伏击点还没清空，但已经没有命令）
        if (predictive != null && waveActive)
        {
            for (int i = 0; i < predictive.AmbushSiteCount; i++)
            {
                if (!predictive.TryGetAmbushSite(i, out int sector, out Vector2 position, out _, out Squad squad, out int quota))
                    continue;
                int waiting = CountWaiting(squad, position);
                worldLabels.Add(new WorldLabel
                {
                    Position = position + Vector2.up * (predictive.HoldEngageRadius + 0.8f),
                    Push = Vector2.up,
                    Text = $"<color=#FF40D9>AMBUSH  {waiting}/{quota} waiting</color>"
                });
            }
        }

        // 进行中的逃跑：起点到玩家
        EscapeEpisode escape = monitor.Tracker != null ? monitor.Tracker.ActiveEscape : null;
        if (escape != null && monitor.Player != null)
            Line(EscapeColor, width, false, escape.StartPosition, monitor.Player.transform.position);
    }

    private bool EnsureMaterial()
    {
        if (lineMaterial != null)
            return true;
        if (materialMissing)
            return false;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null)
        {
            materialMissing = true;
            Debug.LogWarning("[RecordingHud] 找不到 Sprites/Default 着色器，不画场景中的线（面板照常显示）");
            return false;
        }
        lineMaterial = new Material(shader);
        return true;
    }

    private LineRenderer NextLine()
    {
        if (usedLines >= lines.Count)
        {
            var child = new GameObject("RecordingHudLine");
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = lineMaterial;
            // Tilemaps 层：在 MainGame 的地面（Tilemaps -1）和训练场的地面（Default -100）之上，在建筑、玩家和敌人之下
            line.sortingLayerID = SortingLayer.NameToID("Tilemaps");
            line.sortingOrder = 1000;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            lines.Add(line);
        }
        LineRenderer next = lines[usedLines++];
        next.enabled = true;
        return next;
    }

    private void Line(Color color, float width, bool loop, Vector2 from, Vector2 to)
    {
        LineRenderer line = NextLine();
        line.loop = loop;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
    }

    private void Polyline(Color color, float width, bool loop, List<Vector2> points)
    {
        LineRenderer line = NextLine();
        line.loop = loop;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            line.SetPosition(i, points[i]);
    }

    private void Circle(Color color, float width, Vector2 center, float radius)
    {
        const int segments = 40;
        LineRenderer line = NextLine();
        line.loop = true;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.positionCount = segments;
        for (int i = 0; i < segments; i++)
            line.SetPosition(i, center + Dir(2f * Mathf.PI * i / segments) * radius);
    }

    // ---------- 屏幕文字 ----------

    private void OnGUI()
    {
        if (!Visible)
            return;
        EnsureStyles();

        float scale = FontScale();
        GUIContent content = new GUIContent(BuildPanel());
        float width = Mathf.Min(Screen.width - 20f, 640f * scale);
        float height = panelStyle.CalcHeight(content, width);
        GUI.Box(new Rect(10f, 10f, width, height), content, panelStyle);

        Camera cam = Camera.main;
        if (cam != null)
        {
            foreach (WorldLabel label in worldLabels)
            {
                Vector3 screen = cam.WorldToScreenPoint(label.Position);
                if (screen.z < 0f)
                    continue;
                var labelContent = new GUIContent(label.Text);
                Vector2 size = labelStyle.CalcSize(labelContent);
                // 屏幕坐标 y 向下：把文字中心沿 Push 方向推出半个文字框，文字就整体落在锚点外侧
                float x = screen.x - size.x * 0.5f + label.Push.x * size.x * 0.5f;
                float y = Screen.height - screen.y - size.y * 0.5f - label.Push.y * size.y * 0.5f;
                GUI.Box(new Rect(x, y, size.x, size.y), labelContent, labelStyle);
            }
        }

        if (arena != null && catches.TryGetValue(arena, out CatchInfo info) && info.RealTime >= 0f &&
            Time.unscaledTime - info.RealTime < BannerSeconds)
        {
            // 机器人的"被拦截"：逃跑途中路线前方 4 以内出现敌人、避难点有敌人或受伤（BotBrain），不一定是接触
            string banner = info.ByAmbusher
                ? $"<color=#FF40D9>INTERCEPTED BY A WAITING ZOMBIE</color>  (route {info.Route})"
                : info.KnownAttacker
                    ? $"<color=#FFB060>Intercepted by a chasing zombie</color>  (route {info.Route})"
                    : $"<color=#FFB060>Intercepted (attacker unknown)</color>  (route {info.Route})";
            var bannerContent = new GUIContent(banner);
            Vector2 size = bannerStyle.CalcSize(bannerContent);
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.8f, size.x, size.y), bannerContent, bannerStyle);
        }
    }

    private string BuildPanel()
    {
        text.Length = 0;
        text.Append("<b>ENEMY AI · LIVE</b>   <color=#999999>[V] hide</color>\n");

        IHordeCommander commander = spawner != null ? spawner.Commander : null;
        PredictiveCommander predictive = commander as PredictiveCommander;
        text.Append("Commander:  ").Append(DescribeCommander(commander, predictive)).Append('\n');

        if (arena != null)
        {
            BotPersona persona = arena.Persona;
            string who = arena.HumanControl || persona == null
                ? "human (keyboard)"
                : $"test bot \"{persona.name}\": {DescribePersona(persona.name, PreferredRoute())}";
            text.Append("Player:  ").Append(who).Append('\n');
        }

        PlayerProfile profile = monitor != null ? monitor.Profile : null;
        bool waveActive = spawner != null && spawner.IsWaveActive;
        if (predictive != null && profile != null)
        {
            int needed = EscapesNeeded(profile.Settings.escapeDecay, predictive.MinEvidence);
            if (!WarmedUp(predictive))
                text.Append($"Warm-up:  {Mathf.Min(EscapesSeen(profile), needed - 1)}/{needed} escapes seen, no ambush yet (zombies act like the original AI)\n");
            else if (!predictive.PredictionConfident)
                text.Append(predictive.UsesBanditPlacement || predictive.UsesThompsonDirection
                    ? "Warm-up:  done, ambush decided after the current escape is scored\n"
                    : "Warm-up:  done, no confident guess yet (top direction needs 35%)\n");
            else
                text.Append($"Warm-up:  done, ambush unlocked  <color=#999999>({profile.Escapes} escapes seen)</color>\n");

            text.Append("Prediction:  ");
            if (predictive.HasPrediction)
            {
                IReadOnlyList<float> p = predictive.SectorProbabilities;
                int first = TopSector(p, 0, -1, -1);
                int second = TopSector(p, 0, first, -1);
                int third = TopSector(p, 0, first, second);
                string list = SectorPercent(p, first) + "   " + SectorPercent(p, second) + "   " + SectorPercent(p, third);
                text.Append(predictive.PredictionConfident ? list : $"<color=#999999>{list}  (not used yet)</color>");
            }
            else
            {
                text.Append("<color=#999999>none yet</color>");
            }
            text.Append('\n');

            if (waveActive)
            {
                AppendAmbush(predictive);
                AppendBandit(predictive);
            }
            else
            {
                text.Append("Ambush:  <color=#999999>none (between waves)</color>\n");
            }

            // 只显示玩家跑向伏击点的那种逃跑（老虎机也只用这种学习）；被追兵从避难点赶出来的逃跑方向五花八门，观众看了会困惑
            if (testedAmbushes.TryGetValue(predictive, out TestedAmbush tested) && tested.Time >= 0f && tested.Time >= EpisodeStartTime())
            {
                string result = tested.Contact
                    ? $"<color=#FF40D9>ran into it</color> (a waiting zombie got within {predictive.ContactRadius:0} u)"
                    : "got past it (no waiting zombie within " + predictive.ContactRadius.ToString("0", CultureInfo.InvariantCulture) + " u)";
                text.Append($"Last ambush test:  bot ran {SectorName(tested.Sector)} toward the ambush, {result}\n");
            }
        }

        if (arena != null && !arena.HumanControl && arena.Bot != null && arena.Bot.Brain != null)
        {
            BotBrain brain = arena.Bot.Brain;
            if (catches.TryGetValue(arena, out CatchInfo info) && info.Count > 0 && info.Episode == arena.Episode)
            {
                string by = info.ByAmbusher ? "a waiting zombie" : info.KnownAttacker ? "a chaser" : "an unknown attacker";
                text.Append($"Bot intercepted while fleeing:  {info.Count}× this episode, last by {by} " +
                            $"on route {info.Route}, {Time.time - info.GameTime:0}s ago\n");
            }
            text.Append("<color=#999999>Bot's route odds (hidden from the horde):  ");
            IReadOnlyList<float> weights = brain.RouteWeights;
            for (int i = 0; i < weights.Count; i++)
                text.Append(i > 0 ? "  " : "").Append(brain.RouteName(i)).Append(' ').Append(Percent(weights[i]));
            text.Append("</color>\n");
        }

        text.Append("<color=#999999>Game time ").Append(FormatClock(Time.timeSinceLevelLoad)).Append("   ");
        if (Time.captureDeltaTime > 0f)
            text.Append("speed: lockstep (uneven, set Lockstep Frame Time 0 for filming)");
        else
            text.Append("speed ").Append(Time.timeScale.ToString("0.##", CultureInfo.InvariantCulture)).Append('×')
                .Append(Mathf.Approximately(Time.timeScale, 1f) ? "" : Time.timeScale > 1f ? " (fast-forward)" : " (slow motion)");
        if (arena != null)
            text.Append($"   arena {arena.ArenaIndex}, episode {arena.Episode}, wave {arena.WaveInEpisode}");
        text.Append("</color>");
        return text.ToString();
    }

    private void AppendAmbush(PredictiveCommander predictive)
    {
        Vector2 center = monitor.Zones.GetZoneCenter(0);
        text.Append("Ambush:  ");
        bool any = false;
        int firstSector = -1;
        for (int i = 0; i < predictive.AmbushSiteCount; i++)
        {
            if (!predictive.TryGetAmbushSite(i, out int sector, out Vector2 position, out _, out Squad squad, out int quota))
                continue;
            if (firstSector < 0)
                firstSector = sector;
            text.Append(any ? "   +   " : "")
                .Append($"<color=#FF40D9>{SectorName(sector)}, {Vector2.Distance(position, center):0} u from base, " +
                        $"{CountWaiting(squad, position)}/{quota} waiting</color>");
            any = true;
        }
        if (!any)
        {
            text.Append("<color=#999999>none</color>\n");
            return;
        }
        // 伏击点不在最可能的方向时说明原因（不然观众看到扇形和伏击点不一致会困惑）
        int top = predictive.HasPrediction ? TopSector(predictive.SectorProbabilities, 0, -1, -1) : -1;
        if (top >= 0 && firstSector != top)
        {
            text.Append(predictive.UsesThompsonDirection
                ? "  <color=#999999>(direction drawn at random from the prediction)</color>"
                : predictive.UsesBanditPlacement
                    ? "  <color=#999999>(re-decided only after each scored escape)</color>"
                    : "  <color=#999999>(kept until another direction leads by 15 points)</color>");
        }
        text.Append('\n');
    }

    private void AppendBandit(PredictiveCommander predictive)
    {
        if (!predictive.UsesBanditPlacement || predictive.Bandit == null)
            return;
        if (!predictive.TryGetAmbushSite(0, out int sector, out _, out int chosen, out _, out _))
            return;
        AmbushBandit bandit = predictive.Bandit;
        text.Append(bandit.Shared ? "Bandit (all directions):  " : $"Bandit, {Compass[sector]}:  ");
        for (int d = 0; d < bandit.DistanceCount; d++)
        {
            string arm = $"{bandit.Distance(d):0} u {Percent(bandit.Mean(sector, d))}";
            text.Append(d > 0 ? "   " : "").Append(d == chosen ? $"<b>[{arm}]</b>" : arm);
        }
        text.Append("  <color=#999999>estimated hit rate</color>\n");
    }

    // ---------- 说明文字 ----------

    private static string DescribeCommander(IHordeCommander commander, PredictiveCommander predictive)
    {
        if (commander == null)
            return "<color=#999999>none (no horde yet)</color>";
        if (predictive != null)
        {
            bool counting = predictive.Predictor == null || predictive.Predictor is FrequencyPredictor;
            string text = (counting ? "V2, counts which way you flee" : "V2, a learned model guesses which way you flee") +
                          $", then sends {predictive.AmbushSize} zombies to wait there";
            if (predictive.UsesBanditPlacement)
                text += "; a bandit picks how far out they wait";
            if (predictive.UsesThompsonDirection)
                text += "; direction drawn at random from the prediction";
            return text + $"  <color=#999999>({commander.DisplayName})</color>";
        }
        if (commander is RLCommander)
            return "V1, reinforcement learning (PPO policy orders 3 squads)";
        if (commander is BaselineCommander)
            return "original AI, chase what it sees, otherwise attack the base";
        return commander.DisplayName;
    }

    /// <param name="route">本回合最偏好的路线，例如 "route A (E)"（训练场可能打乱路线顺序）</param>
    private static string DescribePersona(string persona, string route)
    {
        switch (persona)
        {
            case "Fighter": return "stands and fights, never flees";
            case "RunnerA": return $"always flees the same way, {route}";
            case "RunnerB80": return $"flees {route} 80% of the time";
            case "Mixed": return $"flees 60% of the time, prefers {route}";
            case "Adaptive": return $"prefers {route}, avoids a route after being caught on it";
            case "Kiter": return $"backs away while shooting, prefers {route}";
            case "Random": return "random habits, new each episode";
            default: return "scripted";
        }
    }

    /// <summary>本回合参数中权重最大的路线（机器人当前的偏好会随被拦截变化，这里用回合开始时的）</summary>
    private string PreferredRoute()
    {
        BotParams parameters = arena != null ? arena.CurrentParams : null;
        if (parameters == null || parameters.RouteWeights == null || parameters.RouteWeights.Length == 0)
            return "its favourite route";
        int best = 0;
        for (int i = 1; i < parameters.RouteWeights.Length; i++)
        {
            if (parameters.RouteWeights[i] > parameters.RouteWeights[best])
                best = i;
        }
        if (best >= arena.Routes.Count || arena.Routes[best] == null || monitor == null || monitor.Zones == null)
            return "its favourite route";
        Vector2 center = monitor.Zones.GetZoneCenter(0);
        int sector = RadialZoneMap.DirectionToSector((Vector2)arena.Routes[best].transform.position - center, Compass.Length);
        return $"route {arena.Routes[best].RouteName(best)} ({Compass[sector]})";
    }

    /// <summary>扇区名；训练场中加上路线名（A = 东，B = 西北，C = 西南）</summary>
    private string SectorName(int sector)
    {
        if (sector < 0 || sector >= Compass.Length)
            return "?";
        if (arena != null && monitor != null && monitor.Zones != null)
        {
            Vector2 center = monitor.Zones.GetZoneCenter(0);
            for (int i = 0; i < arena.Routes.Count; i++)
            {
                ArenaRoute route = arena.Routes[i];
                if (route != null && RadialZoneMap.DirectionToSector((Vector2)route.transform.position - center, Compass.Length) == sector)
                    return $"{Compass[sector]} (route {route.RouteName(i)})";
            }
        }
        return Compass[sector];
    }

    private string SectorPercent(IReadOnlyList<float> probabilities, int sector)
    {
        return sector >= 0 ? $"{SectorName(sector)} {Percent(probabilities[sector])}" : "";
    }

    // ---------- 计算 ----------

    private bool WarmedUp(PredictiveCommander predictive)
    {
        return monitor != null && monitor.Profile != null && monitor.Profile.EscapeEvidence >= predictive.MinEvidence;
    }

    /// <summary>证据每次有效逃跑先乘 decay 再加 1：达到 minEvidence 需要几次逃跑（默认 0.85、2 → 3）</summary>
    private static int EscapesNeeded(float decay, float minEvidence)
    {
        int count = 1;
        float evidence = 1f;
        while (evidence < minEvidence - 1e-4f && count < 100)
        {
            evidence = evidence * decay + 1f;
            count++;
        }
        return count;
    }

    /// <summary>证据相当于几次逃跑（预热期间显示用；画像每回合重置时与逃跑次数相同）</summary>
    private static int EscapesSeen(PlayerProfile profile)
    {
        float decay = profile.Settings.escapeDecay;
        int count = 0;
        float evidence = 0f;
        while (count < 100)
        {
            float next = evidence * decay + 1f;
            if (next > profile.EscapeEvidence + 1e-3f)
                break;
            evidence = next;
            count++;
        }
        return count;
    }

    private static int TopSector(IReadOnlyList<float> probabilities, int start, int skipA, int skipB)
    {
        int best = -1;
        for (int s = start; s < probabilities.Count; s++)
        {
            if (s == skipA || s == skipB)
                continue;
            if (best < 0 || probabilities[s] > probabilities[best])
                best = s;
        }
        return best;
    }

    private static int CountWaiting(Squad squad, Vector2 post)
    {
        if (squad == null)
            return 0;
        int waiting = 0;
        foreach (Enemy enemy in squad.Members)
        {
            if (enemy != null && !enemy.IsDead && Vector2.Distance(enemy.transform.position, post) <= WaitingDistance)
                waiting++;
        }
        return waiting;
    }

    private static Vector2 Dir(float radians)
    {
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private static string Percent(float value)
    {
        return Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatClock(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
               (seconds - minutes * 60f).ToString("00.0", CultureInfo.InvariantCulture);
    }

    /// <summary>字号按屏幕高度缩放：1080 高时 20 px（OnGUI 不随 DPI 缩放，Retina 录屏时文字会很小）</summary>
    private static float FontScale()
    {
        return Mathf.Max(0.6f, Screen.height / 1080f);
    }

    private void EnsureStyles()
    {
        int fontSize = Mathf.RoundToInt(20f * FontScale());
        if (panelStyle != null && fontSize == styledFontSize)
            return;
        styledFontSize = fontSize;

        if (panelBackground == null)
        {
            panelBackground = new Texture2D(1, 1);
            panelBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            panelBackground.Apply();
        }
        int padding = Mathf.RoundToInt(fontSize * 0.6f);
        panelStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = fontSize,
            wordWrap = true,
            richText = true,
            padding = new RectOffset(padding, padding, padding, padding)
        };
        panelStyle.normal.background = panelBackground;
        panelStyle.normal.textColor = Color.white;

        int labelPadding = Mathf.RoundToInt(fontSize * 0.25f);
        labelStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(labelPadding * 2, labelPadding * 2, labelPadding, labelPadding),
            fontSize = Mathf.RoundToInt(fontSize * 0.9f),
            fontStyle = FontStyle.Bold,
            wordWrap = false, // 矩形宽度按 CalcSize（不换行）计算
            richText = true
        };
        labelStyle.normal.background = panelBackground; // 深色底：文字压在线上也看得清
        labelStyle.normal.textColor = Color.white;

        bannerStyle = new GUIStyle(panelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(fontSize * 1.5f),
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
    }
}
