using BattleServer;
using BattleServer.Models;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var logStore = new BattleLogStore();
builder.Services.AddSingleton(logStore);
builder.Services.AddSingleton<BattlePostgresDatabase>();
builder.Services.AddSingleton<BattleHistoryDatabase>();
builder.Services.AddSingleton<BattleTurnDatabase>();
builder.Services.AddSingleton<BattleUserDatabase>();
builder.Services.AddSingleton<BattleWeaponDatabase>();
builder.Services.AddSingleton<BattleObstacleBalanceDatabase>();
builder.Services.AddSingleton<BattleRoomStore>(sp => new BattleRoomStore(
    sp.GetRequiredService<BattleHistoryDatabase>(),
    sp.GetRequiredService<BattleTurnDatabase>(),
    sp.GetRequiredService<BattleWeaponDatabase>(),
    sp.GetRequiredService<BattleObstacleBalanceDatabase>()));
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
Console.SetOut(new BattleLogConsoleWriter(Console.Out, logStore, isError: false));
Console.SetError(new BattleLogConsoleWriter(Console.Error, logStore, isError: true));

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

var postgresDb = app.Services.GetRequiredService<BattlePostgresDatabase>();
postgresDb.EnsureCreated();
var battleHistoryDb = app.Services.GetRequiredService<BattleHistoryDatabase>();
var battleTurnDb = app.Services.GetRequiredService<BattleTurnDatabase>();
var battleUserDb = app.Services.GetRequiredService<BattleUserDatabase>();
var battleWeaponDb = app.Services.GetRequiredService<BattleWeaponDatabase>();

BattleRoom.RoundClosedForPush += room =>
{
    if (room.LastTurnResult == null) return;
    var battleId = room.BattleId;
    var turn = room.LastTurnResult;
    var nextRi = room.RoundIndex;
    var roundDeadlineUtcMs = room.RoundDeadlineUtcMs;
    var turnId = Guid.NewGuid().ToString("N");
    battleTurnDb.Save(new BattleTurnRecordDto
    {
        TurnId = turnId,
        BattleId = battleId,
        TurnResult = turn
    });
    var battleRecord = battleHistoryDb.AppendTurn(battleId, turnId);
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
    string username = body?.username?.Trim() ?? "";
    string password = body?.password ?? "";
    if (!battleUserDb.ValidateCredentials(username, password))
        return Results.Json(new ErrorResponse { Error = "Invalid username or password." }, jsonOpt, statusCode: 401);
    battleUserDb.TryGetCombatProfile(username, out int playerMaxHp, out int playerMaxAp, out string weaponCode);
    if (!battleWeaponDb.TryGetWeaponByCode(weaponCode, out var weapon))
    {
        weaponCode = "fist";
        battleWeaponDb.TryGetWeaponByCode(weaponCode, out weapon);
    }

    var resp = s.JoinOrCreate(
        startCol,
        startRow,
        solo,
        playerMaxHp,
        playerMaxAp,
        weaponCode,
        weapon.Damage,
        weapon.Range,
        weapon.AttackApCost);
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

    room.FillSpawnArrays(out var spawnIds, out var spawnCols, out var spawnRows, out var spawnCurrentAps, out var spawnMaxHps, out var spawnCurrentHps, out var spawnCurrentPostures, out var spawnWeaponCodes, out var spawnWeaponDamages, out var spawnWeaponRanges, out var spawnWeaponAttackApCosts);
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
        SpawnRows = spawnRows,
        SpawnCurrentAps = spawnCurrentAps,
        SpawnMaxHps = spawnMaxHps,
        SpawnCurrentHps = spawnCurrentHps,
        SpawnCurrentPostures = spawnCurrentPostures,
        SpawnWeaponCodes = spawnWeaponCodes,
        SpawnWeaponDamages = spawnWeaponDamages,
        SpawnWeaponRanges = spawnWeaponRanges,
        SpawnWeaponAttackApCosts = spawnWeaponAttackApCosts
    };
    return Results.Json(response, jsonOpt);
});

app.MapPost("/api/battle/{battleId}/equip-weapon", (string battleId, EquipWeaponHttpRequest? body, BattleRoomStore store, BattleWeaponDatabase weapons) =>
{
    if (body == null || string.IsNullOrWhiteSpace(body.PlayerId) || string.IsNullOrWhiteSpace(body.WeaponCode))
        return Results.Json(new { error = "playerId and weaponCode required" }, statusCode: 400);
    var room = store.GetRoom(battleId);
    if (room == null)
        return Results.Json(new { error = "Battle not found" }, statusCode: 404);
    if (!weapons.TryGetWeaponByCode(body.WeaponCode.Trim(), out var w))
        return Results.Json(new { error = "Unknown weapon" }, statusCode: 400);
    if (!room.TryEquipWeapon(body.PlayerId.Trim(), w.Code, w.Damage, w.Range, w.AttackApCost, out var fail))
        return Results.Json(new { error = fail ?? "equip_failed" }, statusCode: 400);
    return Results.Json(new { ok = true, weaponCode = w.Code, weaponDamage = w.Damage, weaponRange = w.Range, weaponAttackApCost = w.AttackApCost });
});

app.MapGet("/api/battle/{battleId}/turns/{turnId}", (string battleId, string turnId) =>
{
    var record = battleTurnDb.GetTurn(turnId);
    if (record == null || !string.Equals(record.BattleId, battleId, StringComparison.Ordinal))
        return Results.Json(new { error = "Turn not found" }, statusCode: 404);

    return Results.Json(record, jsonOpt);
});

app.MapGet("/api/db/battles", (BattleHistoryDatabase db, int? take) =>
{
    int requested = take ?? 100;
    return Results.Json(db.ListBattles(requested), jsonOpt);
});

app.MapGet("/api/db/battles/{battleId}/turns", (string battleId, BattleTurnDatabase db, int? take) =>
{
    int requested = take ?? 200;
    return Results.Json(db.ListTurnsForBattle(battleId, requested), jsonOpt);
});

app.MapGet("/api/db/turns/{turnId}", (string turnId, BattleTurnDatabase db) =>
{
    var detail = db.GetTurnDetail(turnId);
    if (detail == null)
        return Results.Json(new { error = "Turn not found" }, statusCode: 404);

    return Results.Json(detail, jsonOpt);
});

app.MapGet("/api/db/users", (BattleUserDatabase db, int? take) =>
{
    int requested = take ?? 100;
    return Results.Json(db.ListUsers(requested), jsonOpt);
});

app.MapPost("/api/db/user/inventory", (BattleUserDatabase users, UserInventoryAuthRequest? body) =>
{
    if (body == null || string.IsNullOrWhiteSpace(body.username))
        return Results.Json(new { error = "username required" }, jsonOpt, statusCode: 400);
    if (!users.TryGetInventory(body.username.Trim(), body.password ?? "", out var slots))
        return Results.Json(new { error = "Invalid credentials" }, jsonOpt, statusCode: 401);
    return Results.Json(new { slots }, jsonOpt);
});

app.MapGet("/api/db/weapons", (BattleWeaponDatabase db, int? take) =>
{
    int requested = take ?? 100;
    return Results.Json(db.ListWeapons(requested), jsonOpt);
});

app.MapPost("/api/db/weapons", (BattleWeaponDatabase db, WeaponUpsertRequest request) =>
{
    if (request == null)
        return Results.Json(new { error = "request body required" }, statusCode: 400);
    if (string.IsNullOrWhiteSpace(request.code) || string.IsNullOrWhiteSpace(request.name))
        return Results.Json(new { error = "code and name are required" }, statusCode: 400);

    db.UpsertWeapon(request.code, request.name, request.damage, request.range, request.iconKey, request.attackApCost);
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/db/obstacle-balance", (BattleObstacleBalanceDatabase db) =>
    Results.Json(db.GetBalance(), jsonOpt));
app.MapPut("/api/db/obstacle-balance", (BattleObstacleBalanceDatabase db, BattleObstacleBalanceRowDto? body) =>
{
    if (body == null)
        return Results.Json(new { error = "body required" }, jsonOpt, statusCode: 400);
    db.UpsertBalance(body);
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/logs/recent", (BattleLogStore logs, int? take) =>
{
    int requested = take ?? 200;
    return Results.Json(logs.GetRecent(requested), jsonOpt);
});

app.MapGet("/api/logs/stream", async (HttpContext ctx, BattleLogStore logs) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    ctx.Response.ContentType = "text/event-stream";

    var (subscriptionId, reader) = logs.Subscribe();
    try
    {
        await ctx.Response.WriteAsync(": connected\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            while (reader.TryRead(out var entry))
            {
                string json = JsonSerializer.Serialize(entry, jsonOpt);
                await ctx.Response.WriteAsync($"data: {json}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }

            await Task.Delay(250, ctx.RequestAborted);
            await ctx.Response.WriteAsync(": heartbeat\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected.
    }
    finally
    {
        logs.Unsubscribe(subscriptionId);
    }
});

app.MapGet("/logs", () => Results.Content(BattleLogDashboardPage.Html, "text/html; charset=utf-8"));
app.MapGet("/db", () => Results.Content(BattleDbDashboardPage.Html, "text/html; charset=utf-8"));
app.MapGet("/users", () => Results.Content(BattleUsersDashboardPage.Html, "text/html; charset=utf-8"));
app.MapGet("/weapons", () => Results.Content(BattleWeaponsDashboardPage.Html, "text/html; charset=utf-8"));
app.MapGet("/obstacle-balance", () => Results.Content(BattleObstacleBalanceDashboardPage.Html, "text/html; charset=utf-8"));

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
    public string username { get; set; } = "";
    public string password { get; set; } = "";
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
    public int[]? SpawnCurrentAps { get; set; }
    public int[]? SpawnMaxHps { get; set; }
    public int[]? SpawnCurrentHps { get; set; }
    public string[]? SpawnCurrentPostures { get; set; }
    public string[]? SpawnWeaponCodes { get; set; }
    public int[]? SpawnWeaponDamages { get; set; }
    public int[]? SpawnWeaponRanges { get; set; }
    public int[]? SpawnWeaponAttackApCosts { get; set; }
}

public class PollResponse
{
    public string Status { get; set; } = "";
    public BattleStartedPayloadDto? BattleStarted { get; set; }
    public int RoundIndex { get; set; }
    public float RoundDuration { get; set; }
}

public class ErrorResponse
{
    public string Error { get; set; } = "";
}

public class WeaponUpsertRequest
{
    public string code { get; set; } = "";
    public string name { get; set; } = "";
    public int damage { get; set; }
    public int range { get; set; }
    public string? iconKey { get; set; }
    /// <summary>Стоимость атаки этим оружием (ОД). По умолчанию 1.</summary>
    public int attackApCost { get; set; } = 1;
}

public class UserInventoryAuthRequest
{
    public string username { get; set; } = "";
    public string password { get; set; } = "";
}

public class EquipWeaponHttpRequest
{
    public string PlayerId { get; set; } = "";
    public string WeaponCode { get; set; } = "";
}

public static class BattleLogDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Battle Server Logs</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #0f1117;
      --panel: #171a22;
      --border: #2b3140;
      --text: #e8ecf4;
      --muted: #9aa4b2;
      --info: #6ea8fe;
      --warn: #ffcc66;
      --error: #ff6b6b;
      --http: #6dd3a0;
      --ws: #7aa2ff;
      --tz: #c792ea;
      --mob: #f78c6c;
      --system: #7f8ea3;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: var(--bg);
      color: var(--text);
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
    }
    .topbar {
      position: sticky;
      top: 0;
      z-index: 10;
      background: rgba(15,17,23,.92);
      backdrop-filter: blur(10px);
      border-bottom: 1px solid var(--border);
      padding: 14px 18px;
    }
    .title { margin: 0 0 12px; font-size: 20px; }
    .controls {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      align-items: center;
    }
    .chip-row {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
    .chip {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      background: var(--panel);
      border: 1px solid var(--border);
      padding: 6px 10px;
      border-radius: 999px;
      color: var(--text);
      font-size: 13px;
    }
    input[type="search"] {
      background: var(--panel);
      border: 1px solid var(--border);
      color: var(--text);
      border-radius: 10px;
      padding: 9px 12px;
      min-width: 280px;
    }
    button {
      background: var(--panel);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 9px 12px;
      cursor: pointer;
    }
    button:hover { border-color: #46506a; }
    .status {
      color: var(--muted);
      font-size: 13px;
      margin-left: auto;
    }
    .log-list {
      padding: 14px 18px 24px;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .entry {
      display: grid;
      grid-template-columns: 110px 72px 110px 1fr;
      gap: 10px;
      align-items: start;
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 10px 12px;
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 12px;
      line-height: 1.45;
    }
    .entry.hidden { display: none; }
    .time { color: var(--muted); }
    .level {
      text-transform: uppercase;
      font-weight: 700;
      letter-spacing: .04em;
    }
    .level-info { color: var(--info); }
    .level-warn { color: var(--warn); }
    .level-error { color: var(--error); }
    .tag {
      display: inline-flex;
      width: fit-content;
      min-width: 70px;
      justify-content: center;
      border-radius: 999px;
      padding: 3px 8px;
      font-weight: 700;
      border: 1px solid transparent;
    }
    .tag-http { color: var(--http); border-color: rgba(109,211,160,.3); background: rgba(109,211,160,.08); }
    .tag-BattleWS { color: var(--ws); border-color: rgba(122,162,255,.3); background: rgba(122,162,255,.08); }
    .tag-tzInfo { color: var(--tz); border-color: rgba(199,146,234,.3); background: rgba(199,146,234,.08); }
    .tag-mobAI { color: var(--mob); border-color: rgba(247,140,108,.3); background: rgba(247,140,108,.08); }
    .tag-system { color: var(--system); border-color: rgba(127,142,163,.3); background: rgba(127,142,163,.08); }
    .message {
      white-space: pre-wrap;
      word-break: break-word;
    }
    .footer-note {
      color: var(--muted);
      font-size: 12px;
      padding: 0 18px 18px;
    }
    @media (max-width: 900px) {
      .entry {
        grid-template-columns: 1fr;
      }
    }
  </style>
</head>
<body>
  <div class="topbar">
    <h1 class="title">Battle Server Logs</h1>
    <div class="controls">
      <input id="search" type="search" placeholder="Search logs, tags, battleId, playerId">
      <button id="toggleScroll" type="button">Auto-scroll: on</button>
      <button id="clear" type="button">Clear View</button>
      <div class="chip-row">
        <label class="chip"><input class="tag-filter" type="checkbox" value="BattleWS" checked> `BattleWS`</label>
        <label class="chip"><input class="tag-filter" type="checkbox" value="tzInfo" checked> `tzInfo`</label>
        <label class="chip"><input class="tag-filter" type="checkbox" value="mobAI" checked> `mobAI`</label>
        <label class="chip"><input class="tag-filter" type="checkbox" value="http" checked> `http`</label>
        <label class="chip"><input class="tag-filter" type="checkbox" value="system" checked> `system`</label>
      </div>
      <div id="status" class="status">connecting...</div>
    </div>
  </div>
  <div id="logList" class="log-list"></div>
  <div class="footer-note">Live stream via Server-Sent Events from `/api/logs/stream`.</div>
  <script>
    const logList = document.getElementById('logList');
    const statusEl = document.getElementById('status');
    const searchEl = document.getElementById('search');
    const toggleScrollEl = document.getElementById('toggleScroll');
    const clearEl = document.getElementById('clear');
    const filterEls = Array.from(document.querySelectorAll('.tag-filter'));
    const entries = [];
    let autoScroll = true;

    function activeTags() {
      const checked = filterEls.filter(el => el.checked).map(el => el.value);
      return new Set(checked);
    }

    function entryMatches(entry) {
      const tags = activeTags();
      if (!tags.has(entry.tag) && !(entry.tag === 'system' && tags.has('system')))
        return false;

      const query = searchEl.value.trim().toLowerCase();
      if (!query)
        return true;
      const haystack = `${entry.tag} ${entry.level} ${entry.message} ${entry.raw}`.toLowerCase();
      return haystack.includes(query);
    }

    function fmtTime(utc) {
      const d = new Date(utc);
      return d.toLocaleTimeString();
    }

    function renderEntry(entry, prepend = false) {
      const row = document.createElement('div');
      row.className = 'entry';
      row.dataset.tag = entry.tag;
      row.dataset.level = entry.level;
      row.innerHTML = `
        <div class="time">${fmtTime(entry.utc)}</div>
        <div class="level level-${entry.level}">${entry.level}</div>
        <div><span class="tag tag-${entry.tag}">${entry.tag}</span></div>
        <div class="message"></div>
      `;
      row.querySelector('.message').textContent = entry.message;
      entry.element = row;
      if (!entryMatches(entry))
        row.classList.add('hidden');

      if (prepend && logList.firstChild)
        logList.insertBefore(row, logList.firstChild);
      else
        logList.appendChild(row);
    }

    function applyFilters() {
      for (const entry of entries)
        entry.element.classList.toggle('hidden', !entryMatches(entry));
    }

    function addEntry(entry) {
      entries.push(entry);
      renderEntry(entry);
      while (entries.length > 1500) {
        const removed = entries.shift();
        removed.element?.remove();
      }
      if (autoScroll)
        window.scrollTo({ top: document.body.scrollHeight, behavior: 'instant' });
    }

    async function loadRecent() {
      const resp = await fetch('/api/logs/recent?take=250');
      const items = await resp.json();
      for (const entry of items)
        addEntry(entry);
    }

    function connect() {
      const stream = new EventSource('/api/logs/stream');
      statusEl.textContent = 'connected';
      stream.onopen = () => statusEl.textContent = 'connected';
      stream.onerror = () => statusEl.textContent = 'reconnecting...';
      stream.onmessage = ev => {
        try {
          addEntry(JSON.parse(ev.data));
        } catch {
          // ignore malformed event
        }
      };
    }

    searchEl.addEventListener('input', applyFilters);
    filterEls.forEach(el => el.addEventListener('change', applyFilters));
    toggleScrollEl.addEventListener('click', () => {
      autoScroll = !autoScroll;
      toggleScrollEl.textContent = `Auto-scroll: ${autoScroll ? 'on' : 'off'}`;
    });
    clearEl.addEventListener('click', () => {
      entries.splice(0, entries.length);
      logList.innerHTML = '';
    });

    loadRecent().then(connect).catch(err => {
      statusEl.textContent = 'failed to load logs';
      console.error(err);
    });
  </script>
</body>
</html>
""";
}
