using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace BattleServer;

/// <summary>
/// WebSocket-подключения по battleId для пуша результата раунда (без SignalR на клиенте Unity).
/// </summary>
public static class BattleWebSocketRegistry
{
    private static readonly Dictionary<string, List<WebSocket>> Sockets = new();
    private static readonly object Gate = new();

    public static void Add(string battleId, WebSocket socket, string? connectionLabel = null)
    {
        if (string.IsNullOrEmpty(battleId)) return;
        int total;
        lock (Gate)
        {
            if (!Sockets.TryGetValue(battleId, out var list))
            {
                list = new List<WebSocket>();
                Sockets[battleId] = list;
            }
            list.Add(socket);
            total = list.Count;
        }
        var label = string.IsNullOrEmpty(connectionLabel) ? "?" : connectionLabel;
        Console.WriteLine($"[BattleWS] registry add: battleId={battleId}, conn={label}, openSocketsInBattle={total}, utc={DateTime.UtcNow:O}");
    }

    public static void Remove(string battleId, WebSocket socket, string? connectionLabel = null)
    {
        if (string.IsNullOrEmpty(battleId) || socket == null) return;
        int remaining;
        lock (Gate)
        {
            if (!Sockets.TryGetValue(battleId, out var list)) return;
            list.Remove(socket);
            remaining = list.Count;
            if (list.Count == 0)
                Sockets.Remove(battleId);
        }
        var label = string.IsNullOrEmpty(connectionLabel) ? "?" : connectionLabel;
        Console.WriteLine($"[BattleWS] registry remove: battleId={battleId}, conn={label}, remainingInBattle={remaining}, utc={DateTime.UtcNow:O}");
    }

    public static async Task BroadcastTextAsync(string battleId, string json, CancellationToken ct = default)
    {
        List<WebSocket>? copy;
        lock (Gate)
        {
            if (!Sockets.TryGetValue(battleId, out var list) || list.Count == 0)
            {
                Console.WriteLine($"[BattleWS] broadcast skip: battleId={battleId}, reason=no_subscribers, utc={DateTime.UtcNow:O}");
                return;
            }
            copy = list.Where(w => w.State == WebSocketState.Open).ToList();
        }

        if (copy.Count == 0)
        {
            Console.WriteLine($"[BattleWS] broadcast skip: battleId={battleId}, reason=all_sockets_closed, utc={DateTime.UtcNow:O}");
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        var previewLen = Math.Min(180, json.Length);
        var preview = previewLen < json.Length ? json[..previewLen] + "…" : json;
        Console.WriteLine($"[BattleWS] broadcast: battleId={battleId}, recipients={copy.Count}, bytes={bytes.Length}, preview={preview}, utc={DateTime.UtcNow:O}");

        var idx = 0;
        foreach (var ws in copy)
        {
            idx++;
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                Console.WriteLine($"[BattleWS] send ok: battleId={battleId}, socket#{idx}, state={ws.State}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BattleWS] send fail: battleId={battleId}, socket#{idx}, {ex.Message}");
            }
        }
    }
}
