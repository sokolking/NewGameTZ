using System;

/// <summary>
/// Хуки вокруг BattleSessionState для сторонних систем (SignalR и т.п.).
/// </summary>
public static class BattleSessionStateHooks
{
    /// <summary>
    /// Клиент узнал, в какой бой он вошёл (battleId, playerId, serverUrl).
    /// Вызывается после успешного join/poll.
    /// </summary>
    public static event Action<string, string, string> OnBattleIdentified;

    public static void RaiseBattleIdentified(string battleId, string playerId, string serverUrl)
    {
        OnBattleIdentified?.Invoke(battleId, playerId, serverUrl);
    }
}

