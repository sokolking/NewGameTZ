using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;

namespace BattleServer;

/// <summary>Session WebSocket (<c>/ws/session</c>) per <c>users.id</c>; new login revokes previous connections.</summary>
public static class UserSessionSocketRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<long, List<Entry>> ByUser = new();

    /// <summary>Invoked when the user has no remaining session sockets (disconnect or revoke).</summary>
    public static event Action<long>? UserFullyDisconnected;

    private sealed class Entry
    {
        public required WebSocket Ws;
        public required string ConnId;
    }

    public static void Add(long userId, WebSocket ws, string connId)
    {
        if (userId <= 0 || ws == null)
            return;
        lock (Gate)
        {
            if (!ByUser.TryGetValue(userId, out var list))
            {
                list = new List<Entry>();
                ByUser[userId] = list;
            }

            list.Add(new Entry { Ws = ws, ConnId = connId });
        }
    }

    public static void Remove(long userId, WebSocket ws)
    {
        if (userId <= 0 || ws == null)
            return;
        bool becameEmpty = false;
        lock (Gate)
        {
            if (!ByUser.TryGetValue(userId, out var list))
                return;
            list.RemoveAll(e => ReferenceEquals(e.Ws, ws));
            if (list.Count == 0)
            {
                ByUser.Remove(userId);
                becameEmpty = true;
            }
        }

        if (becameEmpty)
            RaiseUserFullyDisconnected(userId);
    }

    public static void RevokeAllForUser(long userId)
    {
        List<Entry>? copy;
        lock (Gate)
        {
            if (!ByUser.TryGetValue(userId, out var list) || list.Count == 0)
                return;
            copy = list.ToList();
            list.Clear();
            ByUser.Remove(userId);
        }

        RaiseUserFullyDisconnected(userId);

        foreach (var e in copy)
        {
            try
            {
                if (e.Ws.State == WebSocketState.Open)
                {
                    var payload = Encoding.UTF8.GetBytes(
                        "{\"type\":\"sessionRevoked\",\"reason\":\"login_elsewhere\"}");
                    e.Ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    e.Ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "session_revoked", CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void RaiseUserFullyDisconnected(long userId)
    {
        try
        {
            UserFullyDisconnected?.Invoke(userId);
        }
        catch
        {
            // ignore subscriber errors
        }
    }

    /// <summary>Sends a text frame to every open session socket for <paramref name="userId"/>.</summary>
    public static async Task SendTextToUserAsync(long userId, string text, CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || string.IsNullOrEmpty(text))
            return;
        List<WebSocket>? sockets = null;
        lock (Gate)
        {
            if (!ByUser.TryGetValue(userId, out var list) || list.Count == 0)
                return;
            sockets = new List<WebSocket>();
            foreach (var e in list)
            {
                if (e.Ws.State == WebSocketState.Open)
                    sockets.Add(e.Ws);
            }
        }

        if (sockets == null || sockets.Count == 0)
            return;
        var bytes = Encoding.UTF8.GetBytes(text);
        var seg = new ArraySegment<byte>(bytes);
        foreach (var ws in sockets)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(seg, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore per-socket failures
            }
        }
    }

    public static async Task BroadcastTextToUsersAsync(IEnumerable<long> userIds, string text, CancellationToken cancellationToken = default)
    {
        foreach (var uid in userIds.Distinct())
            await SendTextToUserAsync(uid, text, cancellationToken).ConfigureAwait(false);
    }
}
