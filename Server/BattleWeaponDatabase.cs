using System.Globalization;
using System.Text;
using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public sealed class BattleWeaponDatabase
{
    private readonly BattlePostgresDatabase _database;

    /// <summary>Canonical weapon <c>category</c> codes (DB + API). Order is stable for admin UI.</summary>
    public static readonly string[] CanonicalWeaponCategories =
    [
        "cold",
        "light",
        "medium",
        "heavy",
        "throwing",
        "medicine"
    ];

    public BattleWeaponDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    private const string SelectWeaponColumns = """
w.id, w.item_id, w.code, COALESCE(i.name, w.name) AS name, w.damage, w.range, COALESCE(i.icon_key, w.icon_key) AS icon_key, w.attack_ap_cost, w.spread_penalty, w.trajectory_height, COALESCE(i.quality, w.quality) AS quality, COALESCE(i.condition, w.weapon_condition) AS weapon_condition, w.is_sniper,
COALESCE(i.mass, w.mass) AS mass, w.caliber, w.armor_pierce, w.magazine_size, w.reload_ap_cost, w.category,
w.req_level, w.req_strength, w.req_endurance, w.req_accuracy, w.req_mastery_category,
w.stat_effect_strength, w.stat_effect_endurance, w.stat_effect_accuracy,
w.damage_type, w.damage_min, w.damage_max, w.burst_rounds, w.burst_ap_cost, w.inventory_slot_width, COALESCE(i.inventorygrid, 1) AS inventorygrid,
w.effect_type, w.effect_sign, w.effect_min, w.effect_max, w.effect_target, COALESCE(i.is_equippable, FALSE) AS is_equippable
""";

    public IReadOnlyList<BattleWeaponBrowseRowDto> ListWeapons(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT {SelectWeaponColumns}
FROM weapons w
LEFT JOIN items i ON i.id = w.item_id
ORDER BY w.id
LIMIT @take;
""";
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

        using var reader = command.ExecuteReader();
        var rows = new List<BattleWeaponBrowseRowDto>();
        while (reader.Read())
        {
            rows.Add(ReadWeaponRow(reader));
        }

        return rows;
    }

    public bool TryGetWeaponByCode(string code, out BattleWeaponBrowseRowDto weapon)
    {
        weapon = DefaultFistRow();
        if (string.IsNullOrWhiteSpace(code))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT {SelectWeaponColumns}
FROM weapons w
LEFT JOIN items i ON i.id = w.item_id
WHERE w.code = @code
LIMIT 1;
""";
        command.Parameters.AddWithValue("code", code.Trim().ToLowerInvariant());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        weapon = ReadWeaponRow(reader);
        ApplyWeaponCombatDefaults(weapon);
        return true;
    }

    /// <summary>
    /// DB may store <c>-1</c> on numeric fields meaning "not applicable" (e.g. knife: no magazine, no spread).
    /// Combat and equip paths expect non-negative / clamped values — apply defaults in one place.
    /// Admin list/export still uses raw rows from <see cref="ReadWeaponRow"/> without this step.
    /// </summary>
    public static void ApplyWeaponCombatDefaults(BattleWeaponBrowseRowDto w)
    {
        if (w.Range < 0)
            w.Range = 1;
        if (w.AttackApCost < 0)
            w.AttackApCost = 1;
        if (w.Tightness < 0)
            w.Tightness = 1.0;
        if (w.TrajectoryHeight < 0)
            w.TrajectoryHeight = 0;
        if (w.ArmorPierce < 0)
            w.ArmorPierce = 0;
        if (w.MagazineSize < 0)
            w.MagazineSize = 0;
        if (w.ReloadApCost < 0)
            w.ReloadApCost = 0;
        if (w.BurstRounds < 0)
            w.BurstRounds = 0;
        if (w.BurstApCost < 0)
            w.BurstApCost = 0;
        if (w.ReqLevel < 0)
            w.ReqLevel = 0;
        if (w.ReqStrength < 0)
            w.ReqStrength = 0;
        if (w.ReqEndurance < 0)
            w.ReqEndurance = 0;
        if (w.ReqAccuracy < 0)
            w.ReqAccuracy = 0;
        // Only -1 is N/A; other negatives may be intentional stat penalties from the weapon.
        if (w.StatEffectStrength == -1)
            w.StatEffectStrength = 0;
        if (w.StatEffectEndurance == -1)
            w.StatEffectEndurance = 0;
        if (w.StatEffectAccuracy == -1)
            w.StatEffectAccuracy = 0;
    }

    private static int StoreWeaponRangeForDb(int r) => r < 0 ? -1 : Math.Max(0, r);

    private static int StoreAttackApCostForDb(int v) => v < 0 ? -1 : Math.Max(1, v);

    private static double StoreTightnessForDb(double v) => v < 0 ? -1.0 : Math.Clamp(v, 0.0, 1.0);

    private static int StoreTrajectoryHeightForDb(int v) => v < 0 ? -1 : Math.Clamp(v, 0, 3);

    private static int StoreNonNegativeOrSentinelForDb(int v) => v < 0 ? -1 : Math.Max(0, v);

    private static int StoreReqLevelForDb(int v) => v < 0 ? -1 : Math.Max(0, v);

    private static int StoreReqStatForDb(int v) => v < 0 ? -1 : Math.Max(0, v);

    private static int StoreStatEffectForDb(int v) => v == -1 ? -1 : v;

    private static BattleWeaponBrowseRowDto ReadWeaponRow(NpgsqlDataReader reader)
    {
        int legacyDamage = reader.GetInt32(4);
        int dMin = reader.GetInt32(28);
        int dMax = reader.GetInt32(29);
        if (dMin == 1 && dMax == 1 && legacyDamage > 1)
        {
            dMin = Math.Max(0, legacyDamage);
            dMax = Math.Max(0, legacyDamage);
        }

        return new BattleWeaponBrowseRowDto
        {
            Id = reader.GetInt64(0),
            Code = reader.GetString(2),
            Name = reader.GetString(3),
            DamageMin = dMin,
            DamageMax = dMax,
            Range = reader.GetInt32(5),
            IconKey = reader.GetString(6),
            AttackApCost = reader.GetInt32(7),
            Tightness = reader.GetDouble(8),
            TrajectoryHeight = reader.GetInt32(9),
            Quality = reader.GetInt32(10),
            WeaponCondition = reader.GetInt32(11),
            IsSniper = reader.GetBoolean(12),
            Mass = reader.GetDouble(13),
            Caliber = reader.GetString(14),
            ArmorPierce = reader.GetInt32(15),
            MagazineSize = reader.GetInt32(16),
            ReloadApCost = reader.GetInt32(17),
            Category = reader.GetString(18),
            ReqLevel = reader.GetInt32(19),
            ReqStrength = reader.GetInt32(20),
            ReqEndurance = reader.GetInt32(21),
            ReqAccuracy = reader.GetInt32(22),
            ReqMasteryCategory = reader.GetString(23),
            StatEffectStrength = reader.GetInt32(24),
            StatEffectEndurance = reader.GetInt32(25),
            StatEffectAccuracy = reader.GetInt32(26),
            DamageType = reader.GetString(27),
            BurstRounds = reader.GetInt32(30),
            BurstApCost = reader.GetInt32(31),
            InventorySlotWidth = reader.GetInt32(32),
            InventoryGrid = reader.GetInt32(33),
            EffectType = reader.GetString(34),
            EffectSign = reader.GetString(35),
            EffectMin = reader.GetInt32(36),
            EffectMax = reader.GetInt32(37),
            EffectTarget = reader.GetString(38),
            IsEquippable = reader.GetBoolean(39)
        };
    }

    private static BattleWeaponBrowseRowDto DefaultFistRow() =>
        new()
        {
            Code = "fist",
            Name = "Fist",
            DamageMin = 1,
            DamageMax = 1,
            Range = 1,
            IconKey = "fist",
            AttackApCost = 3,
            Tightness = 1.0,
            TrajectoryHeight = 1,
            Quality = 100,
            WeaponCondition = 100,
            IsSniper = false,
            Category = "cold",
            DamageType = "physical",
            InventorySlotWidth = 1,
            EffectSign = "positive",
            EffectTarget = "enemy",
            IsEquippable = true
        };

    private static int StoreInventorySlotWidthForDb(int w) => w >= 2 ? 2 : 1;

    public void UpsertWeapon(BattleWeaponUpsertDto d)
    {
        if (string.IsNullOrWhiteSpace(d.Code))
            throw new ArgumentException("Weapon code is required.", nameof(d));
        if (string.IsNullOrWhiteSpace(d.Name))
            throw new ArgumentException("Weapon name is required.", nameof(d));

        int dMin = Math.Max(0, d.DamageMin);
        int dMax = Math.Max(0, d.DamageMax);
        if (dMin > dMax)
            (dMin, dMax) = (dMax, dMin);

        string ik = string.IsNullOrWhiteSpace(d.IconKey) ? d.Code.Trim().ToLowerInvariant() : d.IconKey.Trim().ToLowerInvariant();
        string category = NormalizeCategory(d.Category);
        int rangeDb = category == "cold" ? 1 : StoreWeaponRangeForDb(d.Range);
        int ac = StoreAttackApCostForDb(d.AttackApCost);
        double tn = StoreTightnessForDb(d.Tightness);
        int th = StoreTrajectoryHeightForDb(d.TrajectoryHeight);
        int legacyDamage = dMax;

        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        long itemId = UpsertItemForWeapon(connection, tx, d);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
INSERT INTO weapons (
    item_id, code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
    mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category,
    req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
    stat_effect_strength, stat_effect_endurance, stat_effect_accuracy,
    damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost, inventory_slot_width,
    effect_type, effect_sign, effect_min, effect_max, effect_target)
VALUES (
    @itemId, @code, @name, @damage, @range, @iconKey, @attackApCost, @spreadPenalty, @trajectoryHeight, @quality, @weaponCondition, @isSniper,
    @mass, @caliber, @armorPierce, @magazineSize, @reloadApCost, @category,
    @reqLevel, @reqStrength, @reqEndurance, @reqAccuracy, @reqMasteryCategory,
    @statEffectStrength, @statEffectEndurance, @statEffectAccuracy,
    @damageType, @damageMin, @damageMax, @burstRounds, @burstApCost, @inventorySlotWidth,
    @effectType, @effectSign, @effectMin, @effectMax, @effectTarget)
ON CONFLICT (code) DO UPDATE
SET item_id = EXCLUDED.item_id,
    name = EXCLUDED.name,
    damage = EXCLUDED.damage,
    range = EXCLUDED.range,
    icon_key = EXCLUDED.icon_key,
    attack_ap_cost = EXCLUDED.attack_ap_cost,
    spread_penalty = EXCLUDED.spread_penalty,
    trajectory_height = EXCLUDED.trajectory_height,
    quality = EXCLUDED.quality,
    weapon_condition = EXCLUDED.weapon_condition,
    is_sniper = EXCLUDED.is_sniper,
    mass = EXCLUDED.mass,
    caliber = EXCLUDED.caliber,
    armor_pierce = EXCLUDED.armor_pierce,
    magazine_size = EXCLUDED.magazine_size,
    reload_ap_cost = EXCLUDED.reload_ap_cost,
    category = EXCLUDED.category,
    req_level = EXCLUDED.req_level,
    req_strength = EXCLUDED.req_strength,
    req_endurance = EXCLUDED.req_endurance,
    req_accuracy = EXCLUDED.req_accuracy,
    req_mastery_category = EXCLUDED.req_mastery_category,
    stat_effect_strength = EXCLUDED.stat_effect_strength,
    stat_effect_endurance = EXCLUDED.stat_effect_endurance,
    stat_effect_accuracy = EXCLUDED.stat_effect_accuracy,
    damage_type = EXCLUDED.damage_type,
    damage_min = EXCLUDED.damage_min,
    damage_max = EXCLUDED.damage_max,
    burst_rounds = EXCLUDED.burst_rounds,
    burst_ap_cost = EXCLUDED.burst_ap_cost,
    inventory_slot_width = EXCLUDED.inventory_slot_width,
    effect_type = EXCLUDED.effect_type,
    effect_sign = EXCLUDED.effect_sign,
    effect_min = EXCLUDED.effect_min,
    effect_max = EXCLUDED.effect_max,
    effect_target = EXCLUDED.effect_target;
""";
        int invW = StoreInventorySlotWidthForDb(d.InventorySlotWidth);
        command.Parameters.AddWithValue("itemId", itemId);
        command.Parameters.AddWithValue("code", d.Code.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("name", d.Name.Trim());
        command.Parameters.AddWithValue("damage", legacyDamage);
        command.Parameters.AddWithValue("range", rangeDb);
        command.Parameters.AddWithValue("iconKey", ik);
        command.Parameters.AddWithValue("attackApCost", ac);
        command.Parameters.AddWithValue("spreadPenalty", tn);
        command.Parameters.AddWithValue("trajectoryHeight", th);
        command.Parameters.AddWithValue("quality", Math.Clamp(d.Quality, 0, 9999));
        command.Parameters.AddWithValue("weaponCondition", Math.Clamp(d.WeaponCondition, 0, 9999));
        command.Parameters.AddWithValue("isSniper", d.IsSniper);
        command.Parameters.AddWithValue("mass", d.Mass);
        command.Parameters.AddWithValue("caliber", d.Caliber ?? "");
        command.Parameters.AddWithValue("armorPierce", StoreNonNegativeOrSentinelForDb(d.ArmorPierce));
        command.Parameters.AddWithValue("magazineSize", StoreNonNegativeOrSentinelForDb(d.MagazineSize));
        command.Parameters.AddWithValue("reloadApCost", StoreNonNegativeOrSentinelForDb(d.ReloadApCost));
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("reqLevel", StoreReqLevelForDb(d.ReqLevel));
        command.Parameters.AddWithValue("reqStrength", StoreReqStatForDb(d.ReqStrength));
        command.Parameters.AddWithValue("reqEndurance", StoreReqStatForDb(d.ReqEndurance));
        command.Parameters.AddWithValue("reqAccuracy", StoreReqStatForDb(d.ReqAccuracy));
        command.Parameters.AddWithValue("reqMasteryCategory", d.ReqMasteryCategory ?? "");
        command.Parameters.AddWithValue("statEffectStrength", StoreStatEffectForDb(d.StatEffectStrength));
        command.Parameters.AddWithValue("statEffectEndurance", StoreStatEffectForDb(d.StatEffectEndurance));
        command.Parameters.AddWithValue("statEffectAccuracy", StoreStatEffectForDb(d.StatEffectAccuracy));
        command.Parameters.AddWithValue("damageType", string.IsNullOrWhiteSpace(d.DamageType) ? "physical" : d.DamageType.Trim());
        command.Parameters.AddWithValue("damageMin", dMin);
        command.Parameters.AddWithValue("damageMax", dMax);
        command.Parameters.AddWithValue("burstRounds", StoreNonNegativeOrSentinelForDb(d.BurstRounds));
        command.Parameters.AddWithValue("burstApCost", StoreNonNegativeOrSentinelForDb(d.BurstApCost));
        command.Parameters.AddWithValue("inventorySlotWidth", invW);
        string effectType = (d.EffectType ?? "").Trim().ToLowerInvariant();
        string effectSign = (d.EffectSign ?? "positive").Trim().ToLowerInvariant();
        string effectTarget = (d.EffectTarget ?? "enemy").Trim().ToLowerInvariant();
        int effectMin = d.EffectMin;
        int effectMax = d.EffectMax;
        if (effectMin > effectMax)
            (effectMin, effectMax) = (effectMax, effectMin);
        if (category == "medicine")
        {
            effectType = "hp";
            effectSign = "positive";
            effectTarget = "self";
            rangeDb = 0;
            effectMin = Math.Max(0, effectMin);
            effectMax = Math.Max(effectMin, effectMax);
        }
        command.Parameters.AddWithValue("effectType", effectType);
        command.Parameters.AddWithValue("effectSign", effectSign);
        command.Parameters.AddWithValue("effectMin", effectMin);
        command.Parameters.AddWithValue("effectMax", effectMax);
        command.Parameters.AddWithValue("effectTarget", effectTarget);
        command.ExecuteNonQuery();
        tx.Commit();
    }

    public BattleWeaponMetaDto GetWeaponMeta()
    {
        using var connection = _database.DataSource.OpenConnection();
        var fromDb = ListDistinctStrings(connection, "category");
        return new BattleWeaponMetaDto
        {
            DamageTypes = ListDistinctStrings(connection, "damage_type"),
            Categories = MergeCanonicalCategories(fromDb)
        };
    }

    /// <summary>Canonical categories first (fixed order), then any other distinct values from DB.</summary>
    private static List<string> MergeCanonicalCategories(IReadOnlyList<string> fromDb)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string c in CanonicalWeaponCategories)
        {
            if (seen.Add(c))
                list.Add(c);
        }

        foreach (string c in fromDb)
        {
            if (string.IsNullOrWhiteSpace(c))
                continue;
            if (seen.Add(c))
                list.Add(c);
        }

        return list;
    }

    private static string NormalizeCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "cold";
        string t = raw.Trim();
        foreach (string c in CanonicalWeaponCategories)
        {
            if (c.Equals(t, StringComparison.OrdinalIgnoreCase))
                return c;
        }

        return t;
    }

    private static IReadOnlyList<string> ListDistinctStrings(NpgsqlConnection connection, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT DISTINCT {column}
FROM weapons
ORDER BY 1;
""";
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                list.Add(reader.GetString(0));
        }

        return list;
    }

    /// <summary>Removes a weapon row. <c>fist</c> cannot be deleted.</summary>
    public bool TryDeleteWeaponByCode(string code, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(code))
        {
            error = "code required";
            return false;
        }

        string c = code.Trim().ToLowerInvariant();
        if (c == "fist")
        {
            error = "cannot delete default weapon fist";
            return false;
        }

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM weapons WHERE code = @code;";
        command.Parameters.AddWithValue("code", c);
        int n = command.ExecuteNonQuery();
        if (n == 0)
        {
            error = "weapon not found";
            return false;
        }

        return true;
    }

    /// <summary>Truncates <c>weapons</c> and inserts the given rows (must include <c>fist</c>).</summary>
    public void ReplaceAllWeapons(IReadOnlyList<BattleWeaponUpsertDto> items)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("At least one weapon is required.", nameof(items));

        bool hasFist = false;
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i.Code) || string.IsNullOrWhiteSpace(i.Name))
                throw new ArgumentException("Each weapon must have code and name.", nameof(items));
            if (string.Equals(i.Code.Trim(), "fist", StringComparison.OrdinalIgnoreCase))
                hasFist = true;
        }

        if (!hasFist)
            throw new InvalidOperationException("Replacement set must include weapon code \"fist\".");

        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        using (var trunc = connection.CreateCommand())
        {
            trunc.Transaction = tx;
            trunc.CommandText = "TRUNCATE TABLE weapons RESTART IDENTITY;";
            trunc.ExecuteNonQuery();
        }

        foreach (var d in items)
            InsertWeapon(connection, tx, d);

        tx.Commit();
    }

    private static void InsertWeapon(NpgsqlConnection connection, NpgsqlTransaction tx, BattleWeaponUpsertDto d)
    {
        if (string.IsNullOrWhiteSpace(d.Code))
            throw new ArgumentException("Weapon code is required.", nameof(d));
        if (string.IsNullOrWhiteSpace(d.Name))
            throw new ArgumentException("Weapon name is required.", nameof(d));

        int dMin = Math.Max(0, d.DamageMin);
        int dMax = Math.Max(0, d.DamageMax);
        if (dMin > dMax)
            (dMin, dMax) = (dMax, dMin);

        string ik = string.IsNullOrWhiteSpace(d.IconKey) ? d.Code.Trim().ToLowerInvariant() : d.IconKey.Trim().ToLowerInvariant();
        string category = NormalizeCategory(d.Category);
        int rangeDb = category == "cold" ? 1 : StoreWeaponRangeForDb(d.Range);
        int ac = StoreAttackApCostForDb(d.AttackApCost);
        double tn = StoreTightnessForDb(d.Tightness);
        int th = StoreTrajectoryHeightForDb(d.TrajectoryHeight);
        int legacyDamage = dMax;

        long itemId = UpsertItemForWeapon(connection, tx, d);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
INSERT INTO weapons (
    item_id, code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
    mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category,
    req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
    stat_effect_strength, stat_effect_endurance, stat_effect_accuracy,
    damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost, inventory_slot_width,
    effect_type, effect_sign, effect_min, effect_max, effect_target)
VALUES (
    @itemId, @code, @name, @damage, @range, @iconKey, @attackApCost, @spreadPenalty, @trajectoryHeight, @quality, @weaponCondition, @isSniper,
    @mass, @caliber, @armorPierce, @magazineSize, @reloadApCost, @category,
    @reqLevel, @reqStrength, @reqEndurance, @reqAccuracy, @reqMasteryCategory,
    @statEffectStrength, @statEffectEndurance, @statEffectAccuracy,
    @damageType, @damageMin, @damageMax, @burstRounds, @burstApCost, @inventorySlotWidth,
    @effectType, @effectSign, @effectMin, @effectMax, @effectTarget);
""";
        int invWInsert = StoreInventorySlotWidthForDb(d.InventorySlotWidth);
        command.Parameters.AddWithValue("itemId", itemId);
        command.Parameters.AddWithValue("code", d.Code.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("name", d.Name.Trim());
        command.Parameters.AddWithValue("damage", legacyDamage);
        command.Parameters.AddWithValue("range", rangeDb);
        command.Parameters.AddWithValue("iconKey", ik);
        command.Parameters.AddWithValue("attackApCost", ac);
        command.Parameters.AddWithValue("spreadPenalty", tn);
        command.Parameters.AddWithValue("trajectoryHeight", th);
        command.Parameters.AddWithValue("quality", Math.Clamp(d.Quality, 0, 9999));
        command.Parameters.AddWithValue("weaponCondition", Math.Clamp(d.WeaponCondition, 0, 9999));
        command.Parameters.AddWithValue("isSniper", d.IsSniper);
        command.Parameters.AddWithValue("mass", d.Mass);
        command.Parameters.AddWithValue("caliber", d.Caliber ?? "");
        command.Parameters.AddWithValue("armorPierce", StoreNonNegativeOrSentinelForDb(d.ArmorPierce));
        command.Parameters.AddWithValue("magazineSize", StoreNonNegativeOrSentinelForDb(d.MagazineSize));
        command.Parameters.AddWithValue("reloadApCost", StoreNonNegativeOrSentinelForDb(d.ReloadApCost));
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("reqLevel", StoreReqLevelForDb(d.ReqLevel));
        command.Parameters.AddWithValue("reqStrength", StoreReqStatForDb(d.ReqStrength));
        command.Parameters.AddWithValue("reqEndurance", StoreReqStatForDb(d.ReqEndurance));
        command.Parameters.AddWithValue("reqAccuracy", StoreReqStatForDb(d.ReqAccuracy));
        command.Parameters.AddWithValue("reqMasteryCategory", d.ReqMasteryCategory ?? "");
        command.Parameters.AddWithValue("statEffectStrength", StoreStatEffectForDb(d.StatEffectStrength));
        command.Parameters.AddWithValue("statEffectEndurance", StoreStatEffectForDb(d.StatEffectEndurance));
        command.Parameters.AddWithValue("statEffectAccuracy", StoreStatEffectForDb(d.StatEffectAccuracy));
        command.Parameters.AddWithValue("damageType", string.IsNullOrWhiteSpace(d.DamageType) ? "physical" : d.DamageType.Trim());
        command.Parameters.AddWithValue("damageMin", dMin);
        command.Parameters.AddWithValue("damageMax", dMax);
        command.Parameters.AddWithValue("burstRounds", StoreNonNegativeOrSentinelForDb(d.BurstRounds));
        command.Parameters.AddWithValue("burstApCost", StoreNonNegativeOrSentinelForDb(d.BurstApCost));
        command.Parameters.AddWithValue("inventorySlotWidth", invWInsert);
        string effectType = (d.EffectType ?? "").Trim().ToLowerInvariant();
        string effectSign = (d.EffectSign ?? "positive").Trim().ToLowerInvariant();
        string effectTarget = (d.EffectTarget ?? "enemy").Trim().ToLowerInvariant();
        int effectMin = d.EffectMin;
        int effectMax = d.EffectMax;
        if (effectMin > effectMax)
            (effectMin, effectMax) = (effectMax, effectMin);
        if (category == "medicine")
        {
            effectType = "hp";
            effectSign = "positive";
            effectTarget = "self";
            rangeDb = 0;
            effectMin = Math.Max(0, effectMin);
            effectMax = Math.Max(effectMin, effectMax);
        }
        command.Parameters.AddWithValue("effectType", effectType);
        command.Parameters.AddWithValue("effectSign", effectSign);
        command.Parameters.AddWithValue("effectMin", effectMin);
        command.Parameters.AddWithValue("effectMax", effectMax);
        command.Parameters.AddWithValue("effectTarget", effectTarget);
        command.ExecuteNonQuery();
    }

    private static long UpsertItemForWeapon(NpgsqlConnection connection, NpgsqlTransaction tx, BattleWeaponUpsertDto d)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
INSERT INTO items (name, mass, quality, condition, icon_key, type, is_equippable, inventorygrid)
VALUES (@name, @mass, @quality, @condition, @iconKey, 'weapon', @isEquippable, @inventorygrid)
ON CONFLICT DO NOTHING;
SELECT id FROM items
WHERE type = 'weapon' AND name = @name AND icon_key = @iconKey
ORDER BY id DESC
LIMIT 1;
""";
        cmd.Parameters.AddWithValue("name", d.Name.Trim());
        cmd.Parameters.AddWithValue("mass", d.Mass);
        cmd.Parameters.AddWithValue("quality", Math.Clamp(d.Quality, 0, 9999));
        cmd.Parameters.AddWithValue("condition", Math.Clamp(d.WeaponCondition, 0, 9999));
        string ik = string.IsNullOrWhiteSpace(d.IconKey) ? d.Code.Trim().ToLowerInvariant() : d.IconKey.Trim().ToLowerInvariant();
        cmd.Parameters.AddWithValue("iconKey", ik);
        cmd.Parameters.AddWithValue("isEquippable", d.IsEquippable);
        cmd.Parameters.AddWithValue("inventorygrid", Math.Clamp(d.InventoryGrid, 0, 2));
        object? scalar = cmd.ExecuteScalar();
        if (scalar is long id)
            return id;
        if (scalar != null)
            return Convert.ToInt64(scalar);
        throw new InvalidOperationException("Failed to upsert item for weapon.");
    }

    /// <summary>SQL script: <c>BEGIN; TRUNCATE; INSERT...; COMMIT;</c> — same shape as <see cref="ImportWeaponsSqlScript"/> expects.</summary>
    public string BuildWeaponsSqlExportScript()
    {
        var rows = ListWeapons(500);
        var sb = new StringBuilder();
        sb.AppendLine("-- Hope weapons table export (TRUNCATE + INSERT). Execute on the same schema.");
        sb.AppendLine("BEGIN;");
        sb.AppendLine("TRUNCATE TABLE weapons RESTART IDENTITY;");
        foreach (var w in rows)
        {
            sb.Append("INSERT INTO weapons (");
            sb.Append("code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper, ");
            sb.Append("mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category, ");
            sb.Append("req_level, req_strength, req_endurance, req_accuracy, req_mastery_category, ");
            sb.Append("stat_effect_strength, stat_effect_endurance, stat_effect_accuracy, ");
            sb.Append("damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost, inventory_slot_width) VALUES (");
            sb.Append(SqlString(w.Code)).Append(", ");
            sb.Append(SqlString(w.Name)).Append(", ");
            sb.Append(w.DamageMax.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.Range.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(SqlString(w.IconKey)).Append(", ");
            sb.Append(w.AttackApCost.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.Tightness.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.TrajectoryHeight.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.Quality.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.WeaponCondition.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.IsSniper ? "TRUE" : "FALSE").Append(", ");
            sb.Append(w.Mass.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(SqlString(w.Caliber)).Append(", ");
            sb.Append(w.ArmorPierce.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.MagazineSize.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.ReloadApCost.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(SqlString(w.Category)).Append(", ");
            sb.Append(w.ReqLevel.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.ReqStrength.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.ReqEndurance.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.ReqAccuracy.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(SqlString(w.ReqMasteryCategory)).Append(", ");
            sb.Append(w.StatEffectStrength.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.StatEffectEndurance.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.StatEffectAccuracy.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(SqlString(w.DamageType)).Append(", ");
            sb.Append(w.DamageMin.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.DamageMax.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.BurstRounds.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(w.BurstApCost.ToString(CultureInfo.InvariantCulture)).Append(", ");
            sb.Append(StoreInventorySlotWidthForDb(w.InventorySlotWidth).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(");");
        }

        sb.AppendLine("COMMIT;");
        return sb.ToString();
    }

    private static string SqlString(string? s)
    {
        string t = s ?? "";
        return "'" + t.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>Runs a script produced by this admin UI / <see cref="BuildWeaponsSqlExportScript"/> (BEGIN … COMMIT).</summary>
    public void ImportWeaponsSqlScript(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL script is empty.", nameof(sql));

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql.Trim();
        command.ExecuteNonQuery();
    }
}
