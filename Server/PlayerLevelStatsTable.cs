namespace BattleServer;

/// <summary>
/// Design progression per level. Combat caps:
/// max HP = <see cref="BaseMaxHp"/> + 2×(Strength);
/// max AP = <see cref="BaseMaxAp"/> + 2×Stamina (e.g. L1: 15 + 3×2 = 21).
/// <see cref="PlayerLevelStatsRow.ActionPoints"/> mirrors the design table for reference only.
/// Intuition/Intellect are stored in DB only (not exposed in player-facing APIs).
/// </summary>
public readonly struct PlayerLevelStatsRow
{
    public int Strength { get; init; }
    public int Agility { get; init; }
    public int Intuition { get; init; }
    public int Stamina { get; init; }
    public int Accuracy { get; init; }
    public int Intellect { get; init; }
    public int ActionPoints { get; init; }
}

public static class PlayerLevelStatsTable
{
    public const int MinLevel = 1;
    public const int MaxLevel = 22;
    public const int BaseMaxHp = 20;
    public const int BaseMaxAp = 15;

    private static readonly PlayerLevelStatsRow[] Rows =
    {
        new() { Strength = 3, Agility = 3, Intuition = 3, Stamina = 3, Accuracy = 0, Intellect = 0, ActionPoints = 21 },
        new() { Strength = 4, Agility = 3, Intuition = 3, Stamina = 5, Accuracy = 1, Intellect = 0, ActionPoints = 25 },
        new() { Strength = 5, Agility = 3, Intuition = 3, Stamina = 7, Accuracy = 3, Intellect = 1, ActionPoints = 29 },
        new() { Strength = 7, Agility = 4, Intuition = 4, Stamina = 10, Accuracy = 4, Intellect = 1, ActionPoints = 35 },
        new() { Strength = 10, Agility = 5, Intuition = 4, Stamina = 14, Accuracy = 5, Intellect = 2, ActionPoints = 43 },
        new() { Strength = 13, Agility = 6, Intuition = 6, Stamina = 18, Accuracy = 6, Intellect = 3, ActionPoints = 51 },
        new() { Strength = 16, Agility = 8, Intuition = 9, Stamina = 22, Accuracy = 8, Intellect = 3, ActionPoints = 59 },
        new() { Strength = 20, Agility = 9, Intuition = 12, Stamina = 28, Accuracy = 9, Intellect = 4, ActionPoints = 71 },
        new() { Strength = 25, Agility = 10, Intuition = 15, Stamina = 35, Accuracy = 10, Intellect = 5, ActionPoints = 85 },
        new() { Strength = 30, Agility = 12, Intuition = 18, Stamina = 42, Accuracy = 12, Intellect = 6, ActionPoints = 99 },
        new() { Strength = 35, Agility = 15, Intuition = 20, Stamina = 50, Accuracy = 15, Intellect = 7, ActionPoints = 115 },
        new() { Strength = 41, Agility = 16, Intuition = 22, Stamina = 58, Accuracy = 16, Intellect = 13, ActionPoints = 131 },
        new() { Strength = 48, Agility = 19, Intuition = 25, Stamina = 66, Accuracy = 19, Intellect = 15, ActionPoints = 147 },
        new() { Strength = 54, Agility = 22, Intuition = 29, Stamina = 76, Accuracy = 22, Intellect = 17, ActionPoints = 167 },
        new() { Strength = 60, Agility = 25, Intuition = 34, Stamina = 87, Accuracy = 25, Intellect = 19, ActionPoints = 189 },
        new() { Strength = 69, Agility = 28, Intuition = 38, Stamina = 98, Accuracy = 28, Intellect = 21, ActionPoints = 211 },
        new() { Strength = 76, Agility = 31, Intuition = 43, Stamina = 110, Accuracy = 31, Intellect = 25, ActionPoints = 235 },
        new() { Strength = 82, Agility = 35, Intuition = 48, Stamina = 123, Accuracy = 35, Intellect = 29, ActionPoints = 261 },
        new() { Strength = 90, Agility = 39, Intuition = 53, Stamina = 136, Accuracy = 39, Intellect = 33, ActionPoints = 287 },
        new() { Strength = 96, Agility = 43, Intuition = 60, Stamina = 148, Accuracy = 43, Intellect = 40, ActionPoints = 311 },
        new() { Strength = 102, Agility = 47, Intuition = 68, Stamina = 160, Accuracy = 47, Intellect = 48, ActionPoints = 345 },
        new() { Strength = 108, Agility = 52, Intuition = 76, Stamina = 172, Accuracy = 52, Intellect = 56, ActionPoints = 360 }
    };

    private static PlayerLevelStatsRow RowAtLevel1 => Rows[0];

    public static PlayerLevelStatsRow GetForLevel(int level)
    {
        int i = Math.Clamp(level, MinLevel, MaxLevel) - 1;
        return Rows[i];
    }

    public static int GetMaxHpForLevel(int level)
    {
        var r = GetForLevel(level);
        return Math.Max(1, BaseMaxHp + 2 * r.Strength);
    }

    public static int GetMaxApForLevel(int level)
    {
        var r = GetForLevel(level);
        return Math.Max(1, BaseMaxAp + 2 * r.Stamina);
    }

}
