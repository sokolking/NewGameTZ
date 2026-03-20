using BattleServer.Models;

namespace BattleServer;

public sealed class BattleWeaponDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleWeaponDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    public IReadOnlyList<BattleWeaponBrowseRowDto> ListWeapons(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, code, name, damage, range, icon_key, attack_ap_cost
FROM weapons
ORDER BY id
LIMIT @take;
""";
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

        using var reader = command.ExecuteReader();
        var rows = new List<BattleWeaponBrowseRowDto>();
        while (reader.Read())
        {
            rows.Add(new BattleWeaponBrowseRowDto
            {
                Id = reader.GetInt64(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Damage = reader.GetInt32(3),
                Range = reader.GetInt32(4),
                IconKey = reader.GetString(5)
            });
        }

        return rows;
    }

    public bool TryGetWeaponByCode(string code, out BattleWeaponBrowseRowDto weapon)
    {
        weapon = new BattleWeaponBrowseRowDto
        {
            Code = "fist",
            Name = "Fist",
            Damage = 1,
            Range = 1,
            IconKey = "fist",
            AttackApCost = 3
        };
        if (string.IsNullOrWhiteSpace(code))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, code, name, damage, range, icon_key, attack_ap_cost
FROM weapons
WHERE code = @code
LIMIT 1;
""";
        command.Parameters.AddWithValue("code", code.Trim().ToLowerInvariant());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        weapon = new BattleWeaponBrowseRowDto
        {
            Id = reader.GetInt64(0),
            Code = reader.GetString(1),
            Name = reader.GetString(2),
            Damage = reader.GetInt32(3),
            Range = reader.GetInt32(4),
            IconKey = reader.GetString(5),
            AttackApCost = reader.GetInt32(6)
        };
        return true;
    }

    public void UpsertWeapon(string code, string name, int damage, int range, string? iconKey = null, int attackApCost = 1)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Weapon code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Weapon name is required.", nameof(name));

        string ik = string.IsNullOrWhiteSpace(iconKey) ? code.Trim().ToLowerInvariant() : iconKey.Trim().ToLowerInvariant();
        int ac = Math.Max(1, attackApCost);

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO weapons (code, name, damage, range, icon_key, attack_ap_cost)
VALUES (@code, @name, @damage, @range, @iconKey, @attackApCost)
ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name,
    damage = EXCLUDED.damage,
    range = EXCLUDED.range,
    icon_key = EXCLUDED.icon_key,
    attack_ap_cost = EXCLUDED.attack_ap_cost;
""";
        command.Parameters.AddWithValue("code", code.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("damage", Math.Max(0, damage));
        command.Parameters.AddWithValue("range", Math.Max(0, range));
        command.Parameters.AddWithValue("iconKey", ik);
        command.Parameters.AddWithValue("attackApCost", ac);
        command.ExecuteNonQuery();
    }
}
