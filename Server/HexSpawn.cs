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
    /// PvP: две команды на противоположных горизонтальных сторонах прямоугольника (<paramref name="rowMin"/> и <paramref name="rowMax"/>),
    /// по <paramref name="playersPerTeam"/> игроков в линию по колонкам с шагом 2 от центра ширины (1 → центр; 3 → -2,0,+2; 5 → -4..+4).
    /// Порядок в списке: вся команда A (ближний к <paramref name="rowMin"/> ряд), затем вся команда B (ближний к <paramref name="rowMax"/>).
    /// </summary>
    public static List<(int col, int row)> FindTwoTeamSpawnsOnOppositeHorizontalSides(
        int playersPerTeam,
        int gridWidth,
        int gridLength,
        int colMin,
        int colMax,
        int rowMin,
        int rowMax)
    {
        colMin = Math.Clamp(colMin, 0, gridWidth - 1);
        colMax = Math.Clamp(colMax, 0, gridWidth - 1);
        rowMin = Math.Clamp(rowMin, 0, gridLength - 1);
        rowMax = Math.Clamp(rowMax, 0, gridLength - 1);
        if (colMin > colMax)
            (colMin, colMax) = (colMax, colMin);
        if (rowMin > rowMax)
            (rowMin, rowMax) = (rowMax, rowMin);

        int teamRowA = rowMin;
        int teamRowB = rowMax;
        int centerCol = (colMin + colMax) / 2;
        int[] offsets = GetSpawnLineOffsetsForTeamSize(playersPerTeam);
        List<int> cols = BuildLinePositionsFromOffsets(centerCol, offsets, colMin, colMax);

        var list = new List<(int col, int row)>(playersPerTeam * 2);
        foreach (int c in cols)
            list.Add((c, teamRowA));
        foreach (int c in cols)
            list.Add((c, teamRowB));
        return list;
    }

    /// <inheritdoc cref="FindTwoTeamSpawnsOnOppositeHorizontalSides(int,int,int,int,int,int,int)"/>
    public static List<(int col, int row)> FindTwoTeamSpawnsOnOppositeHorizontalSides(int playersPerTeam, int gridWidth, int gridLength) =>
        FindTwoTeamSpawnsOnOppositeHorizontalSides(
            playersPerTeam,
            gridWidth,
            gridLength,
            0,
            gridWidth - 1,
            0,
            gridLength - 1);

    /// <summary>
    /// Смещения вдоль линии (колонки или ряды) от центра с шагом 2: n=1 → [0], n=3 → [-2,0,2], n=5 → [-4,-2,0,2,4].
    /// </summary>
    private static int[] GetSpawnLineOffsetsForTeamSize(int n)
    {
        if (n <= 0)
            return Array.Empty<int>();
        var arr = new int[n];
        for (int i = 0; i < n; i++)
            arr[i] = (i * 2) - (n - 1);
        return arr;
    }

    private static List<int> BuildLinePositionsFromOffsets(int center, int[] offsets, int axisMin, int axisMax)
    {
        if (offsets.Length == 0)
            return new List<int>();

        int minB = int.MaxValue;
        int maxB = int.MinValue;
        for (int i = 0; i < offsets.Length; i++)
        {
            int v = center + offsets[i];
            if (v < minB) minB = v;
            if (v > maxB) maxB = v;
        }

        int shiftLo = axisMin - minB;
        int shiftHi = axisMax - maxB;
        int shift;
        if (shiftLo > shiftHi)
            shift = shiftLo;
        else if (0 < shiftLo)
            shift = shiftLo;
        else if (0 > shiftHi)
            shift = shiftHi;
        else
            shift = 0;

        var positions = new List<int>(offsets.Length);
        for (int i = 0; i < offsets.Length; i++)
            positions.Add(Math.Clamp(center + offsets[i] + shift, axisMin, axisMax));
        return positions;
    }

    /// <summary>
    /// Спавны для двух команд: сначала <paramref name="playersPerTeam"/> слотов команды A (левая треть карты),
    /// затем столько же — команда B (правая треть). Внутри команды — максимизация минимальной дистанции между игроками.
    /// </summary>
    public static List<(int col, int row)> FindTwoTeamSpawns(int playersPerTeam, int width, int length, int minWithinTeam)
    {
        int leftMax = Math.Max(0, width / 3 - 1);
        int rightMin = Math.Min(width - 1, 2 * width / 3);
        var teamA = GreedySpawnsInRect(playersPerTeam, 0, leftMax, 0, length - 1, minWithinTeam, width, length);
        var teamB = GreedySpawnsInRect(playersPerTeam, rightMin, width - 1, 0, length - 1, minWithinTeam, width, length);
        var all = new List<(int col, int row)>(teamA.Count + teamB.Count);
        all.AddRange(teamA);
        all.AddRange(teamB);
        return all;
    }

    private static List<(int col, int row)> GreedySpawnsInRect(int count, int colMin, int colMax, int rowMin, int rowMax, int minWithinTeam, int width, int length)
    {
        var placed = new List<(int col, int row)>();
        if (count <= 0)
            return placed;
        colMin = Math.Clamp(colMin, 0, width - 1);
        colMax = Math.Clamp(colMax, 0, width - 1);
        rowMin = Math.Clamp(rowMin, 0, length - 1);
        rowMax = Math.Clamp(rowMax, 0, length - 1);
        if (colMin > colMax || rowMin > rowMax)
            return placed;

        for (int k = 0; k < count; k++)
        {
            int bestC = -1, bestR = -1, bestScore = int.MinValue;
            for (int c = colMin; c <= colMax; c++)
            {
                for (int r = rowMin; r <= rowMax; r++)
                {
                    int minD = int.MaxValue;
                    foreach (var (pc, pr) in placed)
                    {
                        int d = HexDistance(c, r, pc, pr);
                        if (d < minD)
                            minD = d;
                    }

                    if (placed.Count > 0 && minD < minWithinTeam)
                        continue;
                    int score = placed.Count == 0 ? c + r : minD;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestC = c;
                        bestR = r;
                    }
                }
            }

            if (bestC < 0)
            {
                for (int c = colMin; c <= colMax; c++)
                {
                    for (int r = rowMin; r <= rowMax; r++)
                    {
                        int minD = placed.Count == 0 ? 999 : placed.Min(p => HexDistance(c, r, p.col, p.row));
                        if (minD > bestScore)
                        {
                            bestScore = minD;
                            bestC = c;
                            bestR = r;
                        }
                    }
                }
            }

            if (bestC >= 0)
                placed.Add((bestC, bestR));
        }

        return placed;
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

    private const double Sqrt3 = 1.7320508075688772;

    /// <summary>Центр гекса в плоскости XZ (как <see cref="HexCubeOffset.CubeToWorldFlatTop"/> в Unity).</summary>
    public static void OffsetToWorldFlatTop(int col, int row, float hexSize, out double worldX, out double worldZ)
    {
        OffsetToCube(col, row, out int x, out _, out int z);
        double q = x;
        double r = z;
        double s = hexSize;
        worldX = s * Sqrt3 * (q + r * 0.5);
        worldZ = s * 1.5 * r;
    }

    /// <summary>
    /// Горизонтальный yaw (градусы, вокруг Y) для стены между соседними гексами:
    /// ось стены идёт вдоль общего ребра (перпендикуляр к вектору центр→центр).
    /// </summary>
    public static float ComputeYawAlongEdgeDegrees(int col0, int row0, int col1, int row1, float hexSize)
    {
        OffsetToWorldFlatTop(col0, row0, hexSize, out double ax, out double az);
        OffsetToWorldFlatTop(col1, row1, hexSize, out double bx, out double bz);
        double dx = bx - ax;
        double dz = bz - az;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dz) < 1e-9)
            return 0f;
        return (float)(Math.Atan2(-dz, dx) * (180.0 / Math.PI));
    }

    private static void CubeRoundFromFloat(double fx, double fy, double fz, out int x, out int y, out int z)
    {
        int qi = (int)Math.Round(fx, MidpointRounding.AwayFromZero);
        int ri = (int)Math.Round(fy, MidpointRounding.AwayFromZero);
        int si = (int)Math.Round(fz, MidpointRounding.AwayFromZero);
        double qDiff = Math.Abs(qi - fx);
        double rDiff = Math.Abs(ri - fy);
        double sDiff = Math.Abs(si - fz);
        if (qDiff > rDiff && qDiff > sDiff)
            qi = -ri - si;
        else if (rDiff > sDiff)
            ri = -qi - si;
        else
            si = -qi - ri;
        x = qi;
        y = ri;
        z = si;
    }

    /// <summary>Прямая линия гексов от (col0,row0) до (col1,row1), включая оба конца (как в Unity <see cref="HexCubeOffset.GetHexLine"/>).</summary>
    public static void GetHexLineInclusive(int col0, int row0, int col1, int row1, List<(int col, int row)> outList)
    {
        if (outList == null)
            return;
        outList.Clear();
        OffsetToCube(col0, row0, out int ax, out int ay, out int az);
        OffsetToCube(col1, row1, out int bx, out int by, out int bz);
        int n = HexDistance(col0, row0, col1, row1);
        if (n == 0)
        {
            outList.Add((col0, row0));
            return;
        }

        int lastQx = int.MinValue, lastQy = int.MinValue, lastQz = int.MinValue;
        bool hasLast = false;
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n;
            double fx = ax + (bx - ax) * t;
            double fy = ay + (by - ay) * t;
            double fz = az + (bz - az) * t;
            CubeRoundFromFloat(fx, fy, fz, out int qx, out int qy, out int qz);
            if (hasLast && qx == lastQx && qy == lastQy && qz == lastQz)
                continue;
            CubeToOffset(qx, qz, out int c, out int r);
            outList.Add((c, r));
            lastQx = qx;
            lastQy = qy;
            lastQz = qz;
            hasLast = true;
        }
    }

    /// <summary>Клетки строго между атакующим и целью (без их гексов), в порядке от атакующего к цели.</summary>
    public static void GetHexLineBetweenExclusive(int attackerCol, int attackerRow, int targetCol, int targetRow, List<(int col, int row)> buffer)
    {
        if (buffer == null)
            return;
        buffer.Clear();
        GetHexLineInclusive(attackerCol, attackerRow, targetCol, targetRow, buffer);
        if (buffer.Count <= 2)
        {
            buffer.Clear();
            return;
        }

        buffer.RemoveAt(buffer.Count - 1);
        buffer.RemoveAt(0);
    }
}
