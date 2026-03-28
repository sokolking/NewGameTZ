using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private void GenerateObstaclesIfNeeded()
    {
        if (_obstacleTags.Count > 0)
            return;

        var bal = _obstacleDb?.GetBalance() ?? BattleObstacleBalanceRowDto.Defaults;
        int wallSegments = Math.Max(1, bal.WallSegmentsCount);
        int rockCount = Math.Max(0, bal.RockCount);
        int treeCount = Math.Max(0, bal.TreeCount);
        int wallHp = Math.Max(1, bal.WallMaxHp);

        var reserved = new HashSet<(int col, int row)>();
        foreach (var unit in Units.Values)
        {
            var origin = (col: unit.Col, row: unit.Row);
            reserved.Add(origin);
            for (int col = 0; col < HexSpawn.DefaultGridWidth; col++)
            {
                for (int row = 0; row < HexSpawn.DefaultGridLength; row++)
                {
                    if (HexSpawn.HexDistance(origin.col, origin.row, col, row) <= 2)
                        reserved.Add((col, row));
                }
            }
        }

        var used = new HashSet<(int col, int row)>(reserved);
        const float hexSize = 1f;

        bool TryPlace(int col, int row) =>
            col >= 0 && row >= 0 && col < HexSpawn.DefaultGridWidth && row < HexSpawn.DefaultGridLength
            && !used.Contains((col, row));

        _wallYawDegrees.Clear();
        int attempts = 0;
        int placedWalls = 0;
        while (placedWalls < wallSegments && attempts < 800)
        {
            attempts++;
            int length = _rng.Next(0, 2) == 0 ? 2 : 3;
            int dir = _rng.Next(0, 6);
            int sc = _rng.Next(0, HexSpawn.DefaultGridWidth);
            int sr = _rng.Next(0, HexSpawn.DefaultGridLength);

            var chain = new List<(int col, int row)>();
            int c = sc;
            int r = sr;
            bool ok = true;
            for (int i = 0; i < length; i++)
            {
                if (!TryPlace(c, r))
                {
                    ok = false;
                    break;
                }

                chain.Add((c, r));
                HexSpawn.GetNeighbor(c, r, dir, out c, out r);
            }

            if (!ok || chain.Count != length)
                continue;

            foreach (var cell in chain)
            {
                used.Add(cell);
                _obstacleTags[cell] = "wall";
                _wallHpRemaining[cell] = wallHp;
            }

            for (int i = 0; i < chain.Count; i++)
            {
                int col = chain[i].col;
                int row = chain[i].row;
                if (i < chain.Count - 1)
                {
                    int nc = chain[i + 1].col;
                    int nr = chain[i + 1].row;
                    _wallYawDegrees[(col, row)] = HexSpawn.ComputeYawAlongEdgeDegrees(col, row, nc, nr, hexSize);
                }
                else
                {
                    int pc = chain[i - 1].col;
                    int pr = chain[i - 1].row;
                    _wallYawDegrees[(col, row)] = HexSpawn.ComputeYawAlongEdgeDegrees(pc, pr, col, row, hexSize);
                }
            }

            placedWalls++;
        }

        attempts = 0;
        int placedRocks = 0;
        while (placedRocks < rockCount && attempts < 600)
        {
            attempts++;
            int c = _rng.Next(0, HexSpawn.DefaultGridWidth);
            int r = _rng.Next(0, HexSpawn.DefaultGridLength);
            if (!TryPlace(c, r))
                continue;
            used.Add((c, r));
            _obstacleTags[(c, r)] = "rock";
            placedRocks++;
        }

        attempts = 0;
        int placedTrees = 0;
        while (placedTrees < treeCount && attempts < 600)
        {
            attempts++;
            int c = _rng.Next(0, HexSpawn.DefaultGridWidth);
            int r = _rng.Next(0, HexSpawn.DefaultGridLength);
            if (!TryPlace(c, r))
                continue;
            used.Add((c, r));
            _obstacleTags[(c, r)] = "tree";
            placedTrees++;
        }
    }
}
