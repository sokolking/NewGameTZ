using System.Text;
using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public sealed class BattleUserDatabase
{
    private const int BaseStat = 10;
    private const int MaxLevel = 16;
    private const int ExpPerLevel = 500;

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
SELECT id, username, password, experience, strength, endurance, accuracy, max_hp, max_ap, weapon_code
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
                Experience = reader.GetInt32(3),
                Strength = reader.GetInt32(4),
                Endurance = reader.GetInt32(5),
                Accuracy = reader.GetInt32(6),
                MaxHp = reader.GetInt32(7),
                MaxAp = reader.GetInt32(8),
                WeaponCode = reader.GetString(9)
            });
            var last = rows[^1];
            last.Level = ComputeLevel(last.Experience);
        }

        return rows;
    }

    public bool TryGetCombatProfile(string username, out int maxHp, out int maxAp, out int accuracy, out int level, out string weaponCode)
    {
        maxHp = 20;
        maxAp = 20;
        accuracy = BaseStat;
        level = 1;
        weaponCode = "fist";
        if (string.IsNullOrWhiteSpace(username))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT experience, strength, endurance, accuracy, weapon_code
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", username.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        int exp = Math.Max(0, reader.GetInt32(0));
        int strength = Math.Max(0, reader.GetInt32(1));
        int endurance = Math.Max(0, reader.GetInt32(2));
        accuracy = Math.Max(0, reader.GetInt32(3));
        weaponCode = reader.IsDBNull(4) ? "fist" : reader.GetString(4);
        if (string.IsNullOrWhiteSpace(weaponCode))
            weaponCode = "fist";
        maxHp = ComputeMaxHp(strength);
        maxAp = ComputeMaxAp(endurance);
        level = ComputeLevel(exp);
        return true;
    }

    public bool TryGetUserProgressProfile(string username, string password, out UserProgressProfileDto profile)
    {
        profile = new UserProgressProfileDto();
        if (!ValidateCredentials(username, password))
            return false;
        string user = username.Trim();
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT username, experience, strength, endurance, accuracy, weapon_code
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", user);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        int exp = Math.Max(0, reader.GetInt32(1));
        int strength = Math.Max(0, reader.GetInt32(2));
        int endurance = Math.Max(0, reader.GetInt32(3));
        int accuracy = Math.Max(0, reader.GetInt32(4));
        int level = ComputeLevel(exp);
        profile = new UserProgressProfileDto
        {
            Username = reader.GetString(0),
            Experience = exp,
            Level = level,
            Strength = strength,
            Endurance = endurance,
            Accuracy = accuracy,
            MaxHp = ComputeMaxHp(strength),
            MaxAp = ComputeMaxAp(endurance),
            HitBonusPercent = accuracy * 2,
            WeaponCode = reader.IsDBNull(5) ? "fist" : reader.GetString(5)
        };
        return true;
    }

    public bool TryGetUserProgressProfileByUsername(string username, out UserProgressProfileDto profile)
    {
        profile = new UserProgressProfileDto();
        if (string.IsNullOrWhiteSpace(username))
            return false;

        string user = username.Trim();
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT username, experience, strength, endurance, accuracy, weapon_code
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", user);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        int exp = Math.Max(0, reader.GetInt32(1));
        int strength = Math.Max(0, reader.GetInt32(2));
        int endurance = Math.Max(0, reader.GetInt32(3));
        int accuracy = Math.Max(0, reader.GetInt32(4));
        int level = ComputeLevel(exp);
        profile = new UserProgressProfileDto
        {
            Username = reader.GetString(0),
            Experience = exp,
            Level = level,
            Strength = strength,
            Endurance = endurance,
            Accuracy = accuracy,
            MaxHp = ComputeMaxHp(strength),
            MaxAp = ComputeMaxAp(endurance),
            HitBonusPercent = accuracy * 2,
            WeaponCode = reader.IsDBNull(5) ? "fist" : reader.GetString(5)
        };
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

    public bool TryUpdateUser(UserUpdateRequest req, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(req.Username))
        {
            error = "username required";
            return false;
        }

        if (req.Experience < 0 || req.Strength < 0 || req.Endurance < 0 || req.Accuracy < 0)
        {
            error = "experience and stats must be >= 0";
            return false;
        }

        string username = req.Username.Trim();
        string weaponCode = string.IsNullOrWhiteSpace(req.WeaponCode) ? "fist" : req.WeaponCode.Trim();
        if (req.Password != null && string.IsNullOrWhiteSpace(req.Password))
        {
            error = "password cannot be empty when provided";
            return false;
        }

        int maxHp = ComputeMaxHp(req.Strength);
        int maxAp = ComputeMaxAp(req.Endurance);

        var sb = new StringBuilder();
        sb.Append("""
UPDATE users SET
  username = @username,
  experience = @experience,
  strength = @strength,
  endurance = @endurance,
  accuracy = @accuracy,
  max_hp = @max_hp,
  max_ap = @max_ap,
  weapon_code = @weapon_code
""");
        if (req.Password != null)
            sb.Append(",\n  password = @password");

        sb.Append("\nWHERE id = @id;");

        try
        {
            using var connection = _database.DataSource.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sb.ToString();
            command.Parameters.AddWithValue("id", req.Id);
            command.Parameters.AddWithValue("username", username);
            command.Parameters.AddWithValue("experience", req.Experience);
            command.Parameters.AddWithValue("strength", req.Strength);
            command.Parameters.AddWithValue("endurance", req.Endurance);
            command.Parameters.AddWithValue("accuracy", req.Accuracy);
            command.Parameters.AddWithValue("max_hp", maxHp);
            command.Parameters.AddWithValue("max_ap", maxAp);
            command.Parameters.AddWithValue("weapon_code", weaponCode);
            if (req.Password != null)
                command.Parameters.AddWithValue("password", req.Password);

            int n = command.ExecuteNonQuery();
            if (n == 0)
            {
                error = "user not found";
                return false;
            }

            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            error = "username already taken";
            return false;
        }
    }

    public bool TryAwardBattleExperience(string username, int expToAdd, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "username required";
            return false;
        }
        if (expToAdd <= 0)
            return true;

        string user = username.Trim();
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE users
SET experience = GREATEST(0, experience + @exp)
WHERE username = @username;
""";
        command.Parameters.AddWithValue("exp", expToAdd);
        command.Parameters.AddWithValue("username", user);
        int n = command.ExecuteNonQuery();
        if (n == 0)
        {
            error = "user not found";
            return false;
        }
        return true;
    }

    private static int ComputeLevel(int experience)
    {
        int lv = 1 + Math.Max(0, experience) / ExpPerLevel;
        return Math.Clamp(lv, 1, MaxLevel);
    }

    private static int ComputeMaxHp(int strength) => Math.Max(1, strength * 2);
    private static int ComputeMaxAp(int endurance) => Math.Max(1, endurance * 2);
}
