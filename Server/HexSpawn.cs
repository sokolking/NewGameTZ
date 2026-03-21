namespace BattleServer;

/// <summary>
/// Стартовые позиции на гекс-сетке (flat-top odd-r, как в Unity HexCubeOffset).
/// Минимальное расстояние между игроками — в шагах по гексам (cube distance = max(|Δq|,|Δr|,|Δs|)).
/// </summary>
public static class HexSpawn
{
    public const int DefaultGridWidth = 25;
    public const int DefaultGridLength = 40;
    public const int MinSpawnHexDistance = 10;

    public static int HexDistance(int col0, int row0, int col1, int row1)
    {
        OffsetToCube(col0, row0, out int x0, out int y0, out int z0);
        OffsetToCube(col1, row1, out int x1, out int y1, out int z1);
        return Math.Max(Math.Abs(x0 - x1), Math.Max(Math.Abs(y0 - y1), Math.Abs(z0 - z1)));
    }

    private static void OffsetToCube(int col, int row, out int x, out int y, out int z)
    {
        int q = col;
        int s = row - (col - (col & 1)) / 2;
        x = q;
        z = s;
        y = -x - z;
    }

    private static void CubeToOffset(int x, int z, out int col, out int row)
    {
        col = x;
        row = z + (x - (x & 1)) / 2;
    }

    /// <summary>Клетка на поле, максимально далеко от (p1Col, p1Row), но не ближе minDist шагов.</summary>
    public static (int col, int row) FindOpponentSpawn(int p1Col, int p1Row, int width, int length, int minDist)
    {
        int bestCol = -1, bestRow = -1, bestD = int.MinValue;
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < length; r++)
            {
                int d = HexDistance(p1Col, p1Row, c, r);
                if (d >= minDist && d > bestD)
                {
                    bestD = d;
                    bestCol = c;
                    bestRow = r;
                }
            }
        }

        if (bestCol < 0)
        {
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < length; r++)
                {
                    int d = HexDistance(p1Col, p1Row, c, r);
                    if (d > bestD)
                    {
                        bestD = d;
                        bestCol = c;
                        bestRow = r;
                    }
                }
            }
        }

        return (bestCol, bestRow);
    }

    /// <summary>
    /// Клетка ровно на <paramref name="dist"/> шагах от старта (идём по прямой в одном из 6 направлений).
    /// Для отладки спавна моба на фиксированной дистанции от игрока.
    /// </summary>
    public static bool TryFindHexAtExactDistance(int p1Col, int p1Row, int width, int length, int dist, out int outCol, out int outRow)
    {
        outCol = outRow = -1;
        if (dist <= 0)
        {
            outCol = p1Col;
            outRow = p1Row;
            return true;
        }

        for (int dir = 0; dir < 6; dir++)
        {
            int c = p1Col;
            int r = p1Row;
            bool ok = true;
            for (int step = 0; step < dist; step++)
            {
                GetNeighbor(c, r, dir, out c, out r);
                if (c < 0 || r < 0 || c >= width || r >= length)
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
                continue;
            if (HexDistance(p1Col, p1Row, c, r) != dist)
                continue;
            outCol = c;
            outRow = r;
            return true;
        }

        return false;
    }

    /// <summary>Соседняя клетка по направлению 0..5 (flat-top odd-r).</summary>
    public static void GetNeighbor(int col, int row, int direction, out int outCol, out int outRow)
    {
        OffsetToCube(col, row, out int x, out _, out int z);
        int d = ((direction % 6) + 6) % 6;
        ReadOnlySpan<(int dx, int dz)> dirs =
        [
            (1, -1),  // Right
            (1, 0),   // BottomRight
            (0, 1),   // BottomLeft
            (-1, 1),  // Left
            (-1, 0),  // UpperLeft
            (0, -1),  // UpperRight
        ];
        var (dx, dz) = dirs[d];
        CubeToOffset(x + dx, z + dz, out outCol, out outRow);
    }
}
