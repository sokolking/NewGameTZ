using UnityEngine;

/// <summary>
/// Состояние боя, переданное из главного меню в игровую сцену после успешного матчмейкинга (Find Game).
/// Игровая сцена читает его при старте и не вызывает Join снова.
/// </summary>
public static class BattleSessionState
{
    public static string BattleId { get; private set; }
    public static string PlayerId { get; private set; }
    public static string ServerUrl { get; private set; }
    public static BattleStartedPayload BattleStarted { get; private set; }

    public static bool HasPendingBattle { get; private set; }

    /// <summary>Логин/пароль последнего входа (для загрузки инвентаря в бою).</summary>
    public static string LastUsername { get; private set; } = "";
    public static string LastPassword { get; private set; } = "";

    public static void SetAuthCredentials(string username, string password)
    {
        LastUsername = username ?? "";
        LastPassword = password ?? "";
    }

    public static void SetPending(string battleId, string playerId, string serverUrl, BattleStartedPayload battleStarted)
    {
        BattleId = battleId ?? "";
        PlayerId = playerId ?? "";
        ServerUrl = serverUrl ?? "";
        BattleStarted = battleStarted;
        HasPendingBattle = battleStarted != null;
    }

    public static void ClearPending()
    {
        HasPendingBattle = false;
        BattleStarted = null;
    }
}
