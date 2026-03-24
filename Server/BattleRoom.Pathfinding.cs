using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private static bool AreAdjacent((int col, int row) a, (int col, int row) b) =>
        HexSpawn.HexDistance(a.col, a.row, b.col, b.row) == 1;

    private static bool AreEnemies(UnitStateDto a, UnitStateDto b)
    {
        if (a == null || b == null || a.UnitId == b.UnitId) return false;
        if (a.UnitType == UnitType.Mob && b.UnitType == UnitType.Mob) return false;
        return true;
    }

    private string? ResolveUnitId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return null;
        if (Units.ContainsKey(rawId))
            return rawId;
        if (PlayerToUnitId.TryGetValue(rawId, out var mapped) && Units.ContainsKey(mapped))
            return mapped;
        return null;
    }

    private static IEnumerable<(int col, int row)> EnumerateNeighbors(int col, int row)
    {
        for (int dir = 0; dir < 6; dir++)
        {
            HexSpawn.GetNeighbor(col, row, dir, out int nc, out int nr);
            if (nc < 0 || nr < 0 || nc >= HexSpawn.DefaultGridWidth || nr >= HexSpawn.DefaultGridLength)
                continue;
            yield return (nc, nr);
        }
    }

    private List<(int col, int row)>? FindShortestPathAvoidingBlocked((int col, int row) start, (int col, int row) end, HashSet<(int col, int row)> blocked)
    {
        var queue = new Queue<(int col, int row)>();
        var visited = new HashSet<(int col, int row)> { start };
        var prev = new Dictionary<(int col, int row), (int col, int row)>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (cell == end)
            {
                var reverse = new List<(int col, int row)> { end };
                var cursor = end;
                while (cursor != start)
                {
                    if (!prev.TryGetValue(cursor, out cursor))
                        break;
                    reverse.Add(cursor);
                }
                reverse.Reverse();
                return reverse;
            }

            foreach (var neighbor in EnumerateNeighbors(cell.col, cell.row))
            {
                if (blocked.Contains(neighbor) && neighbor != end)
                    continue;
                if (!visited.Add(neighbor))
                    continue;
                prev[neighbor] = cell;
                queue.Enqueue(neighbor);
            }
        }

        return null;
    }
}
