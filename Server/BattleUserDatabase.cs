using System.Text;
using BattleServer.Models;
using Npgsql;

namespace BattleServer;

public sealed class BattleUserDatabase
{
    private const int MaxLevel = PlayerLevelStatsTable.MaxLevel;
    private const int ExpPerLevel = 500;

    private readonly BattlePostgresDatabase _database;
    private readonly BattleWeaponDatabase _weapons;
    private readonly BattleAmmoDatabase _ammo;

    public BattleUserDatabase(BattlePostgresDatabase database, BattleWeaponDatabase weapons, BattleAmmoDatabase ammo)
    {
        _database = database;
        _weapons = weapons;
        _ammo = ammo;
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
SELECT u.id, u.username, u.password, u.experience, u.strength, u.endurance, u.accuracy, u.max_hp, u.max_ap,
       COALESCE((
           SELECT ii.weapon_code FROM user_inventory_items ii
           WHERE ii.user_id = u.id AND ii.is_equipped
           LIMIT 1
       ), 'fist') AS equipped_weapon
FROM users u
ORDER BY u.id
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
                WeaponCode = reader.IsDBNull(9) ? "fist" : reader.GetString(9)
            });
            var last = rows[^1];
            last.Level = ComputeLevel(last.Experience);
        }

        return rows;
    }

    public bool TryGetCombatProfile(string username, out int maxHp, out int maxAp, out int accuracy, out int level)
    {
        maxHp = PlayerLevelStatsTable.BaseMaxHp;
        maxAp = PlayerLevelStatsTable.BaseMaxAp;
        accuracy = 0;
        level = 1;
        if (string.IsNullOrWhiteSpace(username))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT experience, strength, endurance, accuracy
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", username.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        int exp = Math.Max(0, reader.GetInt32(0));
        _ = reader.GetInt32(1);
        _ = reader.GetInt32(2);
        _ = reader.GetInt32(3);
        level = ComputeLevel(exp);
        PlayerLevelStatsRow stats = PlayerLevelStatsTable.GetForLevel(level);
        maxHp = PlayerLevelStatsTable.GetMaxHpForLevel(level);
        maxAp = PlayerLevelStatsTable.GetMaxApForLevel(level);
        accuracy = Math.Max(0, stats.Accuracy);
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
SELECT username, experience, strength, endurance, accuracy
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", user);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        int exp = Math.Max(0, reader.GetInt32(1));
        int level = ComputeLevel(exp);
        PlayerLevelStatsRow stats = PlayerLevelStatsTable.GetForLevel(level);
        profile = new UserProgressProfileDto
        {
            Username = reader.GetString(0),
            Experience = exp,
            Level = level,
            Strength = stats.Strength,
            Agility = stats.Agility,
            Endurance = stats.Stamina,
            Accuracy = stats.Accuracy,
            MaxHp = PlayerLevelStatsTable.GetMaxHpForLevel(level),
            MaxAp = PlayerLevelStatsTable.GetMaxApForLevel(level),
            HitBonusPercent = stats.Accuracy * 2
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
SELECT username, experience, strength, endurance, accuracy
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", user);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        int exp = Math.Max(0, reader.GetInt32(1));
        int level = ComputeLevel(exp);
        PlayerLevelStatsRow stats = PlayerLevelStatsTable.GetForLevel(level);
        profile = new UserProgressProfileDto
        {
            Username = reader.GetString(0),
            Experience = exp,
            Level = level,
            Strength = stats.Strength,
            Agility = stats.Agility,
            Endurance = stats.Stamina,
            Accuracy = stats.Accuracy,
            MaxHp = PlayerLevelStatsTable.GetMaxHpForLevel(level),
            MaxAp = PlayerLevelStatsTable.GetMaxApForLevel(level),
            HitBonusPercent = stats.Accuracy * 2
        };
        return true;
    }

    public bool TryGetInventory(string username, string password, out List<UserInventorySlotDto> slots)
    {
        slots = new List<UserInventorySlotDto>();
        if (!ValidateCredentials(username, password))
            return false;

        if (!TryGetUserId(username.Trim(), out long userId))
            return false;

        EnsureUserInventoryBaseline(userId);
        slots = BuildInventorySlotGridForUser(userId);
        return true;
    }

    public bool TryGetInventoryItemsForAdmin(long userId, out List<UserInventoryItemAdminDto> items, out string? error)
    {
        items = new List<UserInventoryItemAdminDto>();
        error = null;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }

        using var connection = _database.DataSource.OpenConnection();
        if (!UserExists(connection, userId))
        {
            error = "user not found";
            return false;
        }

        EnsureUserInventoryBaseline(connection, userId);
        items = LoadInventoryItems(connection, userId);
        return true;
    }

    public bool TryReplaceUserInventory(long userId, IReadOnlyList<UserInventoryItemReplaceDto> rawItems, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }

        if (rawItems == null || rawItems.Count == 0)
        {
            error = "at least one inventory row is required";
            return false;
        }

        var normalized = new List<(int start, string code, int width, bool eq)>();
        int equippedCount = 0;
        bool hasFist = false;
        foreach (UserInventoryItemReplaceDto row in rawItems)
        {
            string code = (row.WeaponCode ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(code))
            {
                error = "weapon code required for each row";
                return false;
            }

            if (!_weapons.TryGetWeaponByCode(code, out var wpn))
            {
                error = "unknown weapon: " + code;
                return false;
            }

            int width = wpn.InventorySlotWidth >= 2 ? 2 : 1;
            int start = row.StartSlot;
            if (start < 0 || start > 11)
            {
                error = "startSlot must be 0..11";
                return false;
            }

            if (start + width > 12)
            {
                error = "item does not fit in grid (startSlot + width > 12)";
                return false;
            }

            bool eq = row.IsEquipped;
            if (eq)
                equippedCount++;
            if (wpn.Code == "fist")
                hasFist = true;
            normalized.Add((start, wpn.Code, width, eq));
        }

        if (!hasFist)
        {
            error = "inventory must include weapon fist (bare hands)";
            return false;
        }

        if (equippedCount != 1)
        {
            error = "exactly one item must have isEquipped true";
            return false;
        }

        var occupied = new bool[12];
        foreach (var (start, _, width, _) in normalized)
        {
            for (int i = 0; i < width; i++)
            {
                int idx = start + i;
                if (occupied[idx])
                {
                    error = "overlapping inventory slots";
                    return false;
                }

                occupied[idx] = true;
            }
        }

        var codesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, code, _, _) in normalized)
        {
            if (!codesSeen.Add(code))
            {
                error = "duplicate weapon code in inventory (only one stack per type)";
                return false;
            }
        }

        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        if (!UserExists(connection, userId, tx))
        {
            error = "user not found";
            return false;
        }

        using (var del = connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM user_inventory_items WHERE user_id = @uid;";
            del.Parameters.AddWithValue("uid", userId);
            del.ExecuteNonQuery();
        }

        foreach (var (start, code, width, eq) in normalized)
        {
            using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
INSERT INTO user_inventory_items (user_id, start_slot, weapon_code, slot_width, is_equipped)
VALUES (@uid, @start, @code, @width, @eq);
""";
            ins.Parameters.AddWithValue("uid", userId);
            ins.Parameters.AddWithValue("start", start);
            ins.Parameters.AddWithValue("code", code);
            ins.Parameters.AddWithValue("width", width);
            ins.Parameters.AddWithValue("eq", eq);
            ins.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }

    public bool TryGetAmmoPacksForAdmin(long userId, out List<UserAmmoPackAdminDto> items, out string? error)
    {
        items = new List<UserAmmoPackAdminDto>();
        error = null;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }

        using var connection = _database.DataSource.OpenConnection();
        if (!UserExists(connection, userId))
        {
            error = "user not found";
            return false;
        }

        items = LoadAmmoPacks(connection, userId);
        return true;
    }

    public bool TryReplaceUserAmmoPacks(long userId, IReadOnlyList<UserAmmoPackReplaceDto> rawItems, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }

        if (rawItems == null)
        {
            error = "items array required";
            return false;
        }

        var normalized = new List<(long ammoTypeId, int rounds)>();
        var calibersSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rawItems)
        {
            string caliber = (row.Caliber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(caliber))
            {
                error = "caliber required for each row";
                return false;
            }
            if (!calibersSeen.Add(caliber))
            {
                error = "duplicate caliber: " + caliber;
                return false;
            }
            if (!_ammo.TryGetAmmoTypeByCaliber(caliber, out var ammo))
            {
                error = "unknown caliber: " + caliber;
                return false;
            }
            int rounds = Math.Max(0, row.RoundsCount > 0 ? row.RoundsCount : row.TotalRounds);
            if (rounds == 0)
                continue;
            normalized.Add((ammo.Id, rounds));
        }

        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        if (!UserExists(connection, userId, tx))
        {
            error = "user not found";
            return false;
        }

        using (var del = connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM user_ammo_packs WHERE user_id = @uid;";
            del.Parameters.AddWithValue("uid", userId);
            del.ExecuteNonQuery();
        }

        foreach (var (ammoTypeId, rounds) in normalized)
        {
            using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
INSERT INTO user_ammo_packs (user_id, ammo_type_id, packs_count, rounds_count)
VALUES (@uid, @ammoTypeId, 0, @rounds);
""";
            ins.Parameters.AddWithValue("uid", userId);
            ins.Parameters.AddWithValue("ammoTypeId", ammoTypeId);
            ins.Parameters.AddWithValue("rounds", rounds);
            ins.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }

    public bool TryGetAmmoPacks(string username, string password, out IReadOnlyList<UserAmmoPackAdminDto> items)
    {
        items = Array.Empty<UserAmmoPackAdminDto>();
        if (!ValidateCredentials(username, password))
            return false;
        if (!TryGetUserId(username.Trim(), out long userId))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        items = LoadAmmoPacks(connection, userId);
        return true;
    }

    public bool TryGetUserAmmoRounds(string username, string caliber, out int rounds)
    {
        rounds = 0;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(caliber))
            return false;
        if (!TryGetUserId(username.Trim(), out long userId))
            return false;
        if (!_ammo.TryGetAmmoTypeByCaliber(caliber.Trim(), out var ammoType))
            return false;
        using var connection = _database.DataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT COALESCE(rounds_count, 0)
FROM user_ammo_packs
WHERE user_id = @uid AND ammo_type_id = @aid
LIMIT 1;
""";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("aid", ammoType.Id);
        object? scalar = cmd.ExecuteScalar();
        rounds = scalar == null ? 0 : Math.Max(0, Convert.ToInt32(scalar));
        return true;
    }

    public bool TrySetUserAmmoRounds(string username, string caliber, int rounds, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "username required";
            return false;
        }
        if (string.IsNullOrWhiteSpace(caliber))
            return true;
        if (!TryGetUserId(username.Trim(), out long userId))
        {
            error = "user not found";
            return false;
        }
        if (!_ammo.TryGetAmmoTypeByCaliber(caliber.Trim(), out var ammoType))
            return true;

        int safeRounds = Math.Max(0, rounds);
        using var connection = _database.DataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO user_ammo_packs (user_id, ammo_type_id, packs_count, rounds_count)
VALUES (@uid, @aid, 0, @rounds)
ON CONFLICT (user_id, ammo_type_id) DO UPDATE SET
    rounds_count = EXCLUDED.rounds_count,
    packs_count = 0;
""";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("aid", ammoType.Id);
        cmd.Parameters.AddWithValue("rounds", safeRounds);
        cmd.ExecuteNonQuery();
        return true;
    }

    /// <summary>Weapon allowed in battle equip when it exists in inventory or is <c>fist</c>.</summary>
    public bool TryValidateEquippedWeaponForRegisteredUser(string username, string weaponCode, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(username))
            return true;

        string code = (weaponCode ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
            code = "fist";

        if (!TryGetUserId(username.Trim(), out long userId))
            return true;

        if (code == "fist")
            return true;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT 1 FROM user_inventory_items
WHERE user_id = @uid AND LOWER(weapon_code) = LOWER(@code)
LIMIT 1;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("code", code);
        if (command.ExecuteScalar() == null)
        {
            error = "weapon not in inventory";
            return false;
        }

        return true;
    }

    public void SyncEquippedWeaponForRegisteredUser(string username, string weaponCode)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;
        string code = (weaponCode ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
            code = "fist";
        if (!TryGetUserId(username.Trim(), out long userId))
            return;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE user_inventory_items
SET is_equipped = (LOWER(weapon_code) = LOWER(@code))
WHERE user_id = @uid;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("code", code);
        command.ExecuteNonQuery();

        using var verify = connection.CreateCommand();
        verify.CommandText = """
SELECT COUNT(*)::INT FROM user_inventory_items WHERE user_id = @uid AND is_equipped;
""";
        verify.Parameters.AddWithValue("uid", userId);
        int equippedRows = Convert.ToInt32(verify.ExecuteScalar() ?? 0);
        if (equippedRows != 0)
            return;

        using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "UPDATE user_inventory_items SET is_equipped = FALSE WHERE user_id = @uid;";
            clear.Parameters.AddWithValue("uid", userId);
            clear.ExecuteNonQuery();
        }

        using (var fist = connection.CreateCommand())
        {
            fist.CommandText = """
UPDATE user_inventory_items SET is_equipped = TRUE
WHERE user_id = @uid AND LOWER(weapon_code) = 'fist';
""";
            fist.Parameters.AddWithValue("uid", userId);
            fist.ExecuteNonQuery();
        }
    }

    public bool TryGetEquippedWeaponCodeForUser(string username, out string weaponCode)
    {
        weaponCode = "fist";
        if (string.IsNullOrWhiteSpace(username))
            return false;
        if (!TryGetUserId(username.Trim(), out long userId))
            return false;

        EnsureUserInventoryBaseline(userId);
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT weapon_code FROM user_inventory_items
WHERE user_id = @uid AND is_equipped
LIMIT 1;
""";
        command.Parameters.AddWithValue("uid", userId);
        object? scalar = command.ExecuteScalar();
        if (scalar is string s && !string.IsNullOrWhiteSpace(s))
        {
            weaponCode = s.Trim().ToLowerInvariant();
            return true;
        }

        weaponCode = "fist";
        return true;
    }

    private static bool UserExists(NpgsqlConnection connection, long userId, NpgsqlTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT 1 FROM users WHERE id = @id LIMIT 1;";
        command.Parameters.AddWithValue("id", userId);
        return command.ExecuteScalar() != null;
    }

    private bool TryGetUserId(string username, out long userId)
    {
        userId = 0;
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM users WHERE username = @u LIMIT 1;";
        command.Parameters.AddWithValue("u", username);
        object? scalar = command.ExecuteScalar();
        if (scalar is long l)
        {
            userId = l;
            return true;
        }

        return false;
    }

    private void EnsureUserInventoryBaseline(long userId)
    {
        using var connection = _database.DataSource.OpenConnection();
        EnsureUserInventoryBaseline(connection, userId);
    }

    private static void EnsureUserInventoryBaseline(NpgsqlConnection connection, long userId)
    {
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*)::INT FROM user_inventory_items WHERE user_id = @uid;";
        countCmd.Parameters.AddWithValue("uid", userId);
        int n = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        if (n > 0)
            return;

        using var ins = connection.CreateCommand();
        ins.CommandText = """
INSERT INTO user_inventory_items (user_id, start_slot, weapon_code, slot_width, is_equipped)
VALUES (@uid, 0, 'fist', 1, TRUE);
""";
        ins.Parameters.AddWithValue("uid", userId);
        try
        {
            ins.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // concurrent create — ignore
        }
    }

    private List<UserInventoryItemAdminDto> LoadInventoryItems(NpgsqlConnection connection, long userId)
    {
        var list = new List<UserInventoryItemAdminDto>();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, start_slot, weapon_code, slot_width, is_equipped
FROM user_inventory_items
WHERE user_id = @uid
ORDER BY start_slot, id;
""";
        command.Parameters.AddWithValue("uid", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new UserInventoryItemAdminDto
            {
                Id = reader.GetInt64(0),
                StartSlot = reader.GetInt32(1),
                WeaponCode = reader.GetString(2),
                SlotWidth = reader.GetInt32(3),
                IsEquipped = reader.GetBoolean(4)
            });
        }

        return list;
    }

    private static List<UserAmmoPackAdminDto> LoadAmmoPacks(NpgsqlConnection connection, long userId)
    {
        var list = new List<UserAmmoPackAdminDto>();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT uap.id, uap.ammo_type_id, at.caliber, at.unit_weight, at.rounds_per_pack, uap.packs_count, COALESCE(uap.rounds_count, 0)
FROM user_ammo_packs uap
JOIN ammo_types at ON at.id = uap.ammo_type_id
WHERE uap.user_id = @uid
ORDER BY LOWER(at.caliber), uap.id;
""";
        command.Parameters.AddWithValue("uid", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            int roundsPerPack = reader.GetInt32(4);
            int packsCount = reader.GetInt32(5);
            int roundsCount = reader.GetInt32(6);
            list.Add(new UserAmmoPackAdminDto
            {
                Id = reader.GetInt64(0),
                AmmoTypeId = reader.GetInt64(1),
                Caliber = reader.GetString(2),
                UnitWeight = reader.GetDouble(3),
                RoundsPerPack = roundsPerPack,
                PacksCount = 0,
                RoundsCount = Math.Max(0, roundsCount),
                TotalRounds = Math.Max(0, roundsCount)
            });
        }

        return list;
    }

    private List<UserInventorySlotDto> BuildInventorySlotGridForUser(long userId)
    {
        using var connection = _database.DataSource.OpenConnection();
        var items = LoadInventoryItems(connection, userId);
        var slots = new List<UserInventorySlotDto>(12);
        for (int i = 0; i < 12; i++)
        {
            slots.Add(new UserInventorySlotDto
            {
                SlotIndex = i,
                IconKey = ""
            });
        }

        foreach (UserInventoryItemAdminDto item in items)
        {
            if (!_weapons.TryGetWeaponByCode(item.WeaponCode, out var w))
                continue;
            int width = item.SlotWidth >= 2 ? 2 : 1;
            for (int k = 0; k < width; k++)
            {
                int idx = item.StartSlot + k;
                if (idx < 0 || idx >= 12)
                    continue;
                bool primary = k == 0;
                slots[idx] = new UserInventorySlotDto
                {
                    SlotIndex = idx,
                    WeaponId = w.Id,
                    WeaponCode = w.Code,
                    WeaponName = w.Name,
                    Damage = w.DamageMax,
                    Range = w.Range,
                    IconKey = w.IconKey,
                    AttackApCost = w.AttackApCost,
                    SlotSpan = primary ? width : 0,
                    Equipped = primary && item.IsEquipped,
                    Continuation = !primary
                };
            }
        }

        return slots;
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

        if (req.Password != null && string.IsNullOrWhiteSpace(req.Password))
        {
            error = "password cannot be empty when provided";
            return false;
        }

        string username = req.Username.Trim();

        int levelFromExp = ComputeLevel(req.Experience);
        int maxHp = PlayerLevelStatsTable.GetMaxHpForLevel(levelFromExp);
        int maxAp = PlayerLevelStatsTable.GetMaxApForLevel(levelFromExp);
        PlayerLevelStatsRow tableRow = PlayerLevelStatsTable.GetForLevel(levelFromExp);

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
  intuition = @intuition,
  intellect = @intellect
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
            command.Parameters.AddWithValue("intuition", tableRow.Intuition);
            command.Parameters.AddWithValue("intellect", tableRow.Intellect);
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

}
