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
    private const string ActionEquipWeapon = "EquipWeapon";
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
    public const int DefaultPlayerMaxHp = 10;
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
    public Dictionary<string, (int maxHp, int maxAp, string weaponCode, int weaponDamage, int weaponRange, int weaponAttackApCost)> PlayerCombatProfiles { get; } = new();

    /// <summary>Порядок отправки хода в текущем раунде (кто раньше отправил — выше приоритет на клетку).</summary>
    public List<string> SubmissionOrder { get; } = new();

    /// <summary>Стабильный список участников боя (порядок присоединения).</summary>
    public List<string> ParticipantIds { get; } = new();

    /// <summary>Отображаемое имя для планки над головой (playerId → ник).</summary>
    public Dictionary<string, string> PlayerDisplayNames { get; } = new();

    /// <summary>Уровень персонажа (playerId → level).</summary>
    public Dictionary<string, int> PlayerLevels { get; } = new();

    /// <summary>Кто в этом раунде завершил ход досрочно (пока таймер не истёк).</summary>
    public Dictionary<string, bool> EndedTurnEarlyThisRound { get; } = new();
    /// <summary>Тег препятствия на клетке: wall | damaged_wall | tree | rock.</summary>
    private readonly Dictionary<(int col, int row), string> _obstacleTags = new();
    private readonly Dictionary<(int col, int row), int> _wallHpRemaining = new();
    private readonly List<(int col, int row)> _hexLineBuffer = new();
    /// <summary>Линия к изначально выбранной цели до LOS-редиректа — для дерева/камня на пути (редирекнутый буфер теряет клетки за первым врагом).</summary>
    private readonly List<(int col, int row)> _coverLineBuffer = new();

    private readonly Random _rng;
    private readonly BattleWeaponDatabase? _weaponDb;
    private readonly BattleObstacleBalanceDatabase? _obstacleDb;

    public BattleRoom(string battleId, BattleWeaponDatabase? weaponDb = null, BattleObstacleBalanceDatabase? obstacleDb = null)
    {
        BattleId = battleId;
        _rng = new Random(Guid.NewGuid().GetHashCode());
        _weaponDb = weaponDb;
        _obstacleDb = obstacleDb;
    }

    /// <summary>Вызывается в конце CloseRound — пуш по WebSocket.</summary>
    public static event Action<BattleRoom>? RoundClosedForPush;

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
