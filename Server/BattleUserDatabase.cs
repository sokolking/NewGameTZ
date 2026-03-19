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
}
