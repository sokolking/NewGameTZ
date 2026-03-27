using System.Net.WebSockets;
using System.Text;

namespace BattleServer;

/// <summary>Tracks battle WebSockets by <c>users.id</c> so a new login can disconnect previous sessions.</summary>
public static class UserBattleSocketRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<long, List<Entry>> ByUser = new();

    private sealed class Entry
    {
        public required string BattleId;
        public required string PlayerId;
        public required WebSocket Ws;
        public required string ConnId;
    }

    public static void Add(long userId, string battleId, string playerId, WebSocket ws, string connId)
    {
        if (userId <= 0 || string.IsNullOrEmpty(battleId) || ws == null)
            return;
        lock (Gate)
        {
            if (!ByUser.TryGetValue(userId, out var list))
            {
                list = new List<Entry>();
                ByUser[userId] = list;
            }

            list.Add(new Entry { BattleId = battleId, PlayerId = playerId ?? "", Ws = ws, ConnId = connId });
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
            if (!string.IsNullOrEmpty(e.PlayerId))
                BattleSessionRevokeSkip.MarkSkipPlayerLeft(userId, e.BattleId, e.PlayerId);
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

            BattleWebSocketRegistry.Remove(e.BattleId, e.Ws, e.ConnId);
        }
    }
}
