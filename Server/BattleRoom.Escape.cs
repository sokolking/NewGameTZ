using System;
using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    /// <summary>Rounds of no actions after the player ends a turn on an escape hex; then they leave the battle.</summary>
    public const int EscapeChannelRounds = 3;

    /// <summary>
    /// One-hex frame outside the current active rectangle: indices from (min−1,max+1) on col and row (offset grid),
    /// excluding the active interior. Includes coordinates outside the nominal 0…W−1 map when the arena still fills the grid.
    /// </summary>
    public bool IsEscapeBorderHex(int col, int row)
    {
        EnsureActiveZoneInitialized();
        if (IsInActiveZone(col, row))
            return false;
        return col >= _activeMinCol - 1 && col <= _activeMaxCol + 1
            && row >= _activeMinRow - 1 && row <= _activeMaxRow + 1;
    }

    /// <summary>Legal move destination: active zone or the ±1 escape frame.</summary>
    private bool IsLegalMoveDestinationHex(int col, int row) =>
        IsInActiveZone(col, row) || IsEscapeBorderHex(col, row);

    private bool PlayerStandsOnEscapeBorderHex(string playerId)
    {
        if (string.IsNullOrEmpty(playerId) || !PlayerToUnitId.TryGetValue(playerId, out var uid))
            return false;
        if (!Units.TryGetValue(uid, out var u) || u.UnitType != UnitType.Player)
            return false;
        return IsEscapeBorderHex(u.Col, u.Row);
    }

    /// <summary>HTTP: optional legacy entry into flee channel (turn-based flee is detected in <see cref="CloseRound"/>).</summary>
    public bool TryBeginEscape(string playerId, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(playerId))
        {
            error = "playerId required";
            return false;
        }

        if (!Players.ContainsKey(playerId))
        {
            error = "Player not in battle";
            return false;
        }

        if (EscapingPlayers.ContainsKey(playerId))
        {
            error = "Already fleeing";
            return false;
        }

        if (!PlayerStandsOnEscapeBorderHex(playerId))
        {
            error = "Must stand on an escape hex to flee";
            return false;
        }

        EscapingPlayers[playerId] = EscapeChannelRounds;
        return true;
    }

    /// <summary>
    /// At the start of each new round, escaping human players cannot plan — register an empty submit so the round can close when others finish.
    /// </summary>
    private void InjectAutoSubmissionsForEscapingPlayers()
    {
        foreach (string escPid in EscapingPlayers.Keys.ToList())
        {
            if (!Players.ContainsKey(escPid))
                continue;
            if (Submissions.ContainsKey(escPid))
                continue;
            if (!PlayerToUnitId.TryGetValue(escPid, out var euid) || string.IsNullOrEmpty(euid))
                continue;
            if (!Units.TryGetValue(euid, out var us))
                continue;

            Submissions[escPid] = new SubmitTurnPayloadDto
            {
                BattleId = BattleId,
                PlayerId = escPid,
                RoundIndex = RoundIndex,
                CurrentMagazineRounds = Math.Max(0, us.CurrentMagazineRounds),
                Actions = Array.Empty<QueuedBattleActionDto>()
            };
            UnitCommands[euid] = new UnitCommandDto
            {
                UnitId = euid,
                CommandType = "Queue",
                Actions = Array.Empty<QueuedBattleActionDto>()
            };
            SubmissionOrder.Add(escPid);
            EndedTurnEarlyThisRound[escPid] = true;
        }
    }
}
