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

    /// <summary>Соседняя клетка по направлению 0..5 (flat-top odd-r).</summary>
    public static void GetNeighbor(int col, int row, int direction, out int outCol, out int outRow)
    {
        // Используем те же правила odd-r, что и в Unity HexGrid/HexCubeOffset.
        // Для упрощения здесь дублируем базовую логику соседей.
        int parity = col & 1;
        int[,] offsetsEven = { { +1, 0 }, { 0, -1 }, { -1, -1 }, { -1, 0 }, { -1, +1 }, { 0, +1 } };
        int[,] offsetsOdd  = { { +1, 0 }, { +1, -1 }, { 0, -1 }, { -1, 0 }, { 0, +1 }, { +1, +1 } };
        int dc, dr;
        if (parity == 0)
        {
            dc = offsetsEven[direction % 6, 0];
            dr = offsetsEven[direction % 6, 1];
        }
        else
        {
            dc = offsetsOdd[direction % 6, 0];
            dr = offsetsOdd[direction % 6, 1];
        }
        outCol = col + dc;
        outRow = row + dr;
    }
}
