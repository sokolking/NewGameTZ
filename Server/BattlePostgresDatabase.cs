using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BattleServer;

public sealed class BattlePostgresDatabase : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public BattlePostgresDatabase(IConfiguration configuration)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("BATTLE_DB_CONNECTION_STRING")
            ?? configuration.GetConnectionString("BattleDatabase")
            ?? "Host=localhost;Port=55432;Database=battle_server;Username=battle_user;Password=battle_password";

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        _dataSource = builder.Build();
    }

    public NpgsqlDataSource DataSource => _dataSource;

    public void EnsureCreated()
    {
        try
        {
            using var connection = _dataSource.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
CREATE TABLE IF NOT EXISTS battles (
    battle_id TEXT PRIMARY KEY,
    created_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS users (
    id BIGSERIAL PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password TEXT NOT NULL,
    max_hp INT NOT NULL DEFAULT 10,
    max_ap INT NOT NULL DEFAULT 100,
    weapon_code TEXT NOT NULL DEFAULT 'fist'
);

CREATE TABLE IF NOT EXISTS weapons (
    id BIGSERIAL PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    damage INT NOT NULL,
    range INT NOT NULL
);

CREATE TABLE IF NOT EXISTS battle_turns (
    turn_id TEXT PRIMARY KEY,
    battle_id TEXT NOT NULL REFERENCES battles(battle_id) ON DELETE CASCADE,
    turn_result_json JSONB NOT NULL,
    created_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS battle_turn_links (
    battle_id TEXT NOT NULL REFERENCES battles(battle_id) ON DELETE CASCADE,
    turn_index INT NOT NULL,
    turn_id TEXT NOT NULL REFERENCES battle_turns(turn_id) ON DELETE CASCADE,
    PRIMARY KEY (battle_id, turn_index),
    UNIQUE (turn_id)
);

CREATE INDEX IF NOT EXISTS ix_battle_turn_links_battle_id_turn_index
    ON battle_turn_links (battle_id, turn_index);

ALTER TABLE users ADD COLUMN IF NOT EXISTS max_hp INT NOT NULL DEFAULT 10;
ALTER TABLE users ADD COLUMN IF NOT EXISTS max_ap INT NOT NULL DEFAULT 100;
ALTER TABLE users ADD COLUMN IF NOT EXISTS weapon_code TEXT NOT NULL DEFAULT 'fist';

INSERT INTO users (username, password, max_hp, max_ap, weapon_code)
VALUES
    ('test', 'test', 10, 270, 'fist'),
    ('test2', 'test', 10, 39, 'fist')
ON CONFLICT (username) DO UPDATE
SET password = EXCLUDED.password,
    max_hp = EXCLUDED.max_hp,
    max_ap = EXCLUDED.max_ap,
    weapon_code = EXCLUDED.weapon_code;

INSERT INTO weapons (code, name, damage, range)
VALUES ('fist', 'Fist', 1, 1)
ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name,
    damage = EXCLUDED.damage,
    range = EXCLUDED.range;
""";
            command.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "28000")
        {
            throw new InvalidOperationException(
                "PostgreSQL refused the configured user. Start the project database with `docker compose up -d` from the repo root, " +
                "or update `BATTLE_DB_CONNECTION_STRING` / `Server/appsettings.json` to valid credentials. " +
                "Default project DB now expects localhost:55432 with user `battle_user`.",
                ex);
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                "Unable to connect to the project PostgreSQL database. Start it with `docker compose up -d` from the repo root, " +
                "or update `BATTLE_DB_CONNECTION_STRING` / `Server/appsettings.json`. " +
                "Default project DB now expects localhost:55432.",
                ex);
        }
    }

    public void Dispose()
    {
        _dataSource.Dispose();
    }
}
