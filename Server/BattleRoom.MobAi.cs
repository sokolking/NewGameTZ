using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private QueuedBattleActionDto[] BuildMobActionQueue(UnitStateDto mob, UnitStateDto target)
    {
        if (mob == null || target == null || mob.CurrentAp <= 0)
            return Array.Empty<QueuedBattleActionDto>();

        var actions = new List<QueuedBattleActionDto>();
        var blocked = new HashSet<(int col, int row)>(_obstacleTags.Keys);
        foreach (var unit in Units.Values)
        {
            if (unit == null || unit.UnitId == mob.UnitId || unit.UnitId == target.UnitId || unit.CurrentHp <= 0)
                continue;
            blocked.Add((unit.Col, unit.Row));
        }

        (int col, int row) start = (mob.Col, mob.Row);
        (int col, int row) targetPos = (target.Col, target.Row);
        var attackCells = EnumerateNeighbors(targetPos.col, targetPos.row)
            .Where(cell => !_obstacleTags.ContainsKey(cell) && !blocked.Contains(cell))
            .ToList();

        List<(int col, int row)>? bestRoute = null;
        int bestLen = int.MaxValue;
        foreach (var attackCell in attackCells)
        {
            var route = FindShortestPathAvoidingBlocked(start, attackCell, blocked);
            if (route == null || route.Count == 0)
                continue;
            int routeLen = route.Count - 1;
            if (routeLen < bestLen)
            {
                bestLen = routeLen;
                bestRoute = route;
            }
        }

        int apLeft = mob.CurrentAp;
        if (bestRoute != null && bestRoute.Count > 1)
        {
            for (int i = 1; i < bestRoute.Count && apLeft > 0; i++)
            {
                actions.Add(new QueuedBattleActionDto
                {
                    ActionType = "MoveStep",
                    TargetPosition = new HexPositionDto { Col = bestRoute[i].col, Row = bestRoute[i].row },
                    Cost = 1
                });
                apLeft--;
            }
        }

        var finalPos = bestRoute != null && bestRoute.Count > 0 ? bestRoute[^1] : start;
        int mobRange = Math.Max(0, mob.WeaponRange);
        bool inRange = HexSpawn.HexDistance(finalPos.col, finalPos.row, targetPos.col, targetPos.row) <= mobRange;
        while (apLeft > 0 && inRange)
        {
            actions.Add(new QueuedBattleActionDto
            {
                ActionType = "Attack",
                TargetUnitId = target.UnitId,
                Cost = 1
            });
            apLeft--;
        }

        return actions.ToArray();
    }

    private QueuedBattleActionDto? BuildMobActionForCurrentState(
        string mobUnitId,
        Dictionary<string, (int col, int row)> positions,
        Dictionary<string, bool> alive,
        HashSet<(int col, int row)> occupied)
    {
        // Временный debug-режим: симуляция раунда иначе игнорирует пустую очередь моба и строит ход к игроку здесь.
        if (DebugSoloMobFiveHexNoChase1000Hp && IsSolo)
            return null;

        if (!Units.TryGetValue(mobUnitId, out var mob) || mob.UnitType != UnitType.Mob)
            return null;
        if (!positions.TryGetValue(mobUnitId, out var mobPos))
            return null;

        string? targetId = Units.Values
            .Where(u => u.UnitType == UnitType.Player
                && alive.TryGetValue(u.UnitId, out var isAlive) && isAlive
                && positions.ContainsKey(u.UnitId))
            .OrderBy(u => HexSpawn.HexDistance(mobPos.col, mobPos.row, positions[u.UnitId].col, positions[u.UnitId].row))
            .Select(u => u.UnitId)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(targetId) || !positions.TryGetValue(targetId, out var targetPos))
            return null;

        if (HexSpawn.HexDistance(mobPos.col, mobPos.row, targetPos.col, targetPos.row) <= Math.Max(0, mob.WeaponRange))
        {
            return new QueuedBattleActionDto
            {
                ActionType = "Attack",
                TargetUnitId = targetId,
                Cost = 1
            };
        }

        var blocked = new HashSet<(int col, int row)>(_obstacleTags.Keys);
        foreach (var kv in positions)
        {
            if (kv.Key == mobUnitId || kv.Key == targetId)
                continue;
            if (!alive.TryGetValue(kv.Key, out var isAlive) || !isAlive)
                continue;
            blocked.Add(kv.Value);
        }

        List<(int col, int row)>? bestRoute = null;
        int bestLen = int.MaxValue;
        foreach (var attackCell in EnumerateNeighbors(targetPos.col, targetPos.row))
        {
            if (_obstacleTags.ContainsKey(attackCell))
                continue;
            if (occupied.Contains(attackCell) && attackCell != mobPos)
                continue;

            var route = FindShortestPathAvoidingBlocked(mobPos, attackCell, blocked);
            if (route == null || route.Count <= 1)
                continue;

            int routeLen = route.Count - 1;
            if (routeLen < bestLen)
            {
                bestLen = routeLen;
                bestRoute = route;
            }
        }

        if (bestRoute == null || bestRoute.Count <= 1)
            return null;

        var next = bestRoute[1];
        return new QueuedBattleActionDto
        {
            ActionType = "MoveStep",
            TargetPosition = new HexPositionDto { Col = next.col, Row = next.row },
            Cost = 1
        };
    }
}
