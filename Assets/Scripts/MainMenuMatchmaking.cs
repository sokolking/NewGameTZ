using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
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

    private void OnDestroy()
    {
        if (_searching && !string.IsNullOrEmpty(_queueBattleId) && !string.IsNullOrEmpty(_queuePlayerId))
            BattleServerConnection.NotifyLeaveBlocking(_serverUrl, _queueBattleId, _queuePlayerId);
        _queueBattleId = null;
        _queuePlayerId = null;
    }

    public void FindGame()
    {
        if (_searching) return;
        _searching = true;
        SetStatus("Searching for opponent...");
        StartCoroutine(JoinAndWaitForBattleCoroutine());
    }

    /// <summary>Одиночный режим: запросить на сервере одиночный бой (1 игрок + моб) и сразу загрузить сцену.</summary>
    public void StartSinglePlayerServerBattle()
    {
        if (_searching) return;
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
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = false };
        var json = JsonUtility.ToJson(body);
        using (var req = new UnityWebRequest(_serverUrl + "/api/battle/join", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Connection failed. Check server.");
                _searching = false;
                yield break;
            }

            var response = JsonUtility.FromJson<JoinResponse>(req.downloadHandler.text);
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
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = true };
        var json = JsonUtility.ToJson(body);
        using (var req = new UnityWebRequest(_serverUrl + "/api/battle/join", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Connection failed. Check server.");
                _searching = false;
                yield break;
            }

            var response = JsonUtility.FromJson<JoinResponse>(req.downloadHandler.text);
            string battleId = response.battleId;
            string playerId = response.playerId;

            if (response.status == "battle" && response.battleStarted != null)
            {
                BattleSessionState.SetPending(battleId, playerId, _serverUrl, response.battleStarted);
                _searching = false;
                SceneManager.LoadScene(_gameSceneName);
                yield break;
            }

            // Любой другой статус для solo — ошибка.
            SetStatus("Unexpected response for solo battle.");
            _searching = false;
        }
    }

    private IEnumerator PollUntilBattleStartedCoroutine(string battleId, string playerId)
    {
        while (_searching && !string.IsNullOrEmpty(battleId))
        {
            yield return new WaitForSeconds(0.5f);
            var url = _serverUrl + "/api/battle/" + battleId + "/poll?playerId=" + UnityWebRequest.EscapeURL(playerId);
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.responseCode == 404)
                {
                    _queueBattleId = null;
                    _queuePlayerId = null;
                    SetStatus("Поиск отменён (комната закрыта).");
                    _searching = false;
                    yield break;
                }
                if (req.result != UnityWebRequest.Result.Success) continue;
                var poll = JsonUtility.FromJson<PollResponse>(req.downloadHandler.text);
                if (poll.status == "battle" && poll.battleStarted != null)
                {
                    BattleSessionState.SetPending(battleId, playerId, _serverUrl, poll.battleStarted);
                    _searching = false;
                    SceneManager.LoadScene(_gameSceneName);
                    yield break;
                }
            }
        }
    }

    [System.Serializable]
    private class JoinRequest
    {
        public int startCol;
        public int startRow;
        public bool solo;
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
}
