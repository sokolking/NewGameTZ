using System.Text.Json;
using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public sealed class BattleItemsCatalogDatabase
{
    private readonly BattlePostgresDatabase _database;
    private readonly BattleWeaponDatabase _weapons;
    private readonly BattleMedicineDatabase _medicine;
    private static readonly JsonSerializerOptions JsonOpt = new(JsonSerializerDefaults.Web);

    public BattleItemsCatalogDatabase(BattlePostgresDatabase database, BattleWeaponDatabase weapons, BattleMedicineDatabase medicine)
    {
        _database = database;
        _weapons = weapons;
        _medicine = medicine;
    }

    public IReadOnlyList<ItemCatalogRowDto> List(int take, string? itemType, string? weaponCategory, string? q)
    {
        int safeTake = Math.Clamp(take, 1, 5000);
        string type = (itemType ?? "").Trim().ToLowerInvariant();
        string category = (weaponCategory ?? "").Trim().ToLowerInvariant();
        string query = (q ?? "").Trim().ToLowerInvariant();
        var rows = new List<ItemCatalogRowDto>(safeTake);

        foreach (var w in _weapons.ListWeapons(5000))
        {
            string normalizedWeaponType = BattleWeaponDatabase.NormalizeItemType(w.ItemType, "weapon");
            if (!string.Equals(normalizedWeaponType, "weapon", StringComparison.OrdinalIgnoreCase))
                continue;
            var row = new ItemCatalogRowDto
            {
                ItemId = w.Id,
                ItemType = normalizedWeaponType,
                Name = w.Name,
                Mass = w.Mass,
                Quality = w.Quality,
                Condition = w.WeaponCondition,
                IconKey = w.IconKey,
                InventoryGrid = Math.Clamp(w.InventoryGrid, 0, 2),
                IsEquippable = w.IsEquippable,
                WeaponId = w.Id,
                Category = w.Category,
                DamageMin = w.DamageMin,
                DamageMax = w.DamageMax,
                DamageType = w.DamageType,
                Range = w.Range,
                AttackApCost = w.AttackApCost,
                BurstRounds = w.BurstRounds,
                BurstApCost = w.BurstApCost,
                Tightness = w.Tightness,
                TrajectoryHeight = w.TrajectoryHeight,
                IsSniper = w.IsSniper,
                AmmoTypeId = w.AmmoTypeId,
                ArmorPierce = w.ArmorPierce,
                MagazineSize = w.MagazineSize,
                ReloadApCost = w.ReloadApCost,
                ReqLevel = w.ReqLevel,
                ReqStrength = w.ReqStrength,
                ReqEndurance = w.ReqEndurance,
                ReqAccuracy = w.ReqAccuracy,
                ReqMasteryCategory = w.ReqMasteryCategory,
                StatEffectStrength = w.StatEffectStrength,
                StatEffectEndurance = w.StatEffectEndurance,
                StatEffectAccuracy = w.StatEffectAccuracy,
                EffectType = "",
                EffectSign = "",
                EffectMin = 0,
                EffectMax = 0,
                EffectTarget = "",
                InventorySlotWidth = w.InventorySlotWidth,
                WeaponCondition = w.WeaponCondition
            };
            if (Matches(row, type, category, query))
                rows.Add(row);
        }

        foreach (var m in _medicine.ListMedicine(5000))
        {
            var row = new ItemCatalogRowDto
            {
                ItemId = m.Id,
                ItemType = "medicine",
                Name = m.Name,
                Mass = m.Mass,
                Quality = m.Quality,
                Condition = m.Condition,
                IconKey = m.IconKey,
                InventoryGrid = Math.Clamp(m.InventoryGrid, 0, 2),
                IsEquippable = m.IsEquippable,
                WeaponId = null,
                Category = "",
                DamageMin = 0,
                DamageMax = 0,
                DamageType = "",
                Range = 0,
                AttackApCost = m.AttackApCost,
                BurstRounds = 0,
                BurstApCost = 0,
                Tightness = 1,
                TrajectoryHeight = 0,
                IsSniper = false,
                AmmoTypeId = null,
                ArmorPierce = 0,
                MagazineSize = 0,
                ReloadApCost = 0,
                ReqLevel = m.ReqLevel,
                ReqStrength = m.ReqStrength,
                ReqEndurance = m.ReqEndurance,
                ReqAccuracy = m.ReqAccuracy,
                ReqMasteryCategory = m.ReqMasteryCategory,
                StatEffectStrength = 0,
                StatEffectEndurance = 0,
                StatEffectAccuracy = 0,
                EffectType = m.EffectType,
                EffectSign = m.EffectSign,
                EffectMin = m.EffectMin,
                EffectMax = m.EffectMax,
                EffectTarget = m.EffectTarget,
                InventorySlotWidth = m.InventorySlotWidth,
                WeaponCondition = m.Condition
            };
            if (Matches(row, type, category, query))
                rows.Add(row);
        }

        using (var conn = _database.DataSource.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
SELECT id, name, mass, quality, condition, icon_key, type,
       COALESCE(inventorygrid, 1), COALESCE(category, '')
FROM items
WHERE type NOT IN ('weapon', 'medicine')
ORDER BY id
LIMIT 5000;
""";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new ItemCatalogRowDto
                {
                    ItemId = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Mass = reader.GetDouble(2),
                    Quality = reader.GetInt32(3),
                    Condition = reader.GetInt32(4),
                    IconKey = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    ItemType = BattleWeaponDatabase.NormalizeItemType(reader.GetString(6), "ammo"),
                    InventoryGrid = Math.Clamp(reader.GetInt32(7), 0, 2),
                    Category = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    IsEquippable = false
                };
                if (Matches(row, type, category, query))
                    rows.Add(row);
            }
        }

        return rows
            .OrderBy(x => x.ItemId)
            .Take(safeTake)
            .ToList();
    }

    public bool Upsert(ItemCatalogUpsertDto req, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            error = "name required";
            return false;
        }

        string itemType = BattleWeaponDatabase.NormalizeItemType(req.ItemType, "weapon");
        if (req.ItemId > 0)
        {
            if (itemType == "weapon")
                _medicine.TryDeleteMedicineByItemId(req.ItemId, out _);
            else if (itemType == "medicine")
                _weapons.TryDeleteWeaponByItemId(req.ItemId, out _);
            else
            {
                _weapons.TryDeleteWeaponByItemId(req.ItemId, out _);
                _medicine.TryDeleteMedicineByItemId(req.ItemId, out _);
            }
        }
        if (itemType == "weapon")
        {
            var dto = new BattleWeaponUpsertDto
            {
                ItemId = req.ItemId,
                Name = req.Name.Trim(),
                DamageMin = req.DamageMin,
                DamageMax = req.DamageMax,
                Range = req.Range,
                IconKey = req.IconKey ?? "",
                AttackApCost = req.AttackApCost,
                Tightness = req.Tightness,
                TrajectoryHeight = req.TrajectoryHeight,
                Quality = req.Quality,
                WeaponCondition = req.Condition,
                IsSniper = req.IsSniper,
                Mass = req.Mass,
                AmmoTypeId = req.AmmoTypeId,
                ArmorPierce = req.ArmorPierce,
                MagazineSize = req.MagazineSize,
                ReloadApCost = req.ReloadApCost,
                Category = req.Category ?? "cold",
                ReqLevel = req.ReqLevel,
                ReqStrength = req.ReqStrength,
                ReqEndurance = req.ReqEndurance,
                ReqAccuracy = req.ReqAccuracy,
                ReqMasteryCategory = req.ReqMasteryCategory ?? "",
                StatEffectStrength = req.StatEffectStrength,
                StatEffectEndurance = req.StatEffectEndurance,
                StatEffectAccuracy = req.StatEffectAccuracy,
                DamageType = req.DamageType ?? "physical",
                BurstRounds = req.BurstRounds,
                BurstApCost = req.BurstApCost,
                InventorySlotWidth = Math.Clamp(req.InventorySlotWidth, 1, 2),
                InventoryGrid = Math.Clamp(req.InventoryGrid, 0, 2),
                IsEquippable = req.IsEquippable,
                ItemType = "weapon"
            };
            _weapons.UpsertWeapon(dto);
            return true;
        }

        if (itemType == "medicine")
        {
            _medicine.UpsertMedicine(req);
            return true;
        }

        string name = req.Name.Trim();
        double mass = Math.Max(0.0, req.Mass);
        int quality = Math.Clamp(req.Quality, 0, 9999);
        int condition = Math.Clamp(req.Condition, 0, 9999);
        string iconKey = (req.IconKey ?? "").Trim();
        int inventoryGrid = Math.Clamp(req.InventoryGrid, 0, 2);
        string category = (req.Category ?? "").Trim();

        using var connection = _database.DataSource.OpenConnection();

        if (req.ItemId > 0)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
UPDATE items
SET name = @name,
    mass = @mass,
    quality = @quality,
    condition = @condition,
    icon_key = @iconKey,
    type = @itemType,
    inventorygrid = @inventorygrid,
    category = @category
WHERE id = @itemId;
""";
            cmd.Parameters.AddWithValue("itemId", req.ItemId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("mass", mass);
            cmd.Parameters.AddWithValue("quality", quality);
            cmd.Parameters.AddWithValue("condition", condition);
            cmd.Parameters.AddWithValue("iconKey", iconKey);
            cmd.Parameters.AddWithValue("itemType", itemType);
            cmd.Parameters.AddWithValue("inventorygrid", inventoryGrid);
            cmd.Parameters.AddWithValue("category", category);
            int updated = cmd.ExecuteNonQuery();
            if (updated == 0)
            {
                error = "item not found";
                return false;
            }
            return true;
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
INSERT INTO items (name, mass, quality, condition, icon_key, type, inventorygrid, category)
VALUES (@name, @mass, @quality, @condition, @iconKey, @itemType, @inventorygrid, @category);
""";
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("mass", mass);
            cmd.Parameters.AddWithValue("quality", quality);
            cmd.Parameters.AddWithValue("condition", condition);
            cmd.Parameters.AddWithValue("iconKey", iconKey);
            cmd.Parameters.AddWithValue("itemType", itemType);
            cmd.Parameters.AddWithValue("inventorygrid", inventoryGrid);
            cmd.Parameters.AddWithValue("category", category);
            cmd.ExecuteNonQuery();
        }
        return true;
    }

    public bool Delete(long itemId, out string? error)
    {
        error = null;
        if (itemId <= 0)
        {
            error = "invalid item id";
            return false;
        }

        if (_weapons.TryGetWeaponByItemId(itemId, out _))
        {
            if (!_weapons.TryDeleteWeaponByItemId(itemId, out error))
                return false;
        }

        _medicine.TryDeleteMedicineByItemId(itemId, out _);

        try
        {
            using var connection = _database.DataSource.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
DELETE FROM items
WHERE id = @id
  AND NOT EXISTS (SELECT 1 FROM weapons w WHERE w.item_id = @id)
  AND NOT EXISTS (SELECT 1 FROM medicine m WHERE m.item_id = @id);
""";
            cmd.Parameters.AddWithValue("id", itemId);
            int n = cmd.ExecuteNonQuery();
            if (n == 0)
            {
                error = "item not found";
                return false;
            }
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            error = "item is referenced and cannot be deleted";
            return false;
        }
    }

    public string ExportJson()
    {
        var rows = List(5000, null, null, null);
        return JsonSerializer.Serialize(rows, JsonOpt);
    }

    public bool ImportJson(string json, out string? error, out int importedCount)
    {
        error = null;
        importedCount = 0;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "json body required";
            return false;
        }

        ItemCatalogUpsertDto[]? rows;
        try
        {
            rows = JsonSerializer.Deserialize<ItemCatalogUpsertDto[]>(json, JsonOpt);
        }
        catch (Exception ex)
        {
            error = "invalid json: " + ex.Message;
            return false;
        }

        if (rows == null || rows.Length == 0)
        {
            error = "non-empty array required";
            return false;
        }

        foreach (var r in rows)
        {
            if (!Upsert(r, out error))
                return false;
            importedCount++;
        }

        return true;
    }

    public long GetNextItemId()
    {
        using var connection = _database.DataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT nextval('items_id_seq');";
        object? scalar = cmd.ExecuteScalar();
        if (scalar is long id)
            return id;
        return scalar == null ? 0 : Convert.ToInt64(scalar);
    }

    private static bool Matches(ItemCatalogRowDto row, string itemType, string weaponCategory, string q)
    {
        if (!string.IsNullOrEmpty(itemType) && !string.Equals(row.ItemType, itemType, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(weaponCategory) && !string.Equals(row.Category ?? "", weaponCategory, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrEmpty(q))
            return true;
        string hay = $"{row.Name} {row.IconKey}".ToLowerInvariant();
        return hay.Contains(q, StringComparison.Ordinal);
    }
}

public class ItemCatalogRowDto
{
    public long ItemId { get; set; }
    public string ItemType { get; set; } = "weapon";
    public string Name { get; set; } = "";
    public double Mass { get; set; }
    public int Quality { get; set; } = 100;
    public int Condition { get; set; } = 100;
    public string IconKey { get; set; } = "";
    public int InventoryGrid { get; set; } = 1;
    public bool IsEquippable { get; set; }

    public long? WeaponId { get; set; }
    public string? Category { get; set; }
    public int DamageMin { get; set; }
    public int DamageMax { get; set; }
    public string DamageType { get; set; } = "physical";
    public int Range { get; set; }
    public int AttackApCost { get; set; } = 1;
    public int BurstRounds { get; set; }
    public int BurstApCost { get; set; }
    public double Tightness { get; set; } = 1;
    public int TrajectoryHeight { get; set; } = 1;
    public bool IsSniper { get; set; }
    public long? AmmoTypeId { get; set; }
    public int ArmorPierce { get; set; }
    public int MagazineSize { get; set; }
    public int ReloadApCost { get; set; }
    public int ReqLevel { get; set; } = 1;
    public int ReqStrength { get; set; }
    public int ReqEndurance { get; set; }
    public int ReqAccuracy { get; set; }
    public string ReqMasteryCategory { get; set; } = "";
    public int StatEffectStrength { get; set; }
    public int StatEffectEndurance { get; set; }
    public int StatEffectAccuracy { get; set; }
    public string EffectType { get; set; } = "";
    public string EffectSign { get; set; } = "positive";
    public int EffectMin { get; set; }
    public int EffectMax { get; set; }
    public string EffectTarget { get; set; } = "enemy";
    public int InventorySlotWidth { get; set; } = 1;
    public int WeaponCondition { get; set; } = 100;
}

public sealed class ItemCatalogUpsertDto : ItemCatalogRowDto;
