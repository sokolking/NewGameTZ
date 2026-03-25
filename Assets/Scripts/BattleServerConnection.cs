using System;
using System.Collections;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

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
    public string ServerUrl => _serverUrl;

    /// <summary>Только отмена очереди с главного меню (нет WebSocket). В бою не вызывать.</summary>
    public static void NotifyLeaveBlocking(string serverUrl, string battleId, string playerId)
    {
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(battleId) || string.IsNullOrEmpty(playerId)) return;
        try
        {
            string baseUrl = serverUrl.TrimEnd('/');
            string url = $"{baseUrl}/api/battle/{Uri.EscapeDataString(battleId)}/leave?playerId={Uri.EscapeDataString(playerId)}";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var task = client.PostAsync(url, new StringContent("", Encoding.UTF8, "application/json"));
            task.Wait(TimeSpan.FromSeconds(3));
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
        else
            _serverUrl = BattleServerRuntime.CurrentBaseUrl;
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
        string url = _serverUrl.TrimEnd('/') + "/api/battle/join";
        string responseText = null;
        string err = null;
        yield return HttpSimple.PostJson(url, json, b => responseText = b, e => err = e);

        if (err != null)
        {
            Debug.LogWarning($"[BattleServerConnection] Join failed: {err}");
            _joining = false;
            yield break;
        }

        if (string.IsNullOrEmpty(responseText))
        {
            _joining = false;
            yield break;
        }

        var response = ParseJoinResponse(responseText);
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

    private static JoinResponse ParseJoinResponse(string responseText)
    {
        if (string.IsNullOrEmpty(responseText)) return null;
        try
        {
            return JsonConvert.DeserializeObject<JoinResponse>(responseText);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BattleServerConnection] Join JSON Newtonsoft failed: " + ex.Message + "; JsonUtility fallback");
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
            Debug.LogWarning("[BattleServerConnection] Poll JSON Newtonsoft failed: " + ex.Message + "; JsonUtility fallback");
            return JsonUtility.FromJson<PollResponse>(body);
        }
    }

    private static readonly WaitForSeconds _pollWait = new WaitForSeconds(0.5f);
    private IEnumerator PollUntilBattleStartedCoroutine()
    {
        while (!_inBattle && !string.IsNullOrEmpty(_battleId))
        {
            yield return _pollWait;
            var url = $"{_serverUrl.TrimEnd('/')}/api/battle/{HttpSimple.Escape(_battleId)}/poll?playerId={HttpSimple.Escape(_playerId)}";
            int status = 0;
            string body = null;
            string transportErr = null;
            yield return HttpSimple.GetStringWithStatus(url, (code, b) => { status = code; body = b; }, e => transportErr = e);
            if (transportErr != null)
                continue;
            if (status < 200 || status >= 300 || string.IsNullOrEmpty(body))
                continue;
            var poll = ParsePollResponse(body);
            if (poll.status == "battle" && poll.battleStarted != null)
            {
                _inBattle = true;
                _gameSession?.ApplyBattleStarted(poll.battleStarted);
                BattleSessionStateHooks.RaiseBattleIdentified(_battleId, _playerId, _serverUrl);
                yield break;
            }
        }
    }

    public IEnumerator LoadTurnByIdCoroutine(string turnId, Action<BattleTurnResponsePayload> onLoaded, Action<string> onFailed)
    {
        if (string.IsNullOrEmpty(_serverUrl) || string.IsNullOrEmpty(_battleId) || string.IsNullOrEmpty(turnId))
        {
            onFailed?.Invoke(Loc.T("net.turn_load_failed"));
            yield break;
        }

        string url = $"{_serverUrl.TrimEnd('/')}/api/battle/{HttpSimple.Escape(_battleId)}/turns/{HttpSimple.Escape(turnId)}";
        string body = null;
        string err = null;
        yield return HttpSimple.GetString(url, b => body = b, e => err = e);

        if (err != null)
        {
            onFailed?.Invoke(err);
            yield break;
        }

        if (string.IsNullOrEmpty(body))
        {
            onFailed?.Invoke(Loc.T("net.turn_load_failed"));
            yield break;
        }

        BattleTurnResponsePayload response = null;
        try
        {
            response = JsonConvert.DeserializeObject<BattleTurnResponsePayload>(body);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BattleHTTP] turn JSON Newtonsoft failed: " + ex.Message + "; fallback JsonUtility");
            response = JsonUtility.FromJson<BattleTurnResponsePayload>(body);
        }

        if (response == null || response.turnResult == null)
        {
            onFailed?.Invoke(Loc.T("net.turn_empty"));
            yield break;
        }

        onLoaded?.Invoke(response);
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
