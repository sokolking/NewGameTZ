using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public sealed class BattleWeaponDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleWeaponDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    private const string SelectWeaponColumns = """
id, code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category,
req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
stat_effect_strength, stat_effect_endurance, stat_effect_accuracy,
damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost
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
        return true;
    }

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
            SpreadPenalty = reader.GetDouble(7),
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
            BurstApCost = reader.GetInt32(30)
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
            SpreadPenalty = 0,
            TrajectoryHeight = 1,
            Quality = 100,
            WeaponCondition = 100,
            IsSniper = false,
            Category = "cold",
            DamageType = "physical"
        };

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
        int ac = Math.Max(1, d.AttackApCost);
        double sp = Math.Clamp(d.SpreadPenalty, 0.0, 1.0);
        int th = Math.Clamp(d.TrajectoryHeight, 0, 3);
        int legacyDamage = dMax;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO weapons (
    code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
    mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category,
    req_level, req_strength, req_endurance, req_accuracy, req_mastery_category,
    stat_effect_strength, stat_effect_endurance, stat_effect_accuracy,
    damage_type, damage_min, damage_max, burst_rounds, burst_ap_cost)
VALUES (
    @code, @name, @damage, @range, @iconKey, @attackApCost, @spreadPenalty, @trajectoryHeight, @quality, @weaponCondition, @isSniper,
    @mass, @caliber, @armorPierce, @magazineSize, @reloadApCost, @category,
    @reqLevel, @reqStrength, @reqEndurance, @reqAccuracy, @reqMasteryCategory,
    @statEffectStrength, @statEffectEndurance, @statEffectAccuracy,
    @damageType, @damageMin, @damageMax, @burstRounds, @burstApCost)
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
    burst_ap_cost = EXCLUDED.burst_ap_cost;
""";
        command.Parameters.AddWithValue("code", d.Code.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("name", d.Name.Trim());
        command.Parameters.AddWithValue("damage", legacyDamage);
        command.Parameters.AddWithValue("range", Math.Max(0, d.Range));
        command.Parameters.AddWithValue("iconKey", ik);
        command.Parameters.AddWithValue("attackApCost", ac);
        command.Parameters.AddWithValue("spreadPenalty", sp);
        command.Parameters.AddWithValue("trajectoryHeight", th);
        command.Parameters.AddWithValue("quality", Math.Clamp(d.Quality, 0, 9999));
        command.Parameters.AddWithValue("weaponCondition", Math.Clamp(d.WeaponCondition, 0, 9999));
        command.Parameters.AddWithValue("isSniper", d.IsSniper);
        command.Parameters.AddWithValue("mass", d.Mass);
        command.Parameters.AddWithValue("caliber", d.Caliber ?? "");
        command.Parameters.AddWithValue("armorPierce", d.ArmorPierce);
        command.Parameters.AddWithValue("magazineSize", Math.Max(0, d.MagazineSize));
        command.Parameters.AddWithValue("reloadApCost", Math.Max(0, d.ReloadApCost));
        command.Parameters.AddWithValue("category", string.IsNullOrWhiteSpace(d.Category) ? "cold" : d.Category.Trim());
        command.Parameters.AddWithValue("reqLevel", Math.Max(0, d.ReqLevel));
        command.Parameters.AddWithValue("reqStrength", d.ReqStrength);
        command.Parameters.AddWithValue("reqEndurance", d.ReqEndurance);
        command.Parameters.AddWithValue("reqAccuracy", d.ReqAccuracy);
        command.Parameters.AddWithValue("reqMasteryCategory", d.ReqMasteryCategory ?? "");
        command.Parameters.AddWithValue("statEffectStrength", d.StatEffectStrength);
        command.Parameters.AddWithValue("statEffectEndurance", d.StatEffectEndurance);
        command.Parameters.AddWithValue("statEffectAccuracy", d.StatEffectAccuracy);
        command.Parameters.AddWithValue("damageType", string.IsNullOrWhiteSpace(d.DamageType) ? "physical" : d.DamageType.Trim());
        command.Parameters.AddWithValue("damageMin", dMin);
        command.Parameters.AddWithValue("damageMax", dMax);
        command.Parameters.AddWithValue("burstRounds", Math.Max(0, d.BurstRounds));
        command.Parameters.AddWithValue("burstApCost", Math.Max(0, d.BurstApCost));
        command.ExecuteNonQuery();
    }
}
