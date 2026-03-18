using BattleServer;
using BattleServer.Models;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BattleHistoryDatabase>();
builder.Services.AddSingleton<BattleTurnDatabase>();
builder.Services.AddSingleton<BattleRoomStore>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors();
app.UseWebSockets();

// Простейшее логирование всех запросов: метод, путь, статус и длительность.
app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var method = ctx.Request.Method;
    var path = ctx.Request.Path.ToString();
    await next();
    sw.Stop();
    var status = ctx.Response.StatusCode;
    Console.WriteLine($"[{DateTime.UtcNow:O}] {method} {path} -> {status} ({sw.ElapsedMilliseconds} ms)");
});

var jsonOpt = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
jsonOpt.Converters.Add(new JsonStringEnumConverter());

var battleHistoryDb = app.Services.GetRequiredService<BattleHistoryDatabase>();
var battleTurnDb = app.Services.GetRequiredService<BattleTurnDatabase>();

BattleRoom.RoundClosedForPush += room =>
{
    if (room.LastTurnResult == null) return;
    var battleId = room.BattleId;
    var turn = room.LastTurnResult;
    var nextRi = room.RoundIndex;
    var roundDeadlineUtcMs = room.RoundDeadlineUtcMs;
    var turnId = Guid.NewGuid().ToString("N");
    var battleRecord = battleHistoryDb.AppendTurn(battleId, turnId);
    battleTurnDb.Save(new BattleTurnRecordDto
    {
        TurnId = turnId,
        BattleId = battleId,
        TurnResult = turn
    });
    _ = Task.Run(async () =>
    {
        try
        {
            var wsPayload = JsonSerializer.Serialize(new
            {
                type = BattleWsProtocol.TypeRoundResolved,
                turnResult = turn,
                roundIndex = nextRi,
                roundDeadlineUtcMs,
                turnHistoryIds = battleRecord.TurnIds,
                currentTurnPointer = battleRecord.TurnIds.Count - 1
            }, jsonOpt);
            await BattleWebSocketRegistry.BroadcastTextAsync(battleId, wsPayload);
            Console.WriteLine($"[tzInfo] Round push (ws): battleId={battleId}, resolvedRound={turn.RoundIndex}, nextRound={nextRi}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[tzInfo] Round push error: {ex.Message}");
        }
    });
};

// Фон: тик таймеров раундов раз в 0.2 сек
var store = app.Services.GetRequiredService<BattleRoomStore>();
var timer = new System.Timers.Timer(200);
timer.Elapsed += (_, _) => store.Tick(0.2f);
timer.Start();

// POST /api/battle/join — встать в очередь или начать бой.
// Тело: { "startCol": 0, "startRow": 0, "solo": false }
app.MapPost("/api/battle/join", (BattleRoomStore s, JoinRequest? body) =>
{
    var (startCol, startRow, solo) = body is { } b ? (b.startCol, b.startRow, b.solo) : (0, 0, false);
    var resp = s.JoinOrCreate(startCol, startRow, solo);
    return Results.Json(resp, jsonOpt);
});

app.Map("/ws/battle", async (HttpContext ctx, BattleRoomStore store) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        Console.WriteLine($"[BattleWS] reject: not a WebSocket upgrade, path={ctx.Request.Path}, utc={DateTime.UtcNow:O}");
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var battleId = ctx.Request.Query["battleId"].FirstOrDefault() ?? "";
    var playerId = ctx.Request.Query["playerId"].FirstOrDefault() ?? "";
    if (string.IsNullOrEmpty(battleId))
    {
        Console.WriteLine($"[BattleWS] reject: missing battleId, utc={DateTime.UtcNow:O}");
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var connId = Guid.NewGuid().ToString("N")[..8];
    var remote = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
    Console.WriteLine($"[BattleWS] accept begin: connId={connId}, battleId={battleId}, playerId={playerId}, remote={remote}, utc={DateTime.UtcNow:O}");

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    BattleWebSocketRegistry.Add(battleId, ws, connId);
    var buf = new byte[262144];
    var recvCount = 0;
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                var seg = new ArraySegment<byte>(buf);
                r = await ws.ReceiveAsync(seg, ctx.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"[BattleWS] client close frame: connId={connId}, battleId={battleId}, utc={DateTime.UtcNow:O}");
                    goto disconnect;
                }
                if (r.MessageType == WebSocketMessageType.Text)
                    ms.Write(buf, 0, r.Count);
            }
            while (!r.EndOfMessage);

            if (ms.Length == 0) continue;
            recvCount++;
            var text = Encoding.UTF8.GetString(ms.ToArray());
            Console.WriteLine($"[BattleWS] recv text #{recvCount}: connId={connId}, bytes={text.Length}, utc={DateTime.UtcNow:O}");

            var msgType = BattleWsProtocol.ReadMessageType(text);
            if (msgType == BattleWsProtocol.TypeSubmitTurn)
            {
                SubmitTurnPayloadDto? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<SubmitTurnPayloadDto>(text, jsonOpt);
                }
                catch
                {
                    payload = null;
                }
                if (payload == null)
                {
                    await BattleWsProtocol.SendJsonAsync(ws, new { type = BattleWsProtocol.TypeSubmitAck, ok = false, error = "Invalid submitTurn body" }, jsonOpt, ctx.RequestAborted);
                    continue;
                }
                payload.BattleId = battleId;

                var submit = store.SubmitTurnLocked(battleId, payload);
                if (submit.NotFound)
                    await BattleWsProtocol.SendJsonAsync(ws, new { type = BattleWsProtocol.TypeSubmitAck, ok = false, error = "Battle not found" }, jsonOpt, ctx.RequestAborted);
                else if (submit.UnknownPlayer)
                    await BattleWsProtocol.SendJsonAsync(ws, new { type = BattleWsProtocol.TypeSubmitAck, ok = false, error = "Unknown player" }, jsonOpt, ctx.RequestAborted);
                else if (submit.WrongRound)
                    await BattleWsProtocol.SendJsonAsync(ws, new { type = BattleWsProtocol.TypeSubmitAck, ok = false, error = "Wrong round", expectedRound = submit.ExpectedRound }, jsonOpt, ctx.RequestAborted);
                else
                    await BattleWsProtocol.SendJsonAsync(ws, new { type = BattleWsProtocol.TypeSubmitAck, ok = true }, jsonOpt, ctx.RequestAborted);
            }
            else if (msgType == BattleWsProtocol.TypeLeave)
            {
                string? leavePid = playerId;
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("playerId", out var pe) && pe.ValueKind == JsonValueKind.String)
                        leavePid = pe.GetString();
                }
                catch { /* use query playerId */ }
                if (!string.IsNullOrEmpty(leavePid))
                    store.PlayerLeft(battleId, leavePid);
                await BattleWsProtocol.SendJsonAsync(ws, new { type = BattleWsProtocol.TypeLeaveAck, ok = true }, jsonOpt, ctx.RequestAborted);
            }
        }
    disconnect:;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"[BattleWS] loop cancelled: connId={connId}, battleId={battleId}, utc={DateTime.UtcNow:O}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[BattleWS] loop error: connId={connId}, battleId={battleId}, {ex.Message}, utc={DateTime.UtcNow:O}");
    }
    finally
    {
        if (!string.IsNullOrEmpty(playerId))
            store.PlayerLeft(battleId, playerId);
        Console.WriteLine($"[BattleWS] disconnect: connId={connId}, battleId={battleId}, playerId={playerId}, recvMsgs={recvCount}, utc={DateTime.UtcNow:O}");
        BattleWebSocketRegistry.Remove(battleId, ws, connId);
    }
});

// GET /api/battle/{battleId} — срез состояния (отладка/инструменты). Итог раунда только по WebSocket.
app.MapGet("/api/battle/{battleId}", (string battleId, BattleRoomStore s) =>
{
    var room = s.GetRoom(battleId);
    if (room == null) return Results.Json(new { error = "Battle not found" }, statusCode: 404);
    var battleRecord = battleHistoryDb.GetBattle(battleId);

    room.FillSpawnArrays(out var spawnIds, out var spawnCols, out var spawnRows);
    var response = new BattleStateResponse
    {
        RoundIndex = room.RoundIndex,
        RoundDuration = BattleRoom.RoundDuration,
        RoundTimeLeft = room.RoundTimeLeft,
        RoundDeadlineUtcMs = room.RoundDeadlineUtcMs,
        TurnResult = null,
        TurnHistoryIds = battleRecord?.TurnIds.ToArray() ?? Array.Empty<string>(),
        CurrentTurnPointer = battleRecord != null ? battleRecord.TurnIds.Count - 1 : -1,
        Participants = room.BuildParticipantStatuses(),
        AllSubmittedThisRound = room.Submissions.Count >= room.Players.Count && room.Players.Count > 0,
        SpawnPlayerIds = spawnIds,
        SpawnCols = spawnCols,
        SpawnRows = spawnRows
    };
    return Results.Json(response, jsonOpt);
});

app.MapGet("/api/battle/{battleId}/turns/{turnId}", (string battleId, string turnId) =>
{
    var record = battleTurnDb.GetTurn(turnId);
    if (record == null || !string.Equals(record.BattleId, battleId, StringComparison.Ordinal))
        return Results.Json(new { error = "Turn not found" }, statusCode: 404);

    return Results.Json(record, jsonOpt);
});

// POST leave — только вне игровой сцены (отмена очереди с меню; в бою выход — disconnect WS + leave по сокету).
app.MapPost("/api/battle/{battleId}/leave", (string battleId, string playerId, BattleRoomStore s) =>
{
    if (string.IsNullOrEmpty(playerId))
        return Results.Json(new { error = "playerId required" }, statusCode: 400);
    if (!s.PlayerLeft(battleId, playerId))
        return Results.Json(new { error = "Battle not found or player not in battle" }, statusCode: 404);
    return Results.Ok(new { left = true });
});

// GET /api/battle/{battleId}/poll — для ожидающего первого игрока: когда второй присоединился, вернуть battleStarted
app.MapGet("/api/battle/{battleId}/poll", (string battleId, string playerId, BattleRoomStore s) =>
{
    var room = s.GetRoom(battleId);
    if (room == null) return Results.Json(new { error = "Battle not found" }, statusCode: 404);
    if (room.Players.Count != 2) return Results.Json(new PollResponse { Status = "waiting" }, jsonOpt);

    var started = room.BuildBattleStartedFor(playerId);
    return Results.Json(new PollResponse { Status = "battle", BattleStarted = started, RoundIndex = room.RoundIndex, RoundDuration = BattleRoom.RoundDuration }, jsonOpt);
});

app.Run();

public class JoinRequest
{
    public int startCol { get; set; }
    public int startRow { get; set; }
    /// <summary>Если true — создать одиночный бой (1 игрок + серверный моб) без ожидания оппонента.</summary>
    public bool solo { get; set; }
}

public class BattleStateResponse
{
    public int RoundIndex { get; set; }
    public float RoundDuration { get; set; }
    public float RoundTimeLeft { get; set; }
    public long RoundDeadlineUtcMs { get; set; }
    public TurnResultPayloadDto? TurnResult { get; set; }
    public string[]? TurnHistoryIds { get; set; }
    public int CurrentTurnPointer { get; set; }
    public BattleParticipantStatusDto[]? Participants { get; set; }
    public bool AllSubmittedThisRound { get; set; }
    public string[]? SpawnPlayerIds { get; set; }
    public int[]? SpawnCols { get; set; }
    public int[]? SpawnRows { get; set; }
}

public class PollResponse
{
    public string Status { get; set; } = "";
    public BattleStartedPayloadDto? BattleStarted { get; set; }
    public int RoundIndex { get; set; }
    public float RoundDuration { get; set; }
}
