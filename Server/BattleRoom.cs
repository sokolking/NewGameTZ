using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

/// <summary>Состояние одного боя (2 игрока). Этап 3: пошаговая симуляция, приоритет по порядку отправки хода.</summary>
public partial class BattleRoom
{
    private const string PostureWalk = "walk";
    private const string PostureRun = "run";
    private const string PostureSit = "sit";
    private const string PostureHide = "hide";
    private const string ActionMoveStep = "MoveStep";
    private const string ActionAttack = "Attack";
    private const string ActionChangePosture = "ChangePosture";
    private const string ActionWait = "Wait";
    private const string ActionReload = "Reload";
    private const string ActionEquipWeapon = "EquipWeapon";
    private const string ActionUseItem = "UseItem";
    private const int ChangePostureCost = 2;
    private const float RunCostMultiplier = 0.5f;
    private const float SitCostMultiplier = 1.5f;
    private const float RunStepPenaltyFraction = 0.02f;
    private const float RunStepPenaltyHexFraction = 0.05f;
    private const float RestRecoveryFraction = 0.33f;
    private const int RestRecoveryMinAp = 5;
    private const float RunPenaltyThresholdFraction = 0.85f;
    private const float MaxPenaltyFraction = 0.95f;

    public string BattleId { get; }
    public const float RoundDuration = 100f;
    public const int MaxAp = 100;
    public const int MobMaxAp = 15;
    public const int DefaultPlayerMaxHp = PlayerLevelStatsTable.BaseMaxHp;
    public const int DefaultPlayerMaxAp = PlayerLevelStatsTable.BaseMaxAp;
    public const int DefaultMobMaxHp = 10;
    public const string DefaultWeaponCode = "fist";
    public const int DefaultWeaponDamage = 1;
    public const int DefaultWeaponRange = 1;
    public int RoundIndex { get; set; }
    public float RoundTimeLeft { get; set; }
    public long RoundDeadlineUtcMs { get; set; }
    public bool RoundInProgress { get; set; }

    /// <summary>Флаг: бой создан как одиночный (1 игрок + серверный моб), а не матчмейкинг 1v1.</summary>
    public bool IsSolo { get; set; }

    /// <summary>
    /// DEBUG: одиночный бой — моб на фиксированной дистанции от игрока, много HP, пустой AI (не преследует).
    /// По умолчанию <b>включён</b> (как раньше при const true). Чтобы выключить в проде: env <c>DEBUG_SOLO_MOB=0</c> или <c>false</c>.
    /// Не const/static readonly — иначе Roslyn помечает одну из веток <c>if (flag)</c> как недостижимую (CS0162).
    /// </summary>
    private static bool DebugSoloMobFiveHexNoChase1000Hp => GetDebugSoloMobFiveHexNoChase1000Hp();

    private static bool GetDebugSoloMobFiveHexNoChase1000Hp()
    {
        string? v = Environment.GetEnvironmentVariable("DEBUG_SOLO_MOB");
        // string.Equals — безопасно при v == null (v.Equals(...) давал NRE).
        if (v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
    private const int DebugSoloMobHexDistanceFromPlayer = 5;
    private const int DebugSoloMobHp = 1000;

    /// <summary>Все юниты боя (игроки и мобы) по unitId.</summary>
    public Dictionary<string, UnitStateDto> Units { get; } = new();

    /// <summary>Соответствие playerId → unitId управляемого юнита.</summary>
    public Dictionary<string, string> PlayerToUnitId { get; } = new();

    /// <summary>Команды юнитов на текущий раунд (unitId → команда).</summary>
    public Dictionary<string, UnitCommandDto> UnitCommands { get; } = new();

    /// <summary>Кто уже отправил ход в текущем раунде (playerId -> payload).</summary>
    public Dictionary<string, SubmitTurnPayloadDto> Submissions { get; } = new();

    /// <summary>Результат последнего завершённого раунда (отдаём при poll, потом очищаем).</summary>
    public TurnResultPayloadDto? LastTurnResult { get; set; }

    /// <summary>Участники: playerId -> начальная позиция (col, row).</summary>
    public Dictionary<string, (int col, int row)> Players { get; } = new();

    /// <summary>Текущее состояние каждого игрока (позиция, ОД, штраф). Обновляется после каждого раунда.</summary>
    public Dictionary<string, PlayerBattleState> CurrentState { get; } = new();
    /// <summary>Кортеж: … weaponDamageMin, weaponDamageMax, weaponRange, attackApCost, accuracy, tightness T, traj, sniper.</summary>
    public Dictionary<string, (int maxHp, int maxAp, string weaponCode, int weaponDamageMin, int weaponDamageMax, int weaponRange, int weaponAttackApCost, int accuracy, double weaponTightness, int weaponTrajectoryHeight, bool weaponIsSniper)> PlayerCombatProfiles { get; } = new();
    public Dictionary<string, int> PlayerCurrentHpOverrides { get; } = new();

    /// <summary>Порядок отправки хода в текущем раунде (кто раньше отправил — выше приоритет на клетку).</summary>
    public List<string> SubmissionOrder { get; } = new();

    /// <summary>Стабильный список участников боя (порядок присоединения).</summary>
    public List<string> ParticipantIds { get; } = new();

    /// <summary>Отображаемое имя для планки над головой (playerId → ник).</summary>
    public Dictionary<string, string> PlayerDisplayNames { get; } = new();

    /// <summary><c>users.id</c> для участника боя; заполняется при join с авторизацией (не якорь, не логин-строка).</summary>
    public Dictionary<string, long> PlayerToUserId { get; } = new();

    public bool TryGetBattlePlayerUserId(string playerId, out long userId) =>
        PlayerToUserId.TryGetValue(playerId, out userId);

    /// <summary>
    /// Combat <c>unitId</c> for a human player: decimal <c>users.id</c> when linked; otherwise negative guest slot
    /// (<c>P1</c> → <c>-1</c>, <c>P2</c> → <c>-2</c>). Stable for the whole battle — do not remap mid-fight.
    /// </summary>
    public string GetPlayerUnitId(string playerId)
    {
        if (TryGetBattlePlayerUserId(playerId, out long uid) && uid > 0)
            return uid.ToString();
        if (string.Equals(playerId, "P1", StringComparison.Ordinal))
            return "-1";
        if (string.Equals(playerId, "P2", StringComparison.Ordinal))
            return "-2";
        return "-" + Math.Abs((playerId ?? "").GetHashCode(StringComparison.Ordinal)).ToString();
    }

    /// <summary>Prefix for non-player combatants (not <c>users.id</c>). IDs are battle-local; DB-backed mob rows can map to these later.</summary>
    public const string BattleMobUnitIdPrefix = "mob:";

    /// <summary>Уровень персонажа (playerId → level).</summary>
    public Dictionary<string, int> PlayerLevels { get; } = new();

    /// <summary>Кто в этом раунде завершил ход досрочно (пока таймер не истёк).</summary>
    public Dictionary<string, bool> EndedTurnEarlyThisRound { get; } = new();
    /// <summary>Тег препятствия: wall | wall_low | damaged_* | tree | rock (ЛС стен — см. BattleRoom.LineOfFire).</summary>
    private readonly Dictionary<(int col, int row), string> _obstacleTags = new();
    private readonly Dictionary<(int col, int row), int> _wallHpRemaining = new();
    private readonly Dictionary<(int col, int row), float> _wallYawDegrees = new();
    private readonly List<(int col, int row)> _hexLineBuffer = new();
    /// <summary>Линия к изначально выбранной цели до LOS-редиректа — для дерева/камня на пути (редирекнутый буфер теряет клетки за первым врагом).</summary>
    private readonly List<(int col, int row)> _coverLineBuffer = new();

    private readonly Random _rng;
    private readonly BattleWeaponDatabase? _weaponDb;
    private readonly BattleObstacleBalanceDatabase? _obstacleDb;
    private readonly BattleBodyPartDatabase? _bodyPartDb;
    private readonly BattleUserDatabase? _userDb;

    public BattleRoom(string battleId, BattleWeaponDatabase? weaponDb = null, BattleObstacleBalanceDatabase? obstacleDb = null, BattleBodyPartDatabase? bodyPartDb = null, BattleUserDatabase? userDb = null)
    {
        BattleId = battleId;
        _rng = new Random(Guid.NewGuid().GetHashCode());
        _weaponDb = weaponDb;
        _obstacleDb = obstacleDb;
        _userDb = userDb;
        _bodyPartDb = bodyPartDb;
    }

    private int NormalizeBodyPartId(int raw) =>
        _bodyPartDb != null ? _bodyPartDb.NormalizeBodyPartId(raw) : (raw is >= 1 and <= 5 ? raw : 0);

    /// <summary>Вызывается в конце CloseRound — пуш по WebSocket.</summary>
    public static event Action<BattleRoom>? RoundClosedForPush;

    /// <summary>
    /// Login <c>activeBattle</c> resume: battle continues unless last resolved round ended the fight,
    /// or the room is cleared (e.g. surrender leaves via <see cref="BattleRoomStore.PlayerLeft"/>).
    /// Matchmaking queue (waiting for opponent) counts as unfinished.
    /// </summary>
    public bool IsUnfinishedForLoginResume()
    {
        if (LastTurnResult?.BattleFinished == true)
            return false;

        if (RoundInProgress)
            return true;

        // Matchmaking: one player in queue, rounds not started yet.
        if (!IsSolo && Players.Count == 1 && RoundIndex == 0)
            return true;

        return false;
    }

    public void Tick(float deltaSeconds)
    {
        if (!RoundInProgress) return;
        RefreshRoundTimeLeft();
        if (RoundTimeLeft <= 0)
        {
            RoundTimeLeft = 0;
            Console.WriteLine($"[tzInfo] Round timer expired: battleId={BattleId}, roundIndex={RoundIndex}");
            CloseRound(fromTimer: true);
        }
    }
}
