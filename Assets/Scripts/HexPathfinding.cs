using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* поиск пути по гекс-сетке (только соседние гексы).
/// </summary>
public static class HexPathfinding
{
    public static List<(int col, int row)> FindPath(
        HexGrid grid,
        int startCol, int startRow,
        int endCol, int endRow)
    {
        if (grid == null || !grid.IsInBounds(startCol, startRow) || !grid.IsInBounds(endCol, endRow))
            return null;

        if (GameSession.Active != null && GameSession.Active.IsObstacleCell(endCol, endRow))
            return null;

        if (startCol == endCol && startRow == endRow)
            return new List<(int, int)> { (startCol, startRow) };

        var open = new List<(int col, int row)>();
        var closed = new HashSet<(int col, int row)>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), int>();
        var fScore = new Dictionary<(int, int), int>();

        var start = (startCol, startRow);
        var end = (endCol, endRow);

        open.Add(start);
        gScore[start] = 0;
        fScore[start] = HexGrid.GetDistance(startCol, startRow, endCol, endRow);

        while (open.Count > 0)
        {
            open.Sort((a, b) => (fScore.ContainsKey(a) ? fScore[a] : int.MaxValue)
                .CompareTo(fScore.ContainsKey(b) ? fScore[b] : int.MaxValue));
            var current = open[0];
            open.RemoveAt(0);

            if (closed.Contains(current))
                continue;
            closed.Add(current);

            if (current == end)
                return ReconstructPath(cameFrom, current);

            int curCol = current.col, curRow = current.row;

            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(curCol, curRow, dir, out int nCol, out int nRow);
                if (!grid.IsInBounds(nCol, nRow)) continue;
                if (GameSession.Active != null && GameSession.Active.IsObstacleCell(nCol, nRow)) continue;

                var neighbor = (nCol, nRow);
                if (closed.Contains(neighbor)) continue;

                int tentativeG = (gScore.ContainsKey(current) ? gScore[current] : int.MaxValue) + 1;
                if (tentativeG >= (gScore.ContainsKey(neighbor) ? gScore[neighbor] : int.MaxValue))
                    continue;

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                fScore[neighbor] = tentativeG + HexGrid.GetDistance(nCol, nRow, endCol, endRow);
                if (!open.Contains(neighbor))
                    open.Add(neighbor);
            }
        }

        return null;
    }

    private static List<(int col, int row)> ReconstructPath(
        Dictionary<(int, int), (int, int)> cameFrom,
        (int col, int row) current)
    {
        var path = new List<(int, int)> { current };
        while (cameFrom.TryGetValue(current, out var prev))
        {
            path.Add(prev);
            current = prev;
        }
        path.Reverse();
        return path;
    }
}
