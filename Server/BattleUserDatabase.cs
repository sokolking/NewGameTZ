using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public sealed class BattleUserDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleUserDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    public bool ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT 1
FROM users
WHERE username = @username AND password = @password
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", username.Trim());
        command.Parameters.AddWithValue("password", password);
        return command.ExecuteScalar() != null;
    }

    public IReadOnlyList<BattleUserBrowseRowDto> ListUsers(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, username, password, max_hp, max_ap, weapon_code
FROM users
ORDER BY id
LIMIT @take;
""";
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

        using var reader = command.ExecuteReader();
        var rows = new List<BattleUserBrowseRowDto>();
        while (reader.Read())
        {
            rows.Add(new BattleUserBrowseRowDto
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                Password = reader.GetString(2),
                MaxHp = reader.GetInt32(3),
                MaxAp = reader.GetInt32(4),
                WeaponCode = reader.GetString(5)
            });
        }

        return rows;
    }

    public bool TryGetCombatProfile(string username, out int maxHp, out int maxAp, out string weaponCode)
    {
        maxHp = 10;
        maxAp = 100;
        weaponCode = "fist";
        if (string.IsNullOrWhiteSpace(username))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT max_hp, max_ap, weapon_code
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", username.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        maxHp = Math.Max(1, reader.GetInt32(0));
        maxAp = Math.Max(1, reader.GetInt32(1));
        weaponCode = reader.IsDBNull(2) ? "fist" : reader.GetString(2);
        if (string.IsNullOrWhiteSpace(weaponCode))
            weaponCode = "fist";
        return true;
    }

    /// <summary>12 ячеек (0..11) с привязкой к weapons.id; пустые — без оружия.</summary>
    public bool TryGetInventory(string username, string password, out List<UserInventorySlotDto> slots)
    {
        slots = new List<UserInventorySlotDto>();
        if (!ValidateCredentials(username, password))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var idCmd = connection.CreateCommand();
        idCmd.CommandText = "SELECT id FROM users WHERE username = @u LIMIT 1;";
        idCmd.Parameters.AddWithValue("u", username.Trim());
        var scalar = idCmd.ExecuteScalar();
        if (scalar == null)
            return false;
        long userId = Convert.ToInt64(scalar);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT s.slot_index, s.weapon_id, w.code, w.name, w.damage, w.range, w.icon_key, w.attack_ap_cost
FROM user_inventory_slots s
LEFT JOIN weapons w ON w.id = s.weapon_id
WHERE s.user_id = @uid
ORDER BY s.slot_index;
""";
        cmd.Parameters.AddWithValue("uid", userId);

        var bySlot = new Dictionary<int, UserInventorySlotDto>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                int si = reader.GetInt32(0);
                bool hasWeapon = !reader.IsDBNull(1);
                bySlot[si] = new UserInventorySlotDto
                {
                    SlotIndex = si,
                    WeaponId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    WeaponCode = hasWeapon && !reader.IsDBNull(2) ? reader.GetString(2) : null,
                    WeaponName = hasWeapon && !reader.IsDBNull(3) ? reader.GetString(3) : null,
                    Damage = hasWeapon && !reader.IsDBNull(4) ? reader.GetInt32(4) : 0,
                    Range = hasWeapon && !reader.IsDBNull(5) ? reader.GetInt32(5) : 0,
                    IconKey = hasWeapon && !reader.IsDBNull(6) ? reader.GetString(6) : "",
                    AttackApCost = hasWeapon && !reader.IsDBNull(7) ? reader.GetInt32(7) : 0
                };
            }
        }

        for (int i = 0; i < 12; i++)
        {
            if (bySlot.TryGetValue(i, out var row))
                slots.Add(row);
            else
                slots.Add(new UserInventorySlotDto { SlotIndex = i, IconKey = "" });
        }

        return true;
    }
}
