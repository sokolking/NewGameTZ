using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private static HexPositionDto[] LimitPathByAvailableAp(UnitStateDto unit, HexPositionDto[]? rawPath)
    {
        var start = new HexPositionDto { Col = unit.Col, Row = unit.Row };
        if (rawPath == null || rawPath.Length == 0)
            return new[] { start };

        var limited = new List<HexPositionDto> { start };
        int maxSteps = GetMaxReachableSteps(unit.CurrentAp);
        int stepsAdded = 0;

        for (int i = 1; i < rawPath.Length && stepsAdded < maxSteps; i++)
        {
            limited.Add(new HexPositionDto { Col = rawPath[i].Col, Row = rawPath[i].Row });
            stepsAdded++;
        }

        return limited.ToArray();
    }

    private HexPositionDto[] NormalizePath(UnitStateDto unit, HexPositionDto[]? rawPath)
    {
        var start = new HexPositionDto { Col = unit.Col, Row = unit.Row };
        if (rawPath == null || rawPath.Length == 0)
            return new[] { start };

        var limited = new List<HexPositionDto> { start };
        int maxSteps = GetMaxReachableSteps(unit.CurrentAp);
        int stepsAdded = 0;
        var current = (unit.Col, unit.Row);

        for (int i = 1; i < rawPath.Length && stepsAdded < maxSteps; i++)
        {
            var next = (rawPath[i].Col, rawPath[i].Row);
            if (next == current)
                continue;
            if (next.Item1 < 0 || next.Item2 < 0 || next.Item1 >= HexSpawn.DefaultGridWidth || next.Item2 >= HexSpawn.DefaultGridLength)
                break;
            if (_obstacleTags.ContainsKey(next))
                break;
            if (!AreAdjacent(current, next))
                break;

            limited.Add(new HexPositionDto { Col = next.Item1, Row = next.Item2 });
            current = next;
            stepsAdded++;
        }

        return limited.ToArray();
    }

    private HexPositionDto[] BuildPathFromActions(UnitStateDto unit, QueuedBattleActionDto[]? actions)
    {
        var start = new HexPositionDto { Col = unit.Col, Row = unit.Row };
        if (actions == null || actions.Length == 0)
            return new[] { start };

        var path = new List<HexPositionDto> { start };
        var current = (unit.Col, unit.Row);
        foreach (var action in actions)
        {
            if (action == null || !string.Equals(action.ActionType, "MoveStep", StringComparison.OrdinalIgnoreCase))
                continue;
            if (action.TargetPosition == null)
                continue;
            var next = (action.TargetPosition.Col, action.TargetPosition.Row);
            if (next == current)
                continue;
            path.Add(new HexPositionDto { Col = next.Item1, Row = next.Item2 });
            current = next;
        }
        return NormalizePath(unit, path.ToArray());
    }

    private static QueuedBattleActionDto[] BuildMoveActionsFromPath(HexPositionDto[]? path)
    {
        if (path == null || path.Length <= 1)
            return Array.Empty<QueuedBattleActionDto>();

        var actions = new List<QueuedBattleActionDto>();
        for (int i = 1; i < path.Length; i++)
        {
            actions.Add(new QueuedBattleActionDto
            {
                ActionType = "MoveStep",
                TargetPosition = new HexPositionDto { Col = path[i].Col, Row = path[i].Row },
                Cost = GetMoveCost(i - 1, 1)
            });
        }
        return actions.ToArray();
    }
}
