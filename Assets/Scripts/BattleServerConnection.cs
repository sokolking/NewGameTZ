using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTP: join и poll до старта боя. Ход и результат раунда — WebSocket (<see cref="BattleSignalRConnection"/>).
/// </summary>
public class BattleServerConnection : MonoBehaviour
{
    [SerializeField] private string _serverUrl = "http://localhost:5000";
    [SerializeField] private GameSession _gameSession;
    private string _battleId;
    private string _playerId;
    private bool _inBattle;
    private bool _joining;

    public bool IsInBattle => _inBattle;
    public string BattleId => _battleId;
    public string PlayerId => _playerId;

    /// <summary>Только отмена очереди с главного меню (нет WebSocket). В бою не вызывать.</summary>
    public static void NotifyLeaveBlocking(string serverUrl, string battleId, string playerId)
    {
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(battleId) || string.IsNullOrEmpty(playerId)) return;
        try
        {
            string baseUrl = serverUrl.TrimEnd('/');
            string url = $"{baseUrl}/api/battle/{battleId}/leave?playerId={UnityWebRequest.EscapeURL(playerId)}";
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
                req.timeout = 3;
                req.SendWebRequest();
                for (int w = 0; w < 150 && !req.isDone; w++)
                    Thread.Sleep(20);
            }
        }
        catch { /* ignore */ }
    }

    private void OnApplicationQuit()
    {
        _inBattle = false;
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
            _inBattle = false;
    }

    private void Start()
    {
        if (BattleSessionState.HasPendingBattle)
        {
            _battleId = BattleSessionState.BattleId;
            _playerId = BattleSessionState.PlayerId;
            if (!string.IsNullOrEmpty(BattleSessionState.ServerUrl))
                _serverUrl = BattleSessionState.ServerUrl;
            _inBattle = true;
            _gameSession?.ApplyBattleStarted(BattleSessionState.BattleStarted);
            BattleSessionStateHooks.RaiseBattleIdentified(_battleId, _playerId, _serverUrl);
            BattleSessionState.ClearPending();
        }
    }

    public void ConnectAndJoin(int startCol = 0, int startRow = 0)
    {
        if (_joining || _inBattle) return;
        _joining = true;
        StartCoroutine(JoinCoroutine(startCol, startRow));
    }

    private IEnumerator JoinCoroutine(int startCol, int startRow)
    {
        var body = new JoinRequest { startCol = startCol, startRow = startRow };
        var json = JsonUtility.ToJson(body);
        using (var req = new UnityWebRequest(_serverUrl + "/api/battle/join", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BattleServerConnection] Join failed: {req.error}");
                _joining = false;
                yield break;
            }

            var response = JsonUtility.FromJson<JoinResponse>(req.downloadHandler.text);
            _battleId = response.battleId;
            _playerId = response.playerId;

            if (response.status == "battle" && response.battleStarted != null)
            {
                _inBattle = true;
                _gameSession?.ApplyBattleStarted(response.battleStarted);
                BattleSessionStateHooks.RaiseBattleIdentified(_battleId, _playerId, _serverUrl);
                _joining = false;
                yield break;
            }

            if (response.status == "waiting")
            {
                BattleSessionStateHooks.RaiseBattleIdentified(_battleId, _playerId, _serverUrl);
                StartCoroutine(PollUntilBattleStartedCoroutine());
            }

            _joining = false;
        }
    }

    private IEnumerator PollUntilBattleStartedCoroutine()
    {
        while (!_inBattle && !string.IsNullOrEmpty(_battleId))
        {
            yield return new WaitForSeconds(0.5f);
            var url = $"{_serverUrl}/api/battle/{_battleId}/poll?playerId={UnityWebRequest.EscapeURL(_playerId)}";
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) continue;
                var poll = JsonUtility.FromJson<PollResponse>(req.downloadHandler.text);
                if (poll.status == "battle" && poll.battleStarted != null)
                {
                    _inBattle = true;
                    _gameSession?.ApplyBattleStarted(poll.battleStarted);
                    BattleSessionStateHooks.RaiseBattleIdentified(_battleId, _playerId, _serverUrl);
                    yield break;
                }
            }
        }
    }

    [Serializable]
    private class JoinRequest
    {
        public int startCol;
        public int startRow;
        public bool solo;
    }

    [Serializable]
    private class JoinResponse
    {
        public string battleId;
        public string playerId;
        public string status;
        public BattleStartedPayload battleStarted;
    }

    [Serializable]
    private class PollResponse
    {
        public string status;
        public BattleStartedPayload battleStarted;
    }
}
