using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 脚本机器人玩家：每帧把场景信息交给 BotBrain，再通过 IPlayerInput 驱动同一物体上的
/// PlayerController 和 WeaponManager（与真人使用同一套移动 / 射击规则）。
/// 启用时接管输入，禁用时交还给键盘鼠标（训练场中可按 H 让真人接管）。
/// </summary>
[DefaultExecutionOrder(-50)] // 先于 PlayerController / WeaponManager 的 Update，本帧的输入本帧生效
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerBot : MonoBehaviour, IPlayerInput
{
    [SerializeField] private PlayerController player;
    [SerializeField] private WeaponManager weaponManager;
    [Tooltip("读取敌人的生成器（只看本训练场生成的敌人）。通常由 ArenaEnvironment 指定")]
    [SerializeField] private HordeEventSpawner enemySource;
    [SerializeField] private bool drawGizmos = true;

    private Rigidbody2D body;
    private BotBrain brain;
    private BotCommand command;
    private bool pendingFire;
    private bool pendingReload;
    private readonly List<Vector2> enemyPositions = new List<Vector2>();
    /// <summary>与 enemyPositions 一一对应的敌人（被拦截时找出是哪个敌人）</summary>
    private readonly List<Enemy> observedEnemies = new List<Enemy>();
    private const float ProbableAttackerRadius = 2.5f;

    /// <summary>
    /// 最近一次被拦截时触发它的敌人（Ambushed 触发时有效）。受到伤害时不知道是谁打的，取离玩家
    /// ProbableAttackerRadius 以内最近的敌人（可能的攻击者），没有则为 null
    /// </summary>
    public Enemy LastAmbushEnemy { get; private set; }

    public BotBrain Brain => brain;

    /// <summary>做出交战决定（转发 BotBrain.Decided）</summary>
    public event Action<BotDecision> Decided;
    /// <summary>逃跑路线上被拦截（转发 BotBrain.Ambushed）</summary>
    public event Action<int> Ambushed;

    // ---------- IPlayerInput ----------

    public Vector2 Move => command.Move;
    public Vector2 Aim => command.Aim;

    public bool ConsumeFire()
    {
        bool fire = pendingFire;
        pendingFire = false;
        return fire;
    }

    public bool ConsumeReload()
    {
        bool reload = pendingReload;
        pendingReload = false;
        return reload;
    }

    // ---------- 生命周期 ----------

    private void Awake()
    {
        if (player == null)
            player = GetComponent<PlayerController>();
        if (weaponManager == null)
            weaponManager = GetComponentInChildren<WeaponManager>();
        body = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        if (player != null)
            player.InputOverride = this;
        if (weaponManager != null)
            weaponManager.InputOverride = this;
    }

    private void OnDisable()
    {
        if (player != null && ReferenceEquals(player.InputOverride, this))
            player.InputOverride = null;
        if (weaponManager != null && ReferenceEquals(weaponManager.InputOverride, this))
            weaponManager.InputOverride = null;
        command = default;
        pendingFire = false;
        pendingReload = false;
    }

    /// <summary>指定读取敌人的生成器</summary>
    public void SetEnemySource(HordeEventSpawner spawner)
    {
        enemySource = spawner;
    }

    /// <summary>开始一个新回合：使用新的参数和训练场布局</summary>
    public void Begin(BotParams parameters, BotArenaLayout layout, int seed)
    {
        if (brain != null)
        {
            brain.Decided -= ForwardDecided;
            brain.Ambushed -= ForwardAmbushed;
        }
        brain = new BotBrain(parameters, layout, new System.Random(seed));
        brain.Decided += ForwardDecided;
        brain.Ambushed += ForwardAmbushed;
        command = default;
    }

    /// <summary>新一波开始（玩家已被放回待命位置）</summary>
    public void ResetForWave()
    {
        brain?.ResetForWave();
        command = default;
        pendingFire = false;
        pendingReload = false;
    }

    private void Update()
    {
        if (brain == null || player == null || player.IsDead)
        {
            command = default;
            pendingFire = false;
            return;
        }

        enemyPositions.Clear();
        observedEnemies.Clear();
        IReadOnlyList<GameObject> spawned = enemySource != null && enemySource.Context != null
            ? enemySource.Context.ActiveEnemies
            : null;
        if (spawned != null)
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                Enemy enemy = spawned[i] != null ? spawned[i].GetComponent<Enemy>() : null;
                if (enemy != null && !enemy.IsDead && enemy.IsMovingEnemy())
                {
                    enemyPositions.Add(enemy.transform.position);
                    observedEnemies.Add(enemy);
                }
            }
        }

        RangedWeapon weapon = weaponManager != null ? weaponManager.CurrentWeapon : null;
        var observation = new BotObservation
        {
            Time = Time.time,
            Position = transform.position,
            Velocity = body != null ? body.velocity : Vector2.zero,
            Health = player.CurrentHealth,
            WeaponReady = weapon != null && weapon.CanFire,
            IsReloading = weapon != null && weapon.IsReloading,
            Ammo = weapon != null ? weapon.CurrentAmmo : 0,
            MagazineSize = weapon != null ? weapon.MaxAmmo : 0,
            Enemies = enemyPositions
        };

        command = brain.Step(observation);
        if (command.Fire)
            pendingFire = true;
        if (command.Reload)
            pendingReload = true;
    }

    private void ForwardDecided(BotDecision decision)
    {
        Decided?.Invoke(decision);
    }

    private void ForwardAmbushed(int route)
    {
        int index = brain.LastAmbushEnemyIndex;
        LastAmbushEnemy = index >= 0 && index < observedEnemies.Count ? observedEnemies[index] : ProbableAttacker();
        Ambushed?.Invoke(route);
    }

    private Enemy ProbableAttacker()
    {
        Enemy nearest = null;
        float best = ProbableAttackerRadius;
        Vector2 position = transform.position;
        for (int i = 0; i < observedEnemies.Count; i++)
        {
            float distance = Vector2.Distance(enemyPositions[i], position);
            if (observedEnemies[i] != null && distance <= best)
            {
                best = distance;
                nearest = observedEnemies[i];
            }
        }
        return nearest;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || brain == null || !isActiveAndEnabled)
            return;

        switch (brain.Mode)
        {
            case BotMode.Flee:
            case BotMode.Kite:
                if (brain.Route >= 0 && brain.Route < brain.Layout.Routes.Count)
                {
                    Gizmos.color = brain.Mode == BotMode.Flee ? Color.red : new Color(1f, 0.5f, 0f);
                    Gizmos.DrawLine(transform.position, brain.Layout.Routes[brain.Route].Refuge);
                }
                break;
            case BotMode.Return:
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, brain.Layout.Home);
                break;
            case BotMode.Fight:
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, (Vector3)command.Aim * 3f);
                break;
        }
    }
}
