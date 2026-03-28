using BattleServer.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleServer;

public class BattleTurnDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();
    private readonly BattlePostgresDatabase _database;

    public BattleTurnDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    public void Save(BattleTurnRecordDto record)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO battle_turns (turn_id, battle_id, turn_result_json)
VALUES (@turnId, @battleId, CAST(@turnResultJson AS jsonb))
ON CONFLICT (turn_id) DO UPDATE
SET battle_id = EXCLUDED.battle_id,
    turn_result_json = EXCLUDED.turn_result_json;
""";
        command.Parameters.AddWithValue("turnId", record.TurnId);
        command.Parameters.AddWithValue("battleId", record.BattleId);
        command.Parameters.AddWithValue("turnResultJson", JsonSerializer.Serialize(record.TurnResult, JsonOptions));
        command.ExecuteNonQuery();
    }

    public BattleTurnRecordDto? GetTurn(string turnId)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT battle_id, turn_result_json::text
FROM battle_turns
WHERE turn_id = @turnId
LIMIT 1;
""";
        command.Parameters.AddWithValue("turnId", turnId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        string battleId = reader.GetString(0);
        string json = reader.GetString(1);
        var turnResult = JsonSerializer.Deserialize<TurnResultPayloadDto>(json, JsonOptions);
        if (turnResult == null)
            return null;

        return new BattleTurnRecordDto
        {
            TurnId = turnId,
            BattleId = battleId,
            TurnResult = turnResult
        };
    }

    public void RemoveMany(IEnumerable<string> turnIds)
    {
        var ids = turnIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
        if (ids.Length == 0)
            return;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM battle_turns WHERE turn_id = ANY(@turnIds);";
        command.Parameters.AddWithValue("turnIds", ids);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<BattleTurnBrowseRowDto> ListTurnsForBattle(string battleId, int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT l.turn_id,
       l.battle_id,
       l.turn_index,
       COALESCE((t.turn_result_json->>'roundIndex')::int, 0) AS round_index,
       COALESCE(t.turn_result_json->>'roundResolveReason', '') AS round_resolve_reason,
       t.created_utc
FROM battle_turn_links l
JOIN battle_turns t ON t.turn_id = l.turn_id
WHERE l.battle_id = @battleId
ORDER BY l.turn_index DESC
LIMIT @take;
""";
        command.Parameters.AddWithValue("battleId", battleId);
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 1000));

        using var reader = command.ExecuteReader();
        var rows = new List<BattleTurnBrowseRowDto>();
        while (reader.Read())
        {
            rows.Add(new BattleTurnBrowseRowDto
            {
                TurnId = reader.GetString(0),
                BattleId = reader.GetString(1),
                TurnIndex = reader.GetInt32(2),
                RoundIndex = reader.GetInt32(3),
                RoundResolveReason = reader.GetString(4),
                CreatedUtc = reader.GetFieldValue<DateTimeOffset>(5)
            });
        }

        return rows;
    }

    public BattleTurnDetailDto? GetTurnDetail(string turnId)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT t.turn_id,
       t.battle_id,
       COALESCE(l.turn_index, 0) AS turn_index,
       t.created_utc,
       t.turn_result_json::text
FROM battle_turns t
LEFT JOIN battle_turn_links l ON l.turn_id = t.turn_id
WHERE t.turn_id = @turnId
LIMIT 1;
""";
        command.Parameters.AddWithValue("turnId", turnId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        string battleId = reader.GetString(1);
        string rawJson = reader.GetString(4);
        var turnResult = JsonSerializer.Deserialize<TurnResultPayloadDto>(rawJson, JsonOptions);
        if (turnResult == null)
            return null;

        return new BattleTurnDetailDto
        {
            TurnId = reader.GetString(0),
            BattleId = battleId,
            TurnIndex = reader.GetInt32(2),
            CreatedUtc = reader.GetFieldValue<DateTimeOffset>(3),
            RawJson = rawJson,
            TurnResult = turnResult
        };
    }

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
