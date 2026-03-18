using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сессия боя: отправка хода на сервер и применение результата (по плану ServerSyncPlan).
/// Этап 4: все юниты по TurnResult + параллельная анимация. Этап 5: OnNetworkMessage.
/// </summary>
public class GameSession : MonoBehaviour
{
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

    private bool _waitingForServerRoundResolve;
    private bool _animateResolvedRoundForPendingSubmit = true;
    private int _lastProcessedTurnResultRound = -1;
    /// <summary>Индекс раунда с сервера — только его слать в submit (TurnCount локально может отличаться).</summary>
    private int _serverRoundIndex;

    /// <summary>Блокировка ввода: ожидание результата раунда или анимация.</summary>
    public bool BlockPlayerInput => _waitingForServerRoundResolve || IsBattleAnimationPlaying;

    public bool IsWaitingForServerRoundResolve => _waitingForServerRoundResolve;
    public int LastProcessedTurnResultRound => _lastProcessedTurnResultRound;
    public int ServerRoundIndexForSubmit => _serverRoundIndex;

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
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
    }

    private void Start()
    {
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
        Player local = _localPlayer != null ? _localPlayer : FindFirstObjectByType<Player>();
        local?.SetRoundState(roundIndex, roundDeadlineUtcMs);
    }

    /// <summary>Применить старт боя: локальный и удалённые юниты на позициях с сервера.</summary>
    public void ApplyBattleStarted(BattleStartedPayload payload)
    {
        if (payload == null) return;
        _battleId = payload.battleId ?? _battleId;
        _playerId = payload.playerId ?? _playerId;

        foreach (var kv in _remoteUnits)
        {
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _remoteUnits.Clear();
        _lastProcessedTurnResultRound = -1;
        _serverRoundIndex = 0;
        CancelWaitingForServerRoundResolve();

        long deadlineUtcMs = payload.roundDeadlineUtcMs;
        if (deadlineUtcMs <= 0)
        {
            float duration = payload.roundDuration > 0 ? payload.roundDuration : 30f;
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
            if (pid == _playerId)
            {
                if (local != null)
                    local.ApplyServerTurnResult(new HexPosition(col, row), new[] { new HexPosition(col, row) }, 100, 0f);
                continue;
            }

            var go = new GameObject("Remote_" + pid);
            var rv = go.AddComponent<RemoteBattleUnitView>();
            rv.Initialize(pid, grid, col, row, moveDur);
            _remoteUnits[pid] = rv;
        }

        FindFirstObjectByType<HexGridCamera>()?.RefocusOnLocalPlayer();
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
