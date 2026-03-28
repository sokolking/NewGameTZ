using BattleServer.Models;
using Npgsql;

namespace BattleServer;

/// <summary>Ammo dictionary table (caliber, unit weight, icon key).</summary>
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
SELECT at.id, at.item_id, at.caliber, COALESCE(i.name, at.caliber) AS name, at.unit_weight,
       COALESCE(i.quality, 100) AS quality, COALESCE(i.condition, 100) AS condition,
       COALESCE(i.icon_key, at.icon_key) AS icon_key, COALESCE(i.inventorygrid, 1) AS inventorygrid
FROM ammo_types at
LEFT JOIN items i ON i.id = at.item_id
ORDER BY at.id
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
                ItemId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                Caliber = reader.GetString(2),
                Name = reader.GetString(3),
                UnitWeight = reader.GetDouble(4),
                Quality = reader.GetInt32(5),
                Condition = reader.GetInt32(6),
                IconKey = reader.IsDBNull(7) ? "" : reader.GetString(7),
                InventoryGrid = reader.GetInt32(8)
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
SELECT at.id, at.item_id, at.caliber, COALESCE(i.name, at.caliber) AS name, at.unit_weight,
       COALESCE(i.quality, 100) AS quality, COALESCE(i.condition, 100) AS condition,
       COALESCE(i.icon_key, at.icon_key) AS icon_key, COALESCE(i.inventorygrid, 1) AS inventorygrid
FROM ammo_types at
LEFT JOIN items i ON i.id = at.item_id
WHERE LOWER(at.caliber) = LOWER(@caliber)
LIMIT 1;
""";
        command.Parameters.AddWithValue("caliber", c);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        dto = new AmmoTypeDto
        {
            Id = reader.GetInt64(0),
            ItemId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            Caliber = reader.GetString(2),
            Name = reader.GetString(3),
            UnitWeight = reader.GetDouble(4),
            Quality = reader.GetInt32(5),
            Condition = reader.GetInt32(6),
            IconKey = reader.IsDBNull(7) ? "" : reader.GetString(7),
            InventoryGrid = reader.GetInt32(8)
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

        double weight = Math.Max(0.0, req.UnitWeight);
        string iconKey = (req.IconKey ?? "").Trim();
        string name = string.IsNullOrWhiteSpace(req.Name) ? caliber : req.Name.Trim();
        int quality = Math.Clamp(req.Quality, 0, 9999);
        int condition = Math.Clamp(req.Condition, 0, 9999);
        int inventoryGrid = Math.Clamp(req.InventoryGrid, 0, 2);
        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        bool isEquippable;
        using (var eqCommand = connection.CreateCommand())
        {
            eqCommand.Transaction = tx;
            eqCommand.CommandText = """
SELECT 1
FROM weapons
WHERE LOWER(code) = LOWER(@code)
LIMIT 1;
""";
            eqCommand.Parameters.AddWithValue("code", caliber);
            isEquippable = eqCommand.ExecuteScalar() != null;
        }
        long itemId;
        using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.Transaction = tx;
            itemCommand.CommandText = """
INSERT INTO items (name, mass, quality, condition, icon_key, type, is_equippable, inventorygrid)
VALUES (@name, @mass, @quality, @condition, @iconKey, 'ammo', @isEquippable, @inventorygrid)
ON CONFLICT DO NOTHING;
SELECT id FROM items
WHERE type = 'ammo' AND name = @name AND icon_key = @iconKey
ORDER BY id DESC
LIMIT 1;
""";
            itemCommand.Parameters.AddWithValue("name", name);
            itemCommand.Parameters.AddWithValue("mass", weight);
            itemCommand.Parameters.AddWithValue("quality", quality);
            itemCommand.Parameters.AddWithValue("condition", condition);
            itemCommand.Parameters.AddWithValue("iconKey", iconKey);
            itemCommand.Parameters.AddWithValue("isEquippable", isEquippable);
            itemCommand.Parameters.AddWithValue("inventorygrid", inventoryGrid);
            object? scalar = itemCommand.ExecuteScalar();
            itemId = scalar is long id ? id : Convert.ToInt64(scalar);
        }
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
INSERT INTO ammo_types (item_id, caliber, unit_weight, icon_key)
VALUES (@itemId, @caliber, @unitWeight, @iconKey)
ON CONFLICT (caliber) DO UPDATE SET
    item_id = EXCLUDED.item_id,
    unit_weight = EXCLUDED.unit_weight,
    icon_key = EXCLUDED.icon_key;
""";
        command.Parameters.AddWithValue("itemId", itemId);
        command.Parameters.AddWithValue("caliber", caliber);
        command.Parameters.AddWithValue("unitWeight", weight);
        command.Parameters.AddWithValue("iconKey", iconKey);
        command.ExecuteNonQuery();
        tx.Commit();
        return true;
    }
}

