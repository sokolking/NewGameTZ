using System;
using System.Collections.Generic;
using System.Globalization;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// WebSocket <c>/ws/session</c>: session revoke, matchmaking queue / ready-check / match start.
/// </summary>
public sealed class SessionWebSocketConnection : MonoBehaviour
{
    private static SessionWebSocketConnection _instance;

    [SerializeField] private string _loginSceneName = "LoginScene";

    private ClientWebSocket _ws;
    private CancellationTokenSource _loopCts;
    private bool _revoked;
    /// <summary>Access token used for the current open <see cref="_ws"/>; mismatch forces reconnect.</summary>
    private string _sessionWsTokenSnapshot;

    /// <summary>Coalesce rapid <see cref="EnsureStarted"/> with the same token (login + MainMenu same frame).</summary>
    private string _sessionLoopCoalesceToken;
    private double _sessionLoopLastBORTime;
    private readonly ConcurrentQueue<Action> _mainThread = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>Queue / ready-check / match start (main thread).</summary>
    public static event Action<string, int, int> OnMatchmakingQueueStateUpdated;

    public static event Action OnMatchmakingQueueJoined;
    public static event Action<string> OnMatchmakingQueueLeft;
    public static event Action<string, string, long, int> OnMatchmakingReadyCheckStarted;
    public static event Action<string, int, int> OnMatchmakingReadyCheckProgress;
    public static event Action<string, string, string> OnMatchmakingReadyCheckCancelled;
    public static event Action<MatchmakingMatchStartedMessage> OnMatchmakingMatchStarted;
    public static event Action<string> OnMatchmakingQueueError;

    public static event Action<SpectatorBattleListItem[]> OnSpectatorListReceived;

    public static event Action<bool, string, BattleStartedPayload> OnSpectatorWatchReceived;

    public static event Action<UserProfileSocketDto> OnUserProfileReceived;

    /// <summary>Fired on the main thread after <c>/ws/session</c> connects (each successful connect / reconnect).</summary>
    public static event Action OnSessionSocketConnected;

    public static bool IsSessionSocketConnected =>
        _instance != null && _instance._ws != null && _instance._ws.State == WebSocketState.Open;

    public static void EnsureStarted()
    {
        if (_instance == null)
        {
            var go = new GameObject("SessionWebSocketConnection");
            DontDestroyOnLoad(go);
            go.AddComponent<SessionWebSocketConnection>();
        }

        _instance.StartSessionLoopIfNeeded();
    }

    /// <summary>Starts the receive loop only if we are not already connected with the same session token.</summary>
    private void StartSessionLoopIfNeeded()
    {
        string token = BattleSessionState.AccessToken ?? "";
        // After logout or session-revoked navigation, StopReconnectLoop() sets _revoked; new login sets a token and must reconnect.
        if (!string.IsNullOrEmpty(token))
            _revoked = false;

        if (_revoked)
            return;
        bool open = _ws != null && _ws.State == WebSocketState.Open;
        if (open && !string.IsNullOrEmpty(token) && string.Equals(_sessionWsTokenSnapshot, token, StringComparison.Ordinal))
            return;

        double now = Time.realtimeSinceStartupAsDouble;
        if (!string.IsNullOrEmpty(token)
            && string.Equals(_sessionLoopCoalesceToken, token, StringComparison.Ordinal)
            && now - _sessionLoopLastBORTime < 0.45)
        {
            return;
        }

        _sessionLoopCoalesceToken = token;
        _sessionLoopLastBORTime = now;
        BeginOrRestartConnection();
    }

    /// <summary>Stops reconnect loop when session ends (revoked or navigation to login from any source).</summary>
    public static void StopReconnectLoop()
    {
        if (_instance == null)
            return;
        _instance._revoked = true;
        _instance._loopCts?.Cancel();
        try
        {
            _instance._ws?.Abort();
        }
        catch
        {
            // ignore
        }

        _instance._ws = null;
        _instance._sessionWsTokenSnapshot = null;
        _instance._sessionLoopCoalesceToken = null;
        _instance._sessionLoopLastBORTime = 0;
    }

    public static void SendMatchmakingQueueJoin(string mode)
    {
        TrySendJson(new { type = "queueJoin", mode });
    }

    public static void SendMatchmakingQueueLeave()
    {
        TrySendJson(new { type = "queueLeave" });
    }

    public static void SendMatchmakingReadyConfirm(string readyCheckId)
    {
        if (string.IsNullOrEmpty(readyCheckId))
            return;
        TrySendJson(new { type = "readyCheckConfirm", readyCheckId });
    }

    public static void SendSpectatorListRequest()
    {
        TrySendJson(new { type = "spectatorListRequest" });
    }

    public static void SendSpectatorWatchRequest(string battleId)
    {
        if (string.IsNullOrEmpty(battleId))
            return;
        TrySendJson(new { type = "spectatorWatchRequest", battleId });
    }

    public static void SendUserProfileRequest() => TrySendJson(new { type = "profileRequest" });

    private static void TrySendJson(object payload)
    {
        string json;
        try
        {
            json = JsonConvert.SerializeObject(payload);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SessionWS] serialize send: " + ex.Message);
            return;
        }

        if (_instance == null)
            return;
        _ = _instance.SendJsonInternalAsync(json);
    }

    private async Task SendJsonInternalAsync(string json)
    {
        if (_revoked)
            return;
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var w = _ws;
            if (w == null || w.State != WebSocketState.Open)
                return;

            var bytes = Encoding.UTF8.GetBytes(json);
            await w.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SessionWS] send: " + ex.Message);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        _loopCts?.Cancel();
        try
        {
            _ws?.Abort();
        }
        catch
        {
            // ignore
        }

        if (_instance == this)
            _instance = null;
        _sendLock.Dispose();
    }

    private void Update()
    {
        // Process at most one action per frame to avoid blocking the main thread
        // when a large message arrives (e.g. battle start payload).
        if (_mainThread.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SessionWS] " + e.Message);
            }
        }
    }

    private void BeginOrRestartConnection()
    {
        _revoked = false;
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _ = RunLoopAsync(_loopCts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_revoked)
        {
            try
            {
                while (string.IsNullOrEmpty(BattleSessionState.AccessToken))
                {
                    if (ct.IsCancellationRequested || _revoked)
                        return;
                    await Task.Delay(1000, ct);
                }

                if (_revoked || ct.IsCancellationRequested)
                    return;
                await ConnectAndReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SessionWS] " + ex.Message);
            }

            if (_revoked || ct.IsCancellationRequested)
                return;
            try
            {
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAndReceiveAsync(CancellationToken ct)
    {
        string token = BattleSessionState.AccessToken;
        if (string.IsNullOrEmpty(token) || _revoked)
            return;

        await DisconnectWsAsync();

        string baseUrl = !string.IsNullOrWhiteSpace(BattleSessionState.SessionBaseUrl)
            ? BattleSessionState.SessionBaseUrl
            : BattleServerRuntime.CurrentBaseUrl;
        string root = baseUrl.TrimEnd('/');
        root = root.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);
        string uri = $"{root}/ws/session";

        var ws = new ClientWebSocket();
        try
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
            await ws.ConnectAsync(new Uri(uri), ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                ws.Dispose();
            }
            catch
            {
                // ignore
            }

            return;
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
            {
                try
                {
                    ws.Dispose();
                }
                catch
                {
                    // ignore
                }

                return;
            }

            string detail = ex.InnerException != null ? ex.Message + " | " + ex.InnerException.Message : ex.Message;
            Debug.LogWarning("[SessionWS] connect failed to " + uri + " — " + detail +
                " (is the battle server running? Check BattleServerRuntime / Toggle_Debug for the correct base URL.)");
            try
            {
                ws.Dispose();
            }
            catch
            {
                // ignore
            }

            return;
        }

        _ws = ws;
        _sessionWsTokenSnapshot = token;
        await TrySendProfileRequestOnSocketAsync(ws, ct).ConfigureAwait(false);
        _mainThread.Enqueue(() => OnSessionSocketConnected?.Invoke());
        var buf = new byte[16384];
        try
        {
            while (!ct.IsCancellationRequested && !_revoked && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    var seg = new ArraySegment<byte>(buf);
                    r = await ws.ReceiveAsync(seg, ct);
                    if (r.MessageType == WebSocketMessageType.Close)
                        goto sessionReceiveEnd;
                    if (r.MessageType == WebSocketMessageType.Text)
                        ms.Write(buf, 0, r.Count);
                }
                while (!r.EndOfMessage);

                if (ms.Length == 0)
                    continue;
                var text = Encoding.UTF8.GetString(ms.ToArray());
                if (text.IndexOf("sessionRevoked", StringComparison.Ordinal) >= 0)
                {
                    _mainThread.Enqueue(HandleRevokedElsewhere);
                    break;
                }

                DispatchSessionJson(text);
            }

        sessionReceiveEnd:;
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SessionWS] receive: " + ex.Message);
        }
        finally
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch
            {
                // ignore
            }

            try
            {
                ws.Dispose();
            }
            catch
            {
                // ignore
            }

            if (ReferenceEquals(_ws, ws))
            {
                _ws = null;
                _sessionWsTokenSnapshot = null;
            }
        }
    }

    private static async Task TrySendProfileRequestOnSocketAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            string json = JsonConvert.SerializeObject(new { type = "profileRequest" });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // loop cancelled / reconnect
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SessionWS] profileRequest right after connect: " + ex.Message);
        }
    }

    private void DispatchSessionJson(string text)
    {
        JObject jo;
        try
        {
            jo = JObject.Parse(text);
        }
        catch
        {
            string snippet = text.Length > 220 ? text.Substring(0, 220) + "…" : text;
            _mainThread.Enqueue(() => Debug.LogWarning("[SessionWS] JSON parse failed, snippet: " + snippet));
            return;
        }

        var t = (string)jo["type"];
        if (string.IsNullOrEmpty(t))
        {
            _mainThread.Enqueue(() => Debug.LogWarning("[SessionWS] message without type field"));
            return;
        }

        switch (t)
        {
            case "queueStateUpdated":
            {
                string mode = (string)jo["mode"] ?? "";
                int cur = jo["currentCount"]?.Value<int>() ?? 0;
                int req = jo["requiredCount"]?.Value<int>() ?? 0;
                _mainThread.Enqueue(() => OnMatchmakingQueueStateUpdated?.Invoke(mode, cur, req));
                break;
            }
            case "queueJoined":
                _mainThread.Enqueue(() => OnMatchmakingQueueJoined?.Invoke());
                break;
            case "queueLeft":
            {
                string mode = (string)jo["mode"];
                _mainThread.Enqueue(() => OnMatchmakingQueueLeft?.Invoke(mode));
                break;
            }
            case "readyCheckStarted":
            {
                string id = (string)jo["readyCheckId"] ?? "";
                string mode = (string)jo["mode"] ?? "";
                long deadline = jo["deadlineUtcMs"]?.Value<long>() ?? 0;
                int req = jo["requiredCount"]?.Value<int>() ?? 0;
                _mainThread.Enqueue(() => OnMatchmakingReadyCheckStarted?.Invoke(id, mode, deadline, req));
                break;
            }
            case "readyCheckProgress":
            {
                string id = (string)jo["readyCheckId"] ?? "";
                int conf = jo["confirmedCount"]?.Value<int>() ?? 0;
                int req = jo["requiredCount"]?.Value<int>() ?? 0;
                _mainThread.Enqueue(() => OnMatchmakingReadyCheckProgress?.Invoke(id, conf, req));
                break;
            }
            case "readyCheckCancelled":
            {
                string id = (string)jo["readyCheckId"] ?? "";
                string mode = (string)jo["mode"] ?? "";
                string reason = (string)jo["reason"] ?? "";
                _mainThread.Enqueue(() => OnMatchmakingReadyCheckCancelled?.Invoke(id, mode, reason));
                break;
            }
            case "matchStarted":
            {
                MatchmakingMatchStartedMessage msg;
                try
                {
                    msg = jo.ToObject<MatchmakingMatchStartedMessage>();
                }
                catch
                {
                    return;
                }

                if (msg == null)
                    return;
                _mainThread.Enqueue(() => OnMatchmakingMatchStarted?.Invoke(msg));
                break;
            }
            case "queueError":
            {
                string code = (string)jo["code"] ?? "error";
                _mainThread.Enqueue(() => OnMatchmakingQueueError?.Invoke(code));
                break;
            }
            case "spectatorListResponse":
            {
                SpectatorBattleListItem[] items = Array.Empty<SpectatorBattleListItem>();
                try
                {
                    var arr = jo["battles"] as JArray;
                    if (arr != null && arr.Count > 0)
                    {
                        var list = new List<SpectatorBattleListItem>(arr.Count);
                        foreach (var el in arr)
                        {
                            if (el is not JObject o)
                                continue;
                            list.Add(new SpectatorBattleListItem
                            {
                                battleId = (string)o["battleId"] ?? "",
                                mode = (string)o["mode"] ?? ""
                            });
                        }

                        items = list.ToArray();
                    }
                }
                catch
                {
                    items = Array.Empty<SpectatorBattleListItem>();
                }

                SpectatorBattleListItem[] captured = items;
                _mainThread.Enqueue(() => OnSpectatorListReceived?.Invoke(captured));
                break;
            }
            case "spectatorWatchResponse":
            {
                bool ok = jo["ok"]?.Value<bool>() ?? false;
                string code = (string)jo["code"] ?? "";
                BattleStartedPayload started = null;
                if (ok && jo["battleStarted"] != null)
                {
                    try
                    {
                        started = jo["battleStarted"].ToObject<BattleStartedPayload>(
                            JsonSerializer.Create(HopeBattleJson.DeserializeSettings));
                    }
                    catch
                    {
                        started = null;
                    }
                }

                bool okCap = ok;
                string codeCap = code ?? "";
                BattleStartedPayload stCap = started;
                _mainThread.Enqueue(() => OnSpectatorWatchReceived?.Invoke(okCap, codeCap, stCap));
                break;
            }
            case "profileResponse":
            {
                UserProfileSocketDto dto = TryParseUserProfileFromProfileResponse(jo, out string parseErr);
                UserProfileSocketDto captured = dto;
                string errCap = parseErr;
                _mainThread.Enqueue(() =>
                {
                    if (captured == null)
                    {
                        Debug.LogWarning("[SessionWS] profileResponse parse failed: " + (errCap ?? "unknown"));
                        return;
                    }

                    OnUserProfileReceived?.Invoke(captured);
                });
                break;
            }
            case "profileError":
            {
                string code = (string)jo["code"] ?? "unknown";
                _mainThread.Enqueue(() => Debug.LogWarning("[SessionWS] profileError: " + code));
                break;
            }
        }
    }

    private static int SessionJsonInt(JToken t, int defaultValue)
    {
        if (t == null || t.Type == JTokenType.Null)
            return defaultValue;
        try
        {
            return Convert.ToInt32(((JValue)t).Value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static UserProfileSocketDto TryParseUserProfileFromProfileResponse(JObject jo, out string error)
    {
        error = null;
        try
        {
            return new UserProfileSocketDto
            {
                username = jo["username"]?.Value<string>() ?? "",
                level = Math.Max(1, SessionJsonInt(jo["level"], 1)),
                strength = SessionJsonInt(jo["strength"], 0),
                agility = SessionJsonInt(jo["agility"], 0),
                intuition = SessionJsonInt(jo["intuition"], 0),
                endurance = SessionJsonInt(jo["endurance"], 0),
                accuracy = SessionJsonInt(jo["accuracy"], 0),
                intellect = SessionJsonInt(jo["intellect"], 0),
                maxHp = Math.Max(1, SessionJsonInt(jo["maxHp"], 1)),
                currentHp = SessionJsonInt(jo["currentHp"], 0),
                maxAp = SessionJsonInt(jo["maxAp"], 0)
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private async Task DisconnectWsAsync()
    {
        var w = _ws;
        _ws = null;
        if (w == null)
            return;
        try
        {
            if (w.State == WebSocketState.Open)
                await w.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch
        {
            // ignore
        }

        try
        {
            w.Dispose();
        }
        catch
        {
            // ignore
        }
    }

    private void HandleRevokedElsewhere()
    {
        if (_revoked)
            return;
        SessionRevokedNavigation.GoToLogin(_loginSceneName);
    }
}
