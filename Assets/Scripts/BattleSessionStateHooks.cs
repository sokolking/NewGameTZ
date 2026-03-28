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

    /// <summary>JWT invalidated because the same account logged in elsewhere.</summary>
    public static event Action OnSessionRevokedElsewhere;

    public static void RaiseBattleIdentified(string battleId, string playerId, string serverUrl)
    {
        OnBattleIdentified?.Invoke(battleId, playerId, serverUrl);
    }

    public static void RaiseSessionRevokedElsewhere() => OnSessionRevokedElsewhere?.Invoke();
}

