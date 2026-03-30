using System.Collections.Generic;

namespace BattleServer;

public partial class BattleRoom
{
    private readonly Dictionary<string, (int s, int ag, int i, int e, int acc, int intl)> _unitCardCombatByPlayer = new();

    public void SetPlayerUnitCardCombatStats(string playerId, int strength, int agility, int intuition, int endurance, int accuracy, int intellect)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return;
        _unitCardCombatByPlayer[playerId] = (strength, agility, intuition, endurance, accuracy, intellect);
    }

    private (int s, int ag, int i, int e, int acc, int intl) CombatStatsForPlayerOrZero(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || !_unitCardCombatByPlayer.TryGetValue(playerId, out var t))
            return (0, 0, 0, 0, 0, 0);
        return t;
    }
}
