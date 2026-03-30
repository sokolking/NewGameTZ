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

    /// <summary>Validates credentials and returns <c>users.id</c> in one round-trip (prefer over <see cref="ValidateCredentials"/> + lookup).</summary>
    public bool TryValidateCredentialsAndGetUserId(string username, string password, out long userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id FROM users
WHERE username = @username AND password = @password
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", username.Trim());
        command.Parameters.AddWithValue("password", password);
        object? scalar = command.ExecuteScalar();
        if (scalar is long l)
        {
            userId = l;
            return true;
        }

        if (scalar is int i)
        {
            userId = i;
            return true;
        }

        return false;
    }

    public IReadOnlyList<BattleUserBrowseRowDto> ListUsers(int take)
    {
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT u.id, u.username, u.password, u.experience, u.strength, u.endurance, u.accuracy, u.max_hp, u.current_hp, u.max_ap,
       COALESCE((
           SELECT NULLIF(TRIM(u.equipped_item_code), '')
       ), (
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
                CurrentHp = reader.GetInt32(8),
                MaxAp = reader.GetInt32(9),
                WeaponCode = reader.IsDBNull(10) ? "fist" : reader.GetString(10)
            });
            var last = rows[^1];
            last.Level = ComputeLevel(last.Experience);
        }

        return rows;
    }

    public bool TryGetCombatProfile(string username, out int maxHp, out int currentHp, out int maxAp, out int accuracy, out int level)
    {
        maxHp = PlayerLevelStatsTable.BaseMaxHp;
        currentHp = maxHp;
        maxAp = PlayerLevelStatsTable.BaseMaxAp;
        accuracy = 0;
        level = 1;
        if (string.IsNullOrWhiteSpace(username))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT experience, strength, endurance, accuracy, max_hp, current_hp
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
        int hpFromDb = reader.GetInt32(4);
        int currentHpFromDb = reader.GetInt32(5);
        level = ComputeLevel(exp);
        PlayerLevelStatsRow stats = PlayerLevelStatsTable.GetForLevel(level);
        maxHp = Math.Max(1, hpFromDb);
        currentHp = Math.Clamp(currentHpFromDb, 0, maxHp);
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
SELECT username, experience,
       strength, agility, intuition, intellect, endurance, accuracy,
       max_hp, current_hp, max_ap
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", user);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        return TryMapUserProgressRow(reader, out profile);
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
SELECT username, experience,
       strength, agility, intuition, intellect, endurance, accuracy,
       max_hp, current_hp, max_ap
FROM users
WHERE username = @username
LIMIT 1;
""";
        command.Parameters.AddWithValue("username", user);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        return TryMapUserProgressRow(reader, out profile);
    }

    public bool TryGetInventory(string username, string password, out List<UserInventorySlotDto> slots, out long userId)
    {
        slots = new List<UserInventorySlotDto>();
        userId = 0;
        if (!TryValidateCredentialsAndGetUserId(username, password, out userId))
            return false;

        EnsureUserInventoryBaseline(userId);
        slots = BuildInventorySlotGridForUser(userId);
        return true;
    }

    /// <summary>Inventory for an already authenticated <paramref name="userId"/> (JWT).</summary>
    public bool TryGetInventoryForUser(long userId, out List<UserInventorySlotDto> slots)
    {
        slots = new List<UserInventorySlotDto>();
        if (userId <= 0)
            return false;

        using var connection = _database.DataSource.OpenConnection();
        if (!UserExists(connection, userId))
            return false;

        EnsureUserInventoryBaseline(connection, userId);
        slots = BuildInventorySlotGridForUser(userId);
        return true;
    }

    public bool TryGetUsername(long userId, out string username)
    {
        username = "";
        if (userId <= 0)
            return false;
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT username FROM users WHERE id = @id LIMIT 1;";
        command.Parameters.AddWithValue("id", userId);
        object? scalar = command.ExecuteScalar();
        if (scalar is string s && !string.IsNullOrWhiteSpace(s))
        {
            username = s.Trim();
            return true;
        }

        return false;
    }

    public bool TryGetCombatProfileByUserId(long userId, out int maxHp, out int currentHp, out int maxAp, out int accuracy, out int level)
    {
        maxHp = PlayerLevelStatsTable.BaseMaxHp;
        currentHp = maxHp;
        maxAp = PlayerLevelStatsTable.BaseMaxAp;
        accuracy = 0;
        level = 1;
        if (userId <= 0)
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT experience, strength, endurance, accuracy, max_hp, current_hp
FROM users
WHERE id = @id
LIMIT 1;
""";
        command.Parameters.AddWithValue("id", userId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        int exp = Math.Max(0, reader.GetInt32(0));
        _ = reader.GetInt32(1);
        _ = reader.GetInt32(2);
        _ = reader.GetInt32(3);
        int hpFromDb = reader.GetInt32(4);
        int currentHpFromDb = reader.GetInt32(5);
        level = ComputeLevel(exp);
        PlayerLevelStatsRow stats = PlayerLevelStatsTable.GetForLevel(level);
        maxHp = Math.Max(1, hpFromDb);
        currentHp = Math.Clamp(currentHpFromDb, 0, maxHp);
        maxAp = PlayerLevelStatsTable.GetMaxApForLevel(level);
        accuracy = Math.Max(0, stats.Accuracy);
        return true;
    }

    public bool TryGetEquippedWeaponCodeForUserByUserId(long userId, out string weaponCode)
    {
        weaponCode = "fist";
        if (userId <= 0)
            return false;

        EnsureUserInventoryBaseline(userId);
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(NULLIF(TRIM(u.equipped_item_code), ''), (
    SELECT ii.weapon_code FROM user_inventory_items ii
    WHERE ii.user_id = u.id AND ii.is_equipped
    LIMIT 1
), 'fist')
FROM users u
WHERE u.id = @uid
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

    public bool TryGetUserProgressProfileByUserId(long userId, out UserProgressProfileDto profile)
    {
        profile = new UserProgressProfileDto();
        if (userId <= 0)
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT username, experience,
       strength, agility, intuition, intellect, endurance, accuracy,
       max_hp, current_hp, max_ap
FROM users
WHERE id = @id
LIMIT 1;
""";
        command.Parameters.AddWithValue("id", userId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        return TryMapUserProgressRow(reader, out profile);
    }

    private bool TryMapUserProgressRow(NpgsqlDataReader reader, out UserProgressProfileDto profile)
    {
        int exp = Math.Max(0, reader.GetInt32(1));
        int level = ComputeLevel(exp);
        int maxHp = Math.Max(1, reader.GetInt32(8));
        int curHp = Math.Clamp(reader.GetInt32(9), 0, maxHp);
        int maxAp = Math.Max(1, reader.GetInt32(10));
        int acc = Math.Max(0, reader.GetInt32(7));
        profile = new UserProgressProfileDto
        {
            Username = reader.GetString(0),
            Experience = exp,
            Level = level,
            Strength = Math.Max(0, reader.GetInt32(2)),
            Agility = Math.Max(0, reader.GetInt32(3)),
            Intuition = Math.Max(0, reader.GetInt32(4)),
            Intellect = Math.Max(0, reader.GetInt32(5)),
            Endurance = Math.Max(0, reader.GetInt32(6)),
            Accuracy = acc,
            MaxHp = maxHp,
            CurrentHp = curHp,
            MaxAp = maxAp,
            HitBonusPercent = acc * 2
        };
        return true;
    }

    public bool TryGetUserItemsForAdmin(long userId, out List<UserItemAdminDto> items, out string? error)
    {
        items = new List<UserItemAdminDto>();
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
        string equippedCode = "fist";
        using (var equippedCmd = connection.CreateCommand())
        {
            equippedCmd.CommandText = """
SELECT COALESCE(NULLIF(TRIM(equipped_item_code), ''), 'fist')
FROM users
WHERE id = @uid
LIMIT 1;
""";
            equippedCmd.Parameters.AddWithValue("uid", userId);
            if (equippedCmd.ExecuteScalar() is string selected && !string.IsNullOrWhiteSpace(selected))
                equippedCode = selected.Trim().ToLowerInvariant();
        }
        var inv = LoadInventoryItems(connection, userId);
        foreach (var x in inv)
        {
            if (!_weapons.TryGetWeaponByCode(x.WeaponCode, out var wpn))
                continue;
            bool isEquippable = IsItemCodeEquippable(connection, x.WeaponCode);
            items.Add(new UserItemAdminDto
            {
                ItemType = "weapon",
                Code = x.WeaponCode,
                Name = wpn.Name,
                IconKey = wpn.IconKey,
                Quality = wpn.Quality,
                Condition = wpn.WeaponCondition,
                Mass = wpn.Mass,
                InventoryGrid = Math.Clamp(wpn.InventoryGrid, 0, 2),
                Quantity = 1,
                ChamberRounds = Math.Max(0, x.ChamberRounds),
                StartSlot = x.StartSlot,
                IsEquipped = isEquippable && string.Equals(x.WeaponCode, equippedCode, StringComparison.OrdinalIgnoreCase),
                IsEquippable = isEquippable,
                IsStackable = false,
                SlotWidth = x.SlotWidth >= 2 ? 2 : 1
            });
        }

        var ammo = LoadAmmoPacks(connection, userId);
        foreach (var x in ammo)
        {
            bool isEquippable = IsAmmoCaliberEquippable(connection, x.Caliber);
            items.Add(new UserItemAdminDto
            {
                ItemType = "ammo",
                Code = x.Caliber,
                Name = x.Name,
                IconKey = x.IconKey,
                Quality = x.Quality,
                Condition = x.Condition,
                Mass = x.UnitWeight,
                InventoryGrid = Math.Clamp(x.InventoryGrid, 0, 2),
                Quantity = Math.Max(0, x.RoundsCount),
                ChamberRounds = 0,
                StartSlot = Math.Clamp(x.StartSlot, 0, 11),
                IsEquipped = isEquippable && string.Equals(x.Caliber, equippedCode, StringComparison.OrdinalIgnoreCase),
                IsEquippable = isEquippable,
                IsStackable = true,
                SlotWidth = 0
            });
        }

        return true;
    }

    public bool TryReplaceUserItems(long userId, IReadOnlyList<UserItemReplaceDto> rawItems, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }

        if (rawItems == null || rawItems.Count == 0)
        {
            error = "items array required";
            return false;
        }

        var weaponRows = new List<(int start, string code, int width, int chamber, bool eq)>();
        var ammoRows = new List<(long ammoTypeId, int startSlot, int rounds)>();
        var equippableUpdates = new List<(string type, string code, bool isEquippable)>();
        var weaponCodesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ammoCalibersSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int equippedCount = 0;
        string equippedCode = "fist";
        bool hasFist = false;
        foreach (var row in rawItems)
        {
            string type = (row.ItemType ?? "").Trim().ToLowerInvariant();
            if (type == "weapon")
            {
                string code = (row.Code ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(code))
                {
                    error = "weapon code required for each weapon item";
                    return false;
                }
                if (!_weapons.TryGetWeaponByCode(code, out var wpn))
                {
                    error = "unknown weapon: " + code;
                    return false;
                }
                if (!weaponCodesSeen.Add(wpn.Code))
                {
                    error = "duplicate weapon code in items";
                    return false;
                }
                int width = wpn.InventorySlotWidth >= 2 ? 2 : 1;
                int start = row.StartSlot;
                if (start < 0 || start > 11 || start + width > 12)
                {
                    error = "weapon startSlot invalid";
                    return false;
                }
                bool eq = row.IsEquipped;
                if (eq && !row.IsEquippable)
                {
                    error = "equipped requires isEquippable=true";
                    return false;
                }
                if (eq)
                {
                    equippedCount++;
                    equippedCode = wpn.Code;
                }
                if (wpn.Code == "fist")
                    hasFist = true;
                int chamber = Math.Max(0, row.ChamberRounds);
                if (wpn.MagazineSize <= 0 || string.IsNullOrWhiteSpace(wpn.Caliber))
                    chamber = 0;
                else
                    chamber = Math.Clamp(chamber, 0, Math.Max(0, wpn.MagazineSize));
                weaponRows.Add((start, wpn.Code, width, chamber, eq));
                equippableUpdates.Add(("weapon", wpn.Code, row.IsEquippable));
                continue;
            }

            if (type == "ammo" || type == "medicine")
            {
                string caliber = (row.Code ?? "").Trim();
                if (string.IsNullOrWhiteSpace(caliber))
                {
                    error = type == "medicine"
                        ? "code required for each medicine item"
                        : "caliber required for each ammo item";
                    return false;
                }
                if (!ammoCalibersSeen.Add(caliber))
                {
                    error = "duplicate stackable code: " + caliber;
                    return false;
                }
                if (!_ammo.TryGetAmmoTypeByCaliber(caliber, out var ammo))
                {
                    if (type == "medicine" && _weapons.TryGetWeaponByCode(caliber, out var medWpn)
                        && string.Equals(medWpn.Category, "medicine", StringComparison.OrdinalIgnoreCase))
                    {
                        var upsert = new AmmoTypeUpsertRequest
                        {
                            Caliber = medWpn.Code,
                            UnitWeight = Math.Max(0.0, medWpn.Mass),
                            IconKey = medWpn.IconKey ?? "",
                            Name = medWpn.Name ?? medWpn.Code,
                            Quality = medWpn.Quality,
                            Condition = medWpn.WeaponCondition,
                            InventoryGrid = Math.Clamp(medWpn.InventoryGrid, 0, 2)
                        };
                        if (!_ammo.TryUpsertAmmoType(upsert, out var upsertErr)
                            || !_ammo.TryGetAmmoTypeByCaliber(caliber, out ammo))
                        {
                            error = upsertErr ?? ("unknown medicine code: " + caliber);
                            return false;
                        }
                    }
                    else
                    {
                        error = type == "medicine"
                            ? "unknown medicine code: " + caliber
                            : "unknown caliber: " + caliber;
                        return false;
                    }
                }
                int startSlot = row.StartSlot;
                if (startSlot < 0 || startSlot > 11)
                {
                    error = "stackable item startSlot must be 0..11";
                    return false;
                }
                int rounds = Math.Max(0, row.Quantity);
                if (rounds > 0)
                    ammoRows.Add((ammo.Id, startSlot, rounds));
                if (row.IsEquipped && !row.IsEquippable)
                {
                    error = "equipped requires isEquippable=true";
                    return false;
                }
                if (row.IsEquipped)
                {
                    equippedCount++;
                    equippedCode = ammo.Caliber;
                }
                equippableUpdates.Add(("ammo", ammo.Caliber, row.IsEquippable));
                continue;
            }

            error = "unknown itemType: " + type;
            return false;
        }

        if (weaponRows.Count == 0)
        {
            error = "at least one weapon item is required";
            return false;
        }
        if (!hasFist)
        {
            error = "items must include weapon fist (bare hands)";
            return false;
        }
        if (equippedCount != 1)
        {
            error = "exactly one equippable item must have isEquipped true";
            return false;
        }

        var occupied = new bool[12];
        foreach (var row in weaponRows)
        {
            for (int i = 0; i < row.width; i++)
            {
                int idx = row.start + i;
                if (occupied[idx])
                {
                    error = "overlapping item slots";
                    return false;
                }
                occupied[idx] = true;
            }
        }
        foreach (var row in ammoRows)
        {
            if (row.rounds <= 0)
                continue;
            if (occupied[row.startSlot])
            {
                error = "overlapping item slots";
                return false;
            }
            occupied[row.startSlot] = true;
        }

        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        if (!UserExists(connection, userId, tx))
        {
            error = "user not found";
            return false;
        }

        using (var delInv = connection.CreateCommand())
        {
            delInv.Transaction = tx;
            delInv.CommandText = "DELETE FROM user_inventory_items WHERE user_id = @uid;";
            delInv.Parameters.AddWithValue("uid", userId);
            delInv.ExecuteNonQuery();
        }
        using (var delAmmo = connection.CreateCommand())
        {
            delAmmo.Transaction = tx;
            delAmmo.CommandText = "DELETE FROM user_ammo_packs WHERE user_id = @uid;";
            delAmmo.Parameters.AddWithValue("uid", userId);
            delAmmo.ExecuteNonQuery();
        }

        foreach (var row in weaponRows)
        {
            using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
INSERT INTO user_inventory_items (user_id, start_slot, weapon_code, slot_width, chamber_rounds, is_equipped)
VALUES (@uid, @start, @code, @width, @chamber, @eq);
""";
            ins.Parameters.AddWithValue("uid", userId);
            ins.Parameters.AddWithValue("start", row.start);
            ins.Parameters.AddWithValue("code", row.code);
            ins.Parameters.AddWithValue("width", row.width);
            ins.Parameters.AddWithValue("chamber", row.chamber);
            ins.Parameters.AddWithValue("eq", row.eq);
            ins.ExecuteNonQuery();
        }

        foreach (var update in equippableUpdates)
        {
            if (update.type == "weapon")
            {
                using var eqW = connection.CreateCommand();
                eqW.Transaction = tx;
                eqW.CommandText = """
UPDATE items i
SET is_equippable = @isEquippable
FROM weapons w
WHERE w.item_id = i.id AND LOWER(w.code) = LOWER(@code);
""";
                eqW.Parameters.AddWithValue("isEquippable", update.isEquippable);
                eqW.Parameters.AddWithValue("code", update.code);
                eqW.ExecuteNonQuery();
            }
            else
            {
                using var eqA = connection.CreateCommand();
                eqA.Transaction = tx;
                eqA.CommandText = """
UPDATE items i
SET is_equippable = @isEquippable
FROM ammo_types at
WHERE at.item_id = i.id AND LOWER(at.caliber) = LOWER(@code);
""";
                eqA.Parameters.AddWithValue("isEquippable", update.isEquippable);
                eqA.Parameters.AddWithValue("code", update.code);
                eqA.ExecuteNonQuery();
            }
        }

        foreach (var row in ammoRows)
        {
            using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
INSERT INTO user_ammo_packs (user_id, ammo_type_id, start_slot, packs_count, rounds_count)
VALUES (@uid, @ammoTypeId, @startSlot, 0, @rounds);
""";
            ins.Parameters.AddWithValue("uid", userId);
            ins.Parameters.AddWithValue("ammoTypeId", row.ammoTypeId);
            ins.Parameters.AddWithValue("startSlot", row.startSlot);
            ins.Parameters.AddWithValue("rounds", row.rounds);
            ins.ExecuteNonQuery();
        }

        using (var equipped = connection.CreateCommand())
        {
            equipped.Transaction = tx;
            equipped.CommandText = "UPDATE users SET equipped_item_code = @code WHERE id = @uid;";
            equipped.Parameters.AddWithValue("uid", userId);
            equipped.Parameters.AddWithValue("code", equippedCode);
            equipped.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }

    public bool TryGetUserAmmoRounds(long userId, string caliber, out int rounds)
    {
        rounds = 0;
        if (userId <= 0 || string.IsNullOrWhiteSpace(caliber))
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

    public bool TrySetUserAmmoRounds(long userId, string caliber, int rounds, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "user id required";
            return false;
        }
        if (string.IsNullOrWhiteSpace(caliber))
            return true;
        if (!_ammo.TryGetAmmoTypeByCaliber(caliber.Trim(), out var ammoType))
            return true;

        int safeRounds = Math.Max(0, rounds);
        using var connection = _database.DataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO user_ammo_packs (user_id, ammo_type_id, start_slot, packs_count, rounds_count)
VALUES (@uid, @aid, 0, 0, @rounds)
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

    public bool TryConsumeUserItemQuantity(long userId, string itemCode, int amount, out string? error)
    {
        error = null;
        int safeAmount = Math.Max(0, amount);
        if (safeAmount <= 0 || string.IsNullOrWhiteSpace(itemCode))
            return true;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }
        if (!TryGetUserAmmoRounds(userId, itemCode, out int current))
        {
            error = "item quantity not found";
            return false;
        }

        int next = Math.Max(0, current - safeAmount);
        return TrySetUserAmmoRounds(userId, itemCode, next, out error);
    }

    /// <summary>Weapon/item allowed in battle equip when it is equippable and present in player's inventory/stack.</summary>
    public bool TryValidateEquippedWeaponForRegisteredUser(long userId, string weaponCode, out string? error)
    {
        error = null;
        if (userId <= 0)
            return true;

        string code = (weaponCode ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
            code = "fist";

        if (code == "fist")
            return true;

        using var connection = _database.DataSource.OpenConnection();
        if (!IsItemCodeEquippable(connection, code))
        {
            error = "item cannot be equipped";
            return false;
        }
        if (HasWeaponRowInInventory(connection, userId, code))
            return true;
        if (HasStackableRounds(connection, userId, code))
            return true;

        error = "item not in inventory";
        return false;
    }

    public void SyncEquippedWeaponForRegisteredUser(long userId, string weaponCode)
    {
        if (userId <= 0)
            return;
        string code = (weaponCode ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
            code = "fist";
        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        using (var selected = connection.CreateCommand())
        {
            selected.Transaction = tx;
            selected.CommandText = "UPDATE users SET equipped_item_code = @code WHERE id = @uid;";
            selected.Parameters.AddWithValue("uid", userId);
            selected.Parameters.AddWithValue("code", code);
            selected.ExecuteNonQuery();
        }
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "UPDATE user_inventory_items SET is_equipped = FALSE WHERE user_id = @uid;";
            clear.Parameters.AddWithValue("uid", userId);
            clear.ExecuteNonQuery();
        }
        using (var equip = connection.CreateCommand())
        {
            equip.Transaction = tx;
            equip.CommandText = """
UPDATE user_inventory_items
SET is_equipped = TRUE
WHERE user_id = @uid AND LOWER(weapon_code) = LOWER(@code);
""";
            equip.Parameters.AddWithValue("uid", userId);
            equip.Parameters.AddWithValue("code", code);
            equip.ExecuteNonQuery();
        }
        tx.Commit();

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

    public bool TrySetUserWeaponChamberRounds(long userId, string weaponCode, int chamberRounds, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "user id required";
            return false;
        }

        string code = (weaponCode ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
            return true;

        int rounds = Math.Max(0, chamberRounds);
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE user_inventory_items
SET chamber_rounds = @rounds
WHERE user_id = @uid AND LOWER(weapon_code) = LOWER(@code);
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("rounds", rounds);
        command.ExecuteNonQuery();
        return true;
    }

    public bool TryGetUserWeaponChamberRounds(long userId, string weaponCode, out int chamberRounds)
    {
        chamberRounds = 0;
        if (userId <= 0 || string.IsNullOrWhiteSpace(weaponCode))
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(chamber_rounds, 0)
FROM user_inventory_items
WHERE user_id = @uid AND LOWER(weapon_code) = LOWER(@code)
LIMIT 1;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("code", weaponCode.Trim());
        object? scalar = command.ExecuteScalar();
        if (scalar == null)
            return false;
        chamberRounds = Math.Max(0, Convert.ToInt32(scalar));
        return true;
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
SELECT COALESCE(NULLIF(TRIM(u.equipped_item_code), ''), (
    SELECT ii.weapon_code FROM user_inventory_items ii
    WHERE ii.user_id = u.id AND ii.is_equipped
    LIMIT 1
), 'fist')
FROM users u
WHERE u.id = @uid
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

    private static bool IsItemCodeEquippable(NpgsqlConnection connection, string code)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(MAX(CASE WHEN i.is_equippable THEN 1 ELSE 0 END), 0)
FROM items i
WHERE LOWER(i.name) = LOWER(@code);
""";
        command.Parameters.AddWithValue("code", code);
        int equippable = Convert.ToInt32(command.ExecuteScalar() ?? 0);
        return equippable == 1;
    }

    private static bool HasWeaponRowInInventory(NpgsqlConnection connection, long userId, string code)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT 1 FROM user_inventory_items
WHERE user_id = @uid AND LOWER(weapon_code) = LOWER(@code)
LIMIT 1;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("code", code);
        return command.ExecuteScalar() != null;
    }

    private static bool HasStackableRounds(NpgsqlConnection connection, long userId, string code)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT 1
FROM user_ammo_packs uap
JOIN ammo_types at ON at.id = uap.ammo_type_id
WHERE uap.user_id = @uid AND LOWER(at.caliber) = LOWER(@code) AND uap.rounds_count > 0
LIMIT 1;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("code", code);
        return command.ExecuteScalar() != null;
    }

    private static bool IsAmmoCaliberEquippable(NpgsqlConnection connection, string caliber)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(MAX(CASE WHEN i.is_equippable THEN 1 ELSE 0 END), 0)
FROM ammo_types at
JOIN items i ON i.id = at.item_id
WHERE LOWER(at.caliber) = LOWER(@caliber);
""";
        command.Parameters.AddWithValue("caliber", caliber);
        int equippable = Convert.ToInt32(command.ExecuteScalar() ?? 0);
        return equippable == 1;
    }

    /// <summary>Resolve <c>users.id</c> from <c>users.username</c> alone (no password). Used for equips/profile keyed by login name.</summary>
    public bool TryGetUserId(string username, out long userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(username))
            return false;
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM users WHERE username = @u LIMIT 1;";
        command.Parameters.AddWithValue("u", username.Trim());
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
INSERT INTO user_inventory_items (user_id, start_slot, weapon_code, slot_width, chamber_rounds, is_equipped)
VALUES (@uid, 0, 'fist', 1, 0, TRUE);
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
SELECT id, start_slot, weapon_code, slot_width, COALESCE(chamber_rounds, 0), is_equipped
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
                ChamberRounds = Math.Max(0, reader.GetInt32(4)),
                IsEquipped = reader.GetBoolean(5)
            });
        }

        return list;
    }

    private static List<UserAmmoPackAdminDto> LoadAmmoPacks(NpgsqlConnection connection, long userId)
    {
        var list = new List<UserAmmoPackAdminDto>();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT uap.id, uap.ammo_type_id, at.item_id, at.caliber, COALESCE(i.name, at.caliber), at.unit_weight,
       COALESCE(i.quality, 100), COALESCE(i.condition, 100), COALESCE(i.icon_key, at.icon_key), COALESCE(i.inventorygrid, 1),
       COALESCE(uap.start_slot, 0), uap.packs_count, COALESCE(uap.rounds_count, 0)
FROM user_ammo_packs uap
JOIN ammo_types at ON at.id = uap.ammo_type_id
LEFT JOIN items i ON i.id = at.item_id
WHERE uap.user_id = @uid
ORDER BY LOWER(at.caliber), uap.id;
""";
        command.Parameters.AddWithValue("uid", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            int startSlot = reader.GetInt32(10);
            int packsCount = reader.GetInt32(11);
            int roundsCount = reader.GetInt32(12);
            list.Add(new UserAmmoPackAdminDto
            {
                Id = reader.GetInt64(0),
                AmmoTypeId = reader.GetInt64(1),
                ItemId = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                Caliber = reader.GetString(3),
                Name = reader.GetString(4),
                UnitWeight = reader.GetDouble(5),
                Quality = reader.GetInt32(6),
                Condition = reader.GetInt32(7),
                IconKey = reader.IsDBNull(8) ? "" : reader.GetString(8),
                InventoryGrid = reader.GetInt32(9),
                StartSlot = Math.Clamp(startSlot, 0, 11),
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
        var ammoItems = LoadAmmoPacks(connection, userId);
        var slots = new List<UserInventorySlotDto>(12);
        var occupied = new bool[12];
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
                bool isEquippable = IsItemCodeEquippable(connection, w.Code);
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
                    Continuation = !primary,
                    Stackable = false,
                    Quantity = 1,
                    ChamberRounds = primary ? Math.Clamp(item.ChamberRounds, 0, Math.Max(0, w.MagazineSize)) : 0,
                    IsEquippable = isEquippable
                };
                occupied[idx] = true;
            }
        }

        foreach (UserAmmoPackAdminDto ammo in ammoItems)
        {
            if (ammo == null)
                continue;
            int count = Math.Max(0, ammo.RoundsCount);
            if (count <= 0)
                continue;
            int idx = Math.Clamp(ammo.StartSlot, 0, 11);
            if (occupied[idx])
                continue;
            string iconKey = !string.IsNullOrWhiteSpace(ammo.IconKey) ? ammo.IconKey : ammo.Caliber;
            bool ammoEquippable = IsAmmoCaliberEquippable(connection, ammo.Caliber);
            slots[idx] = new UserInventorySlotDto
            {
                SlotIndex = idx,
                WeaponId = null,
                WeaponCode = ammo.Caliber,
                WeaponName = $"{ammo.Caliber} ammo",
                Damage = 0,
                Range = 0,
                IconKey = iconKey ?? "",
                AttackApCost = 0,
                SlotSpan = 1,
                Equipped = false,
                Continuation = false,
                Stackable = true,
                Quantity = count,
                ChamberRounds = 0,
                IsEquippable = ammoEquippable
            };
            occupied[idx] = true;
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

    public bool TryAwardBattleExperience(long userId, int expToAdd, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "user id required";
            return false;
        }
        if (expToAdd <= 0)
            return true;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE users
SET experience = GREATEST(0, experience + @exp)
WHERE id = @id;
""";
        command.Parameters.AddWithValue("exp", expToAdd);
        command.Parameters.AddWithValue("id", userId);
        int n = command.ExecuteNonQuery();
        if (n == 0)
        {
            error = "user not found";
            return false;
        }
        return true;
    }

    public bool TrySetUserDebugHp(long userId, int maxHp, int currentHp, out string? error)
    {
        error = null;
        int safeMaxHp = Math.Max(1, maxHp);
        int safeCurrentHp = Math.Clamp(currentHp, 0, safeMaxHp);
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE users
SET max_hp = @maxHp,
    current_hp = @currentHp
WHERE id = @id;
""";
        command.Parameters.AddWithValue("maxHp", safeMaxHp);
        command.Parameters.AddWithValue("currentHp", safeCurrentHp);
        command.Parameters.AddWithValue("id", userId);
        int n = command.ExecuteNonQuery();
        if (n == 0)
        {
            error = "user not found";
            return false;
        }
        return true;
    }

    /// <summary>JWT <c>jti</c> for the single active login session (stored in DB; full JWT only on clients).</summary>
    public bool TryGetActiveSessionJti(long userId, out string? jti)
    {
        jti = null;
        if (userId <= 0)
            return false;

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT active_session_jti
FROM users
WHERE id = @id
LIMIT 1;
""";
        command.Parameters.AddWithValue("id", userId);
        object? o = command.ExecuteScalar();
        if (o == null || o is DBNull)
            return false;
        string s = Convert.ToString(o) ?? "";
        if (string.IsNullOrEmpty(s))
            return false;
        jti = s;
        return true;
    }

    public void SetActiveSessionJti(long userId, string jti)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (string.IsNullOrWhiteSpace(jti))
            throw new ArgumentException("jti required", nameof(jti));

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE users
SET active_session_jti = @jti
WHERE id = @id;
""";
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("jti", jti);
        int n = command.ExecuteNonQuery();
        if (n == 0)
            throw new InvalidOperationException("User not found for session update.");
    }

    private static int ComputeLevel(int experience)
    {
        int lv = 1 + Math.Max(0, experience) / ExpPerLevel;
        return Math.Clamp(lv, 1, MaxLevel);
    }

}
