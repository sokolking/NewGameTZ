using BattleServer.Models;
using Npgsql;

namespace BattleServer;

/// <summary>Ammo dictionary table (caliber, unit weight, rounds per pack).</summary>
public sealed class BattleAmmoDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleAmmoDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    public IReadOnlyList<AmmoTypeDto> ListAmmoTypes(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, caliber, unit_weight, rounds_per_pack
FROM ammo_types
ORDER BY id
LIMIT @take;
""";
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

        using var reader = command.ExecuteReader();
        var list = new List<AmmoTypeDto>();
        while (reader.Read())
        {
            list.Add(new AmmoTypeDto
            {
                Id = reader.GetInt64(0),
                Caliber = reader.GetString(1),
                UnitWeight = reader.GetDouble(2),
                RoundsPerPack = reader.GetInt32(3)
            });
        }

        return list;
    }

    public bool TryGetAmmoTypeByCaliber(string caliber, out AmmoTypeDto dto)
    {
        dto = new AmmoTypeDto();
        string c = (caliber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(c))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, caliber, unit_weight, rounds_per_pack
FROM ammo_types
WHERE LOWER(caliber) = LOWER(@caliber)
LIMIT 1;
""";
        command.Parameters.AddWithValue("caliber", c);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        dto = new AmmoTypeDto
        {
            Id = reader.GetInt64(0),
            Caliber = reader.GetString(1),
            UnitWeight = reader.GetDouble(2),
            RoundsPerPack = reader.GetInt32(3)
        };
        return true;
    }

    public bool TryUpsertAmmoType(AmmoTypeUpsertRequest req, out string? error)
    {
        error = null;
        string caliber = (req.Caliber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(caliber))
        {
            error = "caliber required";
            return false;
        }

        int rounds = Math.Clamp(req.RoundsPerPack, 1, 5000);
        double weight = Math.Max(0.0, req.UnitWeight);

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO ammo_types (caliber, unit_weight, rounds_per_pack)
VALUES (@caliber, @unitWeight, @roundsPerPack)
ON CONFLICT (caliber) DO UPDATE SET
    unit_weight = EXCLUDED.unit_weight,
    rounds_per_pack = EXCLUDED.rounds_per_pack;
""";
        command.Parameters.AddWithValue("caliber", caliber);
        command.Parameters.AddWithValue("unitWeight", weight);
        command.Parameters.AddWithValue("roundsPerPack", rounds);
        command.ExecuteNonQuery();
        return true;
    }
}

