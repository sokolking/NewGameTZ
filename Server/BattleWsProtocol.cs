using System.Text;
using System.Text.Json;
using BattleServer.Models;

namespace BattleServer;

/// <summary>Сообщения по WebSocket /ws/battle (клиент ↔ сервер).</summary>
public static class BattleWsProtocol
{
    public const string TypeSubmitTurn = "submitTurn";
    public const string TypeSubmitAck = "submitAck";
    public const string TypeLeave = "leave";
    public const string TypeLeaveAck = "leaveAck";
    public const string TypeRoundResolved = "roundResolved";

    public static async Task SendJsonAsync(System.Net.WebSockets.WebSocket ws, object payload, JsonSerializerOptions opt, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(payload, opt);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, ct);
    }

    public static string? ReadMessageType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
