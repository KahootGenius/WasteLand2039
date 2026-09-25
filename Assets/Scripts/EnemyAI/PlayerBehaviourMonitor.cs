using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家行为监测：以固定频率（游戏时间）采样玩家和敌人，驱动
/// EngagementTracker（交战 / 逃跑识别）→ PlayerProfile（玩家画像），并写入遥测文件。
/// 通过 HordeEventSpawner.AttachPlayerModel 把分区、画像和交战状态提供给指挥官。
///
/// 实验分支上由 EnemyAIBootstrap 自动添加到 HordeEventSpawner 所在物体（无需修改场景）；
/// 若场景中已手动放置本组件，则不会自动添加。
/// 游戏中按 ` 键（Esc 下方）显示 / 隐藏调试面板。
/// </summary>
[DisallowMultipleComponent]
public class PlayerBehaviourMonitor : MonoBehaviour
{
    [Header("引用（为空时自动查找）")]
    [SerializeField] private HordeEventSpawner spawner;
    [SerializeField] private PlayerController player;
    [SerializeField] private WeaponManager weaponManager;

    [Header("分区（以主基地为中心；找不到基地时以玩家初始位置为中心）")]
    [SerializeField] private int zoneSectors = 8;
    [SerializeField] private float coreRadius = 6f;
    [Tooltip("环带边界半径（升序）；最后一个环带延伸到无穷远")]
    [SerializeField] private float[] bandEdges = { 20f };

    [Header("行为识别")]
    [SerializeField] private EngagementSettings engagementSettings = new EngagementSettings();

    [Header("玩家画像")]
    [SerializeField] private ProfileSettings profileSettings = new ProfileSettings();

    [Header("数据记录")]
    [SerializeField] private bool writeTelemetry = true;
    [Tooltip("采样频率（次/秒，游戏时间，不受帧率和 Time.timeScale 影响）")]
    [SerializeField] private float sampleRate = 10f;
    [Tooltip("附加在会话目录名后的标签（多个训练场同时运行时用于区分）")]
    [SerializeField] private string sessionTag = "";

    [Header("调试显示")]
    [SerializeField] private KeyCode overlayKey = KeyCode.BackQuote;
    [SerializeField] private bool showOverlay = false;
    [SerializeField] private bool drawZoneGizmos = true;

    public IZoneMap Zones => zoneMap;
    public EngagementTracker Tracker => tracker;
    public PlayerProfile Profile => profile;
    public string TelemetryDirectory => telemetry != null ? telemetry.DirectoryPath : null;

    /// <summary>所有会话目录的父目录</summary>
    public static string TelemetryRoot => Path.Combine(Application.persistentDataPath, "EnemyAITelemetry");

    private RadialZoneMap zoneMap;
    private EngagementTracker tracker;
    private PlayerProfile profile;
    private TelemetryWriter telemetry;

    private Rigidbody2D playerBody;
    private readonly List<Vector2> enemyPositions = new List<Vector2>();
    private float sampleAccumulator;
    private int shotsSinceSample;
    private float lastHealth = -1f;
    private bool playerWasDead;
    private int waveIndex = -1;
    private float lastFlushRealtime;
    private PlayerSample lastSample;
    private bool closed;
    private GUIStyle overlayStyle;

    // ---------- 生命周期 ----------

    private void Awake()
    {
        if (spawner == null)
            spawner = GetComponent<HordeEventSpawner>();
        if (spawner == null)
            spawner = FindObjectOfType<HordeEventSpawner>();

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.GetComponent<PlayerController>();
        }
        if (player == null)
            player = FindObjectOfType<PlayerController>();

        if (weaponManager == null && player != null)
            weaponManager = player.GetComponentInChildren<WeaponManager>();
        if (weaponManager == null)
            weaponManager = FindObjectOfType<WeaponManager>();

        if (player != null)
            playerBody = player.GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        if (player == null)
        {
            Debug.LogWarning("[PlayerBehaviourMonitor] 未找到玩家，监测停用", this);
            enabled = false;
            return;
        }

        Transform mainBase = FindMainBase();
        Vector2 center = mainBase != null ? (Vector2)mainBase.position : (Vector2)player.transform.position;
        zoneMap = new RadialZoneMap(center, zoneSectors, coreRadius, bandEdges);

        tracker = new EngagementTracker(zoneMap, engagementSettings);
        if (mainBase != null)
            tracker.BasePosition = mainBase.position;
        profile = new PlayerProfile(zoneMap.ZoneCount, profileSettings);

        tracker.EngagementStarted += HandleEngagementStarted;
        tracker.EngagementEnded += HandleEngagementEnded;
        tracker.EscapeStarted += HandleEscapeStarted;
        tracker.EscapeEnded += HandleEscapeEnded;
        tracker.StateChanged += HandleStateChanged;

        if (weaponManager != null)
            weaponManager.OnWeaponFired += HandleWeaponFired;
        else
            Debug.LogWarning("[PlayerBehaviourMonitor] 未找到 WeaponManager，无法统计开火（所有交战会被判为 Passive / Flee）", this);

        Enemy.AnyDied += HandleEnemyDied;

        if (spawner != null)
        {
            spawner.AttachPlayerModel(zoneMap, profile, tracker);
            spawner.OnHordeEventStarted += HandleWaveStarted;
            spawner.OnHordeEventCompleted += HandleWaveCompleted;
            spawner.OnEnemySpawned += HandleEnemySpawned;
        }

        lastHealth = player.CurrentHealth;
        lastSample = new PlayerSample { Time = Time.time, Position = player.transform.position };

        if (writeTelemetry)
            OpenTelemetry(mainBase);
    }

    private void Update()
    {
        if (Input.GetKeyDown(overlayKey))
            showOverlay = !showOverlay;
    }

    private void FixedUpdate()
    {
        if (tracker == null)
            return;

        sampleAccumulator += Time.fixedDeltaTime;
        if (sampleAccumulator < 1f / Mathf.Max(1f, sampleRate))
            return;

        float deltaTime = sampleAccumulator;
        sampleAccumulator = 0f;
        TakeSample(deltaTime);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            telemetry?.Flush();
    }

    private void OnDestroy()
    {
        Close();
    }

    // ---------- 采样 ----------

    private void TakeSample(float deltaTime)
    {
        Vector2 position = player.transform.position;
        Vector2 velocity = playerBody != null
            ? playerBody.velocity
            : (position - lastSample.Position) / Mathf.Max(deltaTime, 0.0001f);

        float health = player.CurrentHealth;
        float damage = lastHealth >= 0f ? Mathf.Max(0f, lastHealth - health) : 0f;
        lastHealth = health;

        enemyPositions.Clear();
        IReadOnlyList<Enemy> enemies = Enemy.AllActive;
        for (int i = 0; i < enemies.Count; i++)
        {
            Enemy enemy = enemies[i];
            if (enemy != null && !enemy.IsDead && enemy.IsMovingEnemy())
                enemyPositions.Add(enemy.transform.position);
        }

        var sample = new PlayerSample
        {
            Time = Time.time,
            DeltaTime = deltaTime,
            Position = position,
            Velocity = velocity,
            ShotsFired = shotsSinceSample,
            DamageTaken = damage
        };
        shotsSinceSample = 0;

        bool dead = player.IsDead;
        if (dead && !playerWasDead)
        {
            tracker.ForceEnd(sample);
            telemetry?.Event("player_died", sample.Time)
                .Add("x", position.x).Add("y", position.y)
                .Add("zone", zoneMap.GetZoneName(zoneMap.GetZone(position)))
                .Write();
        }
        playerWasDead = dead;

        if (!dead)
            tracker.Step(sample, enemyPositions);
        lastSample = sample;

        if (telemetry != null)
        {
            telemetry.WriteSample(sample.Time, waveIndex, position.x, position.y, velocity.x, velocity.y, health,
                zoneMap.GetZoneName(tracker.CurrentZone), tracker.State.ToString(), tracker.CurrentEngagementId,
                tracker.ActiveEscape != null ? tracker.ActiveEscape.Id : -1, tracker.EnemiesInEngageRadius,
                tracker.NearestEnemyDistance, tracker.RetreatRatio, tracker.ShotRate,
                sample.ShotsFired, damage, enemyPositions.Count);

            if (Time.realtimeSinceStartup - lastFlushRealtime > 2f)
            {
                telemetry.Flush();
                lastFlushRealtime = Time.realtimeSinceStartup;
            }
        }
    }

    // ---------- 事件 ----------

    private void HandleWeaponFired()
    {
        shotsSinceSample++;
    }

    private void HandleEnemyDied(Enemy enemy)
    {
        tracker.NotifyKill();
        if (telemetry == null || enemy == null)
            return;

        Vector2 position = enemy.transform.position;
        telemetry.Event("enemy_killed", Time.time)
            .Add("id", enemy.GetInstanceID())
            .Add("x", position.x).Add("y", position.y)
            .Add("zone", zoneMap.GetZoneName(zoneMap.GetZone(position)))
            .Add("player_distance", Vector2.Distance(position, lastSample.Position))
            .Add("engagement", tracker.CurrentEngagementId)
            .Write();
    }

    private void HandleEnemySpawned(GameObject enemyObject)
    {
        if (telemetry == null || enemyObject == null)
            return;

        Vector2 position = enemyObject.transform.position;
        Enemy enemy = enemyObject.GetComponent<Enemy>();
        telemetry.Event("enemy_spawn", Time.time)
            .Add("id", enemy != null ? enemy.GetInstanceID() : enemyObject.GetInstanceID())
            .Add("x", position.x).Add("y", position.y)
            .Add("zone", zoneMap.GetZoneName(zoneMap.GetZone(position)))
            .Add("order", enemy != null ? enemy.CurrentOrder.ToString() : "")
            .Write();
    }

    private void HandleWaveStarted(HordeEvent hordeEvent)
    {
        waveIndex = spawner != null && spawner.Context != null ? spawner.Context.WaveIndex : waveIndex + 1;
        telemetry?.Event("wave_start", Time.time)
            .Add("wave", waveIndex)
            .Add("name", hordeEvent != null ? hordeEvent.hordeName : "")
            .Add("total", hordeEvent != null ? hordeEvent.totalEnemyCount : 0)
            .Add("commander", CommanderName())
            .Write();
    }

    private void HandleWaveCompleted(HordeEvent hordeEvent)
    {
        telemetry?.Event("wave_end", Time.time).Add("wave", waveIndex).Write();
        WriteProfileSnapshot("wave_end");
    }

    private void HandleEngagementStarted(EngagementSummary engagement)
    {
        telemetry?.Event("engagement_start", Time.time)
            .Add("id", engagement.Id)
            .Add("zone", zoneMap.GetZoneName(engagement.StartZone))
            .Add("x", engagement.StartPosition.x).Add("y", engagement.StartPosition.y)
            .Add("enemies_near", tracker.EnemiesInEngageRadius)
            .Write();
    }

    private void HandleEngagementEnded(EngagementSummary engagement)
    {
        profile.AddEngagement(engagement);
        telemetry?.Event("engagement_end", engagement.EndTime)
            .Add("id", engagement.Id)
            .Add("duration", engagement.Duration)
            .Add("dominant", engagement.Dominant.ToString())
            .Add("fight_s", engagement.Seconds(EngagementState.Fight))
            .Add("flee_s", engagement.Seconds(EngagementState.Flee))
            .Add("kite_s", engagement.Seconds(EngagementState.Kite))
            .Add("passive_s", engagement.Seconds(EngagementState.Passive))
            .Add("shots", engagement.Shots)
            .Add("damage", engagement.DamageTaken)
            .Add("kills", engagement.Kills)
            .Add("escapes", engagement.Escapes)
            .Add("released", engagement.Released)
            .Add("start_zone", zoneMap.GetZoneName(engagement.StartZone))
            .Add("end_zone", zoneMap.GetZoneName(engagement.EndZone))
            .Write();
    }

    private void HandleEscapeStarted(EscapeEpisode escape)
    {
        telemetry?.Event("escape_start", escape.StartTime)
            .Add("id", escape.Id)
            .Add("engagement", escape.EngagementId)
            .Add("zone", zoneMap.GetZoneName(escape.StartZone))
            .Add("x", escape.StartPosition.x).Add("y", escape.StartPosition.y)
            .Write();
    }

    private void HandleEscapeEnded(EscapeEpisode escape)
    {
        profile.AddEscape(escape);
        if (telemetry == null)
            return;

        var route = new List<string>(escape.Route.Count);
        foreach (int zone in escape.Route)
            route.Add(zoneMap.GetZoneName(zone));

        telemetry.Event("escape_end", escape.EndTime)
            .Add("id", escape.Id)
            .Add("engagement", escape.EngagementId)
            .Add("valid", escape.IsValid)
            .Add("type", escape.Type.ToString())
            .Add("reason", escape.EndReason.ToString())
            .Add("duration", escape.Duration)
            .Add("start_zone", zoneMap.GetZoneName(escape.StartZone))
            .Add("end_zone", zoneMap.GetZoneName(escape.EndZone))
            .Add("route", route)
            .Add("path", escape.PathLength)
            .Add("dx", escape.Displacement.x).Add("dy", escape.Displacement.y)
            .Add("damage", escape.DamageTaken)
            .Add("base_d0", escape.StartBaseDistance).Add("base_d1", escape.EndBaseDistance)
            .Write();

        if (escape.IsValid)
            WriteProfileSnapshot("escape_end");
    }

    private void HandleStateChanged(EngagementState previous, EngagementState next)
    {
        telemetry?.Event("state", Time.time)
            .Add("from", previous.ToString())
            .Add("to", next.ToString())
            .Add("engagement", tracker.CurrentEngagementId)
            .Write();
    }

    // ---------- 遥测文件 ----------

    private void OpenTelemetry(Transform mainBase)
    {
        try
        {
            string sceneName = SceneManager.GetActiveScene().name;
            string folder = $"{DateTime.Now:yyyyMMdd_HHmmss}_{sceneName}" + (string.IsNullOrEmpty(sessionTag) ? "" : "_" + sessionTag);
            string path = Path.Combine(TelemetryRoot, folder);
            for (int suffix = 2; Directory.Exists(path); suffix++)
                path = Path.Combine(TelemetryRoot, $"{folder}_{suffix}");

            telemetry = new TelemetryWriter(path);

            var zoneNames = new List<string>(zoneMap.ZoneCount);
            for (int z = 0; z < zoneMap.ZoneCount; z++)
                zoneNames.Add(zoneMap.GetZoneName(z));

            string session = JsonLine.Standalone()
                .Add("format_version", 1)
                .Add("created", DateTime.Now.ToString("o"))
                .Add("scene", sceneName)
                .Add("tag", sessionTag)
                .Add("unity", Application.unityVersion)
                .Add("platform", Application.platform.ToString())
                .Add("commander_at_start", CommanderName())
                .Add("sample_rate", sampleRate)
                .Add("zone_type", "radial")
                .Add("zone_center_x", zoneMap.Center.x).Add("zone_center_y", zoneMap.Center.y)
                .Add("zone_center_is_base", mainBase != null)
                .Add("zone_sectors", zoneMap.Sectors)
                .Add("zone_core_radius", zoneMap.CoreRadius)
                .Add("zone_band_edges", new List<float>(zoneMap.BandEdges))
                .Add("zones", zoneNames)
                .Add("engage_radius", engagementSettings.engageRadius)
                .Add("release_radius", engagementSettings.releaseRadius)
                .Add("release_delay", engagementSettings.releaseDelay)
                .Add("threat_radius", engagementSettings.threatRadius)
                .Add("movement_window_seconds", engagementSettings.movementWindowSeconds)
                .Add("shot_window_seconds", engagementSettings.shotWindowSeconds)
                .Add("player_max_speed", engagementSettings.playerMaxSpeed)
                .Add("retreat_ratio", engagementSettings.retreatRatio)
                .Add("shooting_rate", engagementSettings.shootingRate)
                .Add("min_escape_seconds", engagementSettings.minEscapeSeconds)
                .Add("engagement_decay", profileSettings.engagementDecay)
                .Add("escape_decay", profileSettings.escapeDecay)
                .ToString();
            telemetry.WriteJsonFile("session.json", session);

            Debug.Log($"[PlayerBehaviourMonitor] 遥测记录到: {path}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PlayerBehaviourMonitor] 无法创建遥测目录，本次不记录: {exception.Message}", this);
            telemetry = null;
        }
    }

    private void WriteProfileSnapshot(string reason)
    {
        if (telemetry == null)
            return;

        telemetry.Event("profile", Time.time)
            .Add("reason", reason)
            .Add("engagements", profile.Engagements)
            .Add("escapes", profile.Escapes)
            .Add("observation", profile.ToObservation())
            .Write();
    }

    private void Close()
    {
        if (closed)
            return;
        closed = true;

        if (tracker != null)
        {
            if (tracker.IsEngaged)
                tracker.ForceEnd(lastSample);

            tracker.EngagementStarted -= HandleEngagementStarted;
            tracker.EngagementEnded -= HandleEngagementEnded;
            tracker.EscapeStarted -= HandleEscapeStarted;
            tracker.EscapeEnded -= HandleEscapeEnded;
            tracker.StateChanged -= HandleStateChanged;
        }

        if (weaponManager != null)
            weaponManager.OnWeaponFired -= HandleWeaponFired;
        Enemy.AnyDied -= HandleEnemyDied;
        if (spawner != null)
        {
            spawner.OnHordeEventStarted -= HandleWaveStarted;
            spawner.OnHordeEventCompleted -= HandleWaveCompleted;
            spawner.OnEnemySpawned -= HandleEnemySpawned;
        }

        if (telemetry != null)
        {
            WriteProfileSnapshot("session_end");
            var topZones = new List<string>();
            foreach (int zone in profile.TopEscapeZones(5))
                topZones.Add($"{zoneMap.GetZoneName(zone)}:{profile.EscapeZoneProbability(zone):0.###}");

            string summary = JsonLine.Standalone()
                .Add("commander", CommanderName())
                .Add("waves_started", waveIndex + 1)
                .Add("engagements", profile.Engagements)
                .Add("escapes", profile.Escapes)
                .Add("fight_share", profile.FightShare)
                .Add("flee_share", profile.FleeShare)
                .Add("kite_share", profile.KiteShare)
                .Add("passive_share", profile.PassiveShare)
                .Add("consistency", profile.Consistency)
                .Add("mean_escape_distance", profile.MeanEscapeDistance)
                .Add("toward_base_rate", profile.TowardBaseRate)
                .Add("got_away_rate", profile.GotAwayRate)
                .Add("top_escape_zones", topZones)
                .Add("observation", profile.ToObservation())
                .Add("samples", telemetry.SampleCount)
                .Add("events", telemetry.EventCount)
                .ToString();
            try
            {
                telemetry.WriteJsonFile("profile_final.json", summary);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlayerBehaviourMonitor] 写入 profile_final.json 失败: {exception.Message}", this);
            }
            telemetry.Dispose();
            telemetry = null;
        }
    }

    // ---------- 辅助 ----------

    private string CommanderName()
    {
        return spawner != null && spawner.Commander != null ? spawner.Commander.DisplayName : "(none)";
    }

    private static Transform FindMainBase()
    {
        GameObject baseObject = GameObject.FindGameObjectWithTag("MainBase");
        if (baseObject != null)
            return baseObject.transform;

        MainBase mainBase = FindObjectOfType<MainBase>();
        return mainBase != null ? mainBase.transform : null;
    }

    // ---------- 调试显示 ----------

    private void OnGUI()
    {
        if (!showOverlay || tracker == null)
            return;

        if (overlayStyle == null)
        {
            overlayStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };
            overlayStyle.normal.textColor = Color.white;
        }

        string text =
            $"敌人AI监测  （按 {overlayKey} 隐藏）\n" +
            $"指挥官: {CommanderName()}   波次: {waveIndex}   区域: {zoneMap.GetZoneName(tracker.CurrentZone)}\n" +
            $"状态: {tracker.State}   交战 #{tracker.CurrentEngagementId}   撤退 {tracker.RetreatRatio:F2}   射速 {tracker.ShotRate:F1}/s\n" +
            "—— 玩家画像 ——\n" +
            profile.Describe(zoneMap) +
            (telemetry != null ? $"遥测: {telemetry.DirectoryPath}" : "遥测: 未记录");

        const float width = 520f;
        float height = overlayStyle.CalcHeight(new GUIContent(text), width);
        GUI.Box(new Rect(10f, 10f, width, height), text, overlayStyle);
    }

    private void OnDrawGizmos()
    {
        if (!drawZoneGizmos || zoneMap == null)
            return;

        Vector3 center = zoneMap.Center;
        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        DrawCircle(center, zoneMap.CoreRadius);

        float outer = zoneMap.CoreRadius;
        foreach (float edge in zoneMap.BandEdges)
        {
            DrawCircle(center, edge);
            outer = edge;
        }
        float rayEnd = outer * 1.5f + 1f;

        float sectorWidth = 2f * Mathf.PI / zoneMap.Sectors;
        for (int s = 0; s < zoneMap.Sectors; s++)
        {
            float angle = (s + 0.5f) * sectorWidth; // 扇区边界
            Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Gizmos.DrawLine(center + direction * zoneMap.CoreRadius, center + direction * rayEnd);
        }

#if UNITY_EDITOR
        for (int zone = 0; zone < zoneMap.ZoneCount; zone++)
            UnityEditor.Handles.Label(zoneMap.GetZoneCenter(zone), zoneMap.GetZoneName(zone));
#endif

        if (tracker != null && tracker.ActiveEscape != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(tracker.ActiveEscape.StartPosition, lastSample.Position);
        }
    }

    private static void DrawCircle(Vector3 center, float radius, int segments = 64)
    {
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Enemy AI/Open Telemetry Folder")]
    private static void OpenTelemetryFolder()
    {
        Directory.CreateDirectory(TelemetryRoot);
        UnityEditor.EditorUtility.RevealInFinder(TelemetryRoot);
    }
#endif
}
