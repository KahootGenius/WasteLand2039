using UnityEngine;

/// <summary>
/// 地图分区：把连续的位置离散化为有限个区域，用于描述玩家的逃跑路线和指挥官的部署目标。
/// 纯逻辑（不依赖场景），可在 Unity 外单元测试。
/// </summary>
public interface IZoneMap
{
    int ZoneCount { get; }

    /// <summary>位置所属的区域编号 [0, ZoneCount)</summary>
    int GetZone(Vector2 position);

    /// <summary>区域的代表点（指挥官向该区域部署时的目标位置）</summary>
    Vector2 GetZoneCenter(int zone);

    /// <summary>简短名称（用于日志和数据文件，ASCII）</summary>
    string GetZoneName(int zone);
}
