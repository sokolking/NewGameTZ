using BattleServer.Models;
using Npgsql;

namespace BattleServer;

/// <summary>Параметры сужения поля в бою — одна строка в БД (id=1).</summary>
public sealed class BattleZoneShrinkDatabase
{
    private readonly BattlePostgresDatabase _database;

    public BattleZoneShrinkDatabase(BattlePostgresDatabase database)
    {
        _database = database;
    }

    public void UpsertSettings(BattleZoneShrinkRowDto row)
    {
        int startR = Math.Max(1, Math.Min(9999, row.ShrinkStartRound));
        int hInt = Math.Max(1, Math.Min(999, row.HorizontalShrinkInterval));
        int hAmt = Math.Max(0, Math.Min(50, row.HorizontalShrinkAmount));
        int vInt = Math.Max(1, Math.Min(999, row.VerticalShrinkInterval));
        int vAmt = Math.Max(0, Math.Min(50, row.VerticalShrinkAmount));
        int minW = Math.Max(1, Math.Min(HexSpawn.DefaultGridWidth, row.MinWidth));
        int minH = Math.Max(1, Math.Min(HexSpawn.DefaultGridLength, row.MinHeight));

        using var connection = _database.DataSource.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO battle_zone_shrink (id, shrink_start_round, horizontal_shrink_interval, horizontal_shrink_amount, vertical_shrink_interval, vertical_shrink_amount, min_width, min_height)
VALUES (1, @sr, @hi, @ha, @vi, @va, @mw, @mh)
ON CONFLICT (id) DO UPDATE SET
    shrink_start_round = EXCLUDED.shrink_start_round,
    horizontal_shrink_interval = EXCLUDED.horizontal_shrink_interval,
    horizontal_shrink_amount = EXCLUDED.horizontal_shrink_amount,
    vertical_shrink_interval = EXCLUDED.vertical_shrink_interval,
    vertical_shrink_amount = EXCLUDED.vertical_shrink_amount,
    min_width = EXCLUDED.min_width,
    min_height = EXCLUDED.min_height;
""";
        command.Parameters.AddWithValue("sr", startR);
        command.Parameters.AddWithValue("hi", hInt);
        command.Parameters.AddWithValue("ha", hAmt);
        command.Parameters.AddWithValue("vi", vInt);
        command.Parameters.AddWithValue("va", vAmt);
        command.Parameters.AddWithValue("mw", minW);
        command.Parameters.AddWithValue("mh", minH);
        command.ExecuteNonQuery();
    }

    public BattleZoneShrinkRowDto GetSettings()
    {
        try
        {
            using var connection = _database.DataSource.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
SELECT shrink_start_round, horizontal_shrink_interval, horizontal_shrink_amount,
       vertical_shrink_interval, vertical_shrink_amount, min_width, min_height
FROM battle_zone_shrink
WHERE id = 1
LIMIT 1;
""";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return BattleZoneShrinkRowDto.Defaults;

            return new BattleZoneShrinkRowDto
            {
                ShrinkStartRound = Math.Max(1, reader.GetInt32(0)),
                HorizontalShrinkInterval = Math.Max(1, reader.GetInt32(1)),
                HorizontalShrinkAmount = Math.Clamp(reader.GetInt32(2), 0, 50),
                VerticalShrinkInterval = Math.Max(1, reader.GetInt32(3)),
                VerticalShrinkAmount = Math.Clamp(reader.GetInt32(4), 0, 50),
                MinWidth = Math.Clamp(reader.GetInt32(5), 1, HexSpawn.DefaultGridWidth),
                MinHeight = Math.Clamp(reader.GetInt32(6), 1, HexSpawn.DefaultGridLength)
            };
        }
        catch
        {
            return BattleZoneShrinkRowDto.Defaults;
        }
    }
}
