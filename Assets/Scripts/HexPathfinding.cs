using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* поиск пути по гекс-сетке (только соседние гексы).
/// Внутренние коллекции переиспользуются (без GC на каждый поиск) — только главный поток, без реентерабельности.
/// </summary>
public static class HexPathfinding
{
    private static bool IsWalkableBattleHex(int col, int row)
    {
        var s = GameSession.Active;
        return s == null
            || s.IsHexInActiveBattleZone(col, row)
            || s.IsEscapeBorderHex(col, row);
    }

    private static bool IsValidPathEndpoint(HexGrid grid, int col, int row)
    {
        if (grid == null)
            return false;
        if (GameSession.Active == null)
            return grid.IsInBounds(col, row);
        return IsWalkableBattleHex(col, row);
    }

    private static readonly List<(int col, int row)> OpenList = new(256);
    private static readonly HashSet<(int col, int row)> ClosedSet = new(256);
    private static readonly HashSet<(int col, int row)> InOpenSet = new(256);
    private static readonly Dictionary<(int, int), (int, int)> CameFrom = new(512);
    private static readonly Dictionary<(int, int), int> GScore = new(512);
    private static readonly Dictionary<(int, int), int> FScore = new(512);

    /// <summary>
    /// Длина кратчайшего пути в шагах (рёбрах). 0 если start == end. Без аллокаций (для подсветки ОД под курсором).
    /// </summary>
    public static bool TryGetShortestPathStepCount(
        HexGrid grid,
        int startCol, int startRow,
        int endCol, int endRow,
        out int stepCount)
    {
        stepCount = 0;
        if (grid == null || !IsValidPathEndpoint(grid, startCol, startRow) || !IsValidPathEndpoint(grid, endCol, endRow))
            return false;

        if (GameSession.Active != null && grid.IsInBounds(endCol, endRow) && GameSession.Active.IsObstacleCell(endCol, endRow))
            return false;

        if (startCol == endCol && startRow == endRow)
            return true;

        if (!RunAStarSearch(grid, startCol, startRow, endCol, endRow, out int gAtEnd))
            return false;

        stepCount = gAtEnd;
        return true;
    }

    /// <summary>
    /// Заполняет <paramref name="pathOut"/> полным путём (без нового List). Вызывающий может держать один буфер на клик/действие.
    /// </summary>
    public static bool TryBuildPath(
        HexGrid grid,
        int startCol, int startRow,
        int endCol, int endRow,
        List<(int col, int row)> pathOut)
    {
        if (pathOut == null)
            return false;

        if (grid == null || !IsValidPathEndpoint(grid, startCol, startRow) || !IsValidPathEndpoint(grid, endCol, endRow))
            return false;

        if (GameSession.Active != null && grid.IsInBounds(endCol, endRow) && GameSession.Active.IsObstacleCell(endCol, endRow))
            return false;

        if (startCol == endCol && startRow == endRow)
        {
            pathOut.Clear();
            pathOut.Add((startCol, startRow));
            return true;
        }

        if (!RunAStarSearch(grid, startCol, startRow, endCol, endRow, out _))
            return false;

        ReconstructPathInto((endCol, endRow), pathOut);
        return true;
    }

    /// <summary>Один аллок List — для вызовов, где нет своего буфера.</summary>
    public static List<(int col, int row)> FindPath(
        HexGrid grid,
        int startCol, int startRow,
        int endCol, int endRow)
    {
        var result = new List<(int, int)>();
        if (!TryBuildPath(grid, startCol, startRow, endCol, endRow, result))
            return null;
        return result;
    }

    /// <summary>Выполняет A*; при успехе gAtEnd — стоимость пути до цели (число шагов для единичных рёбер).</summary>
    private static bool RunAStarSearch(
        HexGrid grid,
        int startCol, int startRow,
        int endCol, int endRow,
        out int gAtEnd)
    {
        gAtEnd = 0;

        ClearSearchState();

        var start = (startCol, startRow);
        var goal = (endCol, endRow);

        OpenList.Add(start);
        InOpenSet.Add(start);
        GScore[start] = 0;
        FScore[start] = HexGrid.GetDistance(startCol, startRow, endCol, endRow);

        int GetF((int col, int row) key) =>
            FScore.TryGetValue(key, out int f) ? f : int.MaxValue;

        while (OpenList.Count > 0)
        {
            // Минимум f без сортировки всего списка каждый раз
            int bestIdx = 0;
            int bestF = GetF(OpenList[0]);
            for (int i = 1; i < OpenList.Count; i++)
            {
                int f = GetF(OpenList[i]);
                if (f < bestF)
                {
                    bestF = f;
                    bestIdx = i;
                }
            }

            var current = OpenList[bestIdx];
            OpenList.RemoveAt(bestIdx);
            InOpenSet.Remove(current);

            if (ClosedSet.Contains(current))
                continue;
            ClosedSet.Add(current);

            if (current == goal)
            {
                gAtEnd = GScore.TryGetValue(current, out int g) ? g : 0;
                return true;
            }

            int curCol = current.col, curRow = current.row;
            int currentG = GScore.TryGetValue(current, out int cg) ? cg : int.MaxValue;

            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(curCol, curRow, dir, out int nCol, out int nRow);
                if (GameSession.Active != null)
                {
                    if (!IsWalkableBattleHex(nCol, nRow))
                        continue;
                    if (grid.IsInBounds(nCol, nRow) && GameSession.Active.IsObstacleCell(nCol, nRow))
                        continue;
                }
                else if (!grid.IsInBounds(nCol, nRow))
                    continue;

                var neighbor = (nCol, nRow);
                if (ClosedSet.Contains(neighbor)) continue;

                int tentativeG = currentG + 1;
                int oldG = GScore.TryGetValue(neighbor, out int og) ? og : int.MaxValue;
                if (tentativeG >= oldG)
                    continue;

                CameFrom[neighbor] = current;
                GScore[neighbor] = tentativeG;
                FScore[neighbor] = tentativeG + HexGrid.GetDistance(nCol, nRow, endCol, endRow);

                if (InOpenSet.Add(neighbor))
                    OpenList.Add(neighbor);
            }
        }

        return false;
    }

    private static void ClearSearchState()
    {
        OpenList.Clear();
        ClosedSet.Clear();
        InOpenSet.Clear();
        CameFrom.Clear();
        GScore.Clear();
        FScore.Clear();
    }

    private static void ReconstructPathInto((int col, int row) end, List<(int col, int row)> pathOut)
    {
        pathOut.Clear();
        var current = end;
        pathOut.Add(current);
        while (CameFrom.TryGetValue(current, out var prev))
        {
            pathOut.Add(prev);
            current = prev;
        }

        int n = pathOut.Count;
        for (int i = 0; i < n / 2; i++)
        {
            int j = n - 1 - i;
            (pathOut[i], pathOut[j]) = (pathOut[j], pathOut[i]);
        }
    }
}
