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
    private readonly BattleMedicineDatabase _medicine;

    public BattleUserDatabase(BattlePostgresDatabase database, BattleWeaponDatabase weapons, BattleMedicineDatabase medicine)
    {
        _database = database;
        _weapons = weapons;
        _medicine = medicine;
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
       eq.equipped_item_id,
       COALESCE(i.name, '') AS equipped_item_name
FROM users u
LEFT JOIN LATERAL (
    SELECT COALESCE(u.equipped_item_id, (
        SELECT ii.item_id
        FROM user_inventory_items ii
        WHERE ii.user_id = u.id AND ii.is_equipped
        LIMIT 1
    )) AS equipped_item_id
) eq ON TRUE
LEFT JOIN items i ON i.id = eq.equipped_item_id
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
                EquippedItemId = reader.IsDBNull(10) ? null : reader.GetInt64(10),
                EquippedItemDisplay = reader.IsDBNull(10)
                    ? ""
                    : $"{reader.GetInt64(10)}({(reader.IsDBNull(11) ? "" : reader.GetString(11))})"
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
SELECT experience, strength, endurance, accuracy, max_hp, current_hp, max_ap
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
        int maxApFromDb = reader.GetInt32(6);
        level = ComputeLevel(exp);
        PlayerLevelStatsRow stats = PlayerLevelStatsTable.GetForLevel(level);
        maxHp = Math.Max(1, hpFromDb);
        currentHp = Math.Clamp(currentHpFromDb, 0, maxHp);
        maxAp = Math.Max(1, maxApFromDb);
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
SELECT experience, strength, endurance, accuracy, max_hp, current_hp, max_ap
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
        int maxApFromDb = reader.GetInt32(6);
        level = ComputeLevel(exp);
        PlayerLevelStatsRow stats = PlayerLevelStatsTable.GetForLevel(level);
        maxHp = Math.Max(1, hpFromDb);
        currentHp = Math.Clamp(currentHpFromDb, 0, maxHp);
        maxAp = Math.Max(1, maxApFromDb);
        accuracy = Math.Max(0, stats.Accuracy);
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
        long? equippedItemId = null;
        using (var equippedCmd = connection.CreateCommand())
        {
            equippedCmd.CommandText = """
SELECT equipped_item_id
FROM users
WHERE id = @uid
LIMIT 1;
""";
            equippedCmd.Parameters.AddWithValue("uid", userId);
            object? scalar = equippedCmd.ExecuteScalar();
            if (scalar != null && scalar != DBNull.Value)
                equippedItemId = scalar is long l ? l : Convert.ToInt64(scalar);
        }
        var inv = LoadInventoryItems(connection, userId);
        foreach (var x in inv)
        {
            if (_medicine.TryGetMedicineByItemId(x.ItemId, out var med))
            {
                bool isEquippable = med.IsEquippable;
                items.Add(new UserItemAdminDto
                {
                    ItemId = x.ItemId > 0 ? x.ItemId : null,
                    AmmoTypeId = null,
                    ItemType = "medicine",
                    Name = med.Name,
                    IconKey = med.IconKey,
                    Quality = med.Quality,
                    Condition = med.Condition,
                    Mass = med.Mass,
                    InventoryGrid = Math.Clamp(med.InventoryGrid, 0, 2),
                    Quantity = Math.Max(1, x.Rounds > 0 ? x.Rounds : 1),
                    ChamberRounds = 0,
                    StartSlot = x.StartSlot,
                    IsEquipped = isEquippable && x.ItemId > 0 && equippedItemId.HasValue && x.ItemId == equippedItemId.Value,
                    IsEquippable = isEquippable,
                    IsStackable = false,
                    SlotWidth = med.InventorySlotWidth >= 2 ? 2 : 1
                });
                continue;
            }

            if (_weapons.TryGetWeaponByItemId(x.ItemId, out var wpn))
            {
                bool isEquippable = wpn.IsEquippable;
                items.Add(new UserItemAdminDto
                {
                    ItemId = x.ItemId > 0 ? x.ItemId : null,
                    AmmoTypeId = null,
                    ItemType = "weapon",
                    Name = wpn.Name,
                    IconKey = wpn.IconKey,
                    Quality = wpn.Quality,
                    Condition = wpn.WeaponCondition,
                    Mass = wpn.Mass,
                    InventoryGrid = Math.Clamp(wpn.InventoryGrid, 0, 2),
                    Quantity = Math.Max(1, x.Rounds > 0 ? x.Rounds : 1),
                    ChamberRounds = Math.Max(0, x.ChamberRounds),
                    StartSlot = x.StartSlot,
                    IsEquipped = isEquippable && x.ItemId > 0 && equippedItemId.HasValue && x.ItemId == equippedItemId.Value,
                    IsEquippable = isEquippable,
                    IsStackable = false,
                    SlotWidth = x.SlotWidth >= 2 ? 2 : 1
                });
            }
        }

        var stacks = LoadAmmoPacks(connection, userId);
        foreach (var x in stacks)
        {
            bool isEquippable = IsStackItemEquippable(connection, x.ItemId);
            string stackType = BattleWeaponDatabase.NormalizeItemType(x.ItemType, "ammo");
            items.Add(new UserItemAdminDto
            {
                ItemId = x.ItemId > 0 ? x.ItemId : null,
                AmmoTypeId = string.Equals(stackType, "ammo", StringComparison.OrdinalIgnoreCase) ? x.AmmoTypeId : null,
                ItemType = stackType,
                Name = x.Name,
                IconKey = x.IconKey,
                Quality = x.Quality,
                Condition = x.Condition,
                Mass = x.UnitWeight,
                InventoryGrid = Math.Clamp(x.InventoryGrid, 0, 2),
                Quantity = Math.Max(0, x.RoundsCount),
                ChamberRounds = 0,
                StartSlot = Math.Clamp(x.StartSlot, 0, 11),
                IsEquipped = isEquippable && x.ItemId > 0 && equippedItemId.HasValue && x.ItemId == equippedItemId.Value,
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

        var weaponRows = new List<(int start, long itemId, int width, int rounds, int chamber, bool eq)>();
        var ammoRows = new List<(long itemId, int startSlot, int rounds)>();
        var equippableUpdates = new List<(long itemId, bool isEquippable)>();
        var weaponItemIdsSeen = new HashSet<long>();
        var stackItemIdsSeen = new HashSet<long>();
        int equippedCount = 0;
        long equippedItemId = 0;
        bool hasFist = false;
        foreach (var row in rawItems)
        {
            string type = (row.ItemType ?? "").Trim().ToLowerInvariant();
            if (type == "weapon")
            {
                long weaponItemId = row.ItemId ?? 0;
                if (weaponItemId <= 0)
                {
                    error = "weapon itemId required for each weapon item";
                    return false;
                }
                if (!weaponItemIdsSeen.Add(weaponItemId))
                {
                    error = "duplicate weapon itemId in items";
                    return false;
                }
                if (_weapons.TryGetWeaponByItemId(weaponItemId, out var wpn))
                {
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
                        equippedItemId = row.ItemId ?? 0;
                    }
                    if (string.Equals(wpn.Name, "Fist", StringComparison.OrdinalIgnoreCase))
                        hasFist = true;
                    int chamber = Math.Max(0, row.ChamberRounds);
                    if (wpn.MagazineSize <= 0 || wpn.AmmoTypeId == null || wpn.AmmoTypeId.Value <= 0)
                        chamber = 0;
                    else
                        chamber = Math.Clamp(chamber, 0, Math.Max(0, wpn.MagazineSize));
                    int rounds = Math.Max(0, row.Quantity);
                    weaponRows.Add((start, weaponItemId, width, rounds, chamber, eq));
                    equippableUpdates.Add((weaponItemId, row.IsEquippable));
                    continue;
                }
                if (_medicine.TryGetMedicineByItemId(weaponItemId, out var med))
                {
                    int width = med.InventorySlotWidth >= 2 ? 2 : 1;
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
                        equippedItemId = row.ItemId ?? 0;
                    }
                    int rounds = Math.Max(0, row.Quantity);
                    weaponRows.Add((start, weaponItemId, width, rounds, 0, eq));
                    equippableUpdates.Add((weaponItemId, row.IsEquippable));
                    continue;
                }
                error = "unknown weapon itemId: " + weaponItemId;
                return false;
            }

            if (type == "ammo" || type == "medicine")
            {
                long stackItemId = type == "medicine"
                    ? (row.ItemId ?? 0)
                    : (row.AmmoTypeId ?? row.ItemId ?? 0);
                if (stackItemId <= 0)
                {
                    error = type == "medicine"
                        ? "itemId required for each medicine item"
                        : "ammoTypeId required for each ammo item";
                    return false;
                }
                bool hasItem;
                using (var chk = _database.DataSource.OpenConnection())
                using (var chkCmd = chk.CreateCommand())
                {
                    chkCmd.CommandText = """
SELECT 1
FROM items
WHERE id = @id
  AND (@expectedType = '' OR type = @expectedType)
LIMIT 1;
""";
                    chkCmd.Parameters.AddWithValue("id", stackItemId);
                    chkCmd.Parameters.AddWithValue("expectedType", type);
                    hasItem = chkCmd.ExecuteScalar() != null;
                }
                if (!hasItem)
                {
                    error = type == "medicine"
                        ? "unknown medicine itemId: " + stackItemId
                        : "unknown ammoTypeId: " + stackItemId;
                    return false;
                }
                long ammoItemId = stackItemId;
                if (!stackItemIdsSeen.Add(ammoItemId))
                {
                    error = "duplicate stackable itemId: " + ammoItemId;
                    return false;
                }
                int startSlot = row.StartSlot;
                if (startSlot < 0 || startSlot > 11)
                {
                    error = "stackable item startSlot must be 0..11";
                    return false;
                }
                int rounds = Math.Max(0, row.Quantity);
                if (rounds > 0)
                    ammoRows.Add((ammoItemId, startSlot, rounds));
                if (row.IsEquipped && !row.IsEquippable)
                {
                    error = "equipped requires isEquippable=true";
                    return false;
                }
                if (row.IsEquipped)
                {
                    equippedCount++;
                    equippedItemId = ammoItemId;
                }
                equippableUpdates.Add((ammoItemId, row.IsEquippable));
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
        foreach (var row in weaponRows)
        {
            using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
INSERT INTO user_inventory_items (user_id, item_id, start_slot, slot_width, rounds, chamber_rounds, is_equipped)
VALUES (
    @uid,
    @itemId,
    @start,
    @width,
    @rounds,
    @chamber,
    @eq
);
""";
            ins.Parameters.AddWithValue("uid", userId);
            ins.Parameters.AddWithValue("itemId", row.itemId);
            ins.Parameters.AddWithValue("start", row.start);
            ins.Parameters.AddWithValue("width", row.width);
            ins.Parameters.AddWithValue("rounds", row.rounds);
            ins.Parameters.AddWithValue("chamber", row.chamber);
            ins.Parameters.AddWithValue("eq", row.eq);
            ins.ExecuteNonQuery();
        }

        foreach (var update in equippableUpdates)
        {
            using var eq = connection.CreateCommand();
            eq.Transaction = tx;
            eq.CommandText = """
UPDATE items
SET is_equippable = @isEquippable
WHERE id = @itemId;
""";
            eq.Parameters.AddWithValue("isEquippable", update.isEquippable);
            eq.Parameters.AddWithValue("itemId", update.itemId);
            eq.ExecuteNonQuery();
        }

        foreach (var row in ammoRows)
        {
            using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
INSERT INTO user_inventory_items (user_id, item_id, start_slot, slot_width, rounds, chamber_rounds, is_equipped)
SELECT @uid, @itemId, @startSlot, 1, @rounds, 0, FALSE
FROM items i
WHERE i.id = @itemId;
""";
            ins.Parameters.AddWithValue("uid", userId);
            ins.Parameters.AddWithValue("itemId", row.itemId);
            ins.Parameters.AddWithValue("startSlot", row.startSlot);
            ins.Parameters.AddWithValue("rounds", row.rounds);
            ins.ExecuteNonQuery();
        }

        using (var equipped = connection.CreateCommand())
        {
            equipped.Transaction = tx;
            if (equippedItemId <= 0)
            {
                using var fallback = connection.CreateCommand();
                fallback.Transaction = tx;
                fallback.CommandText = """
SELECT i.id
FROM items i
WHERE LOWER(i.name) = 'fist'
LIMIT 1;
""";
                object? scalar = fallback.ExecuteScalar();
                if (scalar != null)
                    equippedItemId = scalar is long l ? l : Convert.ToInt64(scalar);
            }
            equipped.CommandText = "UPDATE users SET equipped_item_id = @itemId WHERE id = @uid;";
            equipped.Parameters.AddWithValue("uid", userId);
            equipped.Parameters.AddWithValue("itemId", equippedItemId > 0 ? equippedItemId : DBNull.Value);
            equipped.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }

    public bool TryGetUserItemQuantityByItemId(long userId, long itemId, out int qty)
    {
        qty = 0;
        if (userId <= 0 || itemId <= 0)
            return false;
        using var connection = _database.DataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT COALESCE(rounds, 0)
FROM user_inventory_items
WHERE user_id = @uid AND item_id = @itemId
LIMIT 1;
""";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("itemId", itemId);
        object? scalar = cmd.ExecuteScalar();
        qty = scalar == null ? 0 : Math.Max(0, Convert.ToInt32(scalar));
        return true;
    }

    public bool TrySetUserItemQuantityByItemId(long userId, long itemId, int qty, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "user id required";
            return false;
        }
        if (itemId <= 0)
            return true;
        int safeQty = Math.Max(0, qty);
        using var connection = _database.DataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
UPDATE user_inventory_items
SET rounds = @rounds
WHERE user_id = @uid AND item_id = @itemId;

INSERT INTO user_inventory_items (user_id, item_id, start_slot, slot_width, rounds, chamber_rounds, is_equipped)
SELECT @uid, @itemId, 0, 1, @rounds, 0, FALSE
FROM items i
WHERE i.id = @itemId
  AND NOT EXISTS (
      SELECT 1
      FROM user_inventory_items uii
      WHERE uii.user_id = @uid AND uii.item_id = @itemId
  );
""";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("rounds", safeQty);
        cmd.ExecuteNonQuery();
        return true;
    }

    public bool TryGetUserAmmoRoundsByAmmoTypeId(long userId, long ammoTypeId, out int rounds) =>
        TryGetUserItemQuantityByItemId(userId, ammoTypeId, out rounds);

    public bool TrySetUserAmmoRoundsByAmmoTypeId(long userId, long ammoTypeId, int rounds, out string? error) =>
        TrySetUserItemQuantityByItemId(userId, ammoTypeId, rounds, out error);

    public bool TryConsumeUserItemQuantityByItemId(long userId, long itemId, int amount, out string? error)
    {
        error = null;
        int safeAmount = Math.Max(0, amount);
        if (safeAmount <= 0 || itemId <= 0)
            return true;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }
        TryGetUserItemQuantityByItemId(userId, itemId, out int current);
        int next = Math.Max(0, current - safeAmount);
        return TrySetUserItemQuantityByItemId(userId, itemId, next, out error);
    }

    /// <summary>
    /// Medicine uses <c>user_inventory_items.rounds</c> as stack qty; weapon-slot medkits often have <c>rounds = 0</c> — treat as 1 use while the row exists.
    /// </summary>
    public bool TryGetMedicineUseQuantity(long userId, long itemId, out int qty)
    {
        qty = 0;
        if (userId <= 0 || itemId <= 0)
            return false;
        using var connection = _database.DataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT COALESCE(uii.rounds, 0), COALESCE(i.type, '')
FROM user_inventory_items uii
JOIN items i ON i.id = uii.item_id
WHERE uii.user_id = @uid AND uii.item_id = @itemId
LIMIT 1;
""";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("itemId", itemId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return false;
        int rounds = Math.Max(0, reader.GetInt32(0));
        string type = reader.IsDBNull(1) ? "" : reader.GetString(1);
        if (!string.Equals(type, "medicine", StringComparison.OrdinalIgnoreCase))
            return false;
        qty = rounds > 0 ? rounds : 1;
        return true;
    }

    /// <summary>Decrements medicine stack or removes a single-use weapon-slot row (<c>rounds = 0</c>).</summary>
    public bool TryConsumeMedicineUse(long userId, long itemId, int amount, out string? error)
    {
        error = null;
        int safeAmount = Math.Max(0, amount);
        if (safeAmount <= 0 || itemId <= 0)
            return true;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }

        string itemType = "";
        using (var conn = _database.DataSource.OpenConnection())
        using (var tcmd = conn.CreateCommand())
        {
            tcmd.CommandText = "SELECT COALESCE(type, '') FROM items WHERE id = @id LIMIT 1;";
            tcmd.Parameters.AddWithValue("id", itemId);
            object? ts = tcmd.ExecuteScalar();
            if (ts != null && ts != DBNull.Value)
                itemType = ts.ToString() ?? "";
        }

        if (!string.Equals(itemType, "medicine", StringComparison.OrdinalIgnoreCase))
            return TryConsumeUserItemQuantityByItemId(userId, itemId, safeAmount, out error);

        TryGetUserItemQuantityByItemId(userId, itemId, out int current);
        if (current >= safeAmount)
            return TrySetUserItemQuantityByItemId(userId, itemId, Math.Max(0, current - safeAmount), out error);
        if (current == 0 && safeAmount > 0)
        {
            using var connection = _database.DataSource.OpenConnection();
            using var del = connection.CreateCommand();
            del.CommandText = """
DELETE FROM user_inventory_items uii
USING items i
WHERE uii.user_id = @uid AND uii.item_id = @itemId
  AND i.id = uii.item_id
  AND i.type = 'medicine'
  AND COALESCE(uii.rounds, 0) = 0;
""";
            del.Parameters.AddWithValue("uid", userId);
            del.Parameters.AddWithValue("itemId", itemId);
            int n = del.ExecuteNonQuery();
            if (n > 0)
                return true;
            error = "no medicine to consume";
            return false;
        }

        error = "not enough medicine quantity";
        return false;
    }

    public bool TryConsumeUserItemQuantity(long userId, string itemName, int amount, out string? error)
    {
        error = null;
        int safeAmount = Math.Max(0, amount);
        if (safeAmount <= 0 || string.IsNullOrWhiteSpace(itemName))
            return true;
        if (userId <= 0)
        {
            error = "invalid user id";
            return false;
        }
        long itemId = 0;
        using (var conn = _database.DataSource.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM items WHERE LOWER(name) = LOWER(@name) LIMIT 1;";
            cmd.Parameters.AddWithValue("name", itemName.Trim());
            object? scalar = cmd.ExecuteScalar();
            if (scalar != null)
                itemId = scalar is long l ? l : Convert.ToInt64(scalar);
        }
        if (itemId <= 0)
        {
            error = "item quantity not found";
            return false;
        }

        return TryConsumeUserItemQuantityByItemId(userId, itemId, safeAmount, out error);
    }

    public bool TryValidateEquippedWeaponForRegisteredUserByItemId(long userId, long weaponItemId, out string? error)
    {
        error = null;
        if (userId <= 0)
            return true;
        if (weaponItemId <= 0)
        {
            error = "weapon item id required";
            return false;
        }
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
  EXISTS(SELECT 1 FROM items i WHERE i.id = @itemId AND i.is_equippable) AS equippable,
  EXISTS(SELECT 1 FROM user_inventory_items ii WHERE ii.user_id = @uid AND ii.item_id = @itemId) AS in_inventory;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("itemId", weaponItemId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            error = "item lookup failed";
            return false;
        }
        bool equippable = !reader.IsDBNull(0) && reader.GetBoolean(0);
        bool inInventory = !reader.IsDBNull(1) && reader.GetBoolean(1);
        if (!equippable)
        {
            error = "item cannot be equipped";
            return false;
        }
        if (!inInventory)
        {
            error = "item not in inventory";
            return false;
        }
        return true;
    }

    public void SyncEquippedWeaponForRegisteredUserByItemId(long userId, long weaponItemId)
    {
        if (userId <= 0 || weaponItemId <= 0)
            return;
        using var connection = _database.DataSource.OpenConnection();
        using var tx = connection.BeginTransaction();
        using (var selected = connection.CreateCommand())
        {
            selected.Transaction = tx;
            selected.CommandText = "UPDATE users SET equipped_item_id = @itemId WHERE id = @uid;";
            selected.Parameters.AddWithValue("uid", userId);
            selected.Parameters.AddWithValue("itemId", weaponItemId);
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
WHERE user_id = @uid AND item_id = @itemId;
""";
            equip.Parameters.AddWithValue("uid", userId);
            equip.Parameters.AddWithValue("itemId", weaponItemId);
            equip.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public bool TrySetUserWeaponChamberRoundsByItemId(long userId, long weaponItemId, int chamberRounds, out string? error)
    {
        error = null;
        if (userId <= 0)
        {
            error = "user id required";
            return false;
        }
        if (weaponItemId <= 0)
            return true;
        int rounds = Math.Max(0, chamberRounds);
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE user_inventory_items
SET chamber_rounds = @rounds
WHERE user_id = @uid AND item_id = @itemId;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("itemId", weaponItemId);
        command.Parameters.AddWithValue("rounds", rounds);
        command.ExecuteNonQuery();
        return true;
    }

    public bool TryGetUserWeaponChamberRoundsByItemId(long userId, long weaponItemId, out int chamberRounds)
    {
        chamberRounds = 0;
        if (userId <= 0 || weaponItemId <= 0)
            return false;
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(chamber_rounds, 0)
FROM user_inventory_items
WHERE user_id = @uid AND item_id = @itemId
LIMIT 1;
""";
        command.Parameters.AddWithValue("uid", userId);
        command.Parameters.AddWithValue("itemId", weaponItemId);
        object? scalar = command.ExecuteScalar();
        if (scalar == null)
            return false;
        chamberRounds = Math.Max(0, Convert.ToInt32(scalar));
        return true;
    }

    public bool TryGetEquippedWeaponItemIdForUserByUserId(long userId, out long weaponItemId)
    {
        weaponItemId = 0;
        if (userId <= 0)
            return false;
        EnsureUserInventoryBaseline(userId);
        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(
    u.equipped_item_id,
    (SELECT ii.item_id FROM user_inventory_items ii WHERE ii.user_id = u.id AND ii.is_equipped LIMIT 1),
    (SELECT w.item_id FROM weapons w WHERE LOWER(COALESCE(w.name, '')) = 'fist' LIMIT 1)
)
FROM users u
WHERE u.id = @uid
LIMIT 1;
""";
        command.Parameters.AddWithValue("uid", userId);
        object? scalar = command.ExecuteScalar();
        if (scalar == null || scalar is DBNull)
            return false;
        weaponItemId = Convert.ToInt64(scalar);
        return weaponItemId > 0;
    }

    private static bool UserExists(NpgsqlConnection connection, long userId, NpgsqlTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT 1 FROM users WHERE id = @id LIMIT 1;";
        command.Parameters.AddWithValue("id", userId);
        return command.ExecuteScalar() != null;
    }

    private static bool IsItemNameEquippable(NpgsqlConnection connection, string itemName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(MAX(CASE WHEN i.is_equippable THEN 1 ELSE 0 END), 0)
FROM items i
WHERE LOWER(i.name) = LOWER(@code);
""";
        command.Parameters.AddWithValue("code", itemName);
        int equippable = Convert.ToInt32(command.ExecuteScalar() ?? 0);
        return equippable == 1;
    }

    private static bool HasStackableRounds(NpgsqlConnection connection, long userId, string code)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT 1
FROM user_inventory_items uii
JOIN items i ON i.id = uii.item_id
WHERE uii.user_id = @uid AND LOWER(i.name) = LOWER(@code) AND COALESCE(uii.rounds, 0) > 0
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
FROM items i
WHERE LOWER(i.name) = LOWER(@caliber)
  AND i.type IN ('ammo', 'medicine');
""";
        command.Parameters.AddWithValue("caliber", caliber);
        int equippable = Convert.ToInt32(command.ExecuteScalar() ?? 0);
        return equippable == 1;
    }

    private static bool IsStackItemEquippable(NpgsqlConnection connection, long itemId)
    {
        if (itemId <= 0)
            return false;
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COALESCE(i.is_equippable, FALSE)
FROM items i
WHERE i.id = @itemId
LIMIT 1;
""";
        command.Parameters.AddWithValue("itemId", itemId);
        object? scalar = command.ExecuteScalar();
        return scalar != null && scalar != DBNull.Value && Convert.ToBoolean(scalar);
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
INSERT INTO user_inventory_items (user_id, item_id, start_slot, slot_width, rounds, chamber_rounds, is_equipped)
SELECT @uid, w.item_id, 0, 1, 0, 0, TRUE
FROM weapons w
WHERE LOWER(COALESCE(w.name, '')) = 'fist'
LIMIT 1;
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
SELECT id, COALESCE(item_id, 0), start_slot, slot_width, COALESCE(rounds, 0), COALESCE(chamber_rounds, 0), is_equipped
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
                ItemId = reader.GetInt64(1),
                StartSlot = reader.GetInt32(2),
                SlotWidth = reader.GetInt32(3),
                Rounds = Math.Max(0, reader.GetInt32(4)),
                ChamberRounds = Math.Max(0, reader.GetInt32(5)),
                IsEquipped = reader.GetBoolean(6)
            });
        }

        return list;
    }

    private static List<UserAmmoPackAdminDto> LoadAmmoPacks(NpgsqlConnection connection, long userId)
    {
        var list = new List<UserAmmoPackAdminDto>();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT uii.id, uii.item_id, i.id, i.name, i.name, COALESCE(i.mass, 0),
       COALESCE(i.quality, 100), COALESCE(i.condition, 100), COALESCE(i.icon_key, ''), COALESCE(i.inventorygrid, 1),
       COALESCE(i.type, 'ammo'), COALESCE(uii.start_slot, 0), 0, COALESCE(uii.rounds, 0)
FROM user_inventory_items uii
JOIN items i ON i.id = uii.item_id
WHERE uii.user_id = @uid
  AND i.type = 'ammo'
  AND COALESCE(uii.rounds, 0) > 0
ORDER BY LOWER(i.name), uii.id;
""";
        command.Parameters.AddWithValue("uid", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string itemType = reader.IsDBNull(10) ? "ammo" : reader.GetString(10);
            int startSlot = reader.GetInt32(11);
            int packsCount = reader.GetInt32(12);
            int roundsCount = reader.GetInt32(13);
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
                ItemType = BattleWeaponDatabase.NormalizeItemType(itemType, "ammo"),
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
            // Medicine before weapons: same item_id may exist in both tables; stack uses user_inventory_items.rounds.
            if (_medicine.TryGetMedicineByItemId(item.ItemId, out var med))
            {
                int medWidth = med.InventorySlotWidth >= 2 ? 2 : 1;
                for (int k = 0; k < medWidth; k++)
                {
                    int idx = item.StartSlot + k;
                    if (idx < 0 || idx >= 12)
                        continue;
                    bool primary = k == 0;
                    bool isEquippable = IsItemNameEquippable(connection, med.Name);
                    int medQty = Math.Max(0, item.Rounds);
                    if (medQty <= 0)
                        medQty = 1;
                    int invRounds = Math.Max(0, item.Rounds);
                    slots[idx] = new UserInventorySlotDto
                    {
                        SlotIndex = idx,
                        ItemId = item.ItemId > 0 ? item.ItemId : null,
                        ItemName = med.Name,
                        ItemType = "medicine",
                        DamageMin = 0,
                        DamageMax = 0,
                        Range = 0,
                        IconKey = med.IconKey,
                        UseApCost = med.AttackApCost,
                        ReloadApCost = 0,
                        AmmoTypeId = null,
                        MagazineSize = 0,
                        SlotSpan = primary ? medWidth : 0,
                        Equipped = primary && item.IsEquipped,
                        Continuation = !primary,
                        Stackable = false,
                        Quantity = medQty,
                        Rounds = invRounds,
                        ChamberRounds = 0,
                        IsEquippable = isEquippable
                    };
                    occupied[idx] = true;
                }

                continue;
            }

            if (_weapons.TryGetWeaponByItemId(item.ItemId, out var w))
            {
                int width = item.SlotWidth >= 2 ? 2 : 1;
                for (int k = 0; k < width; k++)
                {
                    int idx = item.StartSlot + k;
                    if (idx < 0 || idx >= 12)
                        continue;
                    bool primary = k == 0;
                    bool isEquippable = IsItemNameEquippable(connection, w.Name);
                    slots[idx] = new UserInventorySlotDto
                    {
                        SlotIndex = idx,
                        ItemId = item.ItemId > 0 ? item.ItemId : null,
                        ItemName = w.Name,
                        ItemType = BattleWeaponDatabase.NormalizeItemType(w.ItemType, "weapon"),
                        DamageMin = w.DamageMin,
                        DamageMax = w.DamageMax,
                        Range = w.Range,
                        IconKey = w.IconKey,
                        UseApCost = w.AttackApCost,
                        ReloadApCost = w.ReloadApCost,
                        AmmoTypeId = w.AmmoTypeId,
                        MagazineSize = Math.Max(0, w.MagazineSize),
                        SlotSpan = primary ? width : 0,
                        Equipped = primary && item.IsEquipped,
                        Continuation = !primary,
                        Stackable = false,
                        Quantity = 1,
                        Rounds = Math.Max(0, item.Rounds),
                        ChamberRounds = primary ? Math.Clamp(item.ChamberRounds, 0, Math.Max(0, w.MagazineSize)) : 0,
                        IsEquippable = isEquippable
                    };
                    occupied[idx] = true;
                }
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
            string ammoCaliber = ammo.Caliber ?? "";
            string iconKey = !string.IsNullOrWhiteSpace(ammo.IconKey) ? ammo.IconKey : ammoCaliber;
            bool ammoEquippable = IsStackItemEquippable(connection, ammo.ItemId);
            slots[idx] = new UserInventorySlotDto
            {
                SlotIndex = idx,
                ItemId = ammo.ItemId > 0 ? ammo.ItemId : null,
                ItemName = string.IsNullOrWhiteSpace(ammo.Name) ? ammoCaliber : ammo.Name,
                ItemType = BattleWeaponDatabase.NormalizeItemType(ammo.ItemType, "ammo"),
                DamageMin = 0,
                DamageMax = 0,
                Range = 0,
                IconKey = iconKey ?? "",
                UseApCost = 0,
                ReloadApCost = 0,
                AmmoTypeId = ammo.AmmoTypeId > 0 ? ammo.AmmoTypeId : ammo.ItemId > 0 ? ammo.ItemId : null,
                MagazineSize = 0,
                SlotSpan = 1,
                Equipped = false,
                Continuation = false,
                Stackable = true,
                Quantity = count,
                Rounds = count,
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

        if (req.Experience < 0 || req.Strength < 0 || req.Endurance < 0 || req.Accuracy < 0 || req.MaxAp < 1)
        {
            error = "experience/stats must be >= 0 and maxAp must be >= 1";
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
        int maxAp = Math.Max(1, req.MaxAp);
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
