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
DROP TABLE IF EXISTS user_inventory_slots CASCADE;

CREATE TABLE IF NOT EXISTS battles (
    battle_id TEXT PRIMARY KEY,
    created_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS users (
    id BIGSERIAL PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password TEXT NOT NULL,
    experience INT NOT NULL DEFAULT 0,
    strength INT NOT NULL DEFAULT 10,
    endurance INT NOT NULL DEFAULT 10,
    accuracy INT NOT NULL DEFAULT 10,
    max_hp INT NOT NULL DEFAULT 10,
    max_ap INT NOT NULL DEFAULT 100
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
ALTER TABLE users ADD COLUMN IF NOT EXISTS experience INT NOT NULL DEFAULT 0;
ALTER TABLE users ADD COLUMN IF NOT EXISTS strength INT NOT NULL DEFAULT 10;
ALTER TABLE users ADD COLUMN IF NOT EXISTS endurance INT NOT NULL DEFAULT 10;
ALTER TABLE users ADD COLUMN IF NOT EXISTS accuracy INT NOT NULL DEFAULT 10;
ALTER TABLE users DROP COLUMN IF EXISTS weapon_code;

INSERT INTO users (username, password, experience, strength, endurance, accuracy, max_hp, max_ap)
VALUES
    ('test', 'test', 0, 10, 10, 10, 20, 20),
    ('test2', 'test', 0, 10, 10, 10, 20, 20)
ON CONFLICT (username) DO UPDATE
SET password = EXCLUDED.password,
    experience = EXCLUDED.experience,
    strength = EXCLUDED.strength,
    endurance = EXCLUDED.endurance,
    accuracy = EXCLUDED.accuracy,
    max_hp = EXCLUDED.max_hp,
    max_ap = EXCLUDED.max_ap;

UPDATE users
SET
    max_hp = GREATEST(1, strength * 2),
    max_ap = GREATEST(1, endurance * 2);

CREATE TABLE IF NOT EXISTS battle_obstacle_balance (
    id INT PRIMARY KEY,
    wall_max_hp INT NOT NULL DEFAULT 5,
    tree_cover_miss_percent INT NOT NULL DEFAULT 15,
    rock_cover_miss_percent INT NOT NULL DEFAULT 20,
    wall_segments_count INT NOT NULL DEFAULT 10,
    rock_count INT NOT NULL DEFAULT 5,
    tree_count INT NOT NULL DEFAULT 5
);

INSERT INTO battle_obstacle_balance (id, wall_max_hp, tree_cover_miss_percent, rock_cover_miss_percent, wall_segments_count, rock_count, tree_count)
SELECT 1, 5, 15, 20, 10, 5, 5
WHERE NOT EXISTS (SELECT 1 FROM battle_obstacle_balance WHERE id = 1);

CREATE TABLE IF NOT EXISTS weapons (
    id BIGSERIAL PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    damage INT NOT NULL DEFAULT 1,
    range INT NOT NULL DEFAULT 1,
    icon_key TEXT NOT NULL DEFAULT '',
    attack_ap_cost INT NOT NULL DEFAULT 3,
    spread_penalty REAL NOT NULL DEFAULT 0,
    trajectory_height INT NOT NULL DEFAULT 1,
    quality INT NOT NULL DEFAULT 100,
    weapon_condition INT NOT NULL DEFAULT 100
);

ALTER TABLE weapons ADD COLUMN IF NOT EXISTS spread_penalty REAL NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS trajectory_height INT NOT NULL DEFAULT 1;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS quality INT NOT NULL DEFAULT 100;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS weapon_condition INT NOT NULL DEFAULT 100;

INSERT INTO weapons (code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition)
VALUES ('fist', 'Fist', 1, 1, 'fist', 3, 0, 1, 100, 100)
ON CONFLICT (code) DO NOTHING;
""";
            command.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "28000" || ex.SqlState == "28P01")
        {
            throw new InvalidOperationException(
                "PostgreSQL refused login for the configured user (wrong password or user missing). " +
                "Set env `BATTLE_DB_CONNECTION_STRING` to match your server (Host=...;Port=...;Database=...;Username=...;Password=...), " +
                "or align Postgres user/password with `Server/appsettings.json` / docker-compose (`battle_user` / `battle_password` on port 55432). " +
                "SqlState: " + ex.SqlState,
                ex);
        }
        catch (NpgsqlException ex)
        {
            var inner = ex.InnerException as PostgresException;
            string hint = inner?.SqlState == "28P01"
                ? " Password authentication failed — fix BATTLE_DB_CONNECTION_STRING Password or Postgres role password."
                : "";
            throw new InvalidOperationException(
                "Unable to connect to PostgreSQL." + hint +
                " Start DB with `docker compose up -d` or set `BATTLE_DB_CONNECTION_STRING` / `ConnectionStrings:BattleDatabase`.",
                ex);
        }
    }

    public void Dispose()
    {
        _dataSource.Dispose();
    }
}
