using UnityEngine;

/// <summary>
/// Состояние боя и сессии: JWT выдаётся сервером при логине; клиент не шлёт пароль в игровых API.
/// </summary>
public static class BattleSessionState
{
    public const string SpectatorPlayerId = "__spectator__";

    public static string BattleId { get; private set; }
    public static string PlayerId { get; private set; }
    public static string ServerUrl { get; private set; }
    public static BattleStartedPayload BattleStarted { get; private set; }

    public static bool HasPendingBattle { get; private set; }

    /// <summary>Watching another user's battle (no submit / no local combatant).</summary>
    public static bool IsSpectatorMode { get; private set; }

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
        IsSpectatorMode = false;
    }

    public static void SetPending(string battleId, string playerId, string serverUrl, BattleStartedPayload battleStarted)
    {
        IsSpectatorMode = false;
        BattleId = battleId ?? "";
        PlayerId = playerId ?? "";
        ServerUrl = serverUrl ?? "";
        BattleStarted = battleStarted;
        HasPendingBattle = battleStarted != null;
    }

    public static void SetSpectatorPending(string battleId, string serverUrl, BattleStartedPayload battleStarted)
    {
        IsSpectatorMode = true;
        BattleId = battleId ?? "";
        PlayerId = SpectatorPlayerId;
        ServerUrl = serverUrl ?? "";
        BattleStarted = battleStarted;
        HasPendingBattle = battleStarted != null;
    }

    public static void ClearPending()
    {
        HasPendingBattle = false;
        BattleStarted = null;
        // Keep IsSpectatorMode — still needed for battle WebSocket query string and UI after payload is consumed.
    }

    /// <summary>Clears spectator flag after the watched battle ends or when resetting session.</summary>
    public static void ClearSpectatorMode()
    {
        IsSpectatorMode = false;
    }
}
