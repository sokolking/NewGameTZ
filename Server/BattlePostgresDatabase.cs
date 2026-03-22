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
    range INT NOT NULL,
    icon_key TEXT NOT NULL DEFAULT 'fist',
    attack_ap_cost INT NOT NULL DEFAULT 1
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
    ('test', 'test', 10, 100, 'fist'),
    ('test2', 'test', 10, 100, 'fist')
ON CONFLICT (username) DO UPDATE
SET password = EXCLUDED.password,
    max_hp = EXCLUDED.max_hp,
    max_ap = EXCLUDED.max_ap,
    weapon_code = EXCLUDED.weapon_code;

-- На случай уже существующих строк до смены сида:
UPDATE users SET max_ap = 100 WHERE username IN ('test', 'test2');

-- Старые БД: таблица weapons могла быть создана без icon_key / attack_ap_cost
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS icon_key TEXT NOT NULL DEFAULT 'fist';
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS attack_ap_cost INT NOT NULL DEFAULT 1;
ALTER TABLE weapons ALTER COLUMN attack_ap_cost SET DEFAULT 1;

INSERT INTO weapons (code, name, damage, range, icon_key, attack_ap_cost)
VALUES
    ('fist', 'Fist', 1, 1, 'fist', 3),
    ('stone', 'Камень', 3, 2, 'stone', 5),
    ('gun', 'Пистолет', 4, 5, 'gun', 9),
    ('revolver', 'Револьвер', 6, 4, 'revolver', 12),
    ('shotgun', 'Дробовик', 8, 3, 'shotgun', 7),
    ('rifle', 'Винтовка', 10, 6, 'rifle', 7),
    ('sniper', 'Снайперская винтовка', 12, 8, 'sniper', 7),
    ('machine_gun', 'Пулемёт', 14, 10, 'machine_gun', 7),
    ('rocket_launcher', 'Ракетница', 16, 12, 'rocket_launcher', 7),
    ('grenade_launcher', 'Гранатомёт', 18, 14, 'grenade_launcher', 7),
    ('plasma_gun', 'Плазменный пистолет', 20, 16, 'plasma_gun', 7)

ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name,
    damage = EXCLUDED.damage,
    range = EXCLUDED.range,
    icon_key = EXCLUDED.icon_key,
    attack_ap_cost = EXCLUDED.attack_ap_cost;

CREATE TABLE IF NOT EXISTS user_inventory_slots (
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    slot_index INT NOT NULL CHECK (slot_index >= 0 AND slot_index < 12),
    weapon_id BIGINT REFERENCES weapons(id) ON DELETE SET NULL,
    PRIMARY KEY (user_id, slot_index)
);

INSERT INTO user_inventory_slots (user_id, slot_index, weapon_id)
SELECT u.id, 0, w.id FROM users u CROSS JOIN weapons w WHERE w.code = 'fist'
ON CONFLICT (user_id, slot_index) DO NOTHING;

INSERT INTO user_inventory_slots (user_id, slot_index, weapon_id)
SELECT u.id, 1, w.id FROM users u CROSS JOIN weapons w WHERE w.code = 'stone'
ON CONFLICT (user_id, slot_index) DO NOTHING;

INSERT INTO user_inventory_slots (user_id, slot_index, weapon_id)
SELECT u.id, 2, w.id FROM users u CROSS JOIN weapons w WHERE w.code = 'gun'
ON CONFLICT (user_id, slot_index) DO NOTHING;

INSERT INTO user_inventory_slots (user_id, slot_index, weapon_id)
SELECT u.id, 3, w.id FROM users u CROSS JOIN weapons w WHERE w.code = 'revolver'
ON CONFLICT (user_id, slot_index) DO NOTHING;

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
