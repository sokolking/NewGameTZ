using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public sealed class BattleMedicineDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleMedicineDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    private const string SelectMedicineColumns = """
m.item_id AS id, COALESCE(i.name, '') AS name, COALESCE(i.icon_key, '') AS icon_key,
COALESCE(i.mass, 0) AS mass, COALESCE(i.quality, 100) AS quality, COALESCE(i.condition, 100) AS condition,
m.attack_ap_cost, m.req_level, m.req_strength, m.req_endurance, m.req_accuracy, m.req_mastery_category,
m.effect_type, m.effect_sign, m.effect_min, m.effect_max, m.effect_target,
m.inventory_slot_width, COALESCE(i.inventorygrid, 1) AS inventorygrid, COALESCE(i.is_equippable, FALSE) AS is_equippable,
COALESCE(i.type, 'medicine') AS item_type
""";

    public IReadOnlyList<BattleMedicineBrowseRowDto> ListMedicine(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT {SelectMedicineColumns}
FROM medicine m
JOIN items i ON i.id = m.item_id
ORDER BY m.item_id
LIMIT @take;
""";
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 5000));
        using var reader = command.ExecuteReader();
        var rows = new List<BattleMedicineBrowseRowDto>();
        while (reader.Read())
            rows.Add(ReadMedicineRow(reader));
        return rows;
    }

    public bool TryGetMedicineByItemId(long itemId, out BattleMedicineBrowseRowDto med)
    {
        med = default!;
        if (itemId <= 0)
            return false;
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT {SelectMedicineColumns}
FROM medicine m
JOIN items i ON i.id = m.item_id
WHERE m.item_id = @itemId
LIMIT 1;
""";
        command.Parameters.AddWithValue("itemId", itemId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;
        med = ReadMedicineRow(reader);
        ApplyMedicineCombatDefaults(med);
        return true;
    }

    /// <summary>Same sentinel rules as <see cref="BattleWeaponDatabase.ApplyWeaponCombatDefaults"/> for shared req/AP fields.</summary>
    public static void ApplyMedicineCombatDefaults(BattleMedicineBrowseRowDto m)
    {
        if (m.AttackApCost < 0)
            m.AttackApCost = 1;
        if (m.ReqLevel < 0)
            m.ReqLevel = 0;
        if (m.ReqStrength < 0)
            m.ReqStrength = 0;
        if (m.ReqEndurance < 0)
            m.ReqEndurance = 0;
        if (m.ReqAccuracy < 0)
            m.ReqAccuracy = 0;
    }

    private static BattleMedicineBrowseRowDto ReadMedicineRow(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            IconKey = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Mass = reader.GetDouble(3),
            Quality = reader.GetInt32(4),
            Condition = reader.GetInt32(5),
            AttackApCost = reader.GetInt32(6),
            ReqLevel = reader.GetInt32(7),
            ReqStrength = reader.GetInt32(8),
            ReqEndurance = reader.GetInt32(9),
            ReqAccuracy = reader.GetInt32(10),
            ReqMasteryCategory = reader.IsDBNull(11) ? "" : reader.GetString(11),
            EffectType = reader.IsDBNull(12) ? "" : reader.GetString(12),
            EffectSign = reader.IsDBNull(13) ? "positive" : reader.GetString(13),
            EffectMin = reader.GetInt32(14),
            EffectMax = reader.GetInt32(15),
            EffectTarget = reader.IsDBNull(16) ? "enemy" : reader.GetString(16),
            InventorySlotWidth = reader.GetInt32(17),
            InventoryGrid = reader.GetInt32(18),
            IsEquippable = reader.GetBoolean(19),
            ItemType = BattleWeaponDatabase.NormalizeItemType(reader.GetString(20), "medicine")
        };

    public void UpsertMedicine(ItemCatalogUpsertDto d)
    {
        if (string.IsNullOrWhiteSpace(d.Name))
            throw new ArgumentException("Item name is required.", nameof(d));

        string ik = string.IsNullOrWhiteSpace(d.IconKey) ? d.Name.Trim().ToLowerInvariant() : d.IconKey.Trim().ToLowerInvariant();
        int invW = d.InventorySlotWidth >= 2 ? 2 : 1;
        string effectType = (d.EffectType ?? "").Trim().ToLowerInvariant();
        string effectSign = (d.EffectSign ?? "positive").Trim().ToLowerInvariant();
        string effectTarget = (d.EffectTarget ?? "enemy").Trim().ToLowerInvariant();
        int effectMin = d.EffectMin;
        int effectMax = d.EffectMax;
        if (effectMin > effectMax)
            (effectMin, effectMax) = (effectMax, effectMin);

        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        long itemId = UpsertItemForMedicine(connection, tx, d);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
INSERT INTO medicine (
    item_id, attack_ap_cost, req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
    effect_type, effect_sign, effect_min, effect_max, effect_target, inventory_slot_width)
VALUES (
    @itemId, @attackAp, @reqLevel, @reqStr, @reqEnd, @reqAcc, @reqMastery,
    @effectType, @effectSign, @effectMin, @effectMax, @effectTarget, @invW)
ON CONFLICT (item_id) DO UPDATE SET
    attack_ap_cost = EXCLUDED.attack_ap_cost,
    req_level = EXCLUDED.req_level,
    req_strength = EXCLUDED.req_strength,
    req_endurance = EXCLUDED.req_endurance,
    req_accuracy = EXCLUDED.req_accuracy,
    req_mastery_category = EXCLUDED.req_mastery_category,
    effect_type = EXCLUDED.effect_type,
    effect_sign = EXCLUDED.effect_sign,
    effect_min = EXCLUDED.effect_min,
    effect_max = EXCLUDED.effect_max,
    effect_target = EXCLUDED.effect_target,
    inventory_slot_width = EXCLUDED.inventory_slot_width;
""";
        command.Parameters.AddWithValue("itemId", itemId);
        command.Parameters.AddWithValue("attackAp", StoreAttackApCostForDb(d.AttackApCost));
        command.Parameters.AddWithValue("reqLevel", StoreReqLevelForDb(d.ReqLevel));
        command.Parameters.AddWithValue("reqStr", StoreReqStatForDb(d.ReqStrength));
        command.Parameters.AddWithValue("reqEnd", StoreReqStatForDb(d.ReqEndurance));
        command.Parameters.AddWithValue("reqAcc", StoreReqStatForDb(d.ReqAccuracy));
        command.Parameters.AddWithValue("reqMastery", d.ReqMasteryCategory ?? "");
        command.Parameters.AddWithValue("effectType", effectType);
        command.Parameters.AddWithValue("effectSign", effectSign);
        command.Parameters.AddWithValue("effectMin", effectMin);
        command.Parameters.AddWithValue("effectMax", effectMax);
        command.Parameters.AddWithValue("effectTarget", effectTarget);
        command.Parameters.AddWithValue("invW", invW);
        command.ExecuteNonQuery();
        tx.Commit();
    }

    private static int StoreAttackApCostForDb(int v) => v < 0 ? -1 : Math.Max(1, v);

    private static int StoreReqLevelForDb(int v) => v < 0 ? -1 : Math.Max(0, v);

    private static int StoreReqStatForDb(int v) => v < 0 ? -1 : Math.Max(0, v);

    private static long UpsertItemForMedicine(NpgsqlConnection connection, NpgsqlTransaction tx, ItemCatalogUpsertDto d)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        if (d.ItemId > 0)
        {
            cmd.CommandText = """
UPDATE items
SET name = @name,
    mass = @mass,
    quality = @quality,
    condition = @condition,
    icon_key = @iconKey,
    type = 'medicine',
    is_equippable = @isEquippable,
    inventorygrid = @inventorygrid,
    category = @category
WHERE id = @itemId
RETURNING id;
""";
            cmd.Parameters.AddWithValue("itemId", d.ItemId);
            cmd.Parameters.AddWithValue("name", d.Name.Trim());
            cmd.Parameters.AddWithValue("mass", Math.Max(0.0, d.Mass));
            cmd.Parameters.AddWithValue("quality", Math.Clamp(d.Quality, 0, 9999));
            cmd.Parameters.AddWithValue("condition", Math.Clamp(d.Condition, 0, 9999));
            string ik = string.IsNullOrWhiteSpace(d.IconKey) ? d.Name.Trim().ToLowerInvariant() : d.IconKey.Trim().ToLowerInvariant();
            cmd.Parameters.AddWithValue("iconKey", ik);
            cmd.Parameters.AddWithValue("isEquippable", d.IsEquippable);
            cmd.Parameters.AddWithValue("inventorygrid", Math.Clamp(d.InventoryGrid, 0, 2));
            cmd.Parameters.AddWithValue("category", (d.Category ?? "").Trim());
            object? scalar = cmd.ExecuteScalar();
            if (scalar != null && scalar != DBNull.Value)
                return scalar is long id ? id : Convert.ToInt64(scalar);

            // Admin "next id" is reserved sequence value — row may not exist yet; insert with explicit id.
            cmd.Parameters.Clear();
            cmd.CommandText = """
INSERT INTO items (id, name, mass, quality, condition, icon_key, type, is_equippable, inventorygrid, category)
VALUES (@itemId, @name, @mass, @quality, @condition, @iconKey, 'medicine', @isEquippable, @inventorygrid, @category)
RETURNING id;
""";
            cmd.Parameters.AddWithValue("itemId", d.ItemId);
            cmd.Parameters.AddWithValue("name", d.Name.Trim());
            cmd.Parameters.AddWithValue("mass", Math.Max(0.0, d.Mass));
            cmd.Parameters.AddWithValue("quality", Math.Clamp(d.Quality, 0, 9999));
            cmd.Parameters.AddWithValue("condition", Math.Clamp(d.Condition, 0, 9999));
            string ikIns = string.IsNullOrWhiteSpace(d.IconKey) ? d.Name.Trim().ToLowerInvariant() : d.IconKey.Trim().ToLowerInvariant();
            cmd.Parameters.AddWithValue("iconKey", ikIns);
            cmd.Parameters.AddWithValue("isEquippable", d.IsEquippable);
            cmd.Parameters.AddWithValue("inventorygrid", Math.Clamp(d.InventoryGrid, 0, 2));
            cmd.Parameters.AddWithValue("category", (d.Category ?? "").Trim());
            object? inserted = cmd.ExecuteScalar();
            if (inserted is long newItemId)
                return newItemId;
            if (inserted != null && inserted != DBNull.Value)
                return Convert.ToInt64(inserted);
            throw new InvalidOperationException("Medicine item insert with id failed.");
        }

        cmd.CommandText = """
INSERT INTO items (name, mass, quality, condition, icon_key, type, is_equippable, inventorygrid, category)
VALUES (@name, @mass, @quality, @condition, @iconKey, 'medicine', @isEquippable, @inventorygrid, @category)
RETURNING id;
""";
        cmd.Parameters.AddWithValue("name", d.Name.Trim());
        cmd.Parameters.AddWithValue("mass", Math.Max(0.0, d.Mass));
        cmd.Parameters.AddWithValue("quality", Math.Clamp(d.Quality, 0, 9999));
        cmd.Parameters.AddWithValue("condition", Math.Clamp(d.Condition, 0, 9999));
        string ik2 = string.IsNullOrWhiteSpace(d.IconKey) ? d.Name.Trim().ToLowerInvariant() : d.IconKey.Trim().ToLowerInvariant();
        cmd.Parameters.AddWithValue("iconKey", ik2);
        cmd.Parameters.AddWithValue("isEquippable", d.IsEquippable);
        cmd.Parameters.AddWithValue("inventorygrid", Math.Clamp(d.InventoryGrid, 0, 2));
        cmd.Parameters.AddWithValue("category", (d.Category ?? "").Trim());
        object? s2 = cmd.ExecuteScalar();
        if (s2 is long nid)
            return nid;
        if (s2 != null)
            return Convert.ToInt64(s2);
        throw new InvalidOperationException("Failed to insert medicine item.");
    }

    public bool TryDeleteMedicineByItemId(long itemId, out string? error)
    {
        error = null;
        if (itemId <= 0)
        {
            error = "item id required";
            return false;
        }

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM medicine WHERE item_id = @itemId;";
        command.Parameters.AddWithValue("itemId", itemId);
        int n = command.ExecuteNonQuery();
        if (n == 0)
        {
            error = "medicine row not found";
            return false;
        }

        return true;
    }
}
