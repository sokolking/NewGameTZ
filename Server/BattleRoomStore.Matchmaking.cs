using System.Text.Json;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoomStore
{
    private static readonly JsonSerializerOptions MatchmakingJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int ReadyCheckDeadlineSeconds = 45;

    private readonly Dictionary<MatchQueueMode, List<long>> _matchQueues = new();
    private readonly Dictionary<long, UserMatchmakingState> _matchmakingByUser = new();
    private readonly Dictionary<MatchQueueMode, ReadyCheckState> _readyCheckByMode = new();

    private sealed class UserMatchmakingState
    {
        public required MatchQueueMode Mode { get; init; }
        public required bool InReadyCheck { get; init; }
    }

    private sealed class ReadyCheckState
    {
        public required string Id { get; init; }
        public required MatchQueueMode Mode { get; init; }
        public required List<long> UserIds { get; init; }
        public required long DeadlineUtcMs { get; init; }
        public HashSet<long> Confirmed { get; } = new();
    }

    private List<long> QueueFor(MatchQueueMode mode)
    {
        if (!_matchQueues.TryGetValue(mode, out var list))
        {
            list = new List<long>();
            _matchQueues[mode] = list;
        }

        return list;
    }

    /// <summary>Last session socket gone or revoked — leave queues / fail ready checks.</summary>
    public void OnMatchmakingUserSessionEnded(long userId)
    {
        if (userId <= 0)
            return;
        List<(IReadOnlyList<long> targets, string json)>? batch;
        lock (_lock)
        {
            batch = LeaveMatchmakingLocked(userId, "disconnected");
        }

        FireMatchmakingBatch(batch);
    }

    /// <summary>Process session WebSocket matchmaking message and schedule outbound frames.</summary>
    public void HandleMatchmakingWsMessageAndSend(long userId, string messageType, string? modeWire, string? readyCheckId)
    {
        if (userId <= 0)
            return;
        List<(IReadOnlyList<long> targets, string json)>? batch;
        lock (_lock)
        {
            batch = messageType switch
            {
                "queueJoin" => HandleQueueJoinLocked(userId, modeWire),
                "queueLeave" => HandleQueueLeaveLocked(userId),
                "readyCheckConfirm" => HandleReadyConfirmLocked(userId, readyCheckId),
                _ => SingleUser(userId, QueueErrorJson("unknown_message_type"))
            };
        }

        FireMatchmakingBatch(batch);
    }

    public void MatchmakingTick()
    {
        List<(IReadOnlyList<long> targets, string json)>? batch = null;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_lock)
        {
            foreach (var kv in _readyCheckByMode.ToList())
            {
                var mode = kv.Key;
                var ready = kv.Value;
                if (now <= ready.DeadlineUtcMs)
                    continue;
                batch = AppendBatch(batch, CancelReadyCheckLocked(mode, ready, "timeout"));
            }
        }

        FireMatchmakingBatch(batch);
    }

    private static void FireMatchmakingBatch(List<(IReadOnlyList<long> targets, string json)>? batch)
    {
        if (batch == null || batch.Count == 0)
            return;
        _ = Task.Run(async () =>
        {
            foreach (var (targets, json) in batch)
            {
                try
                {
                    await UserSessionSocketRegistry.BroadcastTextToUsersAsync(targets, json).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }
        });
    }

    private static List<(IReadOnlyList<long> targets, string json)>? AppendBatch(
        List<(IReadOnlyList<long> targets, string json)>? batch,
        List<(IReadOnlyList<long> targets, string json)>? more)
    {
        if (more == null || more.Count == 0)
            return batch;
        batch ??= new List<(IReadOnlyList<long> targets, string json)>();
        batch.AddRange(more);
        return batch;
    }

    private List<(IReadOnlyList<long> targets, string json)>? HandleQueueJoinLocked(long userId, string? modeWire)
    {
        if (!MatchQueueModeExtensions.TryParse(modeWire, out var mode))
            return SingleUser(userId, QueueErrorJson("invalid_mode"));

        if (TryFindActiveBattleForUser(userId, out _, out _))
            return SingleUser(userId, QueueErrorJson("already_in_battle"));

        var batch = ClearUserFromMatchmakingBeforeJoinLocked(userId);

        var q = QueueFor(mode);
        if (!q.Contains(userId))
            q.Add(userId);
        _matchmakingByUser[userId] = new UserMatchmakingState { Mode = mode, InReadyCheck = false };

        batch.AddRange(SingleUser(userId, QueueJoinedJson(mode)));
        batch.AddRange(BroadcastQueueState(mode));
        if (TryStartReadyCheckLocked(mode) is { Count: > 0 } readyBatch)
            batch.AddRange(readyBatch);
        return batch;
    }

    private List<(IReadOnlyList<long> targets, string json)>? HandleQueueLeaveLocked(long userId)
    {
        return LeaveMatchmakingLocked(userId, "left");
    }

    /// <summary>Remove user from any queue / cancel ready if needed (switching modes or re-entering queue).</summary>
    private List<(IReadOnlyList<long> targets, string json)> ClearUserFromMatchmakingBeforeJoinLocked(long userId)
    {
        var batch = new List<(IReadOnlyList<long> targets, string json)>();
        if (_matchmakingByUser.TryGetValue(userId, out var st) && st.InReadyCheck
            && _readyCheckByMode.TryGetValue(st.Mode, out var ready))
            batch.AddRange(CancelReadyCheckLocked(st.Mode, ready, "player_left"));

        var touched = new HashSet<MatchQueueMode>();
        foreach (var m in new[] { MatchQueueMode.Pvp1v1, MatchQueueMode.Pvp3v3, MatchQueueMode.Pvp5v5 })
        {
            if (QueueFor(m).Remove(userId))
                touched.Add(m);
        }

        _matchmakingByUser.Remove(userId);
        foreach (var m in touched)
            batch.AddRange(BroadcastQueueState(m));
        return batch;
    }

    private List<(IReadOnlyList<long> targets, string json)>? LeaveMatchmakingLocked(long userId, string reason)
    {
        if (!_matchmakingByUser.TryGetValue(userId, out var st))
            return SingleUser(userId, QueueLeftJson(null));

        if (st.InReadyCheck && _readyCheckByMode.TryGetValue(st.Mode, out var ready) && ready.UserIds.Contains(userId))
            return CancelReadyCheckLocked(st.Mode, ready, reason);

        QueueFor(st.Mode).Remove(userId);
        _matchmakingByUser.Remove(userId);
        var batch = new List<(IReadOnlyList<long> targets, string json)>
        {
            SingleUserTuple(userId, QueueLeftJson(st.Mode))
        };
        batch.AddRange(BroadcastQueueState(st.Mode));
        return batch;
    }

    private List<(IReadOnlyList<long> targets, string json)>? HandleReadyConfirmLocked(long userId, string? readyCheckId)
    {
        if (string.IsNullOrWhiteSpace(readyCheckId))
            return SingleUser(userId, QueueErrorJson("ready_check_id_required"));

        foreach (var kv in _readyCheckByMode)
        {
            var ready = kv.Value;
            if (ready.Id != readyCheckId)
                continue;
            if (!ready.UserIds.Contains(userId))
                return SingleUser(userId, QueueErrorJson("not_in_ready_check"));
            bool firstConfirm = ready.Confirmed.Add(userId);
            var batch = new List<(IReadOnlyList<long> targets, string json)>
            {
                (ready.UserIds.ToArray(), ReadyCheckProgressJson(ready))
            };
            if (firstConfirm && ready.Confirmed.Count >= ready.UserIds.Count)
                batch.AddRange(CompleteReadyCheckAndStartBattleLocked(kv.Key, ready));
            return batch;
        }

        return SingleUser(userId, QueueErrorJson("ready_check_not_found"));
    }

    private List<(IReadOnlyList<long> targets, string json)> CompleteReadyCheckAndStartBattleLocked(MatchQueueMode mode, ReadyCheckState ready)
    {
        int perTeam = mode.PlayersPerTeam();
        var spawns = HexSpawn.FindTwoTeamSpawnsOnOppositeHorizontalSides(perTeam, HexSpawn.DefaultGridWidth, HexSpawn.DefaultGridLength);
        if (spawns.Count < ready.UserIds.Count)
        {
            Console.WriteLine($"[Matchmaking] spawn count {spawns.Count} < players {ready.UserIds.Count}");
            return CancelReadyCheckLocked(mode, ready, "spawn_failed");
        }

        _readyCheckByMode.Remove(mode);
        foreach (var uid in ready.UserIds)
            _matchmakingByUser.Remove(uid);

        var bid = Guid.NewGuid().ToString("N")[..8];
        var room = new BattleRoom(bid, _weaponDb, _obstacleDb, _bodyPartDb, _userDb, _zoneShrinkDb)
        {
            MatchModeWire = mode.ToWireString()
        };
        for (int i = 0; i < ready.UserIds.Count; i++)
        {
            string pid = "P" + (i + 1);
            var (c, r) = spawns[i];
            room.AddPlayer(pid, c, r);
            TryConfigureHumanPlayer(room, pid, ready.UserIds[i]);
        }

        _rooms[bid] = room;
        _battleHistoryDb.EnsureBattle(bid);
        room.StartFirstRound();
        Console.WriteLine($"[Matchmaking] battle started from ready-check: battleId={bid}, mode={mode}, players={ready.UserIds.Count}");

        var batch = new List<(IReadOnlyList<long> targets, string json)>();
        for (int i = 0; i < ready.UserIds.Count; i++)
        {
            long uid = ready.UserIds[i];
            string pid = "P" + (i + 1);
            var started = room.BuildBattleStartedFor(pid);
            var json = JsonSerializer.Serialize(new
            {
                type = "matchStarted",
                mode = mode.ToWireString(),
                battleId = bid,
                playerId = pid,
                battleStarted = started
            }, MatchmakingJsonOptions);
            batch.Add(SingleUserTuple(uid, json));
        }

        return batch;
    }

    private void TryConfigureHumanPlayer(BattleRoom room, string playerId, long battleUserId)
    {
        if (!_userDb.TryGetUsername(battleUserId, out string username))
            username = playerId;
        _userDb.TryGetCombatProfileByUserId(battleUserId, out int playerMaxHp, out int playerCurrentHp, out int playerMaxAp, out int accuracy, out int levelFromDb);
        _userDb.TryGetEquippedWeaponCodeForUserByUserId(battleUserId, out string equippedCode);
        string weaponCode = string.IsNullOrWhiteSpace(equippedCode) ? "fist" : equippedCode.Trim().ToLowerInvariant();
        if (!_weaponDb.TryGetWeaponByCode(weaponCode, out var weapon))
        {
            weaponCode = "fist";
            _weaponDb.TryGetWeaponByCode(weaponCode, out weapon);
        }

        int characterLevel = Math.Max(1, levelFromDb);
        room.SetPlayerDisplayInfo(playerId, username, characterLevel);
        room.RegisterBattlePlayerUserId(playerId, battleUserId);
        room.SetPlayerCombatProfile(playerId, playerMaxHp, playerMaxAp, weaponCode, weapon.DamageMin, weapon.DamageMax, weapon.Range, weapon.AttackApCost, accuracy, weapon.Tightness, weapon.TrajectoryHeight, weapon.IsSniper);
        room.SetPlayerCurrentHpOverride(playerId, playerCurrentHp);
        if (_userDb.TryGetUserProgressProfileByUserId(battleUserId, out var prog))
            room.SetPlayerUnitCardCombatStats(playerId, prog.Strength, prog.Agility, prog.Intuition, prog.Endurance, prog.Accuracy, prog.Intellect);
    }

    private List<(IReadOnlyList<long> targets, string json)>? TryStartReadyCheckLocked(MatchQueueMode mode)
    {
        int need = mode.RequiredHumans();
        var q = QueueFor(mode);
        if (q.Count < need)
            return null;

        var picked = q.Take(need).ToList();
        q.RemoveRange(0, need);
        foreach (var uid in picked)
        {
            _matchmakingByUser[uid] = new UserMatchmakingState { Mode = mode, InReadyCheck = true };
        }

        string id = Guid.NewGuid().ToString("N")[..12];
        long deadline = DateTimeOffset.UtcNow.AddSeconds(ReadyCheckDeadlineSeconds).ToUnixTimeMilliseconds();
        var ready = new ReadyCheckState
        {
            Id = id,
            Mode = mode,
            UserIds = picked,
            DeadlineUtcMs = deadline
        };
        _readyCheckByMode[mode] = ready;

        var batch = new List<(IReadOnlyList<long> targets, string json)>
        {
            (picked, ReadyCheckStartedJson(ready)),
            (picked, ReadyCheckProgressJson(ready))
        };
        batch.AddRange(BroadcastQueueState(mode));
        Console.WriteLine($"[Matchmaking] ready-check started: mode={mode}, id={id}, users={picked.Count}");
        return batch;
    }

    private List<(IReadOnlyList<long> targets, string json)> CancelReadyCheckLocked(MatchQueueMode mode, ReadyCheckState ready, string reason)
    {
        _readyCheckByMode.Remove(mode);
        var q = QueueFor(mode);
        for (int i = ready.UserIds.Count - 1; i >= 0; i--)
        {
            long uid = ready.UserIds[i];
            _matchmakingByUser.Remove(uid);
            if (!q.Contains(uid))
                q.Insert(0, uid);
            _matchmakingByUser[uid] = new UserMatchmakingState { Mode = mode, InReadyCheck = false };
        }

        int qc = QueueFor(mode).Count;
        var batch = new List<(IReadOnlyList<long> targets, string json)>
        {
            (ready.UserIds, ReadyCheckCancelledJson(mode, ready, reason, qc))
        };
        batch.AddRange(BroadcastQueueState(mode));
        Console.WriteLine($"[Matchmaking] ready-check cancelled: mode={mode}, id={ready.Id}, reason={reason}");
        return batch;
    }

    private List<(IReadOnlyList<long> targets, string json)> BroadcastQueueState(MatchQueueMode mode)
    {
        var q = QueueFor(mode);
        int req = mode.RequiredHumans();
        if (q.Count == 0)
            return new List<(IReadOnlyList<long> targets, string json)>();
        var targets = q.Distinct().ToArray();
        string json = QueueStateJson(mode, q.Count, req);
        return new List<(IReadOnlyList<long> targets, string json)> { (targets, json) };
    }

    private static List<(IReadOnlyList<long> targets, string json)> SingleUser(long userId, string json) =>
        new List<(IReadOnlyList<long> targets, string json)> { SingleUserTuple(userId, json) };

    private static (IReadOnlyList<long> targets, string json) SingleUserTuple(long userId, string json) =>
        (new[] { userId }, json);

    private static string QueueErrorJson(string code) =>
        JsonSerializer.Serialize(new { type = "queueError", code }, MatchmakingJsonOptions);

    private static string QueueJoinedJson(MatchQueueMode mode) =>
        JsonSerializer.Serialize(new { type = "queueJoined", mode = mode.ToWireString() }, MatchmakingJsonOptions);

    private static string QueueLeftJson(MatchQueueMode? mode) =>
        JsonSerializer.Serialize(new
        {
            type = "queueLeft",
            mode = mode?.ToWireString()
        }, MatchmakingJsonOptions);

    private static string QueueStateJson(MatchQueueMode mode, int current, int required) =>
        JsonSerializer.Serialize(new
        {
            type = "queueStateUpdated",
            mode = mode.ToWireString(),
            currentCount = current,
            requiredCount = required
        }, MatchmakingJsonOptions);

    private static string ReadyCheckStartedJson(ReadyCheckState r) =>
        JsonSerializer.Serialize(new
        {
            type = "readyCheckStarted",
            readyCheckId = r.Id,
            mode = r.Mode.ToWireString(),
            deadlineUtcMs = r.DeadlineUtcMs,
            requiredCount = r.UserIds.Count
        }, MatchmakingJsonOptions);

    private static string ReadyCheckProgressJson(ReadyCheckState r) =>
        JsonSerializer.Serialize(new
        {
            type = "readyCheckProgress",
            readyCheckId = r.Id,
            mode = r.Mode.ToWireString(),
            confirmedCount = r.Confirmed.Count,
            requiredCount = r.UserIds.Count
        }, MatchmakingJsonOptions);

    private static string ReadyCheckCancelledJson(MatchQueueMode mode, ReadyCheckState r, string reason, int queueCountAfter) =>
        JsonSerializer.Serialize(new
        {
            type = "readyCheckCancelled",
            readyCheckId = r.Id,
            mode = mode.ToWireString(),
            reason,
            currentCount = queueCountAfter,
            requiredCount = mode.RequiredHumans()
        }, MatchmakingJsonOptions);
}
