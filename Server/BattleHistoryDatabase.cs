using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public class BattleHistoryDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleHistoryDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    public BattleRecordDto EnsureBattle(string battleId)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO battles (battle_id)
VALUES (@battleId)
ON CONFLICT (battle_id) DO NOTHING;
""";
        command.Parameters.AddWithValue("battleId", battleId);
        command.ExecuteNonQuery();
        return GetBattleInternal(connection, battleId) ?? new BattleRecordDto { BattleId = battleId };
    }

    public BattleRecordDto AppendTurn(string battleId, string turnId)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var ensureCommand = connection.CreateCommand())
        {
            ensureCommand.Transaction = transaction;
            ensureCommand.CommandText = """
INSERT INTO battles (battle_id)
VALUES (@battleId)
ON CONFLICT (battle_id) DO NOTHING;
""";
            ensureCommand.Parameters.AddWithValue("battleId", battleId);
            ensureCommand.ExecuteNonQuery();
        }

        using (var linkCommand = connection.CreateCommand())
        {
            linkCommand.Transaction = transaction;
            linkCommand.CommandText = """
WITH next_turn AS (
    SELECT COALESCE(MAX(turn_index) + 1, 0) AS next_index
    FROM battle_turn_links
    WHERE battle_id = @battleId
)
INSERT INTO battle_turn_links (battle_id, turn_index, turn_id)
SELECT @battleId, next_index, @turnId
FROM next_turn
ON CONFLICT (turn_id) DO NOTHING;
""";
            linkCommand.Parameters.AddWithValue("battleId", battleId);
            linkCommand.Parameters.AddWithValue("turnId", turnId);
            linkCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return GetBattleInternal(connection, battleId) ?? new BattleRecordDto { BattleId = battleId };
    }

    public BattleRecordDto? GetBattle(string battleId)
    {
        using var connection = _database.DataSource.OpenConnection();
        return GetBattleInternal(connection, battleId);
    }

    public string[] RemoveBattle(string battleId)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        string[] turnIds;
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = """
SELECT turn_id
FROM battle_turn_links
WHERE battle_id = @battleId
ORDER BY turn_index;
""";
            selectCommand.Parameters.AddWithValue("battleId", battleId);
            using var reader = selectCommand.ExecuteReader();
            var list = new List<string>();
            while (reader.Read())
                list.Add(reader.GetString(0));
            turnIds = list.ToArray();
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = """
DELETE FROM battles
WHERE battle_id = @battleId;
""";
            deleteCommand.Parameters.AddWithValue("battleId", battleId);
            deleteCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return turnIds;
    }

    public IReadOnlyList<BattleBrowseRowDto> ListBattles(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT b.battle_id,
       b.created_utc,
       COUNT(l.turn_id)::int AS turn_count,
       (
           SELECT l2.turn_id
           FROM battle_turn_links l2
           WHERE l2.battle_id = b.battle_id
           ORDER BY l2.turn_index DESC
           LIMIT 1
       ) AS latest_turn_id
FROM battles b
LEFT JOIN battle_turn_links l ON l.battle_id = b.battle_id
GROUP BY b.battle_id, b.created_utc
ORDER BY b.created_utc DESC
LIMIT @take;
""";
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

        using var reader = command.ExecuteReader();
        var rows = new List<BattleBrowseRowDto>();
        while (reader.Read())
        {
            rows.Add(new BattleBrowseRowDto
            {
                BattleId = reader.GetString(0),
                CreatedUtc = reader.GetFieldValue<DateTimeOffset>(1),
                TurnCount = reader.GetInt32(2),
                LatestTurnId = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return rows;
    }

    private static BattleRecordDto? GetBattleInternal(NpgsqlConnection connection, string battleId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT turn_id
FROM battle_turn_links
WHERE battle_id = @battleId
ORDER BY turn_index;
""";
        command.Parameters.AddWithValue("battleId", battleId);

        using var reader = command.ExecuteReader();
        var turnIds = new List<string>();
        while (reader.Read())
            turnIds.Add(reader.GetString(0));

        if (turnIds.Count == 0)
        {
            reader.Close();
            using var existsCommand = connection.CreateCommand();
            existsCommand.CommandText = "SELECT 1 FROM battles WHERE battle_id = @battleId LIMIT 1;";
            existsCommand.Parameters.AddWithValue("battleId", battleId);
            var exists = existsCommand.ExecuteScalar();
            if (exists == null)
                return null;
        }

        return new BattleRecordDto
        {
            BattleId = battleId,
            TurnIds = turnIds
        };
    }
}
