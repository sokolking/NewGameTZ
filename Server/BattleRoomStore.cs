using BattleServer.Models;

namespace BattleServer;

/// <summary>Хранилище боёв в памяти. Одна глобальная комната для быстрого теста: первые два join попадают в один бой.</summary>
public partial class BattleRoomStore
{
    private readonly object _lock = new();
    private readonly BattleHistoryDatabase _battleHistoryDb;
    private readonly BattleTurnDatabase _battleTurnDb;
    private readonly BattleWeaponDatabase _weaponDb;
    private readonly BattleObstacleBalanceDatabase _obstacleDb;
    private readonly BattleZoneShrinkDatabase _zoneShrinkDb;
    private readonly BattleBodyPartDatabase _bodyPartDb;
    private readonly BattleUserDatabase _userDb;
    /// <summary>Очередь ожидающих (один игрок). Как только второй присоединился — создаём бой из двух.</summary>
    private string? _waitingBattleId;

    private readonly Dictionary<string, BattleRoom> _rooms = new();

    public BattleRoomStore(BattleHistoryDatabase battleHistoryDb, BattleTurnDatabase battleTurnDb, BattleWeaponDatabase weaponDb, BattleObstacleBalanceDatabase obstacleDb, BattleZoneShrinkDatabase zoneShrinkDb, BattleBodyPartDatabase bodyPartDb, BattleUserDatabase userDb)
    {
        _battleHistoryDb = battleHistoryDb;
        _battleTurnDb = battleTurnDb;
        _weaponDb = weaponDb;
        _obstacleDb = obstacleDb;
        _zoneShrinkDb = zoneShrinkDb;
        _bodyPartDb = bodyPartDb;
        _userDb = userDb;
    }

    /// <summary>Таймер раундов (вызывать из фона).</summary>
    public void Tick(float deltaSeconds)
    {
        lock (_lock)
        {
            foreach (var r in _rooms.Values)
                r.Tick(deltaSeconds);
        }
    }

    /// <summary>Присоединиться к поиску. Возвращает (battleId, playerId, battleStarted?, room). Если battleStarted != null — бой начался (2 игрока).</summary>
    public (string battleId, string playerId, BattleRoom room, BattleStartedPayloadDto? battleStarted) Join(int startCol = 0, int startRow = 0)
    {
        lock (_lock)
        {
            if (_waitingBattleId == null)
            {
                var bid = Guid.NewGuid().ToString("N")[..8];
                _waitingBattleId = bid;
                var room = new BattleRoom(bid, _weaponDb, _obstacleDb, _bodyPartDb, _userDb, _zoneShrinkDb);
                int p1c = Math.Clamp(startCol, 0, HexSpawn.DefaultGridWidth - 1);
                int p1r = Math.Clamp(startRow, 0, HexSpawn.DefaultGridLength - 1);
                room.AddPlayer("P1", p1c, p1r);
                room.SetPlayerDisplayInfo("P1", "P1", 1);
                _rooms[bid] = room;
                _battleHistoryDb.EnsureBattle(bid);
                return (bid, "P1", room, null);
            }

            var existingRoom = _rooms[_waitingBattleId];
            var (firstCol, firstRow) = existingRoom.Players["P1"];
            var (p2c, p2r) = HexSpawn.FindOpponentSpawn(firstCol, firstRow, HexSpawn.DefaultGridWidth, HexSpawn.DefaultGridLength, HexSpawn.MinSpawnHexDistance);
            existingRoom.AddPlayer("P2", p2c, p2r);
            existingRoom.SetPlayerDisplayInfo("P2", "P2", 1);
            existingRoom.StartFirstRound();
            var id = _waitingBattleId;
            _waitingBattleId = null;
            return (id!, "P2", existingRoom, existingRoom.BuildBattleStartedFor("P2"));
        }
    }

    /// <summary>Второй игрок — вызываем после того как первый уже в очереди. Передаём battleId от первого (для теста можно один общий).</summary>
    public (string battleId, string playerId, BattleRoom room, BattleStartedPayloadDto? battleStarted) JoinSecond(string battleId, int startCol = 24, int startRow = 39)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(battleId, out var room) || room.Players.Count != 1)
                throw new InvalidOperationException("No waiting room for this battleId");

            var (p1c, p1r) = room.Players["P1"];
            var (p2c, p2r) = HexSpawn.FindOpponentSpawn(p1c, p1r, HexSpawn.DefaultGridWidth, HexSpawn.DefaultGridLength, HexSpawn.MinSpawnHexDistance);
            room.AddPlayer("P2", p2c, p2r);
            room.SetPlayerDisplayInfo("P2", "P2", 1);
            room.StartFirstRound();
            var started = room.BuildBattleStartedFor("P2");
            _waitingBattleId = null;
            return (battleId, "P2", room, started);
        }
    }

    /// <summary>
    /// Упрощённый join.
    /// - Если solo == true — создаём одиночный бой (1 игрок + серверный моб), сразу запускаем раунд и возвращаем battleStarted.
    /// - Если есть ожидающий и solo == false — создаём пару и возвращаем battleStarted второму игроку.
    /// - Иначе встаём в очередь как P1 и ждём второго игрока.
    /// </summary>
    public JoinResponse JoinOrCreate(int startCol, int startRow, bool solo, int playerMaxHp, int playerCurrentHp, int playerMaxAp, string weaponCode, int weaponDamageMin, int weaponDamageMax, int weaponRange, int weaponAttackApCost, string displayName, long battleUserId, int characterLevel = 1, int accuracy = 10, double weaponTightness = 1, int weaponTrajectoryHeight = 1, bool weaponIsSniper = false)
    {
        lock (_lock)
        {
            bool haveBattleUserId = battleUserId > 0;

            // Одиночный бой: не используем очередь, сразу создаём комнату и запускаем первый раунд.
            if (solo)
            {
                var soloBattleId = Guid.NewGuid().ToString("N")[..8];
                var soloRoom = new BattleRoom(soloBattleId, _weaponDb, _obstacleDb, _bodyPartDb, _userDb, _zoneShrinkDb) { IsSolo = true };
                int soloCol = Math.Clamp(startCol, 0, HexSpawn.DefaultGridWidth - 1);
                int soloRow = Math.Clamp(startRow, 0, HexSpawn.DefaultGridLength - 1);
                soloRoom.AddPlayer("P1", soloCol, soloRow);
                soloRoom.SetPlayerDisplayInfo("P1", displayName, characterLevel);
                if (haveBattleUserId)
                    soloRoom.RegisterBattlePlayerUserId("P1", battleUserId);
                soloRoom.SetPlayerCombatProfile("P1", playerMaxHp, playerMaxAp, weaponCode, weaponDamageMin, weaponDamageMax, weaponRange, weaponAttackApCost, accuracy, weaponTightness, weaponTrajectoryHeight, weaponIsSniper);
                soloRoom.SetPlayerCurrentHpOverride("P1", playerCurrentHp);
                _rooms[soloBattleId] = soloRoom;
                _battleHistoryDb.EnsureBattle(soloBattleId);
                soloRoom.StartFirstRound();
                Console.WriteLine($"[tzInfo] Solo join: battleId={soloBattleId}, playerId=P1, start=({soloCol},{soloRow})");
                return new JoinResponse
                {
                    BattleId = soloBattleId,
                    PlayerId = "P1",
                    Status = "battle",
                    BattleStarted = soloRoom.BuildBattleStartedFor("P1")
                };
            }

            if (_waitingBattleId != null && _rooms.TryGetValue(_waitingBattleId, out var waitingRoom))
            {
                var battleId = _waitingBattleId;
                var (p1c, p1r) = waitingRoom.Players["P1"];
                var (p2c, p2r) = HexSpawn.FindOpponentSpawn(p1c, p1r, HexSpawn.DefaultGridWidth, HexSpawn.DefaultGridLength, HexSpawn.MinSpawnHexDistance);
                waitingRoom.AddPlayer("P2", p2c, p2r);
                waitingRoom.SetPlayerDisplayInfo("P2", displayName, characterLevel);
                if (haveBattleUserId)
                    waitingRoom.RegisterBattlePlayerUserId("P2", battleUserId);
                waitingRoom.SetPlayerCombatProfile("P2", playerMaxHp, playerMaxAp, weaponCode, weaponDamageMin, weaponDamageMax, weaponRange, weaponAttackApCost, accuracy, weaponTightness, weaponTrajectoryHeight, weaponIsSniper);
                waitingRoom.SetPlayerCurrentHpOverride("P2", playerCurrentHp);
                waitingRoom.StartFirstRound();
                Console.WriteLine($"[tzInfo] Matchmaking pair completed: battleId={battleId}, P1=({p1c},{p1r}), P2=({p2c},{p2r})");
                _waitingBattleId = null;
                return new JoinResponse { BattleId = battleId, PlayerId = "P2", Status = "battle", BattleStarted = waitingRoom.BuildBattleStartedFor("P2") };
            }

            var bid = Guid.NewGuid().ToString("N")[..8];
            var r = new BattleRoom(bid, _weaponDb, _obstacleDb, _bodyPartDb, _userDb, _zoneShrinkDb);
            int pc = Math.Clamp(startCol, 0, HexSpawn.DefaultGridWidth - 1);
            int pr = Math.Clamp(startRow, 0, HexSpawn.DefaultGridLength - 1);
            r.AddPlayer("P1", pc, pr);
            r.SetPlayerDisplayInfo("P1", displayName, characterLevel);
            if (haveBattleUserId)
                r.RegisterBattlePlayerUserId("P1", battleUserId);
            r.SetPlayerCombatProfile("P1", playerMaxHp, playerMaxAp, weaponCode, weaponDamageMin, weaponDamageMax, weaponRange, weaponAttackApCost, accuracy, weaponTightness, weaponTrajectoryHeight, weaponIsSniper);
            r.SetPlayerCurrentHpOverride("P1", playerCurrentHp);
            _rooms[bid] = r;
            _battleHistoryDb.EnsureBattle(bid);
            _waitingBattleId = bid;
            Console.WriteLine($"[tzInfo] Matchmaking waiting: battleId={bid}, playerId=P1, start=({pc},{pr})");
            return new JoinResponse { BattleId = bid, PlayerId = "P1", Status = "waiting" };
        }
    }

    public BattleRoom? GetRoom(string battleId)
    {
        lock (_lock) return _rooms.TryGetValue(battleId, out var r) ? r : null;
    }

    /// <summary>Find an in-memory battle where this <c>users.id</c> is registered (queue or active fight).</summary>
    public bool TryFindActiveBattleForUser(long userId, out string battleId, out string playerId)
    {
        battleId = "";
        playerId = "";
        if (userId <= 0)
            return false;
        lock (_lock)
        {
            foreach (var kv in _rooms)
            {
                if (!kv.Value.IsUnfinishedForLoginResume())
                    continue;
                foreach (var p in kv.Value.PlayerToUserId)
                {
                    if (p.Value != userId)
                        continue;
                    battleId = kv.Key;
                    playerId = p.Key;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Submit под общей блокировкой с Tick — исключает гонку «таймер закрыл раунд / второй сабмит».</summary>
    public SubmitTurnResult SubmitTurnLocked(string battleId, SubmitTurnPayloadDto payload)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(battleId, out var room))
                return SubmitTurnResult.NotFoundRoom();
            if (string.IsNullOrEmpty(payload.PlayerId) || !room.Players.ContainsKey(payload.PlayerId))
                return SubmitTurnResult.BadPlayer();
            if (payload.RoundIndex != room.RoundIndex)
                return SubmitTurnResult.RoundMismatch(room.RoundIndex);
            bool shouldClose = room.SubmitTurn(payload);
            Console.WriteLine($"[tzInfo] SubmitTurn: battleId={battleId}, playerId={payload.PlayerId}, round={payload.RoundIndex}, actionCount={(payload.Actions?.Length ?? 0)}, shouldClose={shouldClose}");
            if (!shouldClose)
                return SubmitTurnResult.Accepted(false);
            room.CloseRound(fromTimer: false);
            return SubmitTurnResult.Accepted(true);
        }
    }

    /// <summary>Игрок покинул клиент: отмена ожидания в очереди или завершение боя (таймер не тикает для удалённой комнаты).</summary>
    public bool PlayerLeft(string battleId, string playerId)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(battleId, out var room)) return false;
            if (!room.Players.ContainsKey(playerId)) return false;

            if (room.Players.Count == 1 && _waitingBattleId == battleId)
            {
                _rooms.Remove(battleId);
                _waitingBattleId = null;
                return true;
            }

            _rooms.Remove(battleId);
            if (_waitingBattleId == battleId)
                _waitingBattleId = null;
            return true;
        }
    }
}

public class JoinResponse
{
    public string BattleId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Status { get; set; } = ""; // "waiting" | "battle"
    public BattleStartedPayloadDto? BattleStarted { get; set; }
}
