using BattleServer.Models;

namespace BattleServer;

public class BattleHistoryDatabase
{
    private readonly object _lock = new();
    private readonly Dictionary<string, BattleRecordDto> _battles = new();

    public BattleRecordDto EnsureBattle(string battleId)
    {
        lock (_lock)
        {
            if (!_battles.TryGetValue(battleId, out var record))
            {
                record = new BattleRecordDto { BattleId = battleId };
                _battles[battleId] = record;
            }

            return Clone(record);
        }
    }

    public BattleRecordDto AppendTurn(string battleId, string turnId)
    {
        lock (_lock)
        {
            if (!_battles.TryGetValue(battleId, out var record))
            {
                record = new BattleRecordDto { BattleId = battleId };
                _battles[battleId] = record;
            }

            record.TurnIds.Add(turnId);
            return Clone(record);
        }
    }

    public BattleRecordDto? GetBattle(string battleId)
    {
        lock (_lock)
            return _battles.TryGetValue(battleId, out var record) ? Clone(record) : null;
    }

    public string[] RemoveBattle(string battleId)
    {
        lock (_lock)
        {
            if (!_battles.Remove(battleId, out var record))
                return Array.Empty<string>();

            return record.TurnIds.ToArray();
        }
    }

    private static BattleRecordDto Clone(BattleRecordDto source)
    {
        return new BattleRecordDto
        {
            BattleId = source.BattleId,
            TurnIds = new List<string>(source.TurnIds)
        };
    }
}
