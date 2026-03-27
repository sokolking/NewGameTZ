using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// WebSocket <c>/ws/session</c>: server notifies this channel when the same account logs in elsewhere (JWT revoked).
/// </summary>
public sealed class SessionWebSocketConnection : MonoBehaviour
{
    private static SessionWebSocketConnection _instance;

    [SerializeField] private string _loginSceneName = "LoginScene";

    private ClientWebSocket _ws;
    private CancellationTokenSource _loopCts;
    private bool _revoked;
    private readonly ConcurrentQueue<Action> _mainThread = new();

    public static void EnsureStarted()
    {
        if (_instance == null)
        {
            var go = new GameObject("SessionWebSocketConnection");
            DontDestroyOnLoad(go);
            go.AddComponent<SessionWebSocketConnection>();
        }

        _instance.BeginOrRestartConnection();
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
    }

    private void Update()
    {
        while (_mainThread.TryDequeue(out var action))
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
        // Token in Authorization avoids huge query strings (some stacks limit URI length).
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
        var buf = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && !_revoked && ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close)
                    break;
                if (r.MessageType != WebSocketMessageType.Text)
                    continue;
                var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                if (text.IndexOf("sessionRevoked", StringComparison.Ordinal) >= 0)
                {
                    _mainThread.Enqueue(HandleRevokedElsewhere);
                    break;
                }
            }
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
                _ws = null;
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
