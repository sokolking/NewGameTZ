using BattleServer.Models;

namespace BattleServer;

public class BattleTurnDatabase
{
    private readonly object _lock = new();
    private readonly Dictionary<string, BattleTurnRecordDto> _turns = new();

    public void Save(BattleTurnRecordDto record)
    {
        lock (_lock)
            _turns[record.TurnId] = Clone(record);
    }

    public BattleTurnRecordDto? GetTurn(string turnId)
    {
        lock (_lock)
            return _turns.TryGetValue(turnId, out var record) ? Clone(record) : null;
    }

    public void RemoveMany(IEnumerable<string> turnIds)
    {
        lock (_lock)
        {
            foreach (var turnId in turnIds)
            {
                if (!string.IsNullOrEmpty(turnId))
                    _turns.Remove(turnId);
            }
        }
    }

    private static BattleTurnRecordDto Clone(BattleTurnRecordDto source)
    {
        return new BattleTurnRecordDto
        {
            TurnId = source.TurnId,
            BattleId = source.BattleId,
            TurnResult = source.TurnResult
        };
    }
}
