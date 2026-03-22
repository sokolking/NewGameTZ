using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сессия боя: отправка хода на сервер и применение результата (по плану ServerSyncPlan).
/// Этап 4: все юниты по TurnResult + параллельная анимация. Этап 5: OnNetworkMessage.
/// </summary>
public class GameSession : MonoBehaviour
{
    private sealed class ReplayUnitSnapshot
    {
        public string UnitId;
        public int UnitType;
        public int Col;
        public int Row;
        public int CurrentAp;
        public float PenaltyFraction;
        public string CurrentPosture;
        public bool IsLocal;
    }

    private sealed class LiveTurnDraftSnapshot
    {
        public int RoundIndex;
        public BattleQueuedAction[] Actions = System.Array.Empty<BattleQueuedAction>();
    }

    private sealed class ExecutedActionPlaybackEntry
    {
        public BattleExecutedAction Action;
        public int Order;
    }

    /// <summary>Сообщения для UI: «Не удалось отправить ход», «Клетка занята», rejectedReason и т.д.</summary>
    public static System.Action<string> OnNetworkMessage;
    /// <summary>POST submit успешно принят — показать бар до ответа по WebSocket.</summary>
    public static System.Action OnSubmitTurnDeliveredToServer;
    /// <summary>Пуш результата раунда по WebSocket — скрыть бар ожидания.</summary>
    public static System.Action OnWebSocketRoundPushReceived;
    /// <summary>Ожидание отменено (ошибка отправки, конец боя).</summary>
    public static System.Action OnServerRoundWaitCancelled;
    /// <summary>Итог боя: true = победа, false = поражение.</summary>
    public static System.Action<bool> OnBattleFinished;

    [Header("Режим и идентификаторы")]
    [Tooltip("Включить отправку хода на сервер при завершении хода.")]
    [SerializeField] private bool _isOnlineMode;
    [SerializeField] private string _battleId = "battle-1";
    [SerializeField] private string _playerId = "P1";

    [Header("Сервер (Этап 2)")]
    [SerializeField] private BattleServerConnection _serverConnection;

    [Header("Локальный юнит")]
    [Tooltip("Юнит локального игрока (для применения TurnResult и синхронизации раунда). Если не задан — ищется в сцене.")]
    [SerializeField] private Player _localPlayer;

    [Header("Визуализация раунда")]
    [Tooltip("Пауза после атакующего действия, чтобы было видно ритм тиков и разницу в ОД.")]
    [SerializeField] private float _attackActionPauseSeconds = 0.08f;
    [Tooltip("Короткая пауза между тиками action journal.")]
    [SerializeField] private float _tickPauseSeconds = 0.03f;

    [Header("Дальний бой: пуля")]
    [Tooltip("Базовая длительность полёта пули (сек); масштабируется по числу гексов.")]
    [SerializeField] private float _bulletFlightSeconds = 0.14f;
    [Tooltip("Высота центра сферы над центром гекса (мир).")]
    [SerializeField] private float _bulletHeightAboveGround = 0.28f;
    [Tooltip("Необязательно: материал линии выстрела (иначе Sprites/Default, жёлтый).")]
    [SerializeField] private Material _bulletMaterial;

    [Header("Debug")]
    [Tooltip("Для отладки: заспавнить моба рядом с локальным игроком на соседнем гексе.")]
    [SerializeField] private bool _debugSpawnMobNearLocalPlayer = false;
    [Tooltip("Идентификатор debug-моба (должен начинаться с MOB_, чтобы считался мобом).")]
    [SerializeField] private string _debugMobId = "MOB_DEBUG";
    [Tooltip("Предпочтительное направление соседа (0..5). Если занято/вне поля — берётся следующий по кругу.")]
    [SerializeField] private int _debugNeighborDirection = 0;

    // Ключ: идентификатор сущности в сети (для игроков — playerId, для мобов — unitId сервера).
    private readonly Dictionary<string, RemoteBattleUnitView> _remoteUnits = new();
    private readonly HashSet<(int col, int row)> _obstacleCells = new();
    /// <summary>Yaw стены с BattleStarted — для смены wall ↔ damaged_wall по mapUpdates.</summary>
    private readonly Dictionary<(int col, int row), float> _obstacleWallYawByCell = new();
    private readonly Dictionary<string, ReplayUnitSnapshot> _initialReplayState = new();
    private readonly List<string> _turnHistoryIds = new();
    private readonly Dictionary<string, TurnResultPayload> _turnHistoryCache = new();

    private bool _waitingForServerRoundResolve;
    private bool _animateResolvedRoundForPendingSubmit = true;
    private bool _isTurnHistoryReplayPlaying;
    private int _lastProcessedTurnResultRound = -1;
    /// <summary>Индекс раунда с сервера — только его слать в submit (TurnCount локально может отличаться).</summary>
    private int _serverRoundIndex;
    private int _currentTurnHistoryPointer = -1;
    private int _selectedTurnHistoryPointer = -1;
    private long _liveRoundDeadlineUtcMs;
    private Coroutine _turnReplayCoroutine;
    private readonly List<Coroutine> _activeReplayAnimationCoroutines = new();
    private readonly List<(int col, int row)> _replayTwoPointMovePath = new(2);
    /// <summary>Буфер для линии гексов при расчёте конечной точки пули.</summary>
    private readonly List<(int col, int row)> _bulletLineHexBuffer = new(48);
    /// <summary>Ctrl+прицел: линия от игрока к клику для подстановки гекса на границе дальности.</summary>
    private readonly List<(int col, int row)> _hexAimLineScratch = new(48);
    private readonly HashSet<(int col, int row)> _mapUpdateLineMatchSet = new();
    private LiveTurnDraftSnapshot _liveTurnDraftSnapshot;
    private bool _debugMobSpawned;
    private bool _battleFinished;
    /// <summary>Блокировка ввода: ожидание результата раунда или анимация.</summary>
    public bool BlockPlayerInput => _waitingForServerRoundResolve || IsBattleAnimationPlaying || IsTurnHistoryReplayPlaying || IsViewingHistoricalTurn;

    public bool IsWaitingForServerRoundResolve => _waitingForServerRoundResolve;
    public int LastProcessedTurnResultRound => _lastProcessedTurnResultRound;
    public int ServerRoundIndexForSubmit => _serverRoundIndex;
    public bool IsTurnHistoryReplayPlaying => _isTurnHistoryReplayPlaying;
    public bool IsViewingHistoricalTurn => _selectedTurnHistoryPointer >= 0;
    public int CurrentTurnHistoryPointer => _currentTurnHistoryPointer;
    public int SelectedTurnHistoryPointer => _selectedTurnHistoryPointer;
    public int DisplayedTurnNumber => _selectedTurnHistoryPointer >= 0 ? _selectedTurnHistoryPointer + 1 : _serverRoundIndex + 1;
    public bool CanViewPreviousTurn => _selectedTurnHistoryPointer > 0 || (_selectedTurnHistoryPointer < 0 && _currentTurnHistoryPointer >= 0);
    public bool CanViewNextTurn => _selectedTurnHistoryPointer >= 0;
    public bool IsBattleFinished => _battleFinished;

    public bool IsInBattleWithServer() => _serverConnection != null && _serverConnection.IsInBattle;
    public bool IsObstacleCell(int col, int row) => _obstacleCells.Contains((col, row));
    public Player LocalPlayer => _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();

    /// <summary>
    /// Заполняет переданный список текущими удалёнными юнитами без аллокации нового List (для миникарты и т.п.).
    /// </summary>
    public void CopyRemoteUnitsTo(List<RemoteBattleUnitView> buffer)
    {
        if (buffer == null)
            return;
        buffer.Clear();
        foreach (var unit in _remoteUnits.Values)
        {
            if (unit != null)
                buffer.Add(unit);
        }
    }

    public void RegisterProcessedTurnResult(int roundIndex)
    {
        _lastProcessedTurnResultRound = roundIndex;
    }

    public void ReplaceTurnHistoryIds(string[] turnHistoryIds, int currentTurnPointer)
    {
        _turnHistoryIds.Clear();
        if (turnHistoryIds != null)
        {
            foreach (var turnId in turnHistoryIds)
            {
                if (!string.IsNullOrEmpty(turnId))
                    _turnHistoryIds.Add(turnId);
            }
        }

        _currentTurnHistoryPointer = Mathf.Clamp(currentTurnPointer, -1, _turnHistoryIds.Count - 1);
        _selectedTurnHistoryPointer = -1;
        _liveTurnDraftSnapshot = null;
    }

    public void CacheTurnHistoryEntry(string turnId, TurnResultPayload turnResult)
    {
        if (string.IsNullOrEmpty(turnId) || turnResult == null)
            return;

        _turnHistoryCache[turnId] = turnResult;
    }

    public bool TryStepViewedTurn(int delta)
    {
        if (_turnHistoryIds.Count == 0)
            return false;

        bool restoreLive = false;
        int target = -1;

        if (_selectedTurnHistoryPointer < 0)
        {
            if (delta >= 0 || _currentTurnHistoryPointer < 0)
                return false;

            CaptureLiveTurnDraft();
            target = _currentTurnHistoryPointer;
        }
        else
        {
            target = _selectedTurnHistoryPointer + delta;
            if (target < 0)
                return false;
            if (target > _currentTurnHistoryPointer)
            {
                if (delta > 0 && _selectedTurnHistoryPointer == _currentTurnHistoryPointer)
                    restoreLive = true;
                else
                    return false;
            }
            else if (target == _selectedTurnHistoryPointer)
                return false;
        }

        CancelTurnReplayPlayback();
        if (restoreLive)
            _turnReplayCoroutine = StartCoroutine(RestoreLiveTurnStateCoroutine());
        else
            StartTurnHistoryReplay(target);
        return true;
    }

    public bool TryAutoSubmitTimedOutLiveTurn(bool animateResolvedRound)
    {
        if (_battleFinished)
            return false;
        return TrySubmitCurrentLiveTurnDraft(animateResolvedRound);
    }

    public bool TrySubmitCurrentLiveTurnDraft(bool animateResolvedRound)
    {
        if (_battleFinished)
            return false;
        if (_waitingForServerRoundResolve || !IsInBattleWithServer())
            return false;

        bool isViewingHistory = _selectedTurnHistoryPointer >= 0 || _isTurnHistoryReplayPlaying;
        var draft = _liveTurnDraftSnapshot;
        if (!isViewingHistory || draft == null || draft.RoundIndex != _serverRoundIndex)
        {
            CaptureLiveTurnDraft();
            draft = _liveTurnDraftSnapshot;
        }

        if (draft == null)
            return false;

        if (isViewingHistory)
            CancelTurnReplayPlayback();

        BeginWaitingForServerRoundResolve(animateResolvedRound);
        SubmitTurnLocal(draft.Actions, _serverRoundIndex);
        return true;
    }

    public static GameSession Active { get; private set; }

    public string BattleId => _battleId;
    public string PlayerId => _playerId;

    /// <summary>Смена оружия: в бою — в очередь хода (2 ОД смена + EquipWeapon на сервере), иначе только локально.</summary>
    /// <param name="weaponAttackApCost">Стоимость атаки из БД (weapons.attack_ap_cost); если 0 — используется 1.</param>
    /// <param name="weaponDamageFromDb">Урон из слота инвентаря (БД). Если &lt; 0 — для офлайна подставляется 1.</param>
    /// <param name="weaponRangeFromDb">Дальность из слота. Если &lt; 0 — подставляется 1.</param>
    public void RequestEquipWeapon(string weaponCode, int weaponAttackApCost = 1, int weaponDamageFromDb = -1, int weaponRangeFromDb = -1)
    {
        if (string.IsNullOrWhiteSpace(weaponCode))
            return;
        weaponCode = weaponCode.Trim().ToLowerInvariant();
        var pl = LocalPlayer;
        if (pl == null)
            return;
        int atk = Mathf.Max(1, weaponAttackApCost);
        if (IsInBattleWithServer())
        {
            if (!pl.QueueEquipWeaponAction(weaponCode, null, atk, weaponDamageFromDb, weaponRangeFromDb))
                OnNetworkMessage?.Invoke("Недостаточно ОД");
        }
        else
            ApplyLocalWeaponOnly(weaponCode, atk, weaponDamageFromDb, weaponRangeFromDb);
    }

    private void ApplyLocalWeaponOnly(string weaponCode, int weaponAttackApCost, int weaponDamageFromDb = -1, int weaponRangeFromDb = -1)
    {
        string code = WeaponCatalog.NormalizeWeaponCode(weaponCode);
        int dmg = weaponDamageFromDb >= 0 ? weaponDamageFromDb : 1;
        int range = weaponRangeFromDb >= 0 ? weaponRangeFromDb : 1;
        LocalPlayer?.SetEquippedWeapon(code, dmg, range, weaponAttackApCost);
    }

    /// <summary>Включён ли онлайн-режим (отправка хода при завершении). True также при загрузке через Find Game (сессия в бою).</summary>
    public bool IsOnlineMode => _isOnlineMode || (_serverConnection != null && _serverConnection.IsInBattle);

    /// <summary>Идёт анимация TurnResult (локальный или удалённый юнит) — не завершать ход.</summary>
    public bool IsBattleAnimationPlaying
    {
        get
        {
            var local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
            if (local != null && local.IsMoving) return true;
            foreach (var r in _remoteUnits.Values)
                if (r != null && r.IsMoving) return true;
            return false;
        }
    }

    private void OnEnable()
    {
        // Keep simulation running even when window loses focus.
        Application.runInBackground = true;
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
    }

    private void Start()
    {
        if (_serverConnection == null)
            _serverConnection = FindFirstObjectByType<BattleServerConnection>();
        if (_localPlayer == null)
            _localPlayer = FindFirstObjectByType<Player>();
        // В одиночном режиме теперь тоже используется серверный бой (1 игрок + серверный моб),
        // поэтому не создаём локальный ChasingAiController, а подключаемся к серверу.
        if (_isOnlineMode && _serverConnection != null && !BattleSessionState.HasPendingBattle)
            _serverConnection.ConnectAndJoin(0, 0);

        if (_debugSpawnMobNearLocalPlayer)
            StartCoroutine(SpawnDebugMobWhenReady());
    }

    /// <param name="shiftRepeat">Зажат Shift: поставить в очередь атак столько раз, сколько <c>текущие ОД / стоимость атаки</c> (целочисленно).</param>
    public bool TryPerformSilhouetteAttack(RemoteBattleUnitView target, string bodyPartLabel, bool shiftRepeat = false)
    {
        if (_battleFinished || target == null) return false;
        var local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null) return false;

        int attackCost = Mathf.Max(1, local.WeaponAttackApCost);
        int apBefore = local.CurrentAp;
        int repeatCount = shiftRepeat ? apBefore / attackCost : 1;
        if (repeatCount <= 0)
        {
            OnNetworkMessage?.Invoke("Недостаточно ОД");
            return false;
        }

        int queued = 0;
        for (int i = 0; i < repeatCount; i++)
        {
            if (!local.QueueAttackAction(target.NetworkPlayerId, bodyPartLabel, attackCost))
                break;
            queued++;
        }

        if (queued <= 0)
        {
            OnNetworkMessage?.Invoke("Недостаточно ОД");
            return false;
        }

        if (queued > 1)
            OnNetworkMessage?.Invoke($"Атака x{queued} в очередь ({bodyPartLabel})");
        else
            OnNetworkMessage?.Invoke($"Атака в очередь: {bodyPartLabel}");
        return true;
    }

    /// <summary>Ctrl+клик по гексу: дальнобойный выстрел по направлению (стена на ЛС / враг на клетке).</summary>
    public bool TryPerformHexAimAttack(int col, int row, bool shiftRepeat = false)
    {
        if (_battleFinished) return false;
        var local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null) return false;

        int pc = local.CurrentCol;
        int pr = local.CurrentRow;
        int d = HexGrid.GetDistance(pc, pr, col, row);
        if (d == 0)
        {
            OnNetworkMessage?.Invoke("Выберите другой гекс");
            return false;
        }

        int weaponRange = Mathf.Max(0, local.WeaponRangeHexes);
        if (weaponRange <= 0)
        {
            OnNetworkMessage?.Invoke("Гекс вне дальности оружия");
            return false;
        }

        HexCubeOffset.GetHexLine(pc, pr, col, row, _hexAimLineScratch);
        if (_hexAimLineScratch.Count < 2)
        {
            OnNetworkMessage?.Invoke("Выберите другой гекс");
            return false;
        }

        int step = Mathf.Min(weaponRange, d, _hexAimLineScratch.Count - 1);
        if (step < 1)
        {
            OnNetworkMessage?.Invoke("Выберите другой гекс");
            return false;
        }

        (int aimCol, int aimRow) = _hexAimLineScratch[step];

        int attackCost = Mathf.Max(1, local.WeaponAttackApCost);
        int apBefore = local.CurrentAp;
        int repeatCount = shiftRepeat ? apBefore / attackCost : 1;
        if (repeatCount <= 0)
        {
            OnNetworkMessage?.Invoke("Недостаточно ОД");
            return false;
        }

        int queued = 0;
        for (int i = 0; i < repeatCount; i++)
        {
            if (!local.QueueHexAttackAction(aimCol, aimRow, attackCost))
                break;
            queued++;
        }

        if (queued <= 0)
        {
            OnNetworkMessage?.Invoke("Недостаточно ОД");
            return false;
        }

        if (queued > 1)
            OnNetworkMessage?.Invoke($"Выстрел по гексу x{queued} в очередь");
        else
            OnNetworkMessage?.Invoke("Выстрел по гексу в очередь (Ctrl+клик)");
        return true;
    }

    private IEnumerator SpawnDebugMobWhenReady()
    {
        const int maxFrames = 300; // ~5 сек при 60 FPS
        for (int i = 0; i < maxFrames; i++)
        {
            if (TrySpawnDebugMobNearLocal())
                yield break;
            yield return null;
        }
    }

    private bool TrySpawnDebugMobNearLocal()
    {
        if (_debugMobSpawned) return true;

        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null) return false;

        HexGrid grid = local.Grid != null ? local.Grid : FindFirstObjectByType<HexGrid>();
        if (grid == null) return false;

        string id = string.IsNullOrEmpty(_debugMobId) ? "MOB_DEBUG" : _debugMobId;
        if (!id.StartsWith("MOB_", System.StringComparison.OrdinalIgnoreCase))
            id = "MOB_" + id;

        if (_remoteUnits.TryGetValue(id, out var existing) && existing != null)
        {
            _debugMobSpawned = true;
            return true;
        }

        int startDir = ((_debugNeighborDirection % 6) + 6) % 6;
        for (int step = 0; step < 6; step++)
        {
            int dir = (startDir + step) % 6;
            HexGrid.GetNeighbor(local.CurrentCol, local.CurrentRow, dir, out int nc, out int nr);
            if (!grid.IsInBounds(nc, nr))
                continue;
            if (IsObstacleCell(nc, nr))
                continue;

            var go = new GameObject("Mob_" + id);
            var remote = go.AddComponent<RemoteBattleUnitView>();
            remote.Initialize(id, grid, nc, nr, local.MoveDurationPerHex);
            _remoteUnits[id] = remote;
            _debugMobSpawned = true;
            return true;
        }

        return false;
    }

    // Локальный ChasingAiController больше не используется: серверный моб управляется на стороне сервера.

    public void BeginWaitingForServerRoundResolve(bool animateResolvedRound)
    {
        _waitingForServerRoundResolve = true;
        _animateResolvedRoundForPendingSubmit = animateResolvedRound;
        var p = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        p?.SetTurnTimerPaused(true);
    }

    /// <summary>
    /// При приходе результата раунда с сервера: если игрок в режиме планирования (вид сверху), плавно перейти в 3-е лицо перед анимацией.
    /// Уже в режиме просмотра — без повторного перехода.
    /// </summary>
    private IEnumerator CoEnterServerRoundThirdPersonCamera()
    {
        if (HexGridCamera.ThirdPersonFollowActive)
            yield break;

        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null || local.IsDead || local.IsHidden || local.Grid == null)
            yield break;

        HexGridCamera cam = FindFirstObjectByType<HexGridCamera>();
        if (cam == null)
            yield break;

        Vector3 fh = local.transform.forward;
        fh.y = 0f;
        if (fh.sqrMagnitude < 1e-6f)
            fh = Vector3.forward;
        else
            fh.Normalize();

        yield return cam.EnterThirdPersonFollowRoutine(local.transform, fh);
    }

    public void CancelWaitingForServerRoundResolve()
    {
        bool was = _waitingForServerRoundResolve;
        _waitingForServerRoundResolve = false;
        _animateResolvedRoundForPendingSubmit = true;
        var p = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        p?.SetTurnTimerPaused(false);
        if (was) OnServerRoundWaitCancelled?.Invoke();
    }

    /// <summary>
    /// Собрать данные хода и отправить на сервер (или в заглушку).
    /// Вызывать до применения EndTurn у Player.
    /// </summary>
    public void SubmitTurnLocal(BattleQueuedAction[] actions, int roundIndex)
    {
        if (_battleFinished)
            return;
        var payload = BuildSubmitPayload(actions, roundIndex);
        // Одиночный бой через меню тоже идёт по серверу при IsInBattle — без проверки только _isOnlineMode,
        // иначе UI ждёт раунд, а submit не уходит.
        if (_serverConnection != null && _serverConnection.IsInBattle)
        {
            var sock = FindFirstObjectByType<BattleSignalRConnection>();
            if (sock == null || !sock.IsSocketReady)
            {
                CancelWaitingForServerRoundResolve();
                OnNetworkMessage?.Invoke("Нет сокета к бою");
                return;
            }

            sock.SubmitTurnViaSocket(payload, (success, errorMessage) =>
            {
                if (!success)
                {
                    CancelWaitingForServerRoundResolve();
                    OnNetworkMessage?.Invoke(errorMessage ?? "Ошибка отправки хода");
                }
                else if (_waitingForServerRoundResolve)
                    OnSubmitTurnDeliveredToServer?.Invoke();
            });
            return;
        }
        Debug.Log($"[GameSession] SubmitTurn (offline/stub): roundIndex=" + roundIndex);
        if (GameModeState.IsSinglePlayer)
        {
            // В одиночной игре сервер не участвует, но можно логировать ход.
        }
    }

    // Одиночная игра теперь тоже синхронизируется через сервер; локальный ИИ-ход больше не требуется.

    /// <summary>Применить результат хода с сервера: подготовить юнитов и проиграть action journal по тикам.</summary>
    public void ApplyTurnResult(TurnResultPayload result)
    {
        if (result == null || result.results == null) return;
        AppendServerTurnLogs(result, showDamagePopupsImmediately: false);
        var animJobs = BuildTurnResultAnimationJobs(result, prepareForAnimation: true);
        var playback = BuildExecutedActionPlayback(result);
        if (playback.Count > 0)
            StartCoroutine(PlayExecutedActionTimeline(result, playback));
        else
        {
            ApplyMapUpdatesFromTurnResult(result);
            if (animJobs.Count > 0)
                StartCoroutine(PlayAllTurnAnimationsParallel(animJobs));
        }
        if (result.battleFinished)
            HandleBattleFinishedFromServer(result);
    }

    /// <summary>Ожидали свой сабмит: скрыть бар → анимация → новый раунд и снятие блокировки.</summary>
    public void ApplyTurnResultThenRoundState(TurnResultPayload result, int nextRoundIndex, long roundDeadlineUtcMs)
    {
        if (result == null || result.results == null) return;
        bool animateResolvedRound = _animateResolvedRoundForPendingSubmit;
        _animateResolvedRoundForPendingSubmit = true;
        if (!animateResolvedRound)
            ApplyMapUpdatesFromTurnResult(result);
        AppendServerTurnLogs(result, showDamagePopupsImmediately: !animateResolvedRound);
        var animJobs = BuildTurnResultAnimationJobs(result, animateResolvedRound);
        var playback = animateResolvedRound ? BuildExecutedActionPlayback(result) : null;
        StartCoroutine(DeferredRoundAfterAnimations(animJobs, playback, nextRoundIndex, roundDeadlineUtcMs, result));
    }

    private List<(object unit, bool isLocal, HexPosition[] path)> BuildTurnResultAnimationJobs(TurnResultPayload result, bool prepareForAnimation)
    {
        var animJobs = new List<(object unit, bool isLocal, HexPosition[] path)>();
        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();

        foreach (var r in result.results)
        {
            if (r == null) continue;

            bool isMob = r.unitType == 1; // 0 = Player, 1 = Mob
            string id = isMob && !string.IsNullOrEmpty(r.unitId) ? r.unitId : r.playerId;

            if (!isMob && r.playerId == _playerId)
            {
                if (local == null) continue;
                local.SetHidden(false);
                if (!r.accepted && !string.IsNullOrEmpty(r.rejectedReason))
                    OnNetworkMessage?.Invoke(r.rejectedReason);
                else if (!r.accepted)
                    OnNetworkMessage?.Invoke("Клетка занята");

                local.ApplyServerTurnResult(r.finalPosition, r.actualPath, r.currentAp, r.penaltyFraction, r.currentPosture, prepareForAnimation);
                local.SetHealth(r.currentHp, r.maxHp > 0 ? r.maxHp : local.MaxHp);
                if (!string.IsNullOrEmpty(r.weaponCode))
                {
                    int wAtk = r.weaponAttackApCost > 0 ? r.weaponAttackApCost : 1;
                    local.SetEquippedWeapon(r.weaponCode, r.weaponDamage, r.weaponRange, wAtk);
                }
                if (prepareForAnimation)
                    animJobs.Add((local, true, r.actualPath));
                else if (r.isDead)
                {
                    if (_isTurnHistoryReplayPlaying)
                        ApplyLocalHiddenDeadState(silent: true);
                    else
                        StartCoroutine(HandleLocalDeathAfterMessage());
                }
                continue;
            }

            if (!_remoteUnits.TryGetValue(id, out var remote) || remote == null)
            {
                // Юнит ещё не создан (например, серверный моб) — создаём RemoteBattleUnitView по первой точке пути.
                HexGrid grid = local != null && local.Grid != null ? local.Grid : FindFirstObjectByType<HexGrid>();
                if (grid != null && r.actualPath != null && r.actualPath.Length > 0)
                {
                    var first = r.actualPath[0];
                    var go = new GameObject(isMob ? ("Mob_" + id) : ("Remote_" + id));
                    remote = go.AddComponent<RemoteBattleUnitView>();
                    remote.Initialize(id, grid, first.col, first.row, local != null ? local.MoveDurationPerHex : 0.2f);
                    _remoteUnits[id] = remote;
                }
            }

            if (remote != null)
            {
                remote.ApplyServerTurnResult(r.finalPosition, r.actualPath, r.currentAp, r.penaltyFraction, prepareForAnimation);
                remote.SetHealth(r.currentHp, r.maxHp > 0 ? r.maxHp : 10);
                if (prepareForAnimation)
                    animJobs.Add((remote, false, r.actualPath));
                if (r.isDead && !prepareForAnimation)
                {
                    _remoteUnits.Remove(id);
                    Destroy(remote.gameObject);
                }
            }
        }

        return animJobs;
    }

    private IEnumerator DeferredRoundAfterAnimations(
        List<(object unit, bool isLocal, HexPosition[] path)> jobs,
        List<ExecutedActionPlaybackEntry> playback,
        int nextRoundIndex,
        long roundDeadlineUtcMs,
        TurnResultPayload result)
    {
        if (playback != null && playback.Count > 0)
            yield return PlayExecutedActionTimeline(result, playback);
        else
        {
            if (playback != null)
                ApplyMapUpdatesFromTurnResult(result);
            if (jobs.Count > 0)
                yield return PlayAllTurnAnimationsParallel(jobs);
        }
        if (result == null || !result.battleFinished)
        {
            ApplyRoundState(nextRoundIndex, roundDeadlineUtcMs);
            var p = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
            p?.SetTurnTimerPaused(false);
            _waitingForServerRoundResolve = false;
            if (p != null && !BlockPlayerInput)
                p.TryAutoMoveTowardFlag();
        }
        else
        {
            CancelWaitingForServerRoundResolve();
            HandleBattleFinishedFromServer(result);
        }
    }

    private IEnumerator PlayTurnResultAnimationCoroutine(Player player, HexPosition[] path)
    {
        if (player == null || path == null) yield break;
        yield return player.PlayPathAnimation(path, driveCamera: false);
    }

    private static IEnumerator PlayRemoteAnimationCoroutine(RemoteBattleUnitView remote, HexPosition[] path)
    {
        if (remote == null || path == null) yield break;
        yield return remote.PlayPathAnimation(path);
    }

    private List<ExecutedActionPlaybackEntry> BuildExecutedActionPlayback(TurnResultPayload result)
    {
        var playback = new List<ExecutedActionPlaybackEntry>();
        if (result?.results == null)
            return playback;

        int order = 0;
        foreach (var turnResult in result.results)
        {
            if (turnResult?.executedActions == null)
                continue;

            foreach (var action in turnResult.executedActions)
            {
                if (action == null)
                    continue;

                playback.Add(new ExecutedActionPlaybackEntry
                {
                    Action = action,
                    Order = order++
                });
            }
        }

        playback.Sort((a, b) =>
        {
            int tickCompare = a.Action.tick.CompareTo(b.Action.tick);
            return tickCompare != 0 ? tickCompare : a.Order.CompareTo(b.Order);
        });

        return playback;
    }

    private IEnumerator PlayExecutedActionTimeline(TurnResultPayload result, List<ExecutedActionPlaybackEntry> playback)
    {
        if (playback == null || playback.Count == 0)
            yield break;

        yield return CoEnterServerRoundThirdPersonCamera();

        int currentTick = -1;
        bool abortTimeline = false;
        var appliedMapTicks = new HashSet<int>();
        foreach (var entry in playback)
        {
            // После смерти/окончания боя мы не должны останавливать отображение уже сохраненной истории.
            if (_battleFinished && !_isTurnHistoryReplayPlaying)
            {
                abortTimeline = true;
                break;
            }
            BattleExecutedAction action = entry != null ? entry.Action : null;
            if (action == null)
                continue;

            if (currentTick >= 0 && action.tick != currentTick)
            {
                ApplyMapUpdatesFromTurnResult(result, currentTick);
                appliedMapTicks.Add(currentTick);
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, _tickPauseSeconds));
            }

            currentTick = action.tick;
            yield return PlayExecutedAction(result, action);
        }

        if (!abortTimeline && currentTick >= 0)
        {
            ApplyMapUpdatesFromTurnResult(result, currentTick);
            appliedMapTicks.Add(currentTick);
        }

        if (!abortTimeline && result.mapUpdates != null)
        {
            var orphanTicks = new List<int>();
            foreach (var u in result.mapUpdates)
            {
                if (u == null) continue;
                if (appliedMapTicks.Contains(u.tick)) continue;
                if (orphanTicks.Contains(u.tick)) continue;
                orphanTicks.Add(u.tick);
            }

            orphanTicks.Sort();
            foreach (int t in orphanTicks)
                ApplyMapUpdatesFromTurnResult(result, t);
        }

        if (abortTimeline)
            yield break;
    }

    private IEnumerator PlayExecutedAction(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null)
            yield break;

        if (action.actionType == "MoveStep")
        {
            if (action.succeeded)
                yield return PlayMoveStepAction(result, action);
            yield break;
        }

        if (action.actionType == "Attack")
        {
            yield return PlayRangedBulletAnimation(result, action);

            if (action.succeeded && action.damage > 0)
                ShowDamagePopupForAction(result, action);

            if (action.targetDied)
                yield return HandleUnitDeathAtCurrentAction(result, action.targetUnitId);

            float pause = Mathf.Max(0f, _attackActionPauseSeconds);
            if (pause > 0f)
                yield return new WaitForSecondsRealtime(pause);
        }
    }

    /// <summary>Гекс попадания в стену по журналу mapUpdates: тот же тик, что у Attack, и ближайший к стрелку гекс на линии выстрела.</summary>
    private bool TryGetWallHitHexFromMapUpdates(
        TurnResultPayload result,
        int tick,
        int fc,
        int fr,
        int tc,
        int tr,
        out int hitCol,
        out int hitRow)
    {
        hitCol = -1;
        hitRow = -1;
        if (result?.mapUpdates == null || result.mapUpdates.Length == 0)
            return false;

        _mapUpdateLineMatchSet.Clear();
        HexCubeOffset.GetHexLine(fc, fr, tc, tr, _bulletLineHexBuffer);
        foreach (var cell in _bulletLineHexBuffer)
            _mapUpdateLineMatchSet.Add(cell);

        int bestDist = int.MaxValue;
        foreach (BattleMapUpdate u in result.mapUpdates)
        {
            if (u == null || u.tick != tick)
                continue;
            if (!_mapUpdateLineMatchSet.Contains((u.col, u.row)))
                continue;
            int d = HexGrid.GetDistance(fc, fr, u.col, u.row);
            if (d < bestDist)
            {
                bestDist = d;
                hitCol = u.col;
                hitRow = u.row;
            }
        }

        return hitCol >= 0;
    }

    /// <summary>Пуля от атакующего к цели (или до исчерпания дальности оружия по линии гексов). Только при weaponRange &gt; 1.</summary>
    private IEnumerator PlayRangedBulletAnimation(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null || !action.succeeded || string.IsNullOrEmpty(action.actionType)
            || !string.Equals(action.actionType, "Attack", System.StringComparison.OrdinalIgnoreCase))
            yield break;

        if (!TryGetWeaponRangeForUnit(result, action.unitId, out int weaponRange) || weaponRange <= 1)
            yield break;

        HexGrid grid = _localPlayer != null && _localPlayer.Grid != null
            ? _localPlayer.Grid
            : FindFirstObjectByType<HexGrid>();
        if (grid == null)
            yield break;

        int fc, fr;
        if (action.fromPosition != null)
        {
            fc = action.fromPosition.col;
            fr = action.fromPosition.row;
        }
        else if (!TryGetUnitFinalHexFromTurnResult(result, action.unitId, out fc, out fr))
            yield break;

        int tc, tr;
        if (action.toPosition != null)
        {
            tc = action.toPosition.col;
            tr = action.toPosition.row;
        }
        else if (!TryGetUnitFinalHexFromTurnResult(result, action.targetUnitId, out tc, out tr))
            yield break;

        if (!grid.IsInBounds(fc, fr) || !grid.IsInBounds(tc, tr))
            yield break;

        int dist = HexGrid.GetDistance(fc, fr, tc, tr);
        float y = Mathf.Max(0.05f, _bulletHeightAboveGround);
        Vector3 start = grid.GetCellWorldPosition(fc, fr) + Vector3.up * y;
        Vector3 end;
        int hexEndCol = tc;
        int hexEndRow = tr;

        // Конец полёта: всегда центр клетки цели, если цель в пределах дальности оружия.
        // Нельзя брать _bulletLineHexBuffer[steps]: после дедупа в GetHexLine индекс ≠ «гекс на расстоянии steps»,
        // из‑за этого пуля могла лететь к промежуточному гексу, а не к объекту (камень range 2 и т.п.).
        if (dist <= weaponRange)
        {
            end = grid.GetCellWorldPosition(tc, tr) + Vector3.up * y;
        }
        else
        {
            // Атака вне дальности (редко при succeeded): обрезка по линии гексов к max range шагам.
            HexCubeOffset.GetHexLine(fc, fr, tc, tr, _bulletLineHexBuffer);
            if (_bulletLineHexBuffer.Count == 0)
                yield break;
            int idx = Mathf.Min(weaponRange, dist);
            if (idx >= _bulletLineHexBuffer.Count)
                idx = _bulletLineHexBuffer.Count - 1;
            (hexEndCol, hexEndRow) = _bulletLineHexBuffer[idx];
            end = grid.GetCellWorldPosition(hexEndCol, hexEndRow) + Vector3.up * y;
        }

        int durationSteps = Mathf.Max(1, Mathf.Min(weaponRange, dist));
        if (TryGetWallHitHexFromMapUpdates(result, action.tick, fc, fr, tc, tr, out int obsCol, out int obsRow)
            && grid.IsInBounds(obsCol, obsRow))
        {
            hexEndCol = obsCol;
            hexEndRow = obsRow;
            end = grid.GetCellWorldPosition(obsCol, obsRow) + Vector3.up * y;
            int dObs = HexGrid.GetDistance(fc, fr, obsCol, obsRow);
            durationSteps = Mathf.Max(1, Mathf.Min(weaponRange, dObs));
        }

        float pathLen = Vector3.Distance(start, end);
        if (pathLen < 1e-5f)
            yield break;

        Vector3 fireDir = (end - start) / pathLen;
        HexCubeOffset.GetHexLine(fc, fr, hexEndCol, hexEndRow, _bulletLineHexBuffer);
        float oneHexWorld = grid.HexSize * Mathf.Sqrt(3f);
        if (_bulletLineHexBuffer.Count >= 2)
        {
            var h0 = _bulletLineHexBuffer[0];
            var h1 = _bulletLineHexBuffer[1];
            Vector3 w0 = grid.GetCellWorldPosition(h0.col, h0.row) + Vector3.up * y;
            Vector3 w1 = grid.GetCellWorldPosition(h1.col, h1.row) + Vector3.up * y;
            oneHexWorld = Vector3.Distance(w0, w1);
        }

        float segmentLen = Mathf.Min(pathLen, oneHexWorld);

        float duration = Mathf.Clamp(
            _bulletFlightSeconds * (0.45f + durationSteps * 0.18f),
            _bulletFlightSeconds * 0.35f,
            _bulletFlightSeconds * 2.5f);

        var bulletGo = new GameObject("RangedBulletVFX");
        var line = bulletGo.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.loop = false;
        float lineWidth = Mathf.Max(0.008f, grid.HexSize * 0.022f);
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        var yellow = new Color(1f, 0.92f, 0.12f, 1f);
        line.startColor = yellow;
        line.endColor = yellow;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        if (_bulletMaterial != null)
            line.sharedMaterial = _bulletMaterial;
        else
        {
            Shader sh = Shader.Find("Sprites/Default")
                        ?? Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color");
            if (sh != null)
            {
                var mat = new Material(sh);
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", yellow);
                else if (mat.HasProperty("_Color"))
                    mat.color = yellow;
                line.sharedMaterial = mat;
            }
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            Vector3 head = start + fireDir * (u * pathLen);
            Vector3 tail = head - fireDir * segmentLen;
            if (Vector3.Dot(tail - start, fireDir) < 0f)
                tail = start;
            line.SetPosition(0, tail);
            line.SetPosition(1, head);
            yield return null;
        }

        {
            Vector3 head = end;
            Vector3 tail = head - fireDir * segmentLen;
            if (Vector3.Dot(tail - start, fireDir) < 0f)
                tail = start;
            line.SetPosition(0, tail);
            line.SetPosition(1, head);
        }

        UnityEngine.Object.Destroy(bulletGo);
    }

    private static bool TryGetWeaponRangeForUnit(TurnResultPayload result, string unitId, out int weaponRange)
    {
        weaponRange = 1;
        if (string.IsNullOrEmpty(unitId) || result?.results == null)
            return false;

        foreach (var item in result.results)
        {
            if (item == null || item.unitId != unitId)
                continue;
            weaponRange = Mathf.Max(0, item.weaponRange);
            return true;
        }

        return false;
    }

    /// <summary>Запасной вариант для старых turn result без toPosition/fromPosition в executedActions.</summary>
    private static bool TryGetUnitFinalHexFromTurnResult(TurnResultPayload result, string unitId, out int col, out int row)
    {
        col = row = 0;
        if (string.IsNullOrEmpty(unitId) || result?.results == null)
            return false;
        foreach (var item in result.results)
        {
            if (item == null || item.unitId != unitId || item.finalPosition == null)
                continue;
            col = item.finalPosition.col;
            row = item.finalPosition.row;
            return true;
        }
        return false;
    }

    private IEnumerator PlayMoveStepAction(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action?.toPosition == null)
            yield break;

        if (!TryResolveAnimatedUnit(result, action.unitId, out var unit, out bool isLocal))
            yield break;

        HexPosition from = action.fromPosition ?? action.toPosition;
        HexPosition to = action.toPosition;
        var path = new[] { new HexPosition(from.col, from.row), new HexPosition(to.col, to.row) };

        if (isLocal && unit is Player local)
            yield return local.PlayPathAnimation(path, driveCamera: false);
        else if (!isLocal && unit is RemoteBattleUnitView remote)
            yield return remote.PlayPathAnimation(path);
    }

    /// <summary>Запускает анимацию пути для всех юнитов в одном кадре (параллельно по кадрам).</summary>
    private IEnumerator PlayAllTurnAnimationsParallel(List<(object unit, bool isLocal, HexPosition[] path)> jobs)
    {
        if (jobs == null || jobs.Count == 0)
            yield break;

        yield return CoEnterServerRoundThirdPersonCamera();

        var running = new List<Coroutine>();
        foreach (var j in jobs)
        {
            if (j.isLocal && j.unit is Player pl)
                running.Add(StartCoroutine(PlayTurnResultAnimationCoroutine(pl, j.path)));
            else if (!j.isLocal && j.unit is RemoteBattleUnitView rv)
                running.Add(StartCoroutine(PlayRemoteAnimationCoroutine(rv, j.path)));
        }
        foreach (var c in running)
            yield return c;
    }

    /// <summary>Синхронизировать раунд и UTC deadline таймера с сервером.</summary>
    public void ApplyRoundState(int roundIndex, long roundDeadlineUtcMs)
    {
        _serverRoundIndex = roundIndex;
        _liveRoundDeadlineUtcMs = roundDeadlineUtcMs;
        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        local?.SetRoundState(roundIndex, roundDeadlineUtcMs);
    }

    /// <summary>Применить старт боя: локальный и удалённые юниты на позициях с сервера.</summary>
    public void ApplyBattleStarted(BattleStartedPayload payload)
    {
        if (payload == null) return;
        CancelTurnReplayPlayback();
        ClearAllDamagePopups();
        _battleId = payload.battleId ?? _battleId;
        _playerId = payload.playerId ?? _playerId;

        foreach (var kv in _remoteUnits)
        {
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _remoteUnits.Clear();
        _lastProcessedTurnResultRound = -1;
        _serverRoundIndex = 0;
        _liveRoundDeadlineUtcMs = 0;
        _turnHistoryIds.Clear();
        _turnHistoryCache.Clear();
        _currentTurnHistoryPointer = -1;
        _selectedTurnHistoryPointer = -1;
        _isTurnHistoryReplayPlaying = false;
        _liveTurnDraftSnapshot = null;
        _initialReplayState.Clear();
        CancelWaitingForServerRoundResolve();
        _battleFinished = false;

        long deadlineUtcMs = payload.roundDeadlineUtcMs;
        if (deadlineUtcMs <= 0)
        {
            float duration = payload.roundDuration > 0 ? payload.roundDuration : 100f;
            deadlineUtcMs = Player.BuildRoundDeadlineUtcMs(duration);
        }
        ApplyRoundState(0, deadlineUtcMs);

        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        HexGrid grid = local != null && local.Grid != null ? local.Grid : FindFirstObjectByType<HexGrid>();
        float moveDur = local != null ? local.MoveDurationPerHex : 0.2f;
        ApplyObstacleMap(payload, grid);

        var spawnList = BuildSpawnListFromPayload(payload);
        if (spawnList == null || spawnList.Count == 0)
        {
            Debug.LogWarning("[GameSession] ApplyBattleStarted: нет данных спавна в payload; юниты появятся из первого TurnResult по сокету.");
            return;
        }

        foreach (var (pid, col, row) in spawnList)
        {
            bool isMob = !string.IsNullOrEmpty(pid) && pid.StartsWith("MOB_", System.StringComparison.OrdinalIgnoreCase);
            int spawnIndex = FindSpawnIndex(payload, pid, col, row);
            int startAp = GetSpawnInt(payload.spawnCurrentAps, spawnIndex, isMob ? 15 : 100);
            int maxHp = GetSpawnInt(payload.spawnMaxHps, spawnIndex, 10);
            int currentHp = GetSpawnInt(payload.spawnCurrentHps, spawnIndex, maxHp);
            if (pid == _playerId)
            {
                if (local != null)
                {
                    local.SetHidden(false);
                    string posture = GetSpawnString(payload.spawnCurrentPostures, spawnIndex, MovementPostureUtility.WalkId);
                    local.ApplyServerTurnResult(new HexPosition(col, row), new[] { new HexPosition(col, row) }, startAp, 0f, posture);
                    local.SetHealth(currentHp, maxHp);
                    string wCode = GetSpawnString(payload.spawnWeaponCodes, spawnIndex, WeaponCatalog.DefaultWeaponCode);
                    int wDmg = GetSpawnInt(payload.spawnWeaponDamages, spawnIndex, 1);
                    int wRng = GetSpawnInt(payload.spawnWeaponRanges, spawnIndex, 1);
                    int wAtk = GetSpawnInt(payload.spawnWeaponAttackApCosts, spawnIndex, 1);
                    local.SetEquippedWeapon(wCode, wDmg, wRng, wAtk);
                }
                _initialReplayState[pid] = new ReplayUnitSnapshot
                {
                    UnitId = pid,
                    UnitType = 0,
                    Col = col,
                    Row = row,
                    CurrentAp = startAp,
                    PenaltyFraction = 0f,
                    CurrentPosture = GetSpawnString(payload.spawnCurrentPostures, spawnIndex, MovementPostureUtility.WalkId),
                    IsLocal = true
                };
                continue;
            }

            var go = new GameObject("Remote_" + pid);
            var rv = go.AddComponent<RemoteBattleUnitView>();
            rv.Initialize(pid, grid, col, row, moveDur);
            rv.SetHealth(currentHp, maxHp);
            _remoteUnits[pid] = rv;
            _initialReplayState[pid] = new ReplayUnitSnapshot
            {
                UnitId = pid,
                UnitType = isMob ? 1 : 0,
                Col = col,
                Row = row,
                CurrentAp = startAp,
                PenaltyFraction = 0f,
                CurrentPosture = GetSpawnString(payload.spawnCurrentPostures, spawnIndex, MovementPostureUtility.WalkId),
                IsLocal = false
            };
        }

        FindFirstObjectByType<HexGridCamera>()?.RefocusOnLocalPlayer();

        local?.ClearMovementFlag();
    }

    private void StartTurnHistoryReplay(int targetPointer)
    {
        if (targetPointer < 0 || targetPointer >= _turnHistoryIds.Count)
            return;

        _turnReplayCoroutine = StartCoroutine(ReplayTurnHistoryCoroutine(targetPointer));
    }

    private IEnumerator ReplayTurnHistoryCoroutine(int targetPointer)
    {
        _isTurnHistoryReplayPlaying = true;
        _selectedTurnHistoryPointer = targetPointer;
        if (_serverConnection == null)
            _serverConnection = FindFirstObjectByType<BattleServerConnection>();

        string turnId = _turnHistoryIds[targetPointer];
        if (string.IsNullOrEmpty(turnId))
        {
            _selectedTurnHistoryPointer = -1;
            _isTurnHistoryReplayPlaying = false;
            _turnReplayCoroutine = null;
            yield break;
        }

        if (!_turnHistoryCache.TryGetValue(turnId, out var targetTurn))
        {
            bool loaded = false;
            string loadError = null;
            BattleTurnResponsePayload response = null;
            if (_serverConnection == null)
            {
                _selectedTurnHistoryPointer = -1;
                _isTurnHistoryReplayPlaying = false;
                _turnReplayCoroutine = null;
                yield break;
            }

            yield return _serverConnection.LoadTurnByIdCoroutine(
                turnId,
                result =>
                {
                    response = result;
                    loaded = true;
                },
                error => loadError = error);

            if (!loaded || response == null || response.turnResult == null)
            {
                _selectedTurnHistoryPointer = -1;
                _isTurnHistoryReplayPlaying = false;
                _turnReplayCoroutine = null;
                if (!string.IsNullOrEmpty(loadError))
                    OnNetworkMessage?.Invoke(loadError);
                yield break;
            }

            targetTurn = response.turnResult;
            _turnHistoryCache[turnId] = targetTurn;
        }

        ResetToInitialReplayState();

        for (int i = 0; i < targetPointer; i++)
        {
            string pastTurnId = _turnHistoryIds[i];
            if (string.IsNullOrEmpty(pastTurnId))
                continue;

            if (!_turnHistoryCache.TryGetValue(pastTurnId, out var pastTurn))
            {
                bool loaded = false;
                string loadError = null;
                BattleTurnResponsePayload response = null;
                if (_serverConnection == null)
                {
                    _selectedTurnHistoryPointer = -1;
                    _isTurnHistoryReplayPlaying = false;
                    _turnReplayCoroutine = null;
                    yield break;
                }

                yield return _serverConnection.LoadTurnByIdCoroutine(
                    pastTurnId,
                    result =>
                    {
                        response = result;
                        loaded = true;
                    },
                    error => loadError = error);

                if (!loaded || response == null || response.turnResult == null)
                {
                    _selectedTurnHistoryPointer = -1;
                    _isTurnHistoryReplayPlaying = false;
                    _turnReplayCoroutine = null;
                    if (!string.IsNullOrEmpty(loadError))
                        OnNetworkMessage?.Invoke(loadError);
                    yield break;
                }

                pastTurn = response.turnResult;
                _turnHistoryCache[pastTurnId] = pastTurn;
            }

            BuildTurnResultAnimationJobs(pastTurn, prepareForAnimation: false);
        }

        if (_liveRoundDeadlineUtcMs > 0)
            ApplyRoundState(_serverRoundIndex, _liveRoundDeadlineUtcMs);

        var jobs = BuildTurnResultAnimationJobs(targetTurn, prepareForAnimation: true);
        var playback = BuildExecutedActionPlayback(targetTurn);
        if (playback.Count > 0)
            yield return PlayReplayAnimationsParallel(playback, targetTurn);
        else if (jobs.Count > 0)
            yield return PlayReplayAnimationsParallel(null, targetTurn, jobs);

        _isTurnHistoryReplayPlaying = false;
        _turnReplayCoroutine = null;
    }

    private IEnumerator RestoreLiveTurnStateCoroutine()
    {
        _isTurnHistoryReplayPlaying = true;

        ResetToInitialReplayState();

        for (int i = 0; i <= _currentTurnHistoryPointer; i++)
        {
            string turnId = _turnHistoryIds[i];
            if (string.IsNullOrEmpty(turnId))
                continue;

            if (!_turnHistoryCache.TryGetValue(turnId, out var turn))
            {
                bool loaded = false;
                string loadError = null;
                BattleTurnResponsePayload response = null;
                if (_serverConnection == null)
                    _serverConnection = FindFirstObjectByType<BattleServerConnection>();
                if (_serverConnection == null)
                {
                    _isTurnHistoryReplayPlaying = false;
                    _turnReplayCoroutine = null;
                    yield break;
                }

                yield return _serverConnection.LoadTurnByIdCoroutine(
                    turnId,
                    result =>
                    {
                        response = result;
                        loaded = true;
                    },
                    error => loadError = error);

                if (!loaded || response == null || response.turnResult == null)
                {
                    _isTurnHistoryReplayPlaying = false;
                    _turnReplayCoroutine = null;
                    if (!string.IsNullOrEmpty(loadError))
                        OnNetworkMessage?.Invoke(loadError);
                    yield break;
                }

                turn = response.turnResult;
                _turnHistoryCache[turnId] = turn;
            }

            BuildTurnResultAnimationJobs(turn, prepareForAnimation: false);
        }

        if (_liveRoundDeadlineUtcMs > 0)
            ApplyRoundState(_serverRoundIndex, _liveRoundDeadlineUtcMs);

        ReapplyLiveTurnDraftSnapshot();
        _selectedTurnHistoryPointer = -1;
        _isTurnHistoryReplayPlaying = false;
        _turnReplayCoroutine = null;
    }

    private void CancelTurnReplayPlayback()
    {
        if (_turnReplayCoroutine != null)
        {
            StopCoroutine(_turnReplayCoroutine);
            _turnReplayCoroutine = null;
        }

        foreach (var coroutine in _activeReplayAnimationCoroutines)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);
        }
        _activeReplayAnimationCoroutines.Clear();

        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        local?.ForceStopMovement(exitThirdPersonCamera: false);
        foreach (var remote in _remoteUnits.Values)
            remote?.ForceStopMovement();

        ClearAllDamagePopups();
        _isTurnHistoryReplayPlaying = false;
    }

    private void CaptureLiveTurnDraft()
    {
        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null)
        {
            _liveTurnDraftSnapshot = null;
            return;
        }

        _liveTurnDraftSnapshot = new LiveTurnDraftSnapshot
        {
            RoundIndex = _serverRoundIndex,
            Actions = local.GetTurnActionsCopy()
        };
    }

    private void ReapplyLiveTurnDraftSnapshot()
    {
        if (_liveTurnDraftSnapshot == null || _liveTurnDraftSnapshot.RoundIndex != _serverRoundIndex)
            return;

        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null)
            return;

        if (_liveTurnDraftSnapshot.Actions != null)
        {
            foreach (var action in _liveTurnDraftSnapshot.Actions)
            {
                if (action == null)
                    continue;

                if (action.actionType == "MoveStep" && action.targetPosition != null)
                {
                    _replayTwoPointMovePath.Clear();
                    _replayTwoPointMovePath.Add((local.CurrentCol, local.CurrentRow));
                    _replayTwoPointMovePath.Add((action.targetPosition.col, action.targetPosition.row));
                    local.MoveAlongPath(_replayTwoPointMovePath, animate: false);
                    continue;
                }

                if (action.actionType == "ChangePosture")
                {
                    local.QueuePostureChange(MovementPostureUtility.FromId(action.posture));
                    continue;
                }

                if (action.actionType == "Wait")
                {
                    local.QueueWaitAction(Mathf.Max(1, action.cost));
                    continue;
                }

                if (action.actionType == "Attack" && !string.IsNullOrEmpty(action.targetUnitId))
                    local.QueueAttackAction(action.targetUnitId, action.bodyPart, Mathf.Max(1, action.cost));

                if (action.actionType == "EquipWeapon" && !string.IsNullOrEmpty(action.weaponCode))
                {
                    int atk = action.weaponAttackApCost > 0 ? action.weaponAttackApCost : 1;
                    int? swapCost = action.cost > 0 ? action.cost : (int?)null;
                    local.QueueEquipWeaponAction(action.weaponCode, swapCost, atk, -1, -1);
                }
            }
        }
    }

    private IEnumerator PlayReplayAnimationsParallel(
        List<ExecutedActionPlaybackEntry> playback,
        TurnResultPayload result,
        List<(object unit, bool isLocal, HexPosition[] path)> fallbackJobs = null)
    {
        if (playback != null && playback.Count > 0)
        {
            var coroutine = StartCoroutine(PlayExecutedActionTimeline(result, playback));
            _activeReplayAnimationCoroutines.Clear();
            _activeReplayAnimationCoroutines.Add(coroutine);
            yield return coroutine;
            _activeReplayAnimationCoroutines.Clear();
            yield break;
        }

        _activeReplayAnimationCoroutines.Clear();
        var jobs = fallbackJobs ?? new List<(object unit, bool isLocal, HexPosition[] path)>();
        if (jobs.Count > 0)
            yield return CoEnterServerRoundThirdPersonCamera();
        foreach (var j in jobs)
        {
            if (j.isLocal && j.unit is Player pl)
                _activeReplayAnimationCoroutines.Add(StartCoroutine(PlayTurnResultAnimationCoroutine(pl, j.path)));
            else if (!j.isLocal && j.unit is RemoteBattleUnitView rv)
                _activeReplayAnimationCoroutines.Add(StartCoroutine(PlayRemoteAnimationCoroutine(rv, j.path)));
        }
        foreach (var coroutine in _activeReplayAnimationCoroutines)
            yield return coroutine;
        _activeReplayAnimationCoroutines.Clear();
    }

    private void ResetToInitialReplayState()
    {
        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        HexGrid grid = local != null && local.Grid != null ? local.Grid : FindFirstObjectByType<HexGrid>();
        float moveDur = local != null ? local.MoveDurationPerHex : 0.2f;
        if (grid == null)
            return;

        ClearAllDamagePopups();
        foreach (var snapshot in _initialReplayState.Values)
        {
            if (snapshot.IsLocal)
            {
                if (local == null) continue;
                local.SetHidden(false);
                var point = new HexPosition(snapshot.Col, snapshot.Row);
                local.ApplyServerTurnResult(point, new[] { point }, snapshot.CurrentAp, snapshot.PenaltyFraction, snapshot.CurrentPosture, prepareForAnimation: false);
                continue;
            }

            if (!_remoteUnits.TryGetValue(snapshot.UnitId, out var remote) || remote == null)
            {
                var go = new GameObject(snapshot.UnitType == 1 ? ("Mob_" + snapshot.UnitId) : ("Remote_" + snapshot.UnitId));
                remote = go.AddComponent<RemoteBattleUnitView>();
                remote.Initialize(snapshot.UnitId, grid, snapshot.Col, snapshot.Row, moveDur);
                _remoteUnits[snapshot.UnitId] = remote;
            }

            remote.ApplyServerTurnResult(
                new HexPosition(snapshot.Col, snapshot.Row),
                new[] { new HexPosition(snapshot.Col, snapshot.Row) },
                snapshot.CurrentAp,
                snapshot.PenaltyFraction,
                prepareForAnimation: false);
        }
    }

    private void ApplyObstacleMap(BattleStartedPayload payload, HexGrid grid)
    {
        _obstacleCells.Clear();
        _obstacleWallYawByCell.Clear();
        if (grid == null) return;

        foreach (HexCell cell in grid.GetComponentsInChildren<HexCell>())
            cell.SetObstacle(false);

        if (payload == null || payload.obstacleCols == null || payload.obstacleRows == null)
            return;
        if (payload.obstacleCols.Length != payload.obstacleRows.Length)
            return;

        for (int i = 0; i < payload.obstacleCols.Length; i++)
        {
            int col = payload.obstacleCols[i];
            int row = payload.obstacleRows[i];
            if (!grid.IsInBounds(col, row)) continue;
            _obstacleCells.Add((col, row));
            HexCell cell = grid.GetCell(col, row);
            if (cell != null)
            {
                cell.SetObstacle(true);
                string tag = payload.obstacleTags != null && i < payload.obstacleTags.Length && !string.IsNullOrEmpty(payload.obstacleTags[i])
                    ? payload.obstacleTags[i]
                    : "wall";
                float yaw = payload.obstacleWallYaws != null && i < payload.obstacleWallYaws.Length ? payload.obstacleWallYaws[i] : 0f;
                cell.SetObstacleVisual(tag, yaw);
                if (tag == "wall" || tag == "damaged_wall")
                    _obstacleWallYawByCell[(col, row)] = yaw;
            }
        }
    }

    /// <summary>Применить смену состояния стен с сервера (Full/Damaged/None). Если <paramref name="onlyTick"/> задан — только события этого тика (синхрон с журналом действий).</summary>
    private void ApplyMapUpdatesFromTurnResult(TurnResultPayload result, int? onlyTick = null)
    {
        if (result?.mapUpdates == null || result.mapUpdates.Length == 0)
            return;

        HexGrid grid = _localPlayer != null && _localPlayer.Grid != null
            ? _localPlayer.Grid
            : FindFirstObjectByType<HexGrid>();
        if (grid == null)
            return;

        foreach (BattleMapUpdate u in result.mapUpdates)
        {
            if (u == null) continue;
            if (onlyTick.HasValue && u.tick != onlyTick.Value) continue;
            int col = u.col;
            int row = u.row;
            if (!grid.IsInBounds(col, row)) continue;

            switch (u.newState)
            {
                case CellObjectState.None:
                    RemoveObstacleAtCell(grid, col, row);
                    break;
                case CellObjectState.Full:
                    _obstacleCells.Add((col, row));
                    HexCell fullCell = grid.GetCell(col, row);
                    if (fullCell != null)
                    {
                        float yaw = _obstacleWallYawByCell.TryGetValue((col, row), out float y) ? y : 0f;
                        fullCell.SetObstacle(true);
                        fullCell.SetObstacleVisual("wall", yaw);
                    }
                    break;
                case CellObjectState.Damaged:
                    _obstacleCells.Add((col, row));
                    HexCell damagedCell = grid.GetCell(col, row);
                    if (damagedCell != null)
                    {
                        float yaw = _obstacleWallYawByCell.TryGetValue((col, row), out float yd) ? yd : 0f;
                        damagedCell.SetObstacle(true);
                        damagedCell.SetObstacleVisual("damaged_wall", yaw);
                    }
                    break;
            }
        }
    }

    private void RemoveObstacleAtCell(HexGrid grid, int col, int row)
    {
        _obstacleCells.Remove((col, row));
        _obstacleWallYawByCell.Remove((col, row));
        HexCell cell = grid.GetCell(col, row);
        if (cell != null)
        {
            cell.ClearObstacleVisual();
            cell.SetObstacle(false);
        }
    }

    private static List<(string pid, int col, int row)> BuildSpawnListFromPayload(BattleStartedPayload payload)
    {
        var list = new List<(string, int, int)>();
        var seen = new HashSet<string>();

        if (payload.spawnPlayerIds != null && payload.spawnCols != null && payload.spawnRows != null
            && payload.spawnPlayerIds.Length == payload.spawnCols.Length
            && payload.spawnPlayerIds.Length == payload.spawnRows.Length)
        {
            for (int i = 0; i < payload.spawnPlayerIds.Length; i++)
            {
                string id = payload.spawnPlayerIds[i];
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                list.Add((id, payload.spawnCols[i], payload.spawnRows[i]));
            }
        }

        if (payload.players != null && payload.players.Length > 0)
        {
            foreach (var p in payload.players)
            {
                if (p == null || string.IsNullOrEmpty(p.playerId) || !seen.Add(p.playerId)) continue;
                list.Add((p.playerId, p.col, p.row));
            }
        }

        return list;
    }

    private static int FindSpawnIndex(BattleStartedPayload payload, string id, int col, int row)
    {
        if (payload == null || payload.spawnPlayerIds == null || payload.spawnCols == null || payload.spawnRows == null)
            return -1;
        int len = Mathf.Min(payload.spawnPlayerIds.Length, Mathf.Min(payload.spawnCols.Length, payload.spawnRows.Length));
        for (int i = 0; i < len; i++)
        {
            if (payload.spawnPlayerIds[i] == id && payload.spawnCols[i] == col && payload.spawnRows[i] == row)
                return i;
        }
        return -1;
    }

    private static int GetSpawnInt(int[] values, int index, int fallback)
    {
        if (values == null || index < 0 || index >= values.Length)
            return fallback;
        return values[index];
    }

    private static string GetSpawnString(string[] values, int index, string fallback)
    {
        if (values == null || index < 0 || index >= values.Length || string.IsNullOrEmpty(values[index]))
            return fallback;
        return values[index];
    }

    private IEnumerator HandleLocalDeathAfterMessage()
    {
        if (_battleFinished) yield break;
        _battleFinished = true;
        OnNetworkMessage?.Invoke("Бой проигран");
        OnBattleFinished?.Invoke(false);
        _waitingForServerRoundResolve = false;
        var p = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        p?.SetTurnTimerPaused(true);
        // Stop timeline immediately to prevent further animations/moves after fatal tick.
        ApplyLocalHiddenDeadState(silent: true);
        yield break;
    }

    private void ApplyLocalHiddenDeadState(bool silent)
    {
        var local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null)
            return;

        local.ForceStopMovement();
        local.SetHidden(true);
        if (!silent)
            _battleFinished = true;
    }

    private IEnumerator HandleUnitDeathAtCurrentAction(TurnResultPayload result, string deadUnitId)
    {
        if (string.IsNullOrEmpty(deadUnitId))
            yield break;

        if (!TryResolveAnimatedUnit(result, deadUnitId, out var unit, out bool isLocal))
            yield break;

        if (isLocal)
        {
            if (unit is Player local)
                local.ForceStopMovement();
            yield return HandleLocalDeathAfterMessage();
            yield break;
        }

        if (unit is RemoteBattleUnitView remote)
        {
            string key = ResolveRemoteUnitKey(result, deadUnitId);
            if (!string.IsNullOrEmpty(key))
                _remoteUnits.Remove(key);
            if (remote != null)
                Destroy(remote.gameObject);
        }
    }

    private void HandleBattleFinishedFromServer(TurnResultPayload result)
    {
        if (_battleFinished) return;

        bool localDead = false;
        if (result != null && result.results != null)
        {
            foreach (var item in result.results)
            {
                if (item == null) continue;
                if (item.playerId == _playerId)
                {
                    localDead = item.isDead;
                    break;
                }
            }
        }

        if (!localDead)
        {
            _battleFinished = true;
            _waitingForServerRoundResolve = false;
            var p = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
            p?.SetTurnTimerPaused(true);
            OnBattleFinished?.Invoke(true);
            OnNetworkMessage?.Invoke("Бой выигран");
        }
    }

    private void AppendServerTurnLogs(TurnResultPayload result, bool showDamagePopupsImmediately)
    {
        if (result == null || result.results == null)
            return;

        foreach (var turnResult in result.results)
        {
            if (turnResult?.executedActions == null)
                continue;

            foreach (var action in turnResult.executedActions)
            {
                if (action == null)
                    continue;

                if (action.actionType == "Attack")
                {
                    if (action.succeeded)
                    {
                        string targetLabel = string.IsNullOrEmpty(action.targetUnitId) ? "цель" : action.targetUnitId;
                        string bodyPart = string.IsNullOrEmpty(action.bodyPart) ? "корпус" : action.bodyPart;
                        if (showDamagePopupsImmediately)
                            ShowDamagePopupForAction(result, action);
                        OnNetworkMessage?.Invoke($"[{action.unitId}] удар в {bodyPart}: {targetLabel} -{action.damage} HP");
                        if (action.targetDied)
                            OnNetworkMessage?.Invoke($"{targetLabel} погиб");
                    }
                }
            }
        }
    }

    private void ShowDamagePopupForAction(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null || !action.succeeded || action.damage <= 0 || string.IsNullOrEmpty(action.targetUnitId))
            return;

        Transform target = ResolveDamagePopupTarget(result, action.targetUnitId);
        if (target == null)
            return;

        DamagePopupStack stack = target.GetComponent<DamagePopupStack>();
        if (stack == null)
            stack = target.gameObject.AddComponent<DamagePopupStack>();
        stack.ShowDamage(action.damage);
    }

    private Transform ResolveDamagePopupTarget(TurnResultPayload result, string targetUnitId)
    {
        if (string.IsNullOrEmpty(targetUnitId))
            return null;

        if (result?.results != null)
        {
            foreach (var item in result.results)
            {
                if (item == null || item.unitId != targetUnitId)
                    continue;

                if (item.playerId == _playerId)
                    return _localPlayer != null ? _localPlayer.transform : null;

                string remoteKey = item.unitType == 1 ? item.unitId : item.playerId;
                if (!string.IsNullOrEmpty(remoteKey) && _remoteUnits.TryGetValue(remoteKey, out var remote) && remote != null)
                    return remote.transform;
            }
        }

        if (_remoteUnits.TryGetValue(targetUnitId, out var fallbackRemote) && fallbackRemote != null)
            return fallbackRemote.transform;

        return null;
    }

    private void ClearAllDamagePopups()
    {
        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local != null)
        {
            DamagePopupStack localStack = local.GetComponent<DamagePopupStack>();
            localStack?.ClearAll();
        }

        foreach (var remote in _remoteUnits.Values)
        {
            if (remote == null)
                continue;
            DamagePopupStack stack = remote.GetComponent<DamagePopupStack>();
            stack?.ClearAll();
        }
    }

    private bool TryResolveAnimatedUnit(TurnResultPayload result, string unitId, out object unit, out bool isLocal)
    {
        unit = null;
        isLocal = false;
        if (string.IsNullOrEmpty(unitId) || result?.results == null)
            return false;

        foreach (var item in result.results)
        {
            if (item == null || item.unitId != unitId)
                continue;

            if (item.playerId == _playerId)
            {
                Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
                if (local == null)
                    return false;
                unit = local;
                isLocal = true;
                return true;
            }

            string remoteKey = ResolveRemoteUnitKey(item);
            if (!string.IsNullOrEmpty(remoteKey) && _remoteUnits.TryGetValue(remoteKey, out var remote) && remote != null)
            {
                unit = remote;
                return true;
            }
        }

        return false;
    }

    private string ResolveRemoteUnitKey(TurnResultPayload result, string unitId)
    {
        if (string.IsNullOrEmpty(unitId) || result?.results == null)
            return null;

        foreach (var item in result.results)
        {
            if (item == null || item.unitId != unitId)
                continue;
            return ResolveRemoteUnitKey(item);
        }

        return null;
    }

    private static string ResolveRemoteUnitKey(PlayerTurnResult item)
    {
        if (item == null)
            return null;
        return item.unitType == 1 ? item.unitId : item.playerId;
    }

    private SubmitTurnPayload BuildSubmitPayload(BattleQueuedAction[] actions, int roundIndex)
    {
        var source = actions ?? System.Array.Empty<BattleQueuedAction>();
        var payloadActions = new BattleQueuedAction[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var action = source[i];
            payloadActions[i] = new BattleQueuedAction
            {
                actionType = action.actionType,
                targetPosition = action.targetPosition != null ? new HexPosition(action.targetPosition.col, action.targetPosition.row) : null,
                targetUnitId = action.targetUnitId,
                bodyPart = action.bodyPart,
                posture = action.posture,
                previousPosture = action.previousPosture,
                cost = action.cost,
                weaponCode = action.weaponCode,
                previousWeaponAttackApCost = action.previousWeaponAttackApCost,
                weaponAttackApCost = action.weaponAttackApCost
            };
        }

        return new SubmitTurnPayload
        {
            battleId = _battleId,
            playerId = _playerId,
            roundIndex = roundIndex,
            actions = payloadActions
        };
    }
}
