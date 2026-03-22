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
