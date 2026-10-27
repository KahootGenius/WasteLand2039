using UnityEngine;

/// <summary>
/// 指挥官下达给敌人的命令类型
/// </summary>
public enum EnemyOrderType
{
    /// <summary>无命令：保持原有自主行为（发现玩家则追击，否则进攻基地）</summary>
    None,
    /// <summary>前往指定位置；到达后命令完成，恢复自主行为（途中发现玩家照常追击）</summary>
    MoveTo,
    /// <summary>前往指定位置并驻守（伏击）：不进攻基地，玩家进入交战半径时追击，玩家脱离后返回驻守点</summary>
    HoldAt,
    /// <summary>无视检测半径，持续追击玩家</summary>
    Chase
}

/// <summary>
/// 敌人命令（值类型，可直接复制给小队所有成员）
/// </summary>
[System.Serializable]
public struct EnemyOrder
{
    public EnemyOrderType Type;
    public Vector2 Position;     // MoveTo / HoldAt 的目标位置
    public float EngageRadius;   // HoldAt 的交战半径；<= 0 时使用敌人自身的检测半径

    public bool HasPosition => Type == EnemyOrderType.MoveTo || Type == EnemyOrderType.HoldAt;

    public static EnemyOrder None => default;

    public static EnemyOrder MoveTo(Vector2 position)
    {
        return new EnemyOrder { Type = EnemyOrderType.MoveTo, Position = position };
    }

    public static EnemyOrder HoldAt(Vector2 position, float engageRadius = 0f)
    {
        return new EnemyOrder { Type = EnemyOrderType.HoldAt, Position = position, EngageRadius = engageRadius };
    }

    public static EnemyOrder Chase()
    {
        return new EnemyOrder { Type = EnemyOrderType.Chase };
    }

    public override string ToString()
    {
        return HasPosition ? $"{Type}({Position.x:F1}, {Position.y:F1})" : Type.ToString();
    }
}
