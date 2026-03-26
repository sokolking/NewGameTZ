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
id, code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category,
req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
stat_effect_strength, stat_effect_endurance, stat_effect_accuracy,
damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost, inventory_slot_width
""";

    public IReadOnlyList<BattleWeaponBrowseRowDto> ListWeapons(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT {SelectWeaponColumns}
FROM weapons
ORDER BY id
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
FROM weapons
WHERE code = @code
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
        int legacyDamage = reader.GetInt32(3);
        int dMin = reader.GetInt32(27);
        int dMax = reader.GetInt32(28);
        if (dMin == 1 && dMax == 1 && legacyDamage > 1)
        {
            dMin = Math.Max(0, legacyDamage);
            dMax = Math.Max(0, legacyDamage);
        }

        return new BattleWeaponBrowseRowDto
        {
            Id = reader.GetInt64(0),
            Code = reader.GetString(1),
            Name = reader.GetString(2),
            DamageMin = dMin,
            DamageMax = dMax,
            Range = reader.GetInt32(4),
            IconKey = reader.GetString(5),
            AttackApCost = reader.GetInt32(6),
            Tightness = reader.GetDouble(7),
            TrajectoryHeight = reader.GetInt32(8),
            Quality = reader.GetInt32(9),
            WeaponCondition = reader.GetInt32(10),
            IsSniper = reader.GetBoolean(11),
            Mass = reader.GetDouble(12),
            Caliber = reader.GetString(13),
            ArmorPierce = reader.GetInt32(14),
            MagazineSize = reader.GetInt32(15),
            ReloadApCost = reader.GetInt32(16),
            Category = reader.GetString(17),
            ReqLevel = reader.GetInt32(18),
            ReqStrength = reader.GetInt32(19),
            ReqEndurance = reader.GetInt32(20),
            ReqAccuracy = reader.GetInt32(21),
            ReqMasteryCategory = reader.GetString(22),
            StatEffectStrength = reader.GetInt32(23),
            StatEffectEndurance = reader.GetInt32(24),
            StatEffectAccuracy = reader.GetInt32(25),
            DamageType = reader.GetString(26),
            BurstRounds = reader.GetInt32(29),
            BurstApCost = reader.GetInt32(30),
            InventorySlotWidth = reader.GetInt32(31)
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
            InventorySlotWidth = 1
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
        int rangeDb = StoreWeaponRangeForDb(d.Range);
        int ac = StoreAttackApCostForDb(d.AttackApCost);
        double tn = StoreTightnessForDb(d.Tightness);
        int th = StoreTrajectoryHeightForDb(d.TrajectoryHeight);
        int legacyDamage = dMax;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO weapons (
    code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
    mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category,
    req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
    stat_effect_strength, stat_effect_endurance, stat_effect_accuracy,
    damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost, inventory_slot_width)
VALUES (
    @code, @name, @damage, @range, @iconKey, @attackApCost, @spreadPenalty, @trajectoryHeight, @quality, @weaponCondition, @isSniper,
    @mass, @caliber, @armorPierce, @magazineSize, @reloadApCost, @category,
    @reqLevel, @reqStrength, @reqEndurance, @reqAccuracy, @reqMasteryCategory,
    @statEffectStrength, @statEffectEndurance, @statEffectAccuracy,
    @damageType, @damageMin, @damageMax, @burstRounds, @burstApCost, @inventorySlotWidth)
ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name,
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
    inventory_slot_width = EXCLUDED.inventory_slot_width;
""";
        int invW = StoreInventorySlotWidthForDb(d.InventorySlotWidth);
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
        command.Parameters.AddWithValue("category", NormalizeCategory(d.Category));
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
        command.ExecuteNonQuery();
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
        int rangeDb = StoreWeaponRangeForDb(d.Range);
        int ac = StoreAttackApCostForDb(d.AttackApCost);
        double tn = StoreTightnessForDb(d.Tightness);
        int th = StoreTrajectoryHeightForDb(d.TrajectoryHeight);
        int legacyDamage = dMax;

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
INSERT INTO weapons (
    code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
    mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category,
    req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
    stat_effect_strength, stat_effect_endurance, stat_effect_accuracy,
    damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost, inventory_slot_width)
VALUES (
    @code, @name, @damage, @range, @iconKey, @attackApCost, @spreadPenalty, @trajectoryHeight, @quality, @weaponCondition, @isSniper,
    @mass, @caliber, @armorPierce, @magazineSize, @reloadApCost, @category,
    @reqLevel, @reqStrength, @reqEndurance, @reqAccuracy, @reqMasteryCategory,
    @statEffectStrength, @statEffectEndurance, @statEffectAccuracy,
    @damageType, @damageMin, @damageMax, @burstRounds, @burstApCost, @inventorySlotWidth);
""";
        int invWInsert = StoreInventorySlotWidthForDb(d.InventorySlotWidth);
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
        command.Parameters.AddWithValue("category", NormalizeCategory(d.Category));
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
        command.ExecuteNonQuery();
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
