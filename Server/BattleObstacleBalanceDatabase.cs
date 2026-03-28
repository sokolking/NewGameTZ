using BattleServer.Models;
using Npgsql;

namespace BattleServer;

/// <summary>Баланс препятствий (стены, укрытия) — одна строка в БД, правьте значения без перекомпиляции.</summary>
public sealed class BattleObstacleBalanceDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleObstacleBalanceDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    /// <summary>Сохранить строку id=1 (новые бои читают через <see cref="GetBalance"/>).</summary>
    public void UpsertBalance(BattleObstacleBalanceRowDto row)
    {
        int wall = Math.Max(1, Math.Min(999, row.WallMaxHp));
        int tree = Math.Clamp(row.TreeCoverMissPercent, 0, 95);
        int rock = Math.Clamp(row.RockCoverMissPercent, 0, 95);
        int segments = Math.Clamp(row.WallSegmentsCount, 1, 50);
        int rockC = Math.Clamp(row.RockCount, 0, 200);
        int treeC = Math.Clamp(row.TreeCount, 0, 200);

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO battle_obstacle_balance (id, wall_max_hp, tree_cover_miss_percent, rock_cover_miss_percent, wall_segments_count, rock_count, tree_count)
VALUES (1, @wall, @treeMiss, @rockMiss, @segments, @rockCount, @treeCount)
ON CONFLICT (id) DO UPDATE SET
    wall_max_hp = EXCLUDED.wall_max_hp,
    tree_cover_miss_percent = EXCLUDED.tree_cover_miss_percent,
    rock_cover_miss_percent = EXCLUDED.rock_cover_miss_percent,
    wall_segments_count = EXCLUDED.wall_segments_count,
    rock_count = EXCLUDED.rock_count,
    tree_count = EXCLUDED.tree_count;
""";
        command.Parameters.AddWithValue("wall", wall);
        command.Parameters.AddWithValue("treeMiss", tree);
        command.Parameters.AddWithValue("rockMiss", rock);
        command.Parameters.AddWithValue("segments", segments);
        command.Parameters.AddWithValue("rockCount", rockC);
        command.Parameters.AddWithValue("treeCount", treeC);
        command.ExecuteNonQuery();
    }

    public BattleObstacleBalanceRowDto GetBalance()
    {
        try
        {
            using var connection = _database.DataSource.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
SELECT wall_max_hp, tree_cover_miss_percent, rock_cover_miss_percent,
       wall_segments_count, rock_count, tree_count
FROM battle_obstacle_balance
WHERE id = 1
LIMIT 1;
""";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return BattleObstacleBalanceRowDto.Defaults;

            return new BattleObstacleBalanceRowDto
            {
                WallMaxHp = Math.Max(1, reader.GetInt32(0)),
                TreeCoverMissPercent = Math.Clamp(reader.GetInt32(1), 0, 95),
                RockCoverMissPercent = Math.Clamp(reader.GetInt32(2), 0, 95),
                WallSegmentsCount = Math.Clamp(reader.GetInt32(3), 1, 50),
                RockCount = Math.Clamp(reader.GetInt32(4), 0, 200),
                TreeCount = Math.Clamp(reader.GetInt32(5), 0, 200)
            };
        }
        catch
        {
            return BattleObstacleBalanceRowDto.Defaults;
        }
    }
}
