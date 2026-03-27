using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Server revoked JWT (login elsewhere): leave battle, clear session, show notice, load LoginScene.
/// </summary>
public static class SessionRevokedNavigation
{
    public const string DefaultLoginSceneName = "LoginScene";

    /// <param name="loginSceneName">If null or empty, uses <see cref="DefaultLoginSceneName"/>.</param>
    public static void GoToLogin(string loginSceneName = null)
    {
        SessionWebSocketConnection.StopReconnectLoop();

        BattleEscActions.NotifyLeaveCurrentBattleIfAny();
        BattleSessionState.ClearSession();
        BattleSessionState.ClearPending();
        BattleSessionState.PendingLoginNotice = "";
        BattleSessionState.PendingLoginNoticeLocKey = "session.revoked_elsewhere";
        BattleSessionStateHooks.RaiseSessionRevokedElsewhere();

        string scene = string.IsNullOrEmpty(loginSceneName) ? DefaultLoginSceneName : loginSceneName;
        if (!string.IsNullOrEmpty(scene))
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }
}
