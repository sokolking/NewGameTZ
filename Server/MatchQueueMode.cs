namespace BattleServer;

public enum MatchQueueMode
{
    Pvp1v1,
    Pvp3v3,
    Pvp5v5,
    PvpRandom
}

public static class MatchQueueModeExtensions
{
    public static int PlayersPerTeam(this MatchQueueMode mode) => mode switch
    {
        MatchQueueMode.Pvp1v1 => 1,
        MatchQueueMode.Pvp3v3 => 3,
        MatchQueueMode.Pvp5v5 => 5,
        MatchQueueMode.PvpRandom => 1,
        _ => 1
    };

    public static int RequiredHumans(this MatchQueueMode mode) => mode switch
    {
        MatchQueueMode.PvpRandom => 2,
        _ => mode.PlayersPerTeam() * 2
    };

    public static bool TryParse(string? s, out MatchQueueMode mode)
    {
        mode = MatchQueueMode.Pvp1v1;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        switch (s.Trim().ToLowerInvariant())
        {
            case "1v1":
                mode = MatchQueueMode.Pvp1v1;
                return true;
            case "3v3":
                mode = MatchQueueMode.Pvp3v3;
                return true;
            case "5v5":
                mode = MatchQueueMode.Pvp5v5;
                return true;
            case "random":
                mode = MatchQueueMode.PvpRandom;
                return true;
            default:
                return false;
        }
    }

    public static string ToWireString(this MatchQueueMode mode) => mode switch
    {
        MatchQueueMode.Pvp1v1 => "1v1",
        MatchQueueMode.Pvp3v3 => "3v3",
        MatchQueueMode.Pvp5v5 => "5v5",
        MatchQueueMode.PvpRandom => "random",
        _ => "1v1"
    };
}
