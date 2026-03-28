using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private void EnsureMobCommandsForCurrentRound()
    {
        var playerUnits = Units.Values.Where(u => u.UnitType == UnitType.Player).ToList();
        if (playerUnits.Count == 0) return;

        foreach (var mob in Units.Values.Where(u => u.UnitType == UnitType.Mob))
        {
            if (DebugSoloMobFiveHexNoChase1000Hp)
            {
                UnitCommands[mob.UnitId] = new UnitCommandDto
                {
                    UnitId = mob.UnitId,
                    CommandType = "Queue",
                    Actions = Array.Empty<QueuedBattleActionDto>()
                };
                continue;
            }

            // ближайший игрок по hex-distance
            var target = playerUnits
                .OrderBy(p => HexSpawn.HexDistance(mob.Col, mob.Row, p.Col, p.Row))
                .First();
            var actions = BuildMobActionQueue(mob, target);
            int moveCount = actions.Count(a => a != null && a.ActionType == "MoveStep");
            int attackCount = actions.Count(a => a != null && a.ActionType == "Attack");
            Console.WriteLine(
                $"[mobAI] battleId={BattleId} mob={mob.UnitId} mobPos=({mob.Col},{mob.Row}) ap={mob.CurrentAp} " +
                $"target={target.UnitId} targetPos=({target.Col},{target.Row}) moveActions={moveCount} attackActions={attackCount}");

            UnitCommands[mob.UnitId] = new UnitCommandDto
            {
                UnitId = mob.UnitId,
                CommandType = "Queue",
                Actions = actions
            };
        }
    }
}
