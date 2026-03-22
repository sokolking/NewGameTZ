using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Кубические координаты гекса (cube coordinates): q + r + s = 0.
/// Соответствует axial (q, r) с s = -q-r.
/// См. https://www.redblobgames.com/grids/hexagons/
/// Расстояние: H = Max(|Δq|, |Δr|, |Δs|).
/// </summary>
[System.Serializable]
public struct HexCube
{
    public int x; // q
    public int y; // r (или -q-s)
    public int z; // s

    public HexCube(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        if (x + y + z != 0)
            this.y = -x - z;
    }

    public static HexCube Create(int q, int s)
    {
        return new HexCube(q, -q - s, s);
    }

    public bool IsValid => x + y + z == 0;

    public static int Distance(HexCube a, HexCube b)
    {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        int dz = a.z - b.z;
        int ax = dx < 0 ? -dx : dx;
        int ay = dy < 0 ? -dy : dy;
        int az = dz < 0 ? -dz : dz;
        int m = ax >= ay ? ax : ay;
        return az >= m ? az : m;
    }

    public int DistanceTo(HexCube other) => Distance(this, other);

    public static HexCube operator +(HexCube a, HexCube b) =>
        new HexCube(a.x + b.x, a.y + b.y, a.z + b.z);

    public static HexCube operator -(HexCube a, HexCube b) =>
        new HexCube(a.x - b.x, a.y - b.y, a.z - b.z);

    public static bool operator ==(HexCube a, HexCube b) =>
        a.x == b.x && a.y == b.y && a.z == b.z;

    public static bool operator !=(HexCube a, HexCube b) => !(a == b);

    public override bool Equals(object obj) =>
        obj is HexCube other && this == other;

    public override int GetHashCode() => (x, y, z).GetHashCode();

    public override string ToString() => $"({x}, {y}, {z})";

    /// <summary>Направления к соседям: 0=Right, 1=BottomRight, 2=BottomLeft, 3=Left, 4=UpperLeft, 5=UpperRight.</summary>
    public static readonly HexCube[] Directions =
    {
        new HexCube(1, 0, -1),
        new HexCube(1, -1, 0),
        new HexCube(0, -1, 1),
        new HexCube(-1, 0, 1),
        new HexCube(-1, 1, 0),
        new HexCube(0, 1, -1),
    };

    public const int Right = 0, BottomRight = 1, BottomLeft = 2, Left = 3, UpperLeft = 4, UpperRight = 5;

    public HexCube Neighbor(int direction)
    {
        int d = ((direction % 6) + 6) % 6;
        return this + Directions[d];
    }

    public void GetNeighbors(HexCube[] buffer)
    {
        if (buffer == null || buffer.Length < 6) return;
        for (int i = 0; i < 6; i++)
            buffer[i] = Neighbor(i);
    }

    /// <summary>Округление дробного куба до ближайшего целого гекса (redblobgames.com/grids/hexagons).</summary>
    public static HexCube RoundFromFloat(float q, float r, float s)
    {
        int qi = Mathf.RoundToInt(q);
        int ri = Mathf.RoundToInt(r);
        int si = Mathf.RoundToInt(s);
        float qDiff = Mathf.Abs(qi - q);
        float rDiff = Mathf.Abs(ri - r);
        float sDiff = Mathf.Abs(si - s);
        if (qDiff > rDiff && qDiff > sDiff)
            qi = -ri - si;
        else if (rDiff > sDiff)
            ri = -qi - si;
        else
            si = -qi - ri;
        return new HexCube(qi, ri, si);
    }

}

/// <summary>
/// Преобразование куб ↔ offset (col, row) и куб ↔ мир. Flat-top, odd-r.
/// https://www.redblobgames.com/grids/hexagons/
/// </summary>
public static class HexCubeOffset
{
    private const float Sqrt3 = 1.732050808f;

    public static HexCube FromOffset(int col, int row)
    {
        int q = col;
        int r = row - (col - (col & 1)) / 2;
        return HexCube.Create(q, r);
    }

    public static void ToOffset(HexCube cube, out int col, out int row)
    {
        col = cube.x;
        row = cube.z + (cube.x - (cube.x & 1)) / 2;
    }

    public static bool IsInBounds(int col, int row, int width, int length)
    {
        return col >= 0 && col < width && row >= 0 && row < length;
    }

    /// <summary>Расстояние в шагах (cube / axial) между двумя offset-клетками без лишних структур <see cref="HexCube"/>.</summary>
    public static int DistanceBetweenOffsets(int col1, int row1, int col2, int row2)
    {
        int q1 = col1, s1 = row1 - (col1 - (col1 & 1)) / 2;
        int q2 = col2, s2 = row2 - (col2 - (col2 & 1)) / 2;
        int dx = q1 - q2;
        int dz = s1 - s2;
        int dy = -dx - dz;
        int ax = dx < 0 ? -dx : dx;
        int ay = dy < 0 ? -dy : dy;
        int az = dz < 0 ? -dz : dz;
        int m = ax >= ay ? ax : ay;
        return az >= m ? az : m;
    }

    /// <summary>Центр гекса в мире (flat-top): x = size*sqrt(3)*(q+r/2), z = size*(3/2)*r.</summary>
    public static void CubeToWorldFlatTop(HexCube cube, float size, out float worldX, out float worldZ)
    {
        int q = cube.x;
        int r = cube.z;
        worldX = size * Sqrt3 * (q + r * 0.5f);
        worldZ = size * 1.5f * r;
    }

    /// <summary>Прямая линия гексов от (col0,row0) до (col1,row1), включая концы. Дубликаты подряд убираются.</summary>
    public static void GetHexLine(int col0, int row0, int col1, int row1, List<(int col, int row)> outList)
    {
        if (outList == null)
            return;
        outList.Clear();
        HexCube a = FromOffset(col0, row0);
        HexCube b = FromOffset(col1, row1);
        int n = DistanceBetweenOffsets(col0, row0, col1, row1);
        if (n == 0)
        {
            outList.Add((col0, row0));
            return;
        }

        HexCube lastRounded = default;
        bool hasLast = false;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float fx = Mathf.Lerp(a.x, b.x, t);
            float fy = Mathf.Lerp(a.y, b.y, t);
            float fz = Mathf.Lerp(a.z, b.z, t);
            HexCube rounded = HexCube.RoundFromFloat(fx, fy, fz);
            if (hasLast && rounded == lastRounded)
                continue;
            ToOffset(rounded, out int c, out int r);
            outList.Add((c, r));
            lastRounded = rounded;
            hasLast = true;
        }
    }
}
