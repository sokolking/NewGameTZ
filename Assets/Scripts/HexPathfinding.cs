using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hex grid path search. Movement routing avoids escape-border hexes except as start or goal (detour around the ring).
/// With a <see cref="Player"/>, <see cref="TryBuildMinApPath"/> minimizes total AP (preview posture + escape lump).
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

    /// <summary>
    /// A* node / Dijkstra neighbor: may stand on start or goal (including escape); may not cross other escape cells.
    /// </summary>
    private static bool IsTraversalNodeAllowed(
        HexGrid grid,
        int col, int row,
        int startCol, int startRow,
        int goalCol, int goalRow)
    {
        if ((col == startCol && row == startRow) || (col == goalCol && row == goalRow))
            return true;

        GameSession gs = GameSession.Active;
        if (gs == null)
        {
            if (!grid.IsInBounds(col, row))
                return false;
            HexCell cell = grid.GetCell(col, row);
            return cell == null || !cell.IsObstacle;
        }

        if (gs.IsEscapeBorderHex(col, row))
            return false;

        if (!gs.IsHexInActiveBattleZone(col, row))
            return false;

        if (grid.IsInBounds(col, row))
        {
            HexCell cell = grid.GetCell(col, row);
            if (cell != null && cell.IsObstacle)
                return false;
        }

        return true;
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

    private static readonly Dictionary<(int col, int row, int k), int> DijkBestApSpent = new(2048);
    private static readonly Dictionary<(int col, int row, int k), (int pc, int pr, int pk)> DijkCameFrom = new(2048);
    private static readonly List<(int apSpent, int col, int row, int k)> DijkQueue = new(512);

    /// <summary>
    /// Shortest path in edge count (A*). Escape ring is not used as intermediate cells.
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
    /// Full path by fewest hex steps; escape-border hexes only as start or goal.
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

    /// <summary>
    /// Minimum total AP from current planning state; same traversal rules as <see cref="TryBuildPath"/>.
    /// </summary>
    public static bool TryBuildMinApPath(
        Player player,
        HexGrid grid,
        int startCol, int startRow,
        int endCol, int endRow,
        List<(int col, int row)> pathOut)
    {
        if (player == null || pathOut == null || grid == null)
            return false;
        if (!IsValidPathEndpoint(grid, startCol, startRow) || !IsValidPathEndpoint(grid, endCol, endRow))
            return false;
        if (GameSession.Active != null && grid.IsInBounds(endCol, endRow) && GameSession.Active.IsObstacleCell(endCol, endRow))
            return false;

        if (startCol == endCol && startRow == endRow)
        {
            pathOut.Clear();
            pathOut.Add((startCol, startRow));
            return true;
        }

        int startAp = player.CurrentAp;
        if (startAp < 1)
            return false;

        int maxK = Mathf.Max(8, grid.Width * grid.Length + 4);

        DijkBestApSpent.Clear();
        DijkCameFrom.Clear();
        DijkQueue.Clear();

        var startKey = (startCol, startRow, 0);
        DijkBestApSpent[startKey] = 0;
        DijkQueue.Add((0, startCol, startRow, 0));

        while (DijkQueue.Count > 0)
        {
            int bestQi = 0;
            int bestAp = DijkQueue[0].apSpent;
            for (int i = 1; i < DijkQueue.Count; i++)
            {
                if (DijkQueue[i].apSpent < bestAp)
                {
                    bestAp = DijkQueue[i].apSpent;
                    bestQi = i;
                }
            }

            (int apSpent, int curCol, int curRow, int k) = DijkQueue[bestQi];
            DijkQueue.RemoveAt(bestQi);

            var curState = (curCol, curRow, k);
            if (!DijkBestApSpent.TryGetValue(curState, out int recorded) || apSpent > recorded)
                continue;

            if (curCol == endCol && curRow == endRow)
            {
                ReconstructDijkPathInto(startCol, startRow, curState, pathOut);
                return pathOut.Count > 0
                    && pathOut[0].col == startCol
                    && pathOut[0].row == startRow
                    && pathOut[pathOut.Count - 1].col == endCol
                    && pathOut[pathOut.Count - 1].row == endRow;
            }

            if (k >= maxK)
                continue;

            int apRem = startAp - apSpent;
            if (apRem < 1)
                continue;

            int stepIdx = player.StepsTakenThisTurn + k;

            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(curCol, curRow, dir, out int nCol, out int nRow);
                if (!IsTraversalNodeAllowed(grid, nCol, nRow, startCol, startRow, endCol, endRow))
                    continue;

                int edgeCost = player.ComputePlanningEdgeApCost(curCol, curRow, nCol, nRow, stepIdx, apRem);
                if (edgeCost == int.MaxValue || edgeCost > apRem)
                    continue;

                int newApSpent = apSpent + edgeCost;
                int nk = k + 1;
                var nextState = (nCol, nRow, nk);
                if (DijkBestApSpent.TryGetValue(nextState, out int oldAp) && newApSpent >= oldAp)
                    continue;

                DijkBestApSpent[nextState] = newApSpent;
                DijkCameFrom[nextState] = (curCol, curRow, k);
                DijkQueue.Add((newApSpent, nCol, nRow, nk));
            }
        }

        return false;
    }

    private static void ReconstructDijkPathInto(
        int startCol, int startRow,
        (int col, int row, int k) endState,
        List<(int col, int row)> pathOut)
    {
        pathOut.Clear();
        var cur = endState;
        pathOut.Add((cur.col, cur.row));
        while (!(cur.col == startCol && cur.row == startRow && cur.k == 0))
        {
            if (!DijkCameFrom.TryGetValue(cur, out var prev))
                break;
            cur = (prev.pc, prev.pr, prev.pk);
            pathOut.Add((cur.col, cur.row));
        }

        int n = pathOut.Count;
        for (int i = 0; i < n / 2; i++)
        {
            int j = n - 1 - i;
            (pathOut[i], pathOut[j]) = (pathOut[j], pathOut[i]);
        }
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
                if (!IsTraversalNodeAllowed(grid, nCol, nRow, startCol, startRow, endCol, endRow))
                    continue;

                var neighbor = (nCol, nRow);
                if (ClosedSet.Contains(neighbor))
                    continue;

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
