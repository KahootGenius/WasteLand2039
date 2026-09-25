using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 一个 ML 训练场：基地、若干逃跑路线（避难点）、由机器人（或真人）操作的玩家，
/// 以及尸潮生成器 + 指挥官 + 玩家行为监测。
///
/// 按"回合"循环运行：每回合从性格资产采样一组机器人参数（默认同时清空玩家画像，即"换一个玩家"），
/// 连续打 wavesPerEpisode 波尸潮；每波在敌人全灭、超时或玩家死亡时结束，然后重置玩家和基地。
/// 机器人的每次决定写入遥测（bot_decision），作为评估玩家画像的真实标签。
///
/// 由 ArenaManager 复制多份（相距足够远，互不干扰）；也可以单独放进场景使用。
/// </summary>
public class ArenaEnvironment : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private HordeEventSpawner spawner;
    [SerializeField] private PlayerBehaviourMonitor monitor;
    [SerializeField] private PlayerController player;
    [SerializeField] private PlayerBot bot;
    [SerializeField] private ArenaBase arenaBase;
    [SerializeField] private Transform playerStart;
    [Tooltip("逃跑路线；顺序即 A, B, C…，与性格资产的路线偏好对应")]
    [SerializeField] private List<ArenaRoute> routes = new List<ArenaRoute>();

    [Header("尸潮与回合")]
    [SerializeField] private HordeEvent wave;
    [SerializeField, Min(1)] private int wavesPerEpisode = 3;
    [Tooltip("每波最长时间（秒，游戏时间）；超时则中止本波")]
    [SerializeField] private float waveTimeLimit = 90f;
    [Tooltip("每波开始前的等待（秒）")]
    [SerializeField] private float delayBeforeWave = 2f;
    [Tooltip("玩家死亡后等待多久再重置（秒，让死亡动画播完）")]
    [SerializeField] private float deathPause = 1f;
    [Tooltip("每回合开始时清空玩家画像（每回合相当于一个新的随机化玩家）")]
    [SerializeField] private bool resetProfileEachEpisode = true;

    [Header("机器人")]
    [SerializeField] private BotPersona persona;
    [Tooltip("机器人离基地超过此距离时往回走")]
    [SerializeField] private float boundsRadius = 38f;
    [SerializeField] private int seed = 1;

    private System.Random rng;
    private bool humanControl;
    private BotParams currentParams;
    private int decisionsThisWave;

    public int ArenaIndex { get; private set; }
    public int Episode { get; private set; }
    public int WaveInEpisode { get; private set; }
    public BotPersona Persona => persona;
    public BotParams CurrentParams => currentParams;
    public PlayerController Player => player;
    public PlayerBot Bot => bot;
    public PlayerBehaviourMonitor Monitor => monitor;
    public HordeEventSpawner Spawner => spawner;
    public IReadOnlyList<ArenaRoute> Routes => routes;

    /// <summary>true = 机器人停用，由键盘鼠标操作（本回合剩余时间的数据标记为 human）</summary>
    public bool HumanControl
    {
        get => humanControl || persona == null || bot == null;
        set
        {
            humanControl = value;
            if (bot != null)
                bot.enabled = !HumanControl;
            monitor?.LogEvent("control_changed").Add("control", HumanControl ? "human" : "bot").Write();
        }
    }

    public string ControllerName => HumanControl ? "human" : "bot:" + persona.name;

    /// <summary>
    /// 由 ArenaManager 在实例化后立即（Start 之前）调用：指定序号、性格和随机种子
    /// </summary>
    public void Configure(int index, BotPersona botPersona, int randomSeed)
    {
        ArenaIndex = index;
        persona = botPersona;
        seed = randomSeed;
        name = $"Arena {index} ({(persona != null ? persona.name : "human")})";
        if (monitor != null)
            monitor.SessionTag = $"arena{index}_{(persona != null ? persona.name : "human")}";
    }

    private void Awake()
    {
        if (monitor != null && string.IsNullOrEmpty(monitor.SessionTag))
            monitor.SessionTag = "arena_" + (persona != null ? persona.name : "human");
        if (bot != null)
            bot.SetEnemySource(spawner);
    }

    private IEnumerator Start()
    {
        rng = new System.Random(seed);
        if (bot != null)
        {
            bot.enabled = !HumanControl;
            bot.Decided += HandleBotDecided;
            bot.Ambushed += HandleBotAmbushed;
        }
        if (monitor != null)
            monitor.OverlayExtra = OverlayText;

        yield return null; // 等监测组件完成 Start（遥测目录、画像）

        if (wave == null || spawner == null || player == null)
        {
            Debug.LogError($"[ArenaEnvironment] {name}: 缺少尸潮配置 / 生成器 / 玩家，训练场停用", this);
            yield break;
        }

        while (true)
        {
            BeginEpisode();
            for (WaveInEpisode = 1; WaveInEpisode <= wavesPerEpisode; WaveInEpisode++)
            {
                ResetPlayerAndBase();
                yield return new WaitForSeconds(delayBeforeWave);
                yield return RunWave();
            }
            EndEpisode();
        }
    }

    private void OnDestroy()
    {
        if (bot != null)
        {
            bot.Decided -= HandleBotDecided;
            bot.Ambushed -= HandleBotAmbushed;
        }
    }

    // ---------- 回合 ----------

    private void BeginEpisode()
    {
        Episode++;
        if (resetProfileEachEpisode && Episode > 1)
            monitor?.ResetProfile("episode");

        currentParams = persona != null ? persona.Sample(rng) : null;
        if (bot != null && currentParams != null)
            bot.Begin(currentParams, BuildLayout(), rng.Next());

        if (monitor != null)
        {
            JsonLine line = monitor.LogEvent("episode_start")
                .Add("episode", Episode)
                .Add("arena", ArenaIndex)
                .Add("control", HumanControl ? "human" : "bot");
            if (currentParams != null)
                currentParams.AddTo(line);
            line.Write();
        }
    }

    private void EndEpisode()
    {
        if (monitor == null)
            return;
        monitor.LogEvent("episode_end").Add("episode", Episode).Add("arena", ArenaIndex).Write();
        monitor.LogProfileSnapshot("episode_end");
    }

    private IEnumerator RunWave()
    {
        decisionsThisWave = 0;
        float startTime = Time.time;
        spawner.StartHordeEvent(wave);

        while (spawner.IsWaveActive && !player.IsDead && Time.time - startTime < waveTimeLimit)
            yield return null;

        string outcome = !spawner.IsWaveActive ? "cleared" : player.IsDead ? "player_died" : "timeout";
        int spawned = spawner.Context != null ? spawner.Context.SpawnedCount : 0;
        int alive = spawner.Context != null ? spawner.Context.ActiveEnemies.Count : 0;
        if (spawner.IsWaveActive)
            spawner.AbortHordeEvent();

        monitor?.LogEvent("arena_wave_end")
            .Add("episode", Episode)
            .Add("wave_in_episode", WaveInEpisode)
            .Add("outcome", outcome)
            .Add("duration", Time.time - startTime)
            .Add("spawned", spawned)
            .Add("alive_at_end", alive)
            .Add("player_hp", player.CurrentHealth)
            .Add("base_damage", arenaBase != null ? arenaBase.DamageTaken : 0f)
            .Add("bot_decisions", decisionsThisWave)
            .Add("control", HumanControl ? "human" : "bot")
            .Write();

        if (player.IsDead)
            yield return new WaitForSeconds(deathPause);
    }

    private void ResetPlayerAndBase()
    {
        if (player.IsDead)
            player.Revive();
        else
            player.FullHeal();

        // 死于开火动画中时动画事件 OnAttackComplete 不会触发，攻击状态会一直锁住移动
        player.OnAttackComplete();
        Animator animator = player.GetComponent<Animator>();
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }
        if (player.DiePanel != null)
            player.DiePanel.SetActive(false);

        player.transform.position = playerStart != null ? playerStart.position : transform.position;
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
            body.velocity = Vector2.zero;

        WeaponManager weapons = player.GetComponentInChildren<WeaponManager>();
        if (weapons != null && weapons.CurrentWeapon != null)
            weapons.CurrentWeapon.SetWeaponStats(weapons.CurrentWeapon.Stats); // 满弹匣、取消换弹

        if (arenaBase != null)
            arenaBase.ResetBase();
        if (bot != null)
            bot.ResetForWave();
    }

    private BotArenaLayout BuildLayout()
    {
        var layout = new BotArenaLayout
        {
            Base = arenaBase != null ? (Vector2)arenaBase.transform.position : (Vector2)transform.position,
            Home = playerStart != null ? (Vector2)playerStart.position : (Vector2)transform.position,
            BoundsRadius = boundsRadius
        };
        for (int i = 0; i < routes.Count; i++)
        {
            if (routes[i] != null)
                layout.Routes.Add(new BotRoute { Name = routes[i].RouteName(i), Refuge = routes[i].transform.position });
        }
        return layout;
    }

    // ---------- 遥测 ----------

    private void HandleBotDecided(BotDecision decision)
    {
        decisionsThisWave++;
        IZoneMap zones = monitor != null ? monitor.Zones : null;
        monitor?.LogEvent("bot_decision")
            .Add("episode", Episode)
            .Add("mode", decision.Mode.ToString())
            .Add("route", decision.RouteName)
            .Add("nearest", decision.NearestEnemyDistance)
            .Add("x", decision.Position.x).Add("y", decision.Position.y)
            .Add("zone", zones != null ? zones.GetZoneName(zones.GetZone(decision.Position)) : "")
            .Write();
    }

    private void HandleBotAmbushed(int route)
    {
        if (monitor == null || bot == null || bot.Brain == null)
            return;
        var weights = new List<float>(bot.Brain.RouteWeights);
        monitor.LogEvent("bot_ambushed")
            .Add("episode", Episode)
            .Add("route", bot.Brain.RouteName(route))
            .Add("route_weights", weights)
            .Write();
    }

    // ---------- 显示 ----------

    /// <summary>一行状态（ArenaManager 的状态栏）</summary>
    public string StatusLine()
    {
        string mode = !HumanControl && bot != null && bot.Brain != null ? bot.Brain.Mode.ToString() : "human";
        return string.Format(CultureInfo.InvariantCulture, "{0}  回合 {1} 第 {2}/{3} 波  {4}",
            name, Episode, Mathf.Min(WaveInEpisode, wavesPerEpisode), wavesPerEpisode, mode);
    }

    private string OverlayText()
    {
        string text = $"—— 训练场 {ArenaIndex}：{ControllerName} ——\n回合 {Episode}，第 {Mathf.Min(WaveInEpisode, wavesPerEpisode)}/{wavesPerEpisode} 波";
        if (!HumanControl && currentParams != null)
            text += "\n本回合参数: " + currentParams.Describe();
        if (!HumanControl && bot != null && bot.Brain != null)
            text += "\n机器人: " + bot.Brain.Describe();
        return text;
    }
}
