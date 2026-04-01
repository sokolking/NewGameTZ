using BattleServer.Models;
using Npgsql;

namespace BattleServer;

/// <summary>Ammo dictionary from items table (id-first).</summary>
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
SELECT i.id, i.id, i.name, i.name, i.mass,
       COALESCE(i.quality, 100) AS quality, COALESCE(i.condition, 100) AS condition,
       COALESCE(i.icon_key, '') AS icon_key, COALESCE(i.inventorygrid, 1) AS inventorygrid,
       COALESCE(i.type, 'ammo') AS item_type, COALESCE(i.category, '') AS category
FROM items i
WHERE i.type IN ('ammo', 'medicine')
ORDER BY i.id
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
                InventoryGrid = reader.GetInt32(8),
                ItemType = BattleWeaponDatabase.NormalizeItemType(reader.GetString(9), "ammo"),
                Category = reader.IsDBNull(10) ? "" : reader.GetString(10)
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
SELECT i.id, i.id, i.name, i.name, i.mass,
       COALESCE(i.quality, 100) AS quality, COALESCE(i.condition, 100) AS condition,
       COALESCE(i.icon_key, '') AS icon_key, COALESCE(i.inventorygrid, 1) AS inventorygrid,
       COALESCE(i.type, 'ammo') AS item_type
FROM items i
WHERE i.type IN ('ammo', 'medicine')
  AND LOWER(i.name) = LOWER(@caliber)
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
            InventoryGrid = reader.GetInt32(8),
            ItemType = BattleWeaponDatabase.NormalizeItemType(reader.GetString(9), "ammo")
        };
        return true;
    }

    public bool TryGetAmmoTypeById(long ammoTypeId, out AmmoTypeDto dto)
    {
        dto = new AmmoTypeDto();
        if (ammoTypeId <= 0)
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT i.id, i.id, i.name, i.name, i.mass,
       COALESCE(i.quality, 100) AS quality, COALESCE(i.condition, 100) AS condition,
       COALESCE(i.icon_key, '') AS icon_key, COALESCE(i.inventorygrid, 1) AS inventorygrid,
       COALESCE(i.type, 'ammo') AS item_type
FROM items i
WHERE i.id = @id
  AND i.type IN ('ammo', 'medicine')
LIMIT 1;
""";
        command.Parameters.AddWithValue("id", ammoTypeId);
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
            InventoryGrid = reader.GetInt32(8),
            ItemType = BattleWeaponDatabase.NormalizeItemType(reader.GetString(9), "ammo")
        };
        return true;
    }

    public bool TryUpsertAmmoType(AmmoTypeUpsertRequest req, out string? error)
    {
        error = null;
        string name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "name required";
            return false;
        }
        double weight = Math.Max(0.0, req.UnitWeight);
        string iconKey = (req.IconKey ?? "").Trim();
        // Keep legacy DB column populated, but treat it as internal mirror of name.
        name = name.Trim();
        int quality = Math.Clamp(req.Quality, 0, 9999);
        int condition = Math.Clamp(req.Condition, 0, 9999);
        int inventoryGrid = Math.Clamp(req.InventoryGrid, 0, 2);
        string itemType = BattleWeaponDatabase.NormalizeItemType(req.ItemType, "ammo");
        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        bool isEquippable = false;
        long itemId = req.ItemId > 0 ? req.ItemId : (req.Id > 0 ? req.Id : 0);
        if (itemId > 0)
        {
            using var updItem = connection.CreateCommand();
            updItem.Transaction = tx;
            updItem.CommandText = """
UPDATE items
SET name = @name,
    mass = @mass,
    quality = @quality,
    condition = @condition,
    icon_key = @iconKey,
    type = @itemType,
    is_equippable = @isEquippable,
    inventorygrid = @inventorygrid,
    category = @category
WHERE id = @itemId;
""";
            updItem.Parameters.AddWithValue("itemId", itemId);
            updItem.Parameters.AddWithValue("name", name);
            updItem.Parameters.AddWithValue("mass", weight);
            updItem.Parameters.AddWithValue("quality", quality);
            updItem.Parameters.AddWithValue("condition", condition);
            updItem.Parameters.AddWithValue("iconKey", iconKey);
            updItem.Parameters.AddWithValue("itemType", itemType);
            updItem.Parameters.AddWithValue("isEquippable", isEquippable);
            updItem.Parameters.AddWithValue("inventorygrid", inventoryGrid);
            updItem.Parameters.AddWithValue("category", req.Category ?? "");
            int updated = updItem.ExecuteNonQuery();
            if (updated == 0)
                itemId = 0;
        }
        if (itemId <= 0)
        {
            using (var findExisting = connection.CreateCommand())
            {
                findExisting.Transaction = tx;
                findExisting.CommandText = """
SELECT id
FROM items
WHERE LOWER(name) = LOWER(@name)
LIMIT 1;
""";
                findExisting.Parameters.AddWithValue("name", name);
                object? existing = findExisting.ExecuteScalar();
                if (existing != null)
                    itemId = existing is long ex ? ex : Convert.ToInt64(existing);
            }
        }
        if (itemId <= 0)
        {
            using var insItem = connection.CreateCommand();
            insItem.Transaction = tx;
            insItem.CommandText = """
INSERT INTO items (name, mass, quality, condition, icon_key, type, is_equippable, inventorygrid, category)
VALUES (@name, @mass, @quality, @condition, @iconKey, @itemType, @isEquippable, @inventorygrid, @category)
RETURNING id;
""";
            insItem.Parameters.AddWithValue("name", name);
            insItem.Parameters.AddWithValue("mass", weight);
            insItem.Parameters.AddWithValue("quality", quality);
            insItem.Parameters.AddWithValue("condition", condition);
            insItem.Parameters.AddWithValue("iconKey", iconKey);
            insItem.Parameters.AddWithValue("itemType", itemType);
            insItem.Parameters.AddWithValue("isEquippable", isEquippable);
            insItem.Parameters.AddWithValue("inventorygrid", inventoryGrid);
            insItem.Parameters.AddWithValue("category", req.Category ?? "");
            object? scalar = insItem.ExecuteScalar();
            itemId = scalar is long id ? id : Convert.ToInt64(scalar);
        }
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
UPDATE items
SET name = @name,
    mass = @mass,
    quality = @quality,
    condition = @condition,
    icon_key = @iconKey,
    type = @itemType,
    is_equippable = @isEquippable,
    inventorygrid = @inventorygrid,
    category = @category
WHERE id = @itemId;
""";
        command.Parameters.AddWithValue("itemId", itemId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("mass", weight);
        command.Parameters.AddWithValue("quality", quality);
        command.Parameters.AddWithValue("condition", condition);
        command.Parameters.AddWithValue("iconKey", iconKey);
        command.Parameters.AddWithValue("itemType", itemType);
        command.Parameters.AddWithValue("isEquippable", isEquippable);
        command.Parameters.AddWithValue("inventorygrid", inventoryGrid);
        command.Parameters.AddWithValue("category", req.Category ?? "");
        command.ExecuteNonQuery();
        tx.Commit();
        return true;
    }
}

