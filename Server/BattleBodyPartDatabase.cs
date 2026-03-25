using BattleServer.Models;
using Npgsql;

namespace BattleServer;

/// <summary>Reference data for hit locations; clients send numeric <c>bodyPart</c> ids.</summary>
public sealed class BattleBodyPartDatabase
{
    private readonly BattlePostgresDatabase _database;
    private HashSet<int> _validIds = new() { 1, 2, 3, 4, 5 };

    public BattleBodyPartDatabase(BattlePostgresDatabase database)
    {
        _database = database;
        RefreshCache();
    }

    /// <summary>Reload valid ids from DB (call after migrations).</summary>
    public void RefreshCache()
    {
        try
        {
            using var connection = _database.DataSource.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM body_parts ORDER BY id;";
            using var reader = command.ExecuteReader();
            var set = new HashSet<int>();
            while (reader.Read())
                set.Add(reader.GetInt16(0));
            if (set.Count > 0)
                _validIds = set;
        }
        catch
        {
            // keep seed defaults if table missing
        }
    }

    public IReadOnlyList<BattleBodyPartRowDto> ListBodyParts()
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, code FROM body_parts ORDER BY id;";
        using var reader = command.ExecuteReader();
        var rows = new List<BattleBodyPartRowDto>();
        while (reader.Read())
        {
            rows.Add(new BattleBodyPartRowDto
            {
                Id = reader.GetInt16(0),
                Code = reader.GetString(1)
            });
        }

        return rows;
    }

    /// <summary>0 = unspecified; unknown ids are clamped to 0.</summary>
    public int NormalizeBodyPartId(int bodyPartId)
    {
        if (bodyPartId == 0)
            return 0;
        return _validIds.Contains(bodyPartId) ? bodyPartId : 0;
    }
}
