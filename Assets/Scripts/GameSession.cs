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
        public bool IsLocal;
    }

    private sealed class LiveTurnDraftSnapshot
    {
        public int RoundIndex;
        public List<(int col, int row)> Path = new();
        public int ApSpent;
        public int StepsTaken;
    }

    /// <summary>Сообщения для UI: «Не удалось отправить ход», «Клетка занята», rejectedReason и т.д.</summary>
    public static System.Action<string> OnNetworkMessage;
    /// <summary>POST submit успешно принят — показать бар до ответа по WebSocket.</summary>
    public static System.Action OnSubmitTurnDeliveredToServer;
    /// <summary>Пуш результата раунда по WebSocket — скрыть бар ожидания.</summary>
    public static System.Action OnWebSocketRoundPushReceived;
    /// <summary>Ожидание отменено (ошибка отправки, конец боя).</summary>
    public static System.Action OnServerRoundWaitCancelled;

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

    // Ключ: идентификатор сущности в сети (для игроков — playerId, для мобов — unitId сервера).
    private readonly Dictionary<string, RemoteBattleUnitView> _remoteUnits = new();
    private readonly HashSet<(int col, int row)> _obstacleCells = new();
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
    private LiveTurnDraftSnapshot _liveTurnDraftSnapshot;

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

    public bool IsInBattleWithServer() => _serverConnection != null && _serverConnection.IsInBattle;
    public bool IsObstacleCell(int col, int row) => _obstacleCells.Contains((col, row));
    public Player LocalPlayer => _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();

    public List<RemoteBattleUnitView> GetRemoteUnitsSnapshot()
    {
        var list = new List<RemoteBattleUnitView>();
        foreach (var unit in _remoteUnits.Values)
        {
            if (unit != null)
                list.Add(unit);
        }
        return list;
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
        return TrySubmitCurrentLiveTurnDraft(animateResolvedRound);
    }

    public bool TrySubmitCurrentLiveTurnDraft(bool animateResolvedRound)
    {
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
        SubmitTurnLocal(draft.Path, draft.ApSpent, draft.StepsTaken, _serverRoundIndex);
        return true;
    }

    public static GameSession Active { get; private set; }

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
    }

    // Локальный ChasingAiController больше не используется: серверный моб управляется на стороне сервера.

    public void BeginWaitingForServerRoundResolve(bool animateResolvedRound)
    {
        _waitingForServerRoundResolve = true;
        _animateResolvedRoundForPendingSubmit = animateResolvedRound;
        var p = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        p?.SetTurnTimerPaused(true);
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
    public void SubmitTurnLocal(List<(int col, int row)> path, int apSpentThisTurn, int stepsTakenThisTurn, int roundIndex)
    {
        var payload = BuildSubmitPayload(path, apSpentThisTurn, stepsTakenThisTurn, roundIndex);
        // Одиночный бой через меню тоже идёт по серверу при IsInBattle — без проверки только _isOnlineMode,
        // иначе UI ждёт раунд, а submit не уходит.
        if (_serverConnection != null && _serverConnection.IsInBattle)
        {
            var sock = FindFirstObjectByType<BattleSignalRConnection>();
            if (sock == null || !sock.IsSocketReady)
            {
                CancelWaitingForServerRoundResolve();
                OnNetworkMessage?.Invoke("Нет WebSocket к бою. Подождите подключения.");
                return;
            }

            sock.SubmitTurnViaSocket(payload, (success, errorMessage) =>
            {
                if (!success)
                {
                    CancelWaitingForServerRoundResolve();
                    OnNetworkMessage?.Invoke(errorMessage ?? "Не удалось отправить ход.");
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

    /// <summary>Применить результат хода с сервера: все юниты + параллельная анимация по actualPath.</summary>
    public void ApplyTurnResult(TurnResultPayload result)
    {
        if (result == null || result.results == null) return;
        var animJobs = BuildTurnResultAnimationJobs(result, prepareForAnimation: true);
        if (animJobs.Count > 0)
            StartCoroutine(PlayAllTurnAnimationsParallel(animJobs));
    }

    /// <summary>Ожидали свой сабмит: скрыть бар → анимация → новый раунд и снятие блокировки.</summary>
    public void ApplyTurnResultThenRoundState(TurnResultPayload result, int nextRoundIndex, long roundDeadlineUtcMs)
    {
        if (result == null || result.results == null) return;
        bool animateResolvedRound = _animateResolvedRoundForPendingSubmit;
        _animateResolvedRoundForPendingSubmit = true;
        var animJobs = BuildTurnResultAnimationJobs(result, animateResolvedRound);
        StartCoroutine(DeferredRoundAfterAnimations(animJobs, nextRoundIndex, roundDeadlineUtcMs));
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
                if (!r.accepted && !string.IsNullOrEmpty(r.rejectedReason))
                    OnNetworkMessage?.Invoke(r.rejectedReason);
                else if (!r.accepted)
                    OnNetworkMessage?.Invoke("Клетка занята, ход частично отменён.");

                local.ApplyServerTurnResult(r.finalPosition, r.actualPath, r.currentAp, r.penaltyFraction, prepareForAnimation);
                if (prepareForAnimation)
                    animJobs.Add((local, true, r.actualPath));
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
                if (prepareForAnimation)
                    animJobs.Add((remote, false, r.actualPath));
            }
        }

        return animJobs;
    }

    private IEnumerator DeferredRoundAfterAnimations(List<(object unit, bool isLocal, HexPosition[] path)> jobs, int nextRoundIndex, long roundDeadlineUtcMs)
    {
        if (jobs.Count > 0)
            yield return PlayAllTurnAnimationsParallel(jobs);
        ApplyRoundState(nextRoundIndex, roundDeadlineUtcMs);
        var p = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        p?.SetTurnTimerPaused(false);
        _waitingForServerRoundResolve = false;
    }

    private static IEnumerator PlayTurnResultAnimationCoroutine(Player player, HexPosition[] path)
    {
        if (player == null || path == null) yield break;
        yield return player.PlayPathAnimation(path);
    }

    private static IEnumerator PlayRemoteAnimationCoroutine(RemoteBattleUnitView remote, HexPosition[] path)
    {
        if (remote == null || path == null) yield break;
        yield return remote.PlayPathAnimation(path);
    }

    /// <summary>Запускает анимацию пути для всех юнитов в одном кадре (параллельно по кадрам).</summary>
    private IEnumerator PlayAllTurnAnimationsParallel(List<(object unit, bool isLocal, HexPosition[] path)> jobs)
    {
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
            int startAp = isMob ? 75 : 100;
            if (pid == _playerId)
            {
                if (local != null)
                    local.ApplyServerTurnResult(new HexPosition(col, row), new[] { new HexPosition(col, row) }, 100, 0f);
                _initialReplayState[pid] = new ReplayUnitSnapshot
                {
                    UnitId = pid,
                    UnitType = 0,
                    Col = col,
                    Row = row,
                    CurrentAp = 100,
                    PenaltyFraction = 0f,
                    IsLocal = true
                };
                continue;
            }

            var go = new GameObject("Remote_" + pid);
            var rv = go.AddComponent<RemoteBattleUnitView>();
            rv.Initialize(pid, grid, col, row, moveDur);
            _remoteUnits[pid] = rv;
            _initialReplayState[pid] = new ReplayUnitSnapshot
            {
                UnitId = pid,
                UnitType = isMob ? 1 : 0,
                Col = col,
                Row = row,
                CurrentAp = startAp,
                PenaltyFraction = 0f,
                IsLocal = false
            };
        }

        FindFirstObjectByType<HexGridCamera>()?.RefocusOnLocalPlayer();
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
        if (jobs.Count > 0)
            yield return PlayReplayAnimationsParallel(jobs);

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
        local?.ForceStopMovement();
        foreach (var remote in _remoteUnits.Values)
            remote?.ForceStopMovement();

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
            Path = local.GetTurnPathCopy(),
            ApSpent = local.ApSpentThisTurn,
            StepsTaken = local.StepsTakenThisTurn
        };
    }

    private void ReapplyLiveTurnDraftSnapshot()
    {
        if (_liveTurnDraftSnapshot == null || _liveTurnDraftSnapshot.RoundIndex != _serverRoundIndex)
            return;

        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        if (local == null)
            return;

        if (_liveTurnDraftSnapshot.Path == null || _liveTurnDraftSnapshot.Path.Count < 2)
            return;

        local.MoveAlongPath(new List<(int col, int row)>(_liveTurnDraftSnapshot.Path), animate: false);
    }

    private IEnumerator PlayReplayAnimationsParallel(List<(object unit, bool isLocal, HexPosition[] path)> jobs)
    {
        _activeReplayAnimationCoroutines.Clear();
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

        foreach (var snapshot in _initialReplayState.Values)
        {
            if (snapshot.IsLocal)
            {
                if (local == null) continue;
                var point = new HexPosition(snapshot.Col, snapshot.Row);
                local.ApplyServerTurnResult(point, new[] { point }, snapshot.CurrentAp, snapshot.PenaltyFraction, prepareForAnimation: false);
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
                cell.SetObstacle(true);
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

    private SubmitTurnPayload BuildSubmitPayload(List<(int col, int row)> path, int apSpentThisTurn, int stepsTakenThisTurn, int roundIndex)
    {
        var pathArr = new HexPosition[path?.Count ?? 0];
        if (path != null)
        {
            for (int i = 0; i < path.Count; i++)
                pathArr[i] = new HexPosition(path[i].col, path[i].row);
        }

        return new SubmitTurnPayload
        {
            battleId = _battleId,
            playerId = _playerId,
            roundIndex = roundIndex,
            path = pathArr,
            apSpentThisTurn = apSpentThisTurn,
            stepsTakenThisTurn = stepsTakenThisTurn
        };
    }
}
