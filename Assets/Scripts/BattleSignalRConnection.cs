using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// WebSocket /ws/battle: submitTurn + пуш результата раунда. Во время боя POST не используется.
/// </summary>
public class BattleSignalRConnection : MonoBehaviour
{
    private const string TypeSubmitTurn = "submitTurn";
    private const string TypeSubmitAck = "submitAck";
    /// <summary>Макс. действий из очереди за один кадр — распределяем тяжёлый JSON-парсинг по кадрам, не спайкуя FPS.</summary>
    private const int MaxActionsPerFrame = 3;

    [Header("Server")]
    [SerializeField] private string _serverUrl = "http://localhost:5000";

    [Header("Battle session")]
    [SerializeField] private GameSession _gameSession;

    [Header("Logging")]
    [SerializeField] private bool _logSockets = true;

    [Tooltip("submitAck wait timeout (seconds).")]
    [SerializeField] private float _submitAckTimeoutSeconds = 18f;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<Action> _mainThread = new();
    private string _battleId;
    private string _playerId;
    private Action<bool, string> _submitCallback;
    private Coroutine _submitTimeoutCo;

    public bool IsSocketReady => _ws != null && _ws.State == WebSocketState.Open;

    private void Log(string message)
    {
        if (!_logSockets) return;
        Debug.Log("[BattleWS] " + message);
    }

    private void EnqueueLog(string message)
    {
        if (!_logSockets) return;
        _mainThread.Enqueue(() => Debug.Log("[BattleWS] " + message));
    }

    private void EnqueueLogWarn(string message)
    {
        if (!_logSockets) return;
        _mainThread.Enqueue(() => Debug.LogWarning("[BattleWS] " + message));
    }

    private void Awake()
    {
        if (_gameSession == null)
            _gameSession = FindFirstObjectByType<GameSession>();

        BattleSessionStateHooks.OnBattleIdentified += OnBattleIdentified;
        Log("component awake");
    }

    private void OnDestroy()
    {
        BattleSessionStateHooks.OnBattleIdentified -= OnBattleIdentified;
        try
        {
            _cts?.Cancel();
        }
        catch { /* ignore */ }

        if (_ws != null)
        {
            try
            {
                _ws.Abort();
            }
            catch { /* ignore */ }

            try
            {
                _ws.Dispose();
            }
            catch { /* ignore */ }

            _ws = null;
        }
    }

    private void Update()
    {
        // Ограничиваем число действий за кадр: DispatchIncomingJson содержит JsonConvert.DeserializeObject,
        // который при пачке сообщений давал спайк 10–40 ms. Остаток обработается в следующих кадрах.
        int processed = 0;
        while (processed < MaxActionsPerFrame && _mainThread.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BattleWS] mainThread: " + e.Message);
            }
            processed++;
        }
    }

    private void OnBattleIdentified(string battleId, string playerId, string serverUrl)
    {
        _battleId = battleId;
        _playerId = playerId ?? "";
        if (!string.IsNullOrEmpty(serverUrl))
            _serverUrl = serverUrl;
        Log($"OnBattleIdentified: battleId={battleId}, playerId={_playerId}");
        _ = ConnectAsync();
    }

    /// <summary>Отправить ход по WebSocket; onComplete вызывается на главном потоке после submitAck.</summary>
    public void SubmitTurnViaSocket(SubmitTurnPayload payload, Action<bool, string> onComplete)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            onComplete?.Invoke(false, Loc.T("net.ws_not_ready"));
            return;
        }

        if (_submitCallback != null)
        {
            onComplete?.Invoke(false, Loc.T("net.previous_submit_pending"));
            return;
        }

        _submitCallback = onComplete;
        _ = SendSubmitTurnTask(payload);
    }

    private async Task SendSubmitTurnTask(SubmitTurnPayload p)
    {
        try
        {
            var msg = new WsClientSubmitTurn
            {
                type = TypeSubmitTurn,
                battleId = _battleId,
                playerId = _playerId,
                roundIndex = p.roundIndex,
                currentMagazineRounds = p.currentMagazineRounds,
                actions = p.actions
            };
            var json = JsonUtility.ToJson(msg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            _mainThread.Enqueue(StartSubmitTimeoutIfNeeded);
        }
        catch (Exception ex)
        {
            _mainThread.Enqueue(() => FinishSubmitCallback(false, ex.Message));
        }
    }

    private void StartSubmitTimeoutIfNeeded()
    {
        if (_submitCallback == null) return;
        if (_submitTimeoutCo != null)
            StopCoroutine(_submitTimeoutCo);
        _submitTimeoutCo = StartCoroutine(SubmitAckTimeoutCoroutine());
    }

    private IEnumerator SubmitAckTimeoutCoroutine()
    {
        float t = 0f;
        float maxT = Mathf.Max(5f, _submitAckTimeoutSeconds);
        while (t < maxT && _submitCallback != null)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_submitCallback != null)
            FinishSubmitCallback(false, Loc.T("net.submit_ack_timeout"));
        _submitTimeoutCo = null;
    }

    private void FinishSubmitCallback(bool success, string error)
    {
        if (_submitTimeoutCo != null)
        {
            StopCoroutine(_submitTimeoutCo);
            _submitTimeoutCo = null;
        }

        var cb = _submitCallback;
        _submitCallback = null;
        cb?.Invoke(success, error);
    }

    private async Task ConnectAsync()
    {
        await DisconnectAsync();
        if (string.IsNullOrEmpty(_battleId))
        {
            EnqueueLogWarn("ConnectAsync: empty battleId");
            return;
        }

        string root = _serverUrl.TrimEnd('/');
        root = root.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);
        string token = BattleSessionState.AccessToken ?? "";
        bool spectator = GameSession.Active != null && GameSession.Active.IsSpectatorMode;
        string uri = $"{root}/ws/battle?battleId={Uri.EscapeDataString(_battleId)}&playerId={Uri.EscapeDataString(_playerId)}&access_token={Uri.EscapeDataString(token)}";
        if (spectator)
            uri += "&spectator=true";

        EnqueueLog("ConnectAsync: " + uri);
        _cts = new CancellationTokenSource();
        var ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri(uri), _cts.Token);
        }
        catch (Exception ex)
        {
            EnqueueLogWarn("connect FAILED: " + ex.Message);
            try
            {
                ws.Dispose();
            }
            catch { /* ignore */ }

            return;
        }

        _ws = ws;
        EnqueueLog("connected OK");
        _ = ReceiveLoop(ws, _cts.Token);
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[512 * 1024];
        var msgIndex = 0;
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(buf, 0, r.Count);
                }
                while (!r.EndOfMessage);

                msgIndex++;
                var text = Encoding.UTF8.GetString(ms.ToArray());
                EnqueueLog($"recv #{msgIndex} len={text.Length}");

                // Fast-path lightweight messages on background thread to avoid main-thread JSON spike.
                if (text.IndexOf("sessionRevoked", StringComparison.Ordinal) >= 0)
                {
                    _mainThread.Enqueue(() => SessionRevokedNavigation.GoToLogin());
                    continue;
                }
                if (text.IndexOf(TypeSubmitAck, StringComparison.Ordinal) >= 0)
                {
                    var ack = JsonUtility.FromJson<WsSubmitAckMsg>(text);
                    if (ack != null && ack.type == TypeSubmitAck)
                    {
                        bool ok = ack.ok;
                        string err = ack.error;
                        _mainThread.Enqueue(() =>
                        {
                            if (ok) FinishSubmitCallback(true, null);
                            else FinishSubmitCallback(false, string.IsNullOrEmpty(err) ? Loc.T("net.rejected_by_server") : err);
                        });
                        continue;
                    }
                }
                if (text.IndexOf("\"leaveAck\"", StringComparison.Ordinal) >= 0)
                    continue;

                // Heavy round-result: deserialize on background thread, dispatch parsed object to main thread.
                BattleRoundWsPush push = null;
                try
                {
                    push = JsonConvert.DeserializeObject<BattleRoundWsPush>(text, HopeBattleJson.DeserializeSettings);
                }
                catch
                {
                    push = JsonUtility.FromJson<BattleRoundWsPush>(text);
                }
                if (push?.turnResult == null) continue;

                var capturedPush = push;
                var capturedIdx = msgIndex;
                _mainThread.Enqueue(() => DispatchParsedPush(capturedPush, capturedIdx));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                string details = $"state={ws.State}, closeStatus={ws.CloseStatus}, closeDesc={ws.CloseStatusDescription}";
                EnqueueLogWarn("ReceiveLoop: " + ex.Message + " (" + details + ")");
                break;
            }
        }

        EnqueueLog($"ReceiveLoop exit: state={ws.State}, closeStatus={ws.CloseStatus}, closeDesc={ws.CloseStatusDescription}");
    }

    private void DispatchParsedPush(BattleRoundWsPush push, int msgIndex)
    {
        if (_gameSession == null)
            _gameSession = FindFirstObjectByType<GameSession>();
        if (_gameSession == null) return;

        _gameSession.ReplaceTurnHistoryIds(push.turnHistoryIds, push.currentTurnPointer);
        if (push.turnHistoryIds != null
            && push.currentTurnPointer >= 0
            && push.currentTurnPointer < push.turnHistoryIds.Length)
        {
            string currentTurnId = push.turnHistoryIds[push.currentTurnPointer];
            _gameSession.CacheTurnHistoryEntry(currentTurnId, push.turnResult);
        }

        int resolvedRound = push.turnResult.roundIndex;
        if (resolvedRound <= _gameSession.LastProcessedTurnResultRound)
            return;

        _gameSession.RegisterProcessedTurnResult(resolvedRound);
        GameSession.OnWebSocketRoundPushReceived?.Invoke();
        _gameSession.ApplyTurnResultThenRoundState(push.turnResult, push.roundIndex, push.roundDeadlineUtcMs);
    }

    private async Task DisconnectAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { /* ignore */ }

        _cts = null;
        if (_ws == null) return;
        var w = _ws;
        _ws = null;
        try
        {
            if (w.State == WebSocketState.Open)
                await w.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch { /* ignore */ }

        try
        {
            w.Dispose();
        }
        catch { /* ignore */ }
    }
}
