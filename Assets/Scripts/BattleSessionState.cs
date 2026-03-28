using UnityEngine;

/// <summary>
/// Состояние боя и сессии: JWT выдаётся сервером при логине; клиент не шлёт пароль в игровых API.
/// </summary>
public static class BattleSessionState
{
    public static string BattleId { get; private set; }
    public static string PlayerId { get; private set; }
    public static string ServerUrl { get; private set; }
    public static BattleStartedPayload BattleStarted { get; private set; }

    public static bool HasPendingBattle { get; private set; }

    /// <summary>Bearer token for <c>Authorization</c> on all game APIs.</summary>
    public static string AccessToken { get; private set; } = "";
    /// <summary>Base URL that issued current JWT; used by session socket reconnects.</summary>
    public static string SessionBaseUrl { get; private set; } = "";

    /// <summary>Display name from server (login response); optional for UI.</summary>
    public static string LastUsername { get; private set; } = "";

    /// <summary>Set when the server revokes the session (login from another device). Shown on the next login screen.</summary>
    public static string PendingLoginNotice { get; set; } = "";
    /// <summary>Localized key for pending notice (preferred over raw string).</summary>
    public static string PendingLoginNoticeLocKey { get; set; } = "";

    public static void SetSessionToken(string accessToken, string displayUsername = "", string serverBaseUrl = "")
    {
        AccessToken = accessToken ?? "";
        LastUsername = displayUsername ?? "";
        SessionBaseUrl = serverBaseUrl ?? "";
    }

    public static void ClearSession()
    {
        AccessToken = "";
        LastUsername = "";
        SessionBaseUrl = "";
        PendingLoginNotice = "";
        PendingLoginNoticeLocKey = "";
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
