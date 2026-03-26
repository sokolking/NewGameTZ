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

CREATE TABLE IF NOT EXISTS user_inventory_items (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    start_slot SMALLINT NOT NULL,
    weapon_code TEXT NOT NULL,
    slot_width SMALLINT NOT NULL DEFAULT 1,
    chamber_rounds INT NOT NULL DEFAULT 0,
    is_equipped BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT chk_user_inv_start_slot CHECK (start_slot >= 0 AND start_slot < 12),
    CONSTRAINT chk_user_inv_slot_width CHECK (slot_width IN (1, 2))
);
ALTER TABLE user_inventory_items ADD COLUMN IF NOT EXISTS chamber_rounds INT NOT NULL DEFAULT 0;
UPDATE user_inventory_items SET chamber_rounds = 0 WHERE chamber_rounds < 0;

CREATE UNIQUE INDEX IF NOT EXISTS uq_user_inventory_one_equipped
    ON user_inventory_items (user_id) WHERE is_equipped;

CREATE INDEX IF NOT EXISTS ix_user_inventory_items_user_id ON user_inventory_items (user_id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_user_inv_user_weapon_lower
    ON user_inventory_items (user_id, lower(weapon_code));

CREATE TABLE IF NOT EXISTS ammo_types (
    id BIGSERIAL PRIMARY KEY,
    caliber TEXT NOT NULL UNIQUE,
    unit_weight DOUBLE PRECISION NOT NULL DEFAULT 0,
    icon_key TEXT NOT NULL DEFAULT '',
    rounds_per_pack INT NOT NULL DEFAULT 1,
    CONSTRAINT chk_ammo_types_unit_weight CHECK (unit_weight >= 0),
    CONSTRAINT chk_ammo_types_rounds_per_pack CHECK (rounds_per_pack > 0)
);
ALTER TABLE ammo_types ADD COLUMN IF NOT EXISTS icon_key TEXT NOT NULL DEFAULT '';

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
ALTER TABLE users ADD COLUMN IF NOT EXISTS intuition INT NOT NULL DEFAULT 0;
ALTER TABLE users ADD COLUMN IF NOT EXISTS intellect INT NOT NULL DEFAULT 0;
ALTER TABLE users DROP COLUMN IF EXISTS weapon_code;

CREATE TABLE IF NOT EXISTS user_ammo_packs (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    ammo_type_id BIGINT NOT NULL REFERENCES ammo_types(id) ON DELETE RESTRICT,
    start_slot INT NOT NULL DEFAULT 0,
    packs_count INT NOT NULL DEFAULT 0,
    rounds_count INT NOT NULL DEFAULT 0,
    CONSTRAINT chk_user_ammo_packs_count CHECK (packs_count >= 0),
    CONSTRAINT uq_user_ammo_type UNIQUE (user_id, ammo_type_id)
);

CREATE INDEX IF NOT EXISTS ix_user_ammo_packs_user_id ON user_ammo_packs (user_id);
ALTER TABLE user_ammo_packs ADD COLUMN IF NOT EXISTS rounds_count INT NOT NULL DEFAULT 0;
ALTER TABLE user_ammo_packs ADD COLUMN IF NOT EXISTS start_slot INT NOT NULL DEFAULT 0;
UPDATE user_ammo_packs SET start_slot = 0 WHERE start_slot < 0 OR start_slot > 11;
UPDATE user_ammo_packs uap
SET rounds_count = GREATEST(0, uap.packs_count) * GREATEST(0, at.rounds_per_pack)
FROM ammo_types at
WHERE at.id = uap.ammo_type_id
  AND (uap.rounds_count IS NULL OR uap.rounds_count = 0);

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
    weapon_condition INT NOT NULL DEFAULT 100,
    is_sniper BOOLEAN NOT NULL DEFAULT FALSE,
    mass DOUBLE PRECISION NOT NULL DEFAULT 0,
    caliber TEXT NOT NULL DEFAULT '',
    armor_pierce INT NOT NULL DEFAULT 0,
    magazine_size INT NOT NULL DEFAULT 0,
    reload_ap_cost INT NOT NULL DEFAULT 0,
    category TEXT NOT NULL DEFAULT 'cold',
    req_level INT NOT NULL DEFAULT 1,
    req_strength INT NOT NULL DEFAULT 0,
    req_endurance INT NOT NULL DEFAULT 0,
    req_accuracy INT NOT NULL DEFAULT 0,
    req_mastery_category TEXT NOT NULL DEFAULT '',
    stat_effect_strength INT NOT NULL DEFAULT 0,
    stat_effect_endurance INT NOT NULL DEFAULT 0,
    stat_effect_accuracy INT NOT NULL DEFAULT 0,
    damage_type TEXT NOT NULL DEFAULT 'physical',
    damage_min INT NOT NULL DEFAULT 1,
    damage_max INT NOT NULL DEFAULT 1,
    burst_rounds INT NOT NULL DEFAULT 0,
    burst_ap_cost INT NOT NULL DEFAULT 0
);

ALTER TABLE weapons ADD COLUMN IF NOT EXISTS spread_penalty REAL NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS trajectory_height INT NOT NULL DEFAULT 1;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS quality INT NOT NULL DEFAULT 100;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS weapon_condition INT NOT NULL DEFAULT 100;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS is_sniper BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS mass DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS caliber TEXT NOT NULL DEFAULT '';
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS armor_pierce INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS magazine_size INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS reload_ap_cost INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS category TEXT NOT NULL DEFAULT 'cold';
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS req_level INT NOT NULL DEFAULT 1;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS req_strength INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS req_endurance INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS req_accuracy INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS req_mastery_category TEXT NOT NULL DEFAULT '';
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS stat_effect_strength INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS stat_effect_endurance INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS stat_effect_accuracy INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS damage_type TEXT NOT NULL DEFAULT 'physical';
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS damage_min INT NOT NULL DEFAULT 1;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS damage_max INT NOT NULL DEFAULT 1;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS burst_rounds INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS burst_ap_cost INT NOT NULL DEFAULT 0;
ALTER TABLE weapons ADD COLUMN IF NOT EXISTS inventory_slot_width INT NOT NULL DEFAULT 1;
UPDATE weapons SET inventory_slot_width = 1 WHERE inventory_slot_width IS NULL OR inventory_slot_width < 1 OR inventory_slot_width > 2;

UPDATE weapons SET damage_min = GREATEST(0, damage), damage_max = GREATEST(0, damage), damage = GREATEST(0, damage)
WHERE damage > 1 AND damage_min = 1 AND damage_max = 1;

CREATE TABLE IF NOT EXISTS hope_schema_migrations (id TEXT PRIMARY KEY);
WITH ins AS (
  INSERT INTO hope_schema_migrations (id) VALUES ('weapons_spread_column_is_tightness_v1')
  ON CONFLICT (id) DO NOTHING
  RETURNING 1
)
UPDATE weapons AS w
SET spread_penalty = GREATEST(0.0::real, LEAST(1.0::real, (1.0::real - w.spread_penalty)))
FROM ins
WHERE (SELECT COUNT(*) FROM weapons) >= 1;
ALTER TABLE weapons ALTER COLUMN spread_penalty SET DEFAULT 1.0;

INSERT INTO weapons (code, name, damage, range, icon_key, attack_ap_cost, spread_penalty, trajectory_height, quality, weapon_condition, is_sniper,
    mass, caliber, armor_pierce, magazine_size, reload_ap_cost, category, req_level, damage_type, damage_min, damage_max)
VALUES ('fist', 'Fist', 1, 1, 'fist', 3, 1.0, 1, 100, 100, FALSE,
    0, '', 0, 0, 0, 'cold', 1, 'physical', 1, 1)
ON CONFLICT (code) DO NOTHING;

INSERT INTO ammo_types (caliber, unit_weight, icon_key, rounds_per_pack)
SELECT DISTINCT LOWER(TRIM(w.caliber)), 0.02, '', 30
FROM weapons w
WHERE TRIM(w.caliber) <> ''
ON CONFLICT (caliber) DO NOTHING;

CREATE TABLE IF NOT EXISTS body_parts (
    id SMALLINT PRIMARY KEY CHECK (id > 0),
    code TEXT NOT NULL UNIQUE
);

INSERT INTO body_parts (id, code) VALUES
    (1, 'head'),
    (2, 'torso'),
    (3, 'legs'),
    (4, 'left_arm'),
    (5, 'right_arm')
ON CONFLICT (id) DO NOTHING;

INSERT INTO user_inventory_items (user_id, start_slot, weapon_code, slot_width, is_equipped)
SELECT u.id, 0, 'fist', 1, TRUE
FROM users u
WHERE NOT EXISTS (SELECT 1 FROM user_inventory_items x WHERE x.user_id = u.id);
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
