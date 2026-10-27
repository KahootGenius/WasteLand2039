using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 以中心点（通常是主基地）为圆心的径向分区，适合 MainGame 这种没有走廊的开阔地图：
/// - 区域 0 = 核心区（距中心 &lt; coreRadius）
/// - 其余区域 = 环带 × 扇区。扇区 0 以正东为中心，逆时针：E, NE, N, NW, W, SW, S, SE
/// - 环带按 bandEdges 划分，最后一个环带延伸到无穷远；名称中的数字为环带序号（"NE1" = 东北近环）
/// 编号：zone = 1 + band * sectors + sector
/// </summary>
public class RadialZoneMap : IZoneMap
{
    private static readonly string[] CompassNames = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

    public Vector2 Center { get; }
    public int Sectors { get; }
    public float CoreRadius { get; }
    public IReadOnlyList<float> BandEdges => bandEdges;
    public int BandCount => bandEdges.Length + 1;
    public int ZoneCount => 1 + Sectors * BandCount;

    private readonly float[] bandEdges;

    /// <param name="bandEdges">环带之间的边界半径（升序，均应大于 coreRadius）</param>
    public RadialZoneMap(Vector2 center, int sectors = 8, float coreRadius = 6f, params float[] bandEdges)
    {
        Center = center;
        Sectors = Mathf.Max(1, sectors);
        CoreRadius = Mathf.Max(0f, coreRadius);

        var edges = new List<float>();
        float previous = CoreRadius;
        foreach (float edge in bandEdges ?? new float[0])
        {
            if (edge > previous)
            {
                edges.Add(edge);
                previous = edge;
            }
        }
        this.bandEdges = edges.ToArray();
    }

    public int GetZone(Vector2 position)
    {
        Vector2 offset = position - Center;
        float radius = offset.magnitude;
        if (radius < CoreRadius)
            return 0;

        int band = 0;
        while (band < bandEdges.Length && radius >= bandEdges[band])
            band++;

        return 1 + band * Sectors + GetSector(offset);
    }

    /// <summary>方向所属扇区（与区域编号使用相同约定：0 = 正东，逆时针）</summary>
    public int GetSector(Vector2 direction)
    {
        return DirectionToSector(direction, Sectors);
    }

    public static int DirectionToSector(Vector2 direction, int sectors)
    {
        float angle = Mathf.Atan2(direction.y, direction.x);
        if (angle < 0f)
            angle += 2f * Mathf.PI;

        float sectorWidth = 2f * Mathf.PI / sectors;
        return (int)Mathf.Floor((angle + sectorWidth * 0.5f) / sectorWidth) % sectors;
    }

    public Vector2 GetZoneCenter(int zone)
    {
        if (zone <= 0 || zone >= ZoneCount)
            return Center;

        int band = (zone - 1) / Sectors;
        int sector = (zone - 1) % Sectors;

        float inner = band == 0 ? CoreRadius : bandEdges[band - 1];
        float outer = band < bandEdges.Length ? bandEdges[band] : inner * 1.5f + 1f;
        float radius = (inner + outer) * 0.5f;

        float angle = sector * 2f * Mathf.PI / Sectors;
        return Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    public string GetZoneName(int zone)
    {
        if (zone <= 0)
            return "Core";
        if (zone >= ZoneCount)
            return "?";

        int band = (zone - 1) / Sectors;
        int sector = (zone - 1) % Sectors;
        string sectorName = Sectors == 8 ? CompassNames[sector] : $"S{sector}";
        return $"{sectorName}{band + 1}";
    }
}
