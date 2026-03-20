using System.Collections;
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
    [Tooltip("Текст статуса (например «Searching for opponent...»).")]
    [SerializeField] private Text _statusText;

    private bool _searching;
    private string _queueBattleId;
    private string _queuePlayerId;
    private string _username = "test";
    private string _password = "test";

    private void OnDestroy()
    {
        if (_searching && !string.IsNullOrEmpty(_queueBattleId) && !string.IsNullOrEmpty(_queuePlayerId))
            BattleServerConnection.NotifyLeaveBlocking(_serverUrl, _queueBattleId, _queuePlayerId);
        _queueBattleId = null;
        _queuePlayerId = null;
    }

    public void FindGame(string username, string password)
    {
        if (_searching) return;
        _serverUrl = BattleServerRuntime.CurrentBaseUrl;
        _username = username;
        _password = password;
        _searching = true;
        SetStatus("Searching for opponent...");
        StartCoroutine(JoinAndWaitForBattleCoroutine());
    }

    /// <summary>Одиночный режим: запросить на сервере одиночный бой (1 игрок + моб) и сразу загрузить сцену.</summary>
    public void StartSinglePlayerServerBattle(string username, string password)
    {
        if (_searching) return;
        _serverUrl = BattleServerRuntime.CurrentBaseUrl;
        _username = username;
        _password = password;
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
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = false, username = _username, password = _password };
        var json = JsonUtility.ToJson(body);
        string url = _serverUrl.TrimEnd('/') + "/api/battle/join";
        string responseText = null;
        string errBody = null;
        yield return HttpSimple.PostJson(url, json, b => responseText = b, e => errBody = e);

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
            var response = JsonUtility.FromJson<JoinResponse>(responseText);
            string battleId = response.battleId;
            string playerId = response.playerId;

            if (response.status == "battle" && response.battleStarted != null)
            {
                BattleSessionState.SetAuthCredentials(_username, _password);
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
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = true, username = _username, password = _password };
        var json = JsonUtility.ToJson(body);
        string url = _serverUrl.TrimEnd('/') + "/api/battle/join";
        string responseText = null;
        string errBody = null;
        yield return HttpSimple.PostJson(url, json, b => responseText = b, e => errBody = e);

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

        var response = JsonUtility.FromJson<JoinResponse>(responseText);
        string battleId = response.battleId;
        string playerId = response.playerId;

        if (response.status == "battle" && response.battleStarted != null)
        {
            BattleSessionState.SetAuthCredentials(_username, _password);
            BattleSessionState.SetPending(battleId, playerId, _serverUrl, response.battleStarted);
            _searching = false;
            SceneManager.LoadScene(_gameSceneName);
            yield break;
        }

        SetStatus("Unexpected response for solo battle.");
        _searching = false;
    }

    private IEnumerator PollUntilBattleStartedCoroutine(string battleId, string playerId)
    {
        while (_searching && !string.IsNullOrEmpty(battleId))
        {
            yield return new WaitForSeconds(0.5f);
            var url = _serverUrl.TrimEnd('/') + "/api/battle/" + HttpSimple.Escape(battleId) + "/poll?playerId=" + HttpSimple.Escape(playerId);
            int status = 0;
            string body = null;
            string transportErr = null;
            yield return HttpSimple.GetStringWithStatus(url, (code, b) => { status = code; body = b; }, e => transportErr = e);

            if (transportErr != null)
                continue;

            if (status == 404)
            {
                _queueBattleId = null;
                _queuePlayerId = null;
                SetStatus("Поиск отменён (комната закрыта).");
                _searching = false;
                yield break;
            }

            if (status < 200 || status >= 300 || string.IsNullOrEmpty(body))
                continue;

            var poll = JsonUtility.FromJson<PollResponse>(body);
            if (poll.status == "battle" && poll.battleStarted != null)
            {
                BattleSessionState.SetAuthCredentials(_username, _password);
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
        public string username;
        public string password;
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
