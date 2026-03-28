using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Матчмейкинг с главного меню: по нажатию Find Game — join, опрос до старта боя, загрузка игровой сцены.
/// </summary>
[AddComponentMenu("Hex Grid/Main Menu Matchmaking")]
public class MainMenuMatchmaking : MonoBehaviour
{
    [SerializeField] private string _serverUrl = "http://localhost:5000";
    [SerializeField] private string _gameSceneName = "MainScene";
    [Tooltip("Status text (e.g. Searching for opponent...).")]
    [SerializeField] private Text _statusText;

    private bool _searching;
    private string _queueBattleId;
    private string _queuePlayerId;

    private void OnDestroy()
    {
        if (_searching && !string.IsNullOrEmpty(_queueBattleId) && !string.IsNullOrEmpty(_queuePlayerId))
            BattleServerConnection.NotifyLeaveBlocking(_serverUrl, _queueBattleId, _queuePlayerId, BattleSessionState.AccessToken);
        _queueBattleId = null;
        _queuePlayerId = null;
    }

    /// <summary>Uses <see cref="BattleSessionState.AccessToken"/> from <c>/api/auth/login</c>.</summary>
    public void FindGame()
    {
        if (_searching) return;
        if (string.IsNullOrEmpty(BattleSessionState.AccessToken))
        {
            SetStatus(Loc.T("menu.session_required"));
            return;
        }

        _serverUrl = BattleServerRuntime.CurrentBaseUrl;
        _searching = true;
        SetStatus("Searching for opponent...");
        StartCoroutine(JoinAndWaitForBattleCoroutine());
    }

    /// <summary>Одиночный режим: запросить на сервере одиночный бой (1 игрок + моб) и сразу загрузить сцену.</summary>
    public void StartSinglePlayerServerBattle()
    {
        if (_searching) return;
        if (string.IsNullOrEmpty(BattleSessionState.AccessToken))
        {
            SetStatus(Loc.T("menu.session_required"));
            return;
        }

        _serverUrl = BattleServerRuntime.CurrentBaseUrl;
        _searching = true;
        SetStatus("Starting single-player battle...");
        StartCoroutine(JoinSoloAndStartBattleCoroutine());
    }

    private void SetStatus(string message)
    {
        if (_statusText != null) _statusText.text = message;
    }

    private IEnumerator JoinAndWaitForBattleCoroutine()
    {
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = false, characterLevel = 1 };
        var json = JsonConvert.SerializeObject(body);
        string url = _serverUrl.TrimEnd('/') + "/api/battle/join";
        string responseText = null;
        string errBody = null;
        yield return HttpSimple.PostJsonWithAuth(url, json, BattleSessionState.AccessToken, b => responseText = b, e => errBody = e);

        if (errBody != null)
        {
            SetStatus(ExtractRequestErrorFromBody(errBody, "Connection failed. Check server."));
            _searching = false;
            yield break;
        }

        if (string.IsNullOrEmpty(responseText))
        {
            SetStatus("Connection failed. Check server.");
            _searching = false;
            yield break;
        }

        {
            var response = ParseJoinResponse(responseText);
            string battleId = response.battleId;
            string playerId = response.playerId;

            if (response.status == "battle" && response.battleStarted != null)
            {
                BattleSessionState.SetPending(battleId, playerId, _serverUrl, response.battleStarted);
                _searching = false;
                SceneManager.LoadScene(_gameSceneName);
                yield break;
            }

            if (response.status == "waiting")
            {
                _queueBattleId = battleId;
                _queuePlayerId = playerId;
                yield return PollUntilBattleStartedCoroutine(battleId, playerId);
                _queueBattleId = null;
                _queuePlayerId = null;
            }
            else
            {
                SetStatus("Unexpected response.");
                _searching = false;
            }
        }
    }

    /// <summary>Запрос одиночного боя: сервер сразу создаёт бой и возвращает battleStarted без ожидания второго игрока.</summary>
    private IEnumerator JoinSoloAndStartBattleCoroutine()
    {
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = true, characterLevel = 1 };
        var json = JsonConvert.SerializeObject(body);
        string url = _serverUrl.TrimEnd('/') + "/api/battle/join";
        string responseText = null;
        string errBody = null;
        yield return HttpSimple.PostJsonWithAuth(url, json, BattleSessionState.AccessToken, b => responseText = b, e => errBody = e);

        if (errBody != null)
        {
            SetStatus(ExtractRequestErrorFromBody(errBody, "Connection failed. Check server."));
            _searching = false;
            yield break;
        }

        if (string.IsNullOrEmpty(responseText))
        {
            SetStatus("Connection failed. Check server.");
            _searching = false;
            yield break;
        }

        var response = ParseJoinResponse(responseText);
        string battleId = response.battleId;
        string playerId = response.playerId;

        if (response.status == "battle" && response.battleStarted != null)
        {
            BattleSessionState.SetPending(battleId, playerId, _serverUrl, response.battleStarted);
            _searching = false;
            SceneManager.LoadScene(_gameSceneName);
            yield break;
        }

        SetStatus("Unexpected response for solo battle.");
        _searching = false;
    }

    private static readonly WaitForSeconds _pollWait = new WaitForSeconds(0.5f);
    private IEnumerator PollUntilBattleStartedCoroutine(string battleId, string playerId)
    {
        while (_searching && !string.IsNullOrEmpty(battleId))
        {
            yield return _pollWait;
            var url = _serverUrl.TrimEnd('/') + "/api/battle/" + HttpSimple.Escape(battleId) + "/poll?playerId=" + HttpSimple.Escape(playerId);
            int status = 0;
            string body = null;
            string transportErr = null;
            yield return HttpSimple.GetStringWithStatusAndAuth(url, BattleSessionState.AccessToken, (code, b) => { status = code; body = b; }, e => transportErr = e);

            if (transportErr != null)
                continue;

            if (status == 404)
            {
                _queueBattleId = null;
                _queuePlayerId = null;
                SetStatus(Loc.T("menu.search_cancelled"));
                _searching = false;
                yield break;
            }

            if (status == 401 || status == 403)
            {
                _queueBattleId = null;
                _queuePlayerId = null;
                SetStatus(Loc.T("menu.session_required"));
                _searching = false;
                yield break;
            }

            if (status < 200 || status >= 300 || string.IsNullOrEmpty(body))
                continue;

            var poll = ParsePollResponse(body);
            if (poll.status == "battle" && poll.battleStarted != null)
            {
                BattleSessionState.SetPending(battleId, playerId, _serverUrl, poll.battleStarted);
                _searching = false;
                SceneManager.LoadScene(_gameSceneName);
                yield break;
            }
        }
    }

    [System.Serializable]
    private class JoinRequest
    {
        public int startCol;
        public int startRow;
        public bool solo;
        public int characterLevel;
    }

    [System.Serializable]
    private class JoinResponse
    {
        public string battleId;
        public string playerId;
        public string status;
        public BattleStartedPayload battleStarted;
    }

    [System.Serializable]
    private class PollResponse
    {
        public string status;
        public BattleStartedPayload battleStarted;
    }

    [System.Serializable]
    private class ErrorResponse
    {
        public string error;
    }

    private static JoinResponse ParseJoinResponse(string responseText)
    {
        if (string.IsNullOrEmpty(responseText)) return null;
        try
        {
            return JsonConvert.DeserializeObject<JoinResponse>(responseText);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MainMenuMatchmaking] Join JSON Newtonsoft failed: " + ex.Message + "; JsonUtility fallback");
            return JsonUtility.FromJson<JoinResponse>(responseText);
        }
    }

    private static PollResponse ParsePollResponse(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            return JsonConvert.DeserializeObject<PollResponse>(body);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MainMenuMatchmaking] Poll JSON Newtonsoft failed: " + ex.Message + "; JsonUtility fallback");
            return JsonUtility.FromJson<PollResponse>(body);
        }
    }

    private static string ExtractRequestErrorFromBody(string responseText, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                var error = JsonUtility.FromJson<ErrorResponse>(responseText);
                if (error != null && !string.IsNullOrWhiteSpace(error.error))
                    return error.error;
            }
            catch
            {
                // Ignore malformed error body and fall back to generic text.
            }
        }

        return fallback;
    }
}
