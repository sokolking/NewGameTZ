using System.Net.WebSockets;
using System.Text;

namespace BattleServer;

/// <summary>Session WebSocket (<c>/ws/session</c>) per <c>users.id</c>; new login revokes previous connections.</summary>
public static class UserSessionSocketRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<long, List<Entry>> ByUser = new();

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
        lock (Gate)
        {
            if (!ByUser.TryGetValue(userId, out var list))
                return;
            list.RemoveAll(e => ReferenceEquals(e.Ws, ws));
            if (list.Count == 0)
                ByUser.Remove(userId);
        }
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
}
