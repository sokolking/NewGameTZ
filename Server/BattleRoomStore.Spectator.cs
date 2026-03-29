using System.Text.Json;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoomStore
{
    private static readonly JsonSerializerOptions SpectatorJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>All in-memory rooms that are not finished (same rule as login resume).</summary>
    public IReadOnlyList<(string battleId, string modeLabel)> ListUnfinishedBattlesForSpectatorLocked()
    {
        lock (_lock)
        {
            var list = new List<(string, string)>();
            foreach (var kv in _rooms)
            {
                if (!kv.Value.IsUnfinishedForLoginResume())
                    continue;
                list.Add((kv.Key, kv.Value.GetSpectatorModeLabel()));
            }

            return list;
        }
    }

    public string BuildSpectatorListJsonLocked()
    {
        var items = ListUnfinishedBattlesForSpectatorLocked();
        return JsonSerializer.Serialize(new
        {
            type = "spectatorListResponse",
            battles = items.Select(x => new { battleId = x.battleId, mode = x.modeLabel }).ToArray()
        }, SpectatorJsonOptions);
    }

    public string BuildSpectatorWatchResponseJsonLocked(string? battleId, out bool ok, out string? errorCode)
    {
        ok = false;
        errorCode = null;
        if (string.IsNullOrWhiteSpace(battleId))
        {
            errorCode = "battle_id_required";
            return JsonSerializer.Serialize(new { type = "spectatorWatchResponse", ok = false, code = errorCode }, SpectatorJsonOptions);
        }

        BattleStartedPayloadDto? started;
        lock (_lock)
        {
            if (!_rooms.TryGetValue(battleId.Trim(), out var room) || !room.IsUnfinishedForLoginResume())
            {
                errorCode = "battle_not_found_or_finished";
                return JsonSerializer.Serialize(new { type = "spectatorWatchResponse", ok = false, code = errorCode }, SpectatorJsonOptions);
            }

            started = room.BuildBattleStartedForSpectator();
        }

        ok = true;
        return JsonSerializer.Serialize(new
        {
            type = "spectatorWatchResponse",
            ok = true,
            battleId = battleId.Trim(),
            playerId = "__spectator__",
            battleStarted = started
        }, SpectatorJsonOptions);
    }
}
