using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 调试用指挥官：在游戏中验证命令系统（不是对比实验中的一组）。
/// 生成的敌人按顺序轮流分配到：追击小队、MoveTo 小队、各伏击点的驻守小队。
///
/// 用法：给 HordeEventSpawner 所在物体添加此组件（会被自动识别），运行游戏并触发尸潮。
/// 在 Scene 视图开启 Gizmos 可看到命令连线：黄色 = MoveTo，品红 = HoldAt（圆圈为交战半径）。
/// </summary>
public class DebugOrdersCommander : MonoBehaviour, IHordeCommander
{
    [Tooltip("伏击点；为空时在每波开始时以玩家当前位置为中心自动生成 4 个点（上下左右）")]
    [SerializeField] private Transform[] ambushPoints;
    [SerializeField] private float autoPointDistance = 8f;
    [SerializeField] private float ambushEngageRadius = 6f;

    [Tooltip("包含一个无视检测半径、持续追击玩家的小队")]
    [SerializeField] private bool includeChaseSquad = true;
    [Tooltip("包含一个 MoveTo 小队：前往第一个伏击点，到达后恢复原版自主行为")]
    [SerializeField] private bool includeMoveToSquad = true;

    private readonly List<Squad> squads = new List<Squad>();
    private int nextSquadIndex;

    public string DisplayName => "DebugOrders";

    public void OnWaveStarted(HordeContext context)
    {
        squads.Clear();
        nextSquadIndex = 0;

        List<Vector2> points = GetAmbushPositions(context);
        int id = 0;

        if (includeChaseSquad)
        {
            squads.Add(CreateSquad(id++, EnemyOrder.Chase()));
        }
        if (includeMoveToSquad && points.Count > 0)
        {
            squads.Add(CreateSquad(id++, EnemyOrder.MoveTo(points[0])));
        }
        foreach (Vector2 point in points)
        {
            squads.Add(CreateSquad(id++, EnemyOrder.HoldAt(point, ambushEngageRadius)));
        }

        Debug.Log($"[DebugOrdersCommander] 第 {context.WaveIndex + 1} 波：{squads.Count} 个小队\n{string.Join("\n", squads)}");
    }

    public Vector3 ChooseSpawnPosition(HordeContext context)
    {
        return context.DefaultSpawnPosition();
    }

    public void OnEnemySpawned(HordeContext context, Enemy enemy)
    {
        if (squads.Count == 0)
            return;

        squads[nextSquadIndex % squads.Count].Add(enemy);
        nextSquadIndex++;
    }

    public void Tick(HordeContext context) { }

    public void OnWaveCompleted(HordeContext context)
    {
        squads.Clear();
    }

    private static Squad CreateSquad(int id, EnemyOrder order)
    {
        var squad = new Squad(id);
        squad.Issue(order);
        return squad;
    }

    private List<Vector2> GetAmbushPositions(HordeContext context)
    {
        var points = new List<Vector2>();
        if (ambushPoints != null)
        {
            foreach (Transform point in ambushPoints)
            {
                if (point != null)
                    points.Add(point.position);
            }
        }

        if (points.Count == 0 && context.Player != null)
        {
            Vector2 center = context.Player.position;
            points.Add(center + Vector2.up * autoPointDistance);
            points.Add(center + Vector2.right * autoPointDistance);
            points.Add(center + Vector2.down * autoPointDistance);
            points.Add(center + Vector2.left * autoPointDistance);
        }
        return points;
    }
}
