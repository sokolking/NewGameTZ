using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        public int BattleTeamId = -1;
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

    /// <summary>Active rectangle from server changed — refresh <see cref="MapBorderEscapeRing"/> etc.</summary>
    public static event System.Action ActiveBattleZoneChanged;

    /// <summary>Set for one <see cref="OnBattleFinished"/> tick when the local player fled (escape), not a normal win.</summary>
    public static bool LastBattleEndWasEscape { get; private set; }

    [Header("Mode & IDs")]
    [Tooltip("Send turn to server when the local turn ends.")]
    [SerializeField] private bool _isOnlineMode;
    [SerializeField] private string _battleId = "battle-1";
    [SerializeField] private string _playerId = "P1";

    [Header("Server")]
    [SerializeField] private BattleServerConnection _serverConnection;

    [Header("Local unit")]
    [Tooltip("Local player unit for TurnResult / round sync. If unset, found in scene.")]
    [SerializeField] private Player _localPlayer;

    [Header("Battle unit card (right-click inspect)")]
    [Tooltip("Drag the UnitCard instance (UnitCardView) from the Canvas hierarchy. If empty, the first UnitCardView in the scene is used.")]
    [FormerlySerializedAs("_battleInspectUnitCard")]
    [SerializeField] private UnitCardView battleInspectUnitCard;

    [Header("Round playback")]
    [Tooltip("Pause after attack actions so tick rhythm and AP changes read clearly.")]
    [SerializeField] private float _attackActionPauseSeconds = 0.08f;
    [Tooltip("Short pause between action journal ticks.")]
    [SerializeField] private float _tickPauseSeconds = 0.03f;

    [Header("Ranged: bullet")]
    [Tooltip("Base bullet travel time (sec); scaled by hex count.")]
    [SerializeField] private float _bulletFlightSeconds = 0.14f;
    [Tooltip("Fallback line start height if attacker has no Humanoid RightHand (world).")]
    [SerializeField] private float _bulletHeightAboveGround = 0.28f;
    [Tooltip("Line end Y offset from hex floor center. 0 = floor of last hex.")]
    [SerializeField] private float _bulletHexEndYOffset = 0f;
    [Tooltip("Max wait for model to face shot direction before drawing bullet line.")]
    [SerializeField] private float _rangedFaceTurnMaxSeconds = 0.35f;
    [Tooltip("Treat rotation as done when angle to target is below this (degrees).")]
    [SerializeField] private float _rangedFaceAngleThresholdDeg = 4f;
    [Tooltip("Optional shot line material (else Sprites/Default, yellow).")]
    [SerializeField] private Material _bulletMaterial;

    [Header("Debug")]
    [Tooltip("Debug: spawn mob on a neighbor hex near local player.")]
    [SerializeField] private bool _debugSpawnMobNearLocalPlayer = false;
    [Tooltip("Debug mob id (must start with MOB_).")]
    [SerializeField] private string _debugMobId = "MOB_DEBUG";
    [Tooltip("Preferred neighbor direction (0..5). If blocked, next direction is used.")]
    [SerializeField] private int _debugNeighborDirection = 0;

    // Ключ: идентификатор сущности в сети (для игроков — playerId, для мобов — unitId сервера).
    private readonly Dictionary<string, RemoteBattleUnitView> _remoteUnits = new();
    private readonly HashSet<(int col, int row)> _obstacleCells = new();
    private readonly Dictionary<(int col, int row), float> _obstacleWallYawByCell = new();
    /// <summary>Local human team from server (0/1); -1 unknown.</summary>
    private int _localBattleTeamId = -1;
    private int _battleActiveMinCol;
    private int _battleActiveMaxCol;
    private int _battleActiveMinRow;
    private int _battleActiveMaxRow;
    private bool _battleActiveBoundsInitialized;
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
    private bool _localEliminationPending;
    private bool _localEliminationEscape;
    private bool _battleInspectProfileControllerDisabled;
    /// <summary>Network entity id for open battle inspect <see cref="UnitCardView"/>; cleared when hidden.</summary>
    private string _battleInspectEntityId;
    private HexGridCamera _hexGridCamera;
    private HexGrid _hexGrid;
    /// <summary>Watching via MainMenu Watch — no submit, no map actions; camera + history only.</summary>
    private bool _spectatorMode;
    private readonly List<string> _spectatedHumanIds = new();
    private int _spectatedHumanIndex;
    /// <summary>Блокировка ввода: ожидание результата раунда или анимация.</summary>
    public bool BlockPlayerInput =>
        _spectatorMode
        || _waitingForServerRoundResolve || IsBattleAnimationPlaying || IsTurnHistoryReplayPlaying || IsViewingHistoricalTurn
        || LocalPlayerIsEscaping;

    public bool IsSpectatorMode => _spectatorMode;

    /// <summary>Server marked the local unit as fleeing: no planning until they leave the battle or cancel by leaving the escape ring.</summary>
    public bool LocalPlayerIsEscaping => !_spectatorMode && LocalPlayer != null && LocalPlayer.IsServerEscaping;

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

    /// <summary>Build unit inspect card data from cached battle units (spawn arrays + turn updates).</summary>
    public bool TryBuildInspectPayloadForEntity(string entityId, out UnitCardPayload payload)
    {
        payload = new UnitCardPayload();
        if (string.IsNullOrEmpty(entityId))
            return false;

        if (entityId == _playerId)
        {
            Player local = LocalPlayer;
            if (local == null || local.IsHidden)
                return false;
            local.FillUnitCardPayload(payload);
            return true;
        }

        if (_remoteUnits.TryGetValue(entityId, out RemoteBattleUnitView rv) && rv != null)
        {
            rv.FillUnitCardPayload(payload);
            return true;
        }

        return false;
    }

    private bool TryResolveBattleInspectUnitCard(out UnitCardView view)
    {
        view = battleInspectUnitCard;
        if (view == null)
        {
#if UNITY_2023_1_OR_NEWER
            view = FindFirstObjectByType<UnitCardView>();
#else
            view = FindObjectOfType<UnitCardView>();
#endif
        }

        return view != null;
    }

    /// <summary>Battle inspect popup is active (shown after right-click on a unit).</summary>
    public bool IsBattleInspectUnitCardOpen =>
        TryResolveBattleInspectUnitCard(out var v) && v.gameObject.activeInHierarchy;

    /// <summary>Hit-test in screen space for closing the inspect card on outside click.</summary>
    public bool IsScreenPointOverBattleInspectUnitCard(Vector2 screenPoint, Camera worldOrUiCameraFallback)
    {
        if (!TryResolveBattleInspectUnitCard(out UnitCardView view) || !view.gameObject.activeInHierarchy)
            return false;
        var rt = view.transform as RectTransform;
        if (rt == null)
            return false;
        Canvas canvas = view.GetComponentInParent<Canvas>();
        if (canvas == null)
            return false;
        Camera eventCam = null;
        if (canvas.renderMode == RenderMode.ScreenSpaceCamera || canvas.renderMode == RenderMode.WorldSpace)
            eventCam = canvas.worldCamera != null ? canvas.worldCamera : worldOrUiCameraFallback;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, eventCam);
    }

    /// <summary>Battle <see cref="UnitCardView"/> shares the UnitCard prefab with MainMenu profile; disable session profile load so inspect data is not overwritten.</summary>
    private void EnsureBattleInspectCardIgnoresSessionProfile()
    {
        if (_battleInspectProfileControllerDisabled)
            return;
        if (!TryResolveBattleInspectUnitCard(out UnitCardView view))
            return;
        var profile = view.GetComponent<PlayerProfileCardController>();
        if (profile != null)
            profile.enabled = false;
        _battleInspectProfileControllerDisabled = true;
    }

    /// <summary>Show battle <see cref="UnitCardView"/> centered on the screen. <paramref name="inspectEntityId"/> is used to refresh stats after each round while the card stays open.</summary>
    public void TryShowInspectUnitCard(string inspectEntityId, UnitCardPayload payload, Camera worldOrUiCamera)
    {
        if (_battleFinished || payload == null)
            return;

        EnsureBattleInspectCardIgnoresSessionProfile();

        if (!TryResolveBattleInspectUnitCard(out UnitCardView view))
            return;

        _battleInspectEntityId = string.IsNullOrEmpty(inspectEntityId) ? null : inspectEntityId;

        view.gameObject.SetActive(true);
        view.Render(payload);

        RectTransform rt = view.transform as RectTransform;
        if (rt == null)
            return;

        Canvas canvas = view.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        Camera eventCam = null;
        if (canvas.renderMode == RenderMode.ScreenSpaceCamera || canvas.renderMode == RenderMode.WorldSpace)
            eventCam = canvas.worldCamera != null ? canvas.worldCamera : worldOrUiCamera;

        RectTransform rootRt = canvas.transform as RectTransform;
        if (rootRt == null)
            return;

        var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, screenCenter, eventCam, out Vector2 localCenter))
            return;

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = localCenter;
    }

    public void HideBattleInspectUnitCard()
    {
        _battleInspectEntityId = null;
        if (!TryResolveBattleInspectUnitCard(out UnitCardView view))
            return;
        view.SetVisible(false);
    }

    /// <summary>Re-render open inspect card from current battle cache (e.g. after round resolve / skip turn).</summary>
    private void RefreshBattleInspectUnitCardIfVisible()
    {
        if (string.IsNullOrEmpty(_battleInspectEntityId) || !IsBattleInspectUnitCardOpen)
            return;
        if (!TryBuildInspectPayloadForEntity(_battleInspectEntityId, out UnitCardPayload payload))
        {
            HideBattleInspectUnitCard();
            return;
        }

        if (!TryResolveBattleInspectUnitCard(out UnitCardView view))
            return;
        view.Render(payload);
    }

    public bool IsInBattleWithServer() => _serverConnection != null && _serverConnection.IsInBattle;
    public bool IsObstacleCell(int col, int row) => _obstacleCells.Contains((col, row));

    /// <summary>Server-shrunk playable rectangle; if not initialized, whole grid counts (offline / legacy).</summary>
    public bool IsHexInActiveBattleZone(int col, int row)
    {
        if (!_battleActiveBoundsInitialized)
            return true;
        return col >= _battleActiveMinCol && col <= _battleActiveMaxCol
            && row >= _battleActiveMinRow && row <= _battleActiveMaxRow;
    }

    /// <summary>True when the playable rectangle is the whole <see cref="HexGrid"/> (no zone shrink yet).</summary>
    public bool ActiveBattleZoneCoversFullGrid()
    {
        if (!_battleActiveBoundsInitialized)
            return false;
        HexGrid grid = CachedHexGrid;
        if (grid == null)
            return false;
        return _battleActiveMinCol == 0 && _battleActiveMaxCol == grid.Width - 1
            && _battleActiveMinRow == 0 && _battleActiveMaxRow == grid.Length - 1;
    }

    /// <summary>Current server active rectangle (inclusive). False if bounds not initialized.</summary>
    public bool TryGetActiveBattleBounds(out int minCol, out int maxCol, out int minRow, out int maxRow)
    {
        minCol = maxCol = minRow = maxRow = 0;
        if (!_battleActiveBoundsInitialized)
            return false;
        minCol = _battleActiveMinCol;
        maxCol = _battleActiveMaxCol;
        minRow = _battleActiveMinRow;
        maxRow = _battleActiveMaxRow;
        return true;
    }

    /// <summary>
    /// One-hex frame outside the active battle rectangle (col/row ±1 from limits); may include coordinates outside the physical grid.
    /// </summary>
    public bool IsEscapeBorderHex(int col, int row)
    {
        if (!_battleActiveBoundsInitialized)
            return false;
        if (IsHexInActiveBattleZone(col, row))
            return false;
        return col >= _battleActiveMinCol - 1 && col <= _battleActiveMaxCol + 1
            && row >= _battleActiveMinRow - 1 && row <= _battleActiveMaxRow + 1;
    }

    /// <summary>Escape edges use normal movement AP (server matches).</summary>
    public bool IsFreeApStepFromActiveToEscapeBorder(int fromCol, int fromRow, int toCol, int toRow) => false;

    public bool LocalPlayerStandsOnEscapeBorderHex()
    {
        if (_spectatorMode)
            return false;
        Player p = LocalPlayer;
        return p != null && !p.IsDead && !p.IsHidden && IsEscapeBorderHex(p.CurrentCol, p.CurrentRow);
    }

    public void RefreshEscapeBorderRing()
    {
        MapBorderEscapeRing ring = FindFirstObjectByType<MapBorderEscapeRing>();
        ring?.RefreshMarkers();
    }

    private static void RaiseActiveBattleZoneChanged() =>
        ActiveBattleZoneChanged?.Invoke();

    public Player LocalPlayer
    {
        get
        {
            if (_localPlayer == null)
                _localPlayer = FindFirstObjectByType<Player>();
            return _localPlayer;
        }
    }

    /// <summary>Server PvP team for the local player (0/1); -1 if unknown.</summary>
    public int LocalBattleTeamId => _localBattleTeamId;

    private HexGridCamera CachedHexGridCamera
    {
        get
        {
            if (_hexGridCamera == null)
                _hexGridCamera = FindFirstObjectByType<HexGridCamera>();
            return _hexGridCamera;
        }
    }

    private HexGrid CachedHexGrid
    {
        get
        {
            var local = LocalPlayer;
            if (local != null && local.Grid != null)
                return local.Grid;
            if (_hexGrid == null)
                _hexGrid = FindFirstObjectByType<HexGrid>();
            return _hexGrid;
        }
    }

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

    /// <summary>Blue/red hex under units; call after spawn and after round snap.</summary>
    public void RefreshPvpOccupancyHexHighlights()
    {
        HexGrid grid = CachedHexGrid;
        if (grid == null)
            return;

        grid.ClearAllPvpOccupancyHighlights();

        Player local = LocalPlayer;
        int localTeam = _localBattleTeamId;

        // Spectator: scene Player is not a combatant — only remotes occupy the map.
        if (!_spectatorMode && local != null && !local.IsDead && !local.IsHidden && local.CurrentHp > 0)
        {
            HexCell lc = grid.GetCell(local.CurrentCol, local.CurrentRow);
            lc?.SetPvpOccupancyHighlight(HexCell.PvpOccupancyKind.Ally);
        }

        foreach (RemoteBattleUnitView remote in _remoteUnits.Values)
        {
            if (remote == null || remote.CurrentHp <= 0)
                continue;
            HexCell.PvpOccupancyKind occ = ResolvePvpOccupancyKind(remote, localTeam);
            HexCell c = grid.GetCell(remote.CurrentCol, remote.CurrentRow);
            c?.SetPvpOccupancyHighlight(occ);
        }
    }

    private static HexCell.PvpOccupancyKind ResolvePvpOccupancyKind(RemoteBattleUnitView remote, int localTeam)
    {
        if (remote.IsMob)
            return HexCell.PvpOccupancyKind.Enemy;
        if (localTeam < 0 || remote.BattleTeamId < 0)
            return HexCell.PvpOccupancyKind.Enemy;
        return remote.BattleTeamId == localTeam ? HexCell.PvpOccupancyKind.Ally : HexCell.PvpOccupancyKind.Enemy;
    }

    public void RegisterProcessedTurnResult(int roundIndex)
    {
        _lastProcessedTurnResultRound = roundIndex;
    }

    /// <summary>After re-login to an ongoing battle: align turn history and round from GET <c>/api/battle/{battleId}</c>.</summary>
    public void ApplyResumeSnapshotFromServer(int serverRoundIndex, long roundDeadlineUtcMs, string[] turnHistoryIds, int currentTurnPointer)
    {
        ReplaceTurnHistoryIds(turnHistoryIds ?? System.Array.Empty<string>(), currentTurnPointer);
        ApplyRoundState(serverRoundIndex, roundDeadlineUtcMs);
        int lastResolved = serverRoundIndex > 0 ? serverRoundIndex - 1 : -1;
        RegisterProcessedTurnResult(lastResolved);
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
        if (_battleFinished || _spectatorMode)
            return false;
        return TrySubmitCurrentLiveTurnDraft(animateResolvedRound);
    }

    public bool TrySubmitCurrentLiveTurnDraft(bool animateResolvedRound)
    {
        if (_battleFinished || _spectatorMode)
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

        if (TryGetRangedFacingHorizontalForSubmitActions(draft.Actions, out Vector3 faceDir))
        {
            StartCoroutine(CoSubmitOnlineAfterRangedFace(draft.Actions, _serverRoundIndex, animateResolvedRound, faceDir));
            return true;
        }

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
    /// <param name="weaponCategory">Категория из БД (например <c>light</c>) — для анимации в режиме планирования до ответа сервера.</param>
    public void RequestEquipWeapon(string weaponCode, int weaponAttackApCost = 1, int weaponDamageFromDb = -1, int weaponRangeFromDb = -1, string weaponCategory = null)
    {
        if (_spectatorMode)
            return;
        if (string.IsNullOrWhiteSpace(weaponCode))
            return;
        weaponCode = weaponCode.Trim().ToLowerInvariant();
        var pl = LocalPlayer;
        if (pl == null)
            return;
        int atk = Mathf.Max(1, weaponAttackApCost);
        if (IsInBattleWithServer())
        {
            if (!pl.QueueEquipWeaponAction(weaponCode, null, atk, weaponDamageFromDb, weaponRangeFromDb, weaponCategory))
                OnNetworkMessage?.Invoke(Loc.T("ui.not_enough_ap"));
        }
        else
            ApplyLocalWeaponOnly(weaponCode, atk, weaponDamageFromDb, weaponRangeFromDb, weaponCategory);
    }

    private void ApplyLocalWeaponOnly(string weaponCode, int weaponAttackApCost, int weaponDamageFromDb = -1, int weaponRangeFromDb = -1, string weaponCategory = null)
    {
        string code = WeaponCatalog.NormalizeWeaponCode(weaponCode);
        int dmg = weaponDamageFromDb >= 0 ? weaponDamageFromDb : 1;
        int range = weaponRangeFromDb >= 0 ? weaponRangeFromDb : 1;
        LocalPlayer?.SetEquippedWeapon(code, dmg, range, weaponAttackApCost, weaponDamageMin: -1, weaponCategory: weaponCategory ?? "");
    }

    /// <summary>Включён ли онлайн-режим (отправка хода при завершении). True также при загрузке через Find Game (сессия в бою).</summary>
    public bool IsOnlineMode => _isOnlineMode || (_serverConnection != null && _serverConnection.IsInBattle);

    /// <summary>Идёт анимация TurnResult (локальный или удалённый юнит) — не завершать ход.</summary>
    public bool IsBattleAnimationPlaying
    {
        get
        {
            var local = LocalPlayer;
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
        EnsureBattleInspectCardIgnoresSessionProfile();

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
    public bool TryPerformSilhouetteAttack(RemoteBattleUnitView target, int bodyPartId, bool shiftRepeat = false)
    {
        if (_spectatorMode || _battleFinished || target == null) return false;
        var local = LocalPlayer;
        if (local == null) return false;

        int attackCost = Mathf.Max(1, local.WeaponAttackApCost);
        int apBefore = local.CurrentAp;
        int repeatCount = shiftRepeat ? apBefore / attackCost : 1;
        if (repeatCount <= 0)
        {
            OnNetworkMessage?.Invoke(Loc.T("ui.not_enough_ap"));
            return false;
        }

        int queued = 0;
        for (int i = 0; i < repeatCount; i++)
        {
            if (!local.QueueAttackAction(target.NetworkPlayerId, bodyPartId, attackCost))
                break;
            queued++;
        }

        if (queued <= 0)
        {
            OnNetworkMessage?.Invoke(Loc.T("ui.not_enough_ap"));
            return false;
        }

        string partName = BodyPartIds.DisplayName(bodyPartId);
        if (queued > 1)
            OnNetworkMessage?.Invoke(Loc.Tf("combat.attack_queued_multi", queued, partName));
        else
            OnNetworkMessage?.Invoke(Loc.Tf("combat.attack_queued_single", partName));
        return true;
    }

    public bool TryUseSelfItem()
    {
        if (_spectatorMode || _battleFinished) return false;
        var local = LocalPlayer;
        if (local == null) return false;
#if UNITY_2023_1_OR_NEWER
        InventoryUI inv = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
#else
        InventoryUI inv = UnityEngine.Object.FindObjectOfType<InventoryUI>();
#endif
        if (inv == null || !inv.IsActiveItemMedicine())
            return false;

        int useCost = Mathf.Max(1, inv.GetCurrentActiveItemUseApCost());
        if (!local.QueueUseItemAction(useCost))
        {
            OnNetworkMessage?.Invoke(Loc.T("ui.not_enough_ap"));
            return false;
        }

        OnNetworkMessage?.Invoke("Self item queued");
        return true;
    }

    /// <summary>Ctrl+клик по гексу: дальнобойный выстрел по направлению (стена на ЛС / враг на клетке).</summary>
    public bool TryPerformHexAimAttack(int col, int row, bool shiftRepeat = false)
    {
        if (_spectatorMode || _battleFinished) return false;
        var local = LocalPlayer;
        if (local == null) return false;

        int pc = local.CurrentCol;
        int pr = local.CurrentRow;
        int d = HexGrid.GetDistance(pc, pr, col, row);
        if (d == 0)
        {
            OnNetworkMessage?.Invoke(Loc.T("ui.pick_different_hex"));
            return false;
        }

        int weaponRange = Mathf.Max(0, local.WeaponRangeHexes);
        if (weaponRange <= 0)
        {
            OnNetworkMessage?.Invoke(Loc.T("ui.hex_out_of_weapon_range"));
            return false;
        }

        int aimCol = col;
        int aimRow = row;

        int attackCost = Mathf.Max(1, local.WeaponAttackApCost);
        int apBefore = local.CurrentAp;
        int repeatCount = shiftRepeat ? apBefore / attackCost : 1;
        if (repeatCount <= 0)
        {
            OnNetworkMessage?.Invoke(Loc.T("ui.not_enough_ap"));
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
            OnNetworkMessage?.Invoke(Loc.T("ui.not_enough_ap"));
            return false;
        }

        if (queued > 1)
            OnNetworkMessage?.Invoke(Loc.Tf("combat.hex_shot_queued_multi", queued));
        else
            OnNetworkMessage?.Invoke(Loc.T("combat.hex_shot_queued_single"));

        ApplyLocalPlayerRangedFacingTowardTargetHex(aimCol, aimRow);
        return true;
    }

    /// <summary>
    /// Развернуть локального игрока к клетке прицела (в т.ч. за номинальной дальностью — сервер режет урон). ЛКМ по силуэту / Ctrl+клик.
    /// </summary>
    public void ApplyLocalPlayerRangedFacingTowardTargetHex(int targetCol, int targetRow)
    {
        if (_battleFinished)
            return;
        Player pl = LocalPlayer;
        if (pl == null || pl.Grid == null)
            return;
        if (pl.WeaponRangeHexes <= 1)
            return;

        int fc = pl.CurrentCol;
        int fr = pl.CurrentRow;
        HexGrid grid = pl.Grid;
        if (!grid.IsInBounds(fc, fr) || !grid.IsInBounds(targetCol, targetRow))
            return;

        int finalHexCol = targetCol;
        int finalHexRow = targetRow;

        Vector3 end = grid.GetCellWorldPosition(finalHexCol, finalHexRow) + Vector3.up * _bulletHexEndYOffset;
        Vector3 fromCell = grid.GetCellWorldPosition(fc, fr);
        Vector3 horizontalDir = end - fromCell;
        horizontalDir.y = 0f;
        if (horizontalDir.sqrMagnitude < 1e-8f)
            return;
        horizontalDir.Normalize();

        PlayerCharacterAnimator anim = pl.GetComponentInChildren<PlayerCharacterAnimator>();
        anim?.SetHorizontalFacingOverride(horizontalDir);
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

        Player local = LocalPlayer;
        if (local == null) return false;

        HexGrid grid = CachedHexGrid;
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
            remote.Initialize(id, grid, nc, nr, local.MoveDurationPerHex, _localBattleTeamId, -1);
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
        var p = LocalPlayer;
        p?.SetTurnTimerPaused(true);
    }

    public void CancelWaitingForServerRoundResolve()
    {
        bool was = _waitingForServerRoundResolve;
        _waitingForServerRoundResolve = false;
        _animateResolvedRoundForPendingSubmit = true;
        var p = LocalPlayer;
        p?.SetTurnTimerPaused(false);
        if (was) OnServerRoundWaitCancelled?.Invoke();
    }

    /// <summary>
    /// Собрать данные хода и отправить на сервер (или в заглушку).
    /// Вызывать до применения EndTurn у Player.
    /// </summary>
    public void SubmitTurnLocal(BattleQueuedAction[] actions, int roundIndex)
    {
        if (_battleFinished || _spectatorMode)
            return;
        if (LocalPlayerIsEscaping)
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
                OnNetworkMessage?.Invoke(Loc.T("ui.no_battle_socket"));
                return;
            }

            sock.SubmitTurnViaSocket(payload, (success, errorMessage) =>
            {
                if (!success)
                {
                    CancelWaitingForServerRoundResolve();
                    OnNetworkMessage?.Invoke(string.IsNullOrEmpty(errorMessage) ? Loc.T("ui.submit_turn_failed") : errorMessage);
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

    /// <summary>Результат раунда с сервера: сначала анимации журнала/путей, затем <see cref="ApplyRoundState"/> и снятие <c>_waitingForServerRoundResolve</c> (если ждали свой сабмит).</summary>
    public void ApplyTurnResultThenRoundState(TurnResultPayload result, int nextRoundIndex, long roundDeadlineUtcMs)
    {
        if (result == null || result.results == null) return;
        RemoveFledRemoteUnitsFromTurnResult(result);
        bool animateResolvedRound = !_waitingForServerRoundResolve || _animateResolvedRoundForPendingSubmit;
        if (_waitingForServerRoundResolve)
            _animateResolvedRoundForPendingSubmit = true;
        if (!animateResolvedRound)
        {
            ApplyMapUpdatesFromTurnResult(result);
        }
        AppendServerTurnLogs(result);
        LogHitRollsFromTurnResult(result);
        var playback = animateResolvedRound ? BuildExecutedActionPlayback(result) : null;
        var animJobs = BuildTurnResultAnimationJobs(result, animateResolvedRound, deferLocomotionPosture: playback != null && playback.Count > 0);
        StartCoroutine(DeferredRoundAfterAnimations(animJobs, playback, nextRoundIndex, roundDeadlineUtcMs, result));
    }

    private List<(object unit, bool isLocal, HexPosition[] path)> BuildTurnResultAnimationJobs(TurnResultPayload result, bool prepareForAnimation, bool deferLocomotionPosture = false)
    {
        var animJobs = new List<(object unit, bool isLocal, HexPosition[] path)>();
        Player local = LocalPlayer;

        if (result?.results != null)
        {
            foreach (var tr in result.results)
            {
                if (tr == null || tr.unitType != 0)
                    continue;
                if (tr.playerId == _playerId && tr.teamId >= 0)
                {
                    _localBattleTeamId = tr.teamId;
                    break;
                }
            }
        }

        if (result?.results == null)
            return animJobs;

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
                    OnNetworkMessage?.Invoke(Loc.T("ui.cell_occupied"));

                local.ApplyServerTurnResult(r.finalPosition, r.actualPath, r.currentAp, r.penaltyFraction, r.currentPosture, prepareForAnimation, applyLocomotionPosture: !deferLocomotionPosture);
                local.SetServerEscapeState(r.isEscaping);
                local.SetHealth(r.currentHp, r.maxHp > 0 ? r.maxHp : local.MaxHp);
                if (!string.IsNullOrEmpty(r.weaponCode))
                {
                    int wAtk = r.weaponAttackApCost > 0 ? r.weaponAttackApCost : 1;
                    int wMin = r.weaponDamageMin > 0 ? r.weaponDamageMin : r.weaponDamage;
                    local.SetEquippedWeapon(r.weaponCode, r.weaponDamage, r.weaponRange, wAtk, wMin, r.weaponCategory ?? "");
                }
                if (r.isDead || r.hasFled)
                {
                    _localEliminationPending = true;
                    _localEliminationEscape = r.hasFled;
                }
                if (prepareForAnimation)
                    animJobs.Add((local, true, r.actualPath));
                else if (r.isDead || r.hasFled)
                {
                    bool escaped = r.hasFled;
                    if (_isTurnHistoryReplayPlaying)
                        ApplyLocalEliminatedState(escaped, showMessage: false);
                    else
                        StartCoroutine(HandleLocalEliminationAfterMessage(escaped));
                }
                continue;
            }

            if (!_remoteUnits.TryGetValue(id, out var remote) || remote == null)
            {
                // Юнит ещё не создан (например, серверный моб) — создаём RemoteBattleUnitView по первой точке пути.
                HexGrid grid = CachedHexGrid;
                if (grid != null && r.actualPath != null && r.actualPath.Length > 0)
                {
                    var first = r.actualPath[0];
                    var go = new GameObject(isMob ? ("Mob_" + id) : ("Remote_" + id));
                    remote = go.AddComponent<RemoteBattleUnitView>();
                    int rTeam = isMob ? -1 : r.teamId;
                    remote.Initialize(id, grid, first.col, first.row, local != null ? local.MoveDurationPerHex : 0.2f, _localBattleTeamId, rTeam);
                    _remoteUnits[id] = remote;
                }
            }

            if (remote != null)
            {
                if (!isMob && r.teamId >= 0)
                    remote.SetBattleTeamIds(_localBattleTeamId, r.teamId);
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

        SnapBattleUnitsToAuthoritativeHexPositions();
        if (result != null)
            ApplyZoneShrinkFromTurnResult(result);
        if (_localEliminationPending)
        {
            bool escaped = _localEliminationEscape;
            _localEliminationPending = false;
            _localEliminationEscape = false;
            if (_isTurnHistoryReplayPlaying)
                ApplyLocalEliminatedState(escaped, showMessage: false);
            else
                yield return HandleLocalEliminationAfterMessage(escaped);
            yield break;
        }

        // Planning unlocks here (clear wait + ApplyRoundState). Round-wait overlay is hidden on push — see BattleSignalRConnection.
        if (result == null || !result.battleFinished)
        {
            ApplyRoundState(nextRoundIndex, roundDeadlineUtcMs);
            var p = LocalPlayer;
            p?.SetTurnTimerPaused(false);
            _waitingForServerRoundResolve = false;

            RefreshBattleInspectUnitCardIfVisible();

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

        int currentTick = -1;
        bool abortTimeline = false;
        var appliedMapTicks = new HashSet<int>();

        {
            Player localForPhase = LocalPlayer;
            if (localForPhase != null)
            {
                var anim = localForPhase.GetComponentInChildren<PlayerCharacterAnimator>();
                anim?.ResetHexWalkPhaseForNewPath();
            }
        }

        ApplyLocalReplayInitialPostureFromTurnResult(result);

        for (int pi = 0; pi < playback.Count; pi++)
        {
            ExecutedActionPlaybackEntry entry = playback[pi];
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
            yield return PlayExecutedAction(result, action, playback, pi);
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

        {
            Player p = LocalPlayer;
            if (p != null)
            {
                ApplyLocalFinalLocomotionPostureFromTurnResult(result, p);
                p.ClearMovementPlaybackState();
            }
        }

        if (abortTimeline)
            yield break;
    }

    /// <summary>Локальный игрок: до журнала выставить позу начала раунда (сервер), иначе конец планирования даёт ложный Sit→Stand.</summary>
    private void ApplyLocalReplayInitialPostureFromTurnResult(TurnResultPayload result)
    {
        if (result?.results == null || string.IsNullOrEmpty(_playerId))
            return;
        Player local = LocalPlayer;
        if (local == null)
            return;
        foreach (var r in result.results)
        {
            if (r == null || r.unitType == 1)
                continue;
            if (r.playerId != _playerId)
                continue;
            if (string.IsNullOrEmpty(r.postureAtRoundStart))
                return;
            local.ApplyReplayInitialLocomotionPosture(r.postureAtRoundStart);
            return;
        }
    }

    /// <summary>Поза походки для этого действия в журнале (до анимации шага/атаки и т.д.).</summary>
    private void ApplyLocomotionPostureForExecutedActionIfLocal(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null || string.IsNullOrEmpty(action.posture))
            return;
        if (!TryResolveAnimatedUnit(result, action.unitId, out var unit, out bool isLocal))
            return;
        if (!isLocal || unit is not Player pl)
            return;
        pl.SetMovementPostureFromServer(action.posture);
    }

    private void ApplyLocalFinalLocomotionPostureFromTurnResult(TurnResultPayload result, Player local)
    {
        if (local == null || result?.results == null)
            return;
        foreach (var r in result.results)
        {
            if (r == null)
                continue;
            bool isMob = r.unitType == 1;
            if (!isMob && r.playerId == _playerId)
            {
                if (!string.IsNullOrEmpty(r.currentPosture))
                    local.SetMovementPostureFromServer(r.currentPosture);
                return;
            }
        }
    }

    private IEnumerator PlayExecutedAction(
        TurnResultPayload result,
        BattleExecutedAction action,
        List<ExecutedActionPlaybackEntry> playback,
        int playbackIndex)
    {
        if (action == null)
            yield break;

        ApplyLocomotionPostureForExecutedActionIfLocal(result, action);

        if (action.actionType == "MoveStep")
        {
            if (action.succeeded)
                yield return PlayMoveStepAction(result, action, playback, playbackIndex);
            yield break;
        }

        if (action.actionType == "Attack")
        {
            yield return PlayColdMeleeAttackIfApplicable(result, action);
            yield return PlayRangedBulletAnimation(result, action);

            if (action.succeeded && action.damage > 0)
                ShowDamagePopupForAction(result, action);

            if (action.targetDied)
                yield return HandleUnitDeathAtCurrentAction(result, action.targetUnitId);

            float pause = Mathf.Max(0f, _attackActionPauseSeconds);
            if (pause > 0f)
                yield return new WaitForSecondsRealtime(pause);
        }
        else if (action.actionType == "UseItem")
        {
            yield return PlayUseItemMedicineAnimation(result, action);
            if (action.succeeded && action.healed > 0)
                ShowHealPopupForAction(result, action);
        }
    }

    private IEnumerator PlayUseItemMedicineAnimation(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null || !action.succeeded || string.IsNullOrEmpty(action.unitId))
            yield break;
        if (!TryResolveAnimatedUnit(result, action.unitId, out object unit, out bool isLocal))
            yield break;

        PlayerCharacterAnimator anim = null;
        if (isLocal && unit is Player pl)
            anim = pl.GetComponentInChildren<PlayerCharacterAnimator>();
        else if (unit is RemoteBattleUnitView remote)
            anim = remote.GetComponentInChildren<PlayerCharacterAnimator>();

        if (anim != null)
            yield return StartCoroutine(anim.RunUseItemMedicineRoutine());
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

    /// <summary>Старт линии пули: кость RightHand (Humanoid) или запас от центра гекса.</summary>
    private bool TryGetRangedBulletStartWorld(
        TurnResultPayload result,
        string attackerUnitId,
        HexGrid grid,
        int fc,
        int fr,
        out Vector3 start)
    {
        start = default;
        if (!TryResolveAnimatedUnit(result, attackerUnitId, out object unit, out bool isLocal))
        {
            float h = Mathf.Max(0.05f, _bulletHeightAboveGround);
            start = grid.GetCellWorldPosition(fc, fr) + Vector3.up * h;
            return true;
        }

        if (isLocal && unit is Player pl)
        {
            pl.TryGetRangedFireWorldPosition(out start);
            return true;
        }

        if (unit is RemoteBattleUnitView remote)
        {
            remote.TryGetRangedFireWorldPosition(out start);
            return true;
        }

        float hf = Mathf.Max(0.05f, _bulletHeightAboveGround);
        start = grid.GetCellWorldPosition(fc, fr) + Vector3.up * hf;
        return true;
    }

    private static void ClearFacingForRangedShot(PlayerCharacterAnimator localAnim, RemoteBattleUnitView remote)
    {
        localAnim?.ClearHorizontalFacingOverride();
        remote?.ClearHorizontalFacingOverride();
    }

    /// <summary>Включает разворот к <paramref name="horizontalDir"/> (XZ) для локального игрока или удалённого юнита.</summary>
    private bool TryBeginFacingForRangedShot(
        TurnResultPayload result,
        string attackerUnitId,
        Vector3 horizontalDir,
        out PlayerCharacterAnimator localAnim,
        out RemoteBattleUnitView remote)
    {
        localAnim = null;
        remote = null;
        if (!TryResolveAnimatedUnit(result, attackerUnitId, out object unit, out bool isLocal))
            return false;

        horizontalDir.y = 0f;
        if (horizontalDir.sqrMagnitude < 1e-8f)
            return false;
        horizontalDir.Normalize();

        if (isLocal && unit is Player pl)
        {
            localAnim = pl.GetComponentInChildren<PlayerCharacterAnimator>();
            if (localAnim != null)
            {
                localAnim.SetHorizontalFacingOverride(horizontalDir);
                return true;
            }

            return false;
        }

        if (unit is RemoteBattleUnitView r)
        {
            remote = r;
            remote.SetHorizontalFacingOverride(horizontalDir);
            return true;
        }

        return false;
    }

    private IEnumerator CoFaceUntilAligned(Transform pivot, Quaternion targetRot)
    {
        if (pivot == null)
            yield break;

        float elapsed = 0f;
        float maxSec = Mathf.Max(0.02f, _rangedFaceTurnMaxSeconds);
        float thr = Mathf.Max(0.5f, _rangedFaceAngleThresholdDeg);
        while (elapsed < maxSec)
        {
            elapsed += Time.unscaledDeltaTime;
            if (Quaternion.Angle(pivot.rotation, targetRot) < thr)
                yield break;
            yield return null;
        }
    }

    /// <summary>Горизонтальное направление к «дну» конечной клетки выстрела по очереди (как у линии пули после раунда).</summary>
    private bool TryGetRangedFacingHorizontalForSubmitActions(BattleQueuedAction[] actions, out Vector3 horizontalDir)
    {
        horizontalDir = default;
        Player pl = LocalPlayer;
        if (pl == null || pl.Grid == null)
            return false;
        if (pl.WeaponRangeHexes <= 1)
            return false;
        if (actions == null || actions.Length == 0)
            return false;

        BattleQueuedAction lastAttack = null;
        for (int i = actions.Length - 1; i >= 0; i--)
        {
            BattleQueuedAction a = actions[i];
            if (a != null && string.Equals(a.actionType, "Attack", StringComparison.OrdinalIgnoreCase))
            {
                lastAttack = a;
                break;
            }
        }

        if (lastAttack == null)
            return false;

        pl.GetTurnSimulationStartHex(out int fc, out int fr);
        for (int i = 0; i < actions.Length; i++)
        {
            BattleQueuedAction a = actions[i];
            if (a == lastAttack)
                break;
            if (a != null && string.Equals(a.actionType, "MoveStep", StringComparison.OrdinalIgnoreCase)
                && a.targetPosition != null)
            {
                fc = a.targetPosition.col;
                fr = a.targetPosition.row;
            }
        }

        if (!TryResolveQueuedAttackTargetHex(lastAttack, out int tc, out int tr))
            return false;

        HexGrid grid = pl.Grid;
        if (!grid.IsInBounds(fc, fr) || !grid.IsInBounds(tc, tr))
            return false;

        int finalHexCol = tc;
        int finalHexRow = tr;

        Vector3 end = grid.GetCellWorldPosition(finalHexCol, finalHexRow) + Vector3.up * _bulletHexEndYOffset;
        Vector3 fromCell = grid.GetCellWorldPosition(fc, fr);
        horizontalDir = end - fromCell;
        horizontalDir.y = 0f;
        if (horizontalDir.sqrMagnitude < 1e-8f)
            return false;
        horizontalDir.Normalize();
        return true;
    }

    private bool TryResolveQueuedAttackTargetHex(BattleQueuedAction attack, out int tc, out int tr)
    {
        tc = tr = 0;
        if (attack == null)
            return false;

        if (attack.targetPosition != null)
        {
            tc = attack.targetPosition.col;
            tr = attack.targetPosition.row;
            return true;
        }

        if (string.IsNullOrEmpty(attack.targetUnitId))
            return false;

        if (_remoteUnits.TryGetValue(attack.targetUnitId, out RemoteBattleUnitView rem) && rem != null)
        {
            tc = rem.CurrentCol;
            tr = rem.CurrentRow;
            return true;
        }

        foreach (KeyValuePair<string, RemoteBattleUnitView> kv in _remoteUnits)
        {
            if (kv.Value == null || string.IsNullOrEmpty(kv.Value.NetworkPlayerId))
                continue;
            if (string.Equals(kv.Value.NetworkPlayerId, attack.targetUnitId, StringComparison.OrdinalIgnoreCase))
            {
                tc = kv.Value.CurrentCol;
                tr = kv.Value.CurrentRow;
                return true;
            }
        }

        return false;
    }

    /// <summary>Отправка хода по сети: при дальнобойной атаке в очереди — разворот локального игрока, затем submit.</summary>
    public void SubmitTurnOnlineWithOptionalRangedFacing(BattleQueuedAction[] actions, int roundIndex, bool animateResolvedRound)
    {
        if (_battleFinished || _spectatorMode || !IsInBattleWithServer() || LocalPlayerIsEscaping)
            return;

        BattleQueuedAction[] safe = actions ?? Array.Empty<BattleQueuedAction>();
        if (!TryGetRangedFacingHorizontalForSubmitActions(safe, out Vector3 faceDir))
        {
            BeginWaitingForServerRoundResolve(animateResolvedRound);
            SubmitTurnLocal(safe, roundIndex);
            return;
        }

        StartCoroutine(CoSubmitOnlineAfterRangedFace(safe, roundIndex, animateResolvedRound, faceDir));
    }

    private IEnumerator CoSubmitOnlineAfterRangedFace(
        BattleQueuedAction[] actions,
        int roundIndex,
        bool animateResolvedRound,
        Vector3 faceDir)
    {
        BeginWaitingForServerRoundResolve(animateResolvedRound);
        PlayerCharacterAnimator faceA = null;
        try
        {
            Player pl = LocalPlayer;
            faceA = pl != null ? pl.GetComponentInChildren<PlayerCharacterAnimator>() : null;
            if (faceA != null)
            {
                faceA.SetHorizontalFacingOverride(faceDir);
                Quaternion targetRot = Quaternion.LookRotation(faceDir, Vector3.up);
                yield return CoFaceUntilAligned(faceA.FacingPivot, targetRot);
            }
        }
        finally
        {
            ClearFacingForRangedShot(faceA, null);
        }

        SubmitTurnLocal(actions, roundIndex);
    }

    /// <summary>Hex endpoints for an executed Attack (journal); used when final <see cref="PlayerTurnResult.weaponRange"/> reflects post-turn equip (e.g. medkit) but the action was ranged.</summary>
    private static bool TryGetAttackHexEndpointsForPlayback(TurnResultPayload result, BattleExecutedAction action, out int fc, out int fr, out int tc, out int tr)
    {
        fc = fr = tc = tr = 0;
        if (action == null)
            return false;

        if (action.fromPosition != null)
        {
            fc = action.fromPosition.col;
            fr = action.fromPosition.row;
        }
        else if (!TryGetUnitFinalHexFromTurnResult(result, action.unitId, out fc, out fr))
            return false;

        if (action.toPosition != null)
        {
            tc = action.toPosition.col;
            tr = action.toPosition.row;
        }
        else if (!string.IsNullOrEmpty(action.targetUnitId))
        {
            if (!TryGetUnitFinalHexFromTurnResult(result, action.targetUnitId, out tc, out tr))
                return false;
        }
        else
            return false;

        return true;
    }

    /// <summary>Пуля от атакующего к цели по линии гексов; дальность оружия влияет только на урон на сервере, не на длину визуала.</summary>
    private IEnumerator PlayRangedBulletAnimation(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null || !action.succeeded || string.IsNullOrEmpty(action.actionType)
            || !string.Equals(action.actionType, "Attack", System.StringComparison.OrdinalIgnoreCase))
            yield break;

        if (!TryGetAttackHexEndpointsForPlayback(result, action, out int fc, out int fr, out int tc, out int tr))
            yield break;

        HexGrid grid = CachedHexGrid;
        if (grid == null || !grid.IsInBounds(fc, fr) || !grid.IsInBounds(tc, tr))
            yield break;

        int geomDist = HexGrid.GetDistance(fc, fr, tc, tr);
        if (!TryGetWeaponRangeForUnit(result, action.unitId, out int weaponRange))
            weaponRange = 0;
        // Final snapshot may list medkit (range ≤ 1) after attack + UseItem in the same turn; geometry still says ranged.
        if (weaponRange <= 1 && geomDist <= 1)
            yield break;
        if (weaponRange <= 1 && geomDist > 1)
            weaponRange = Mathf.Max(weaponRange, 2);

        Vector3 HexEndWorld(int col, int row) =>
            grid.GetCellWorldPosition(col, row) + Vector3.up * _bulletHexEndYOffset;

        int finalHexCol = tc;
        int finalHexRow = tr;

        Vector3 finalShotEndWorld = HexEndWorld(finalHexCol, finalHexRow);
        int distToFinalHex = HexGrid.GetDistance(fc, fr, finalHexCol, finalHexRow);

        int durationSteps = Mathf.Max(1, distToFinalHex);
        bool obstacleBeforeFinal = false;
        int obsCol = -1;
        int obsRow = -1;
        if (TryGetWallHitHexFromMapUpdates(result, action.tick, fc, fr, finalHexCol, finalHexRow, out obsCol, out obsRow)
            && grid.IsInBounds(obsCol, obsRow))
        {
            int dObs = HexGrid.GetDistance(fc, fr, obsCol, obsRow);
            if (dObs < distToFinalHex)
            {
                obstacleBeforeFinal = true;
                durationSteps = Mathf.Max(1, dObs);
            }
        }

        Vector3 shotHorizontalDir = finalShotEndWorld - grid.GetCellWorldPosition(fc, fr);
        shotHorizontalDir.y = 0f;
        if (shotHorizontalDir.sqrMagnitude < 1e-8f)
            yield break;
        shotHorizontalDir.Normalize();

        PlayerCharacterAnimator faceA = null;
        RemoteBattleUnitView faceR = null;
        if (TryBeginFacingForRangedShot(result, action.unitId, shotHorizontalDir, out faceA, out faceR))
        {
            Transform pivot = faceA != null ? faceA.FacingPivot : faceR.transform;
            Quaternion targetRot = Quaternion.LookRotation(shotHorizontalDir, Vector3.up);
            yield return CoFaceUntilAligned(pivot, targetRot);
        }

        if (!TryGetRangedBulletStartWorld(result, action.unitId, grid, fc, fr, out Vector3 start))
        {
            ClearFacingForRangedShot(faceA, faceR);
            yield break;
        }

        Vector3 toFinal = finalShotEndWorld - start;
        float fullPathLen = toFinal.magnitude;
        if (fullPathLen < 1e-5f)
        {
            ClearFacingForRangedShot(faceA, faceR);
            yield break;
        }

        Vector3 fireDir = toFinal / fullPathLen;

        float pathLen = fullPathLen;
        if (obstacleBeforeFinal)
        {
            Vector3 pObs = HexEndWorld(obsCol, obsRow);
            float u = Vector3.Dot(pObs - start, fireDir);
            u = Mathf.Clamp(u, 0f, fullPathLen);
            pathLen = Mathf.Min(fullPathLen, u);
        }

        if (pathLen < 1e-5f)
        {
            ClearFacingForRangedShot(faceA, faceR);
            yield break;
        }

        HexCubeOffset.GetHexLine(fc, fr, finalHexCol, finalHexRow, _bulletLineHexBuffer);
        float oneHexWorld = grid.HexSize * Mathf.Sqrt(3f);
        if (_bulletLineHexBuffer.Count >= 2)
        {
            var h0 = _bulletLineHexBuffer[0];
            var h1 = _bulletLineHexBuffer[1];
            Vector3 w0 = grid.GetCellWorldPosition(h0.col, h0.row) + Vector3.up * _bulletHexEndYOffset;
            Vector3 w1 = grid.GetCellWorldPosition(h1.col, h1.row) + Vector3.up * _bulletHexEndYOffset;
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
            if (line == null)
            {
                ClearFacingForRangedShot(faceA, faceR);
                yield break;
            }

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

        if (line != null)
        {
            Vector3 head = start + fireDir * pathLen;
            Vector3 tail = head - fireDir * segmentLen;
            if (Vector3.Dot(tail - start, fireDir) < 0f)
                tail = start;
            line.SetPosition(0, tail);
            line.SetPosition(1, head);
            ClearFacingForRangedShot(faceA, faceR);
            UnityEngine.Object.Destroy(bulletGo);
        }
        else
            ClearFacingForRangedShot(faceA, faceR);
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

    private static bool TryGetWeaponCodeForUnit(TurnResultPayload result, string unitId, out string weaponCode)
    {
        weaponCode = "";
        if (string.IsNullOrEmpty(unitId) || result?.results == null)
            return false;

        foreach (var item in result.results)
        {
            if (item == null || item.unitId != unitId)
                continue;
            weaponCode = string.IsNullOrWhiteSpace(item.weaponCode) ? WeaponCatalog.DefaultWeaponCode : item.weaponCode.Trim().ToLowerInvariant();
            return true;
        }

        return false;
    }

    /// <summary>Melee cold-weapon swing when range ≤ 1 and target unit is set; ranged attacks still use <see cref="PlayRangedBulletAnimation"/>.</summary>
    private IEnumerator PlayColdMeleeAttackIfApplicable(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null || !action.succeeded || string.IsNullOrEmpty(action.targetUnitId))
            yield break;

        if (!TryGetWeaponCodeForUnit(result, action.unitId, out string wCode) || !WeaponCatalog.IsColdWeapon(wCode))
            yield break;

        if (!TryGetWeaponRangeForUnit(result, action.unitId, out int wRange) || wRange > 1)
            yield break;

        HexGrid grid = CachedHexGrid;
        if (grid == null)
            yield break;

        if (!TryGetAttackHexEndpointsForPlayback(result, action, out int fc, out int fr, out int tc, out int tr))
            yield break;

        if (HexGrid.GetDistance(fc, fr, tc, tr) > 1)
            yield break;

        Vector3 atkPos = grid.GetCellWorldPosition(fc, fr);
        Vector3 tgtPos = grid.GetCellWorldPosition(tc, tr);
        Vector3 dir = tgtPos - atkPos;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-8f)
            yield break;
        dir.Normalize();

        PlayerCharacterAnimator faceA = null;
        RemoteBattleUnitView faceR = null;
        if (TryBeginFacingForRangedShot(result, action.unitId, dir, out faceA, out faceR))
        {
            Transform pivot = faceA != null ? faceA.FacingPivot : faceR.transform;
            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            yield return CoFaceUntilAligned(pivot, targetRot);
        }

        PlayerCharacterAnimator meleeAnim = null;
        if (TryResolveAnimatedUnit(result, action.unitId, out object unit, out bool isLocal))
        {
            if (isLocal && unit is Player pl)
                meleeAnim = pl.GetComponentInChildren<PlayerCharacterAnimator>();
            else if (unit is RemoteBattleUnitView r)
                meleeAnim = r.GetComponentInChildren<PlayerCharacterAnimator>();
        }

        if (meleeAnim != null)
            yield return StartCoroutine(meleeAnim.RunColdMeleeAttackRoutine(action.bodyPart));

        ClearFacingForRangedShot(faceA, faceR);
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

    private IEnumerator PlayMoveStepAction(
        TurnResultPayload result,
        BattleExecutedAction action,
        List<ExecutedActionPlaybackEntry> playback,
        int playbackIndex)
    {
        if (action?.toPosition == null)
            yield break;

        if (!TryResolveAnimatedUnit(result, action.unitId, out var unit, out bool isLocal))
            yield break;

        HexPosition from = action.fromPosition ?? action.toPosition;
        HexPosition to = action.toPosition;
        var path = new[] { new HexPosition(from.col, from.row), new HexPosition(to.col, to.row) };

        bool prevMoveSameUnit = false;
        if (playbackIndex > 0)
        {
            BattleExecutedAction prev = playback[playbackIndex - 1].Action;
            if (prev != null && prev.succeeded
                && string.Equals(prev.actionType, "MoveStep", StringComparison.OrdinalIgnoreCase)
                && string.Equals(prev.unitId, action.unitId, StringComparison.Ordinal))
                prevMoveSameUnit = true;
        }

        bool nextMoveSameUnit = false;
        if (playbackIndex + 1 < playback.Count)
        {
            BattleExecutedAction next = playback[playbackIndex + 1].Action;
            if (next != null && next.succeeded
                && string.Equals(next.actionType, "MoveStep", StringComparison.OrdinalIgnoreCase)
                && string.Equals(next.unitId, action.unitId, StringComparison.Ordinal))
                nextMoveSameUnit = true;
        }

        bool resetHex = !prevMoveSameUnit;

        if (isLocal && unit is Player local)
            yield return local.PlayPathAnimation(path, driveCamera: false, resetHexWalkPhase: resetHex, clearMovementStateWhenDone: !nextMoveSameUnit);
        else if (!isLocal && unit is RemoteBattleUnitView remote)
            yield return remote.PlayPathAnimation(path);
    }

    /// <summary>Запускает анимацию пути для всех юнитов в одном кадре (параллельно по кадрам).</summary>
    private IEnumerator PlayAllTurnAnimationsParallel(List<(object unit, bool isLocal, HexPosition[] path)> jobs)
    {
        if (jobs == null || jobs.Count == 0)
            yield break;

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
        Player local = LocalPlayer;
        local?.SetRoundState(roundIndex, roundDeadlineUtcMs);
        if (!_isTurnHistoryReplayPlaying && _selectedTurnHistoryPointer < 0)
            FindFirstObjectByType<InventoryUI>()?.ReloadInventoryFromServer();
    }

    /// <summary>Применить старт боя: локальный и удалённые юниты на позициях с сервера.</summary>
    public void ApplyBattleStarted(BattleStartedPayload payload)
    {
        if (payload == null) return;
        CancelTurnReplayPlayback();
        _battleId = payload.battleId ?? _battleId;
        _playerId = payload.playerId ?? _playerId;
        _spectatorMode = BattleSessionState.IsSpectatorMode
            || string.Equals(_playerId, BattleSessionState.SpectatorPlayerId, StringComparison.Ordinal);
        _spectatedHumanIds.Clear();
        _spectatedHumanIndex = 0;

        foreach (var kv in _remoteUnits)
        {
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _remoteUnits.Clear();
        _lastProcessedTurnResultRound = -1;
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
        _localEliminationPending = false;
        _localEliminationEscape = false;
        LastBattleEndWasEscape = false;
        _localBattleTeamId = -1;
        HideBattleInspectUnitCard();

        long deadlineUtcMs = payload.roundDeadlineUtcMs;
        if (deadlineUtcMs <= 0)
        {
            float duration = payload.roundDuration > 0 ? payload.roundDuration : 100f;
            deadlineUtcMs = Player.BuildRoundDeadlineUtcMs(duration);
        }
        int startRound = payload.roundIndex >= 0 ? payload.roundIndex : 0;
        ApplyRoundState(startRound, deadlineUtcMs);

        Player local = LocalPlayer;
        HexGrid grid = CachedHexGrid;
        float moveDur = local != null ? local.MoveDurationPerHex : 0.2f;
        if (grid != null)
            grid.ClearAllZoneShrinkExclusions();
        ApplyBattleActiveBoundsFromPayload(payload, grid);
        ApplyObstacleMap(payload, grid);
        if (grid != null && FindFirstObjectByType<MapBorderEscapeRing>() == null)
        {
            var ringGo = new GameObject("MapBorderEscapeRing");
            ringGo.AddComponent<MapBorderEscapeRing>();
        }
        RefreshEscapeBorderRing();

        var spawnList = BuildSpawnListFromPayload(payload);
        if (spawnList == null || spawnList.Count == 0)
        {
            Debug.LogWarning("[GameSession] ApplyBattleStarted: no spawn data in payload; units will appear from first TurnResult over socket.");
            return;
        }

        if (payload.spawnPlayerIds != null && payload.spawnTeamIds != null
            && payload.spawnPlayerIds.Length == payload.spawnTeamIds.Length)
        {
            for (int ti = 0; ti < payload.spawnPlayerIds.Length; ti++)
            {
                if (payload.spawnPlayerIds[ti] == _playerId)
                {
                    _localBattleTeamId = payload.spawnTeamIds[ti];
                    break;
                }
            }
        }

        if (_spectatorMode)
            _localBattleTeamId = 0;

        foreach (var (pid, col, row) in spawnList)
        {
            bool isMob = !string.IsNullOrEmpty(pid)
                && (pid.StartsWith("mob:", System.StringComparison.OrdinalIgnoreCase)
                    || pid.StartsWith("MOB_", System.StringComparison.OrdinalIgnoreCase));
            int spawnIndex = FindSpawnIndex(payload, pid, col, row);
            int startAp = GetSpawnInt(payload.spawnCurrentAps, spawnIndex, isMob ? 15 : Player.DefaultCombatMaxAp);
            int maxHp = GetSpawnInt(payload.spawnMaxHps, spawnIndex, isMob ? 10 : Player.DefaultCombatMaxHp);
            int maxAp = GetSpawnInt(payload.spawnMaxAps, spawnIndex, isMob ? 15 : Player.DefaultCombatMaxAp);
            int currentHp = GetSpawnInt(payload.spawnCurrentHps, spawnIndex, maxHp);
            if (pid == _playerId)
            {
                if (local != null)
                {
                    local.SetHidden(false);
                    string posture = GetSpawnString(payload.spawnCurrentPostures, spawnIndex, MovementPostureUtility.WalkId);
                    local.SetMaxAp(maxAp);
                    local.SetHealth(currentHp, maxHp);
                    local.ApplyServerTurnResult(new HexPosition(col, row), new[] { new HexPosition(col, row) }, startAp, 0f, posture);
                    string wCode = GetSpawnString(payload.spawnWeaponCodes, spawnIndex, WeaponCatalog.DefaultWeaponCode);
                    int wDmg = GetSpawnInt(payload.spawnWeaponDamages, spawnIndex, 1);
                    int wDmgMin = (payload.spawnWeaponDamageMins != null && spawnIndex >= 0 && spawnIndex < payload.spawnWeaponDamageMins.Length)
                        ? payload.spawnWeaponDamageMins[spawnIndex]
                        : wDmg;
                    int wRng = GetSpawnInt(payload.spawnWeaponRanges, spawnIndex, 1);
                    int wAtk = GetSpawnInt(payload.spawnWeaponAttackApCosts, spawnIndex, 1);
                    string wCat = GetSpawnString(payload.spawnWeaponCategories, spawnIndex, "");
                    local.SetEquippedWeapon(wCode, wDmg, wRng, wAtk, wDmgMin, weaponCategory: wCat ?? "");
                    local.SetServerEscapeState(false);
                    int dispLevel = GetSpawnInt(payload.spawnLevels, spawnIndex, 1);
                    string dispName = GetSpawnString(payload.spawnDisplayNames, spawnIndex, "");
                    if (string.IsNullOrEmpty(dispName))
                        dispName = !string.IsNullOrEmpty(BattleSessionState.LastUsername) ? BattleSessionState.LastUsername : pid;
                    local.SetDisplayProfile(dispName, dispLevel);
                    local.SetInspectCombatStats(
                        GetSpawnInt(payload.spawnStrengths, spawnIndex, 0),
                        GetSpawnInt(payload.spawnAgilities, spawnIndex, 0),
                        GetSpawnInt(payload.spawnIntuitions, spawnIndex, 0),
                        GetSpawnInt(payload.spawnEndurances, spawnIndex, 0),
                        GetSpawnInt(payload.spawnAccuracies, spawnIndex, 0),
                        GetSpawnInt(payload.spawnIntellects, spawnIndex, 0));
                }
                int localTeamSnap = GetSpawnInt(payload.spawnTeamIds, spawnIndex, -1);
                _initialReplayState[pid] = new ReplayUnitSnapshot
                {
                    UnitId = pid,
                    UnitType = 0,
                    Col = col,
                    Row = row,
                    CurrentAp = startAp,
                    PenaltyFraction = 0f,
                    CurrentPosture = GetSpawnString(payload.spawnCurrentPostures, spawnIndex, MovementPostureUtility.WalkId),
                    IsLocal = true,
                    BattleTeamId = localTeamSnap
                };
                continue;
            }

            var go = new GameObject("Remote_" + pid);
            var rv = go.AddComponent<RemoteBattleUnitView>();
            int remoteTeam = GetSpawnInt(payload.spawnTeamIds, spawnIndex, -1);
            rv.Initialize(pid, grid, col, row, moveDur, _localBattleTeamId, remoteTeam);
            rv.SetHealth(currentHp, maxHp);
            int dispLevelR = GetSpawnInt(payload.spawnLevels, spawnIndex, 1);
            string dispNameR = GetSpawnString(payload.spawnDisplayNames, spawnIndex, "");
            if (string.IsNullOrEmpty(dispNameR))
                dispNameR = pid;
            rv.SetDisplayProfile(dispNameR, dispLevelR);
            rv.SetInspectCombatStats(
                GetSpawnInt(payload.spawnStrengths, spawnIndex, 0),
                GetSpawnInt(payload.spawnAgilities, spawnIndex, 0),
                GetSpawnInt(payload.spawnIntuitions, spawnIndex, 0),
                GetSpawnInt(payload.spawnEndurances, spawnIndex, 0),
                GetSpawnInt(payload.spawnAccuracies, spawnIndex, 0),
                GetSpawnInt(payload.spawnIntellects, spawnIndex, 0));
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
                IsLocal = false,
                BattleTeamId = remoteTeam
            };
        }

        if (_spectatorMode)
        {
            if (local != null)
            {
                local.SetHidden(true);
                local.SetTurnTimerPaused(true);
                local.ClearMovementFlag();
                // Keep the scene Player off the battlefield (no ghost hex / minimap confusion).
                local.transform.position = new Vector3(-10000f, -500f, -10000f);
            }
            RebuildSpectatedHumansFromRemotes();
            if (_spectatedHumanIds.Count > 0)
                _spectatedHumanIndex = UnityEngine.Random.Range(0, _spectatedHumanIds.Count);
            FocusCameraOnCurrentSpectated();
        }
        else
        {
            CachedHexGridCamera?.RefocusOnLocalPlayer();
            local?.ClearMovementFlag();
        }

        RefreshPvpOccupancyHexHighlights();
    }

    private void RebuildSpectatedHumansFromRemotes()
    {
        _spectatedHumanIds.Clear();
        foreach (var kv in _remoteUnits)
        {
            if (kv.Value != null && !kv.Value.IsMob)
                _spectatedHumanIds.Add(kv.Key);
        }
        _spectatedHumanIds.Sort(StringComparer.Ordinal);
    }

    private void PruneSpectatedHumanList()
    {
        if (!_spectatorMode)
            return;
        for (int i = _spectatedHumanIds.Count - 1; i >= 0; i--)
        {
            string id = _spectatedHumanIds[i];
            if (!_remoteUnits.TryGetValue(id, out RemoteBattleUnitView rv) || rv == null || rv.IsMob)
                _spectatedHumanIds.RemoveAt(i);
        }
        if (_spectatedHumanIds.Count == 0)
            _spectatedHumanIndex = 0;
        else
            _spectatedHumanIndex = Mathf.Clamp(_spectatedHumanIndex, 0, _spectatedHumanIds.Count - 1);
    }

    /// <summary>Cycle observed PvP human (spectator mode).</summary>
    public bool TrySpectateNextHuman()
    {
        if (!_spectatorMode)
            return false;
        PruneSpectatedHumanList();
        if (_spectatedHumanIds.Count == 0)
            return false;
        _spectatedHumanIndex = (_spectatedHumanIndex + 1) % _spectatedHumanIds.Count;
        FocusCameraOnCurrentSpectated();
        return true;
    }

    /// <summary>Cycle observed PvP human (spectator mode).</summary>
    public bool TrySpectatePrevHuman()
    {
        if (!_spectatorMode)
            return false;
        PruneSpectatedHumanList();
        if (_spectatedHumanIds.Count == 0)
            return false;
        _spectatedHumanIndex = (_spectatedHumanIndex - 1 + _spectatedHumanIds.Count) % _spectatedHumanIds.Count;
        FocusCameraOnCurrentSpectated();
        return true;
    }

    public string GetSpectatedHumanDisplayName()
    {
        if (!_spectatorMode)
            return "";
        PruneSpectatedHumanList();
        if (_spectatedHumanIds.Count == 0)
            return Loc.T("spectator.no_players");
        string id = _spectatedHumanIds[Mathf.Clamp(_spectatedHumanIndex, 0, _spectatedHumanIds.Count - 1)];
        return _remoteUnits.TryGetValue(id, out RemoteBattleUnitView rv) && rv != null ? rv.DisplayName : id;
    }

    /// <summary>Current spectated human for third-person camera (spectator only).</summary>
    public bool TryGetSpectatedHumanFollowTransform(out Transform followTransform)
    {
        followTransform = null;
        if (!_spectatorMode)
            return false;
        PruneSpectatedHumanList();
        if (_spectatedHumanIds.Count == 0)
            return false;
        string id = _spectatedHumanIds[Mathf.Clamp(_spectatedHumanIndex, 0, _spectatedHumanIds.Count - 1)];
        if (!_remoteUnits.TryGetValue(id, out RemoteBattleUnitView rv) || rv == null)
            return false;
        followTransform = rv.transform;
        return true;
    }

    public void FocusCameraOnCurrentSpectated()
    {
        if (!_spectatorMode)
            return;
        PruneSpectatedHumanList();
        if (_spectatedHumanIds.Count == 0)
            return;
        string id = _spectatedHumanIds[_spectatedHumanIndex];
        if (!_remoteUnits.TryGetValue(id, out RemoteBattleUnitView rv) || rv == null)
            return;

        HexGridCamera cam = CachedHexGridCamera;
        if (cam == null)
            return;

        Vector3 flatForward = rv.transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-6f)
            flatForward = Vector3.forward;
        else
            flatForward.Normalize();

        if (HexGridCamera.ThirdPersonFollowActive)
            cam.EnterThirdPersonFollowImmediate(rv.transform, flatForward);
        else
            cam.FocusOnWorldPosition(rv.transform.position);
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

        LogHitRollsFromTurnResult(targetTurn);
        var playback = BuildExecutedActionPlayback(targetTurn);
        var jobs = BuildTurnResultAnimationJobs(targetTurn, prepareForAnimation: true, deferLocomotionPosture: playback.Count > 0);
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

        Player local = LocalPlayer;
        local?.ForceStopMovement(exitThirdPersonCamera: false);
        foreach (var remote in _remoteUnits.Values)
            remote?.ForceStopMovement();

        _isTurnHistoryReplayPlaying = false;
    }

    private void CaptureLiveTurnDraft()
    {
        Player local = LocalPlayer;
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

        Player local = LocalPlayer;
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
                    local.QueueEquipWeaponAction(action.weaponCode, swapCost, atk, -1, -1, action.weaponCategory);
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
        }
        else
        {
            _activeReplayAnimationCoroutines.Clear();
            var jobs = fallbackJobs ?? new List<(object unit, bool isLocal, HexPosition[] path)>();
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

        SnapBattleUnitsToAuthoritativeHexPositions();
        if (result != null)
            ApplyZoneShrinkFromTurnResult(result);
    }

    private void ResetToInitialReplayState()
    {
        Player local = LocalPlayer;
        HexGrid grid = CachedHexGrid;
        float moveDur = local != null ? local.MoveDurationPerHex : 0.2f;
        if (grid == null)
            return;

        _localBattleTeamId = -1;
        foreach (var snap in _initialReplayState.Values)
        {
            if (snap != null && snap.IsLocal && snap.BattleTeamId >= 0)
            {
                _localBattleTeamId = snap.BattleTeamId;
                break;
            }
        }

        if (_spectatorMode && _localBattleTeamId < 0)
            _localBattleTeamId = 0;

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
                remote.Initialize(snapshot.UnitId, grid, snapshot.Col, snapshot.Row, moveDur, _localBattleTeamId, snapshot.BattleTeamId);
                _remoteUnits[snapshot.UnitId] = remote;
            }

            remote.ApplyServerTurnResult(
                new HexPosition(snapshot.Col, snapshot.Row),
                new[] { new HexPosition(snapshot.Col, snapshot.Row) },
                snapshot.CurrentAp,
                snapshot.PenaltyFraction,
                prepareForAnimation: false);
        }

        RefreshPvpOccupancyHexHighlights();
    }

    private void ApplyBattleActiveBoundsFromPayload(BattleStartedPayload p, HexGrid grid)
    {
        if (grid == null || p == null) return;
        bool sane = p.activeMaxCol >= p.activeMinCol && p.activeMaxRow >= p.activeMinRow
            && p.activeMaxCol < grid.Width && p.activeMaxRow < grid.Length;
        if (sane)
        {
            _battleActiveMinCol = p.activeMinCol;
            _battleActiveMaxCol = p.activeMaxCol;
            _battleActiveMinRow = p.activeMinRow;
            _battleActiveMaxRow = p.activeMaxRow;
        }
        else
        {
            _battleActiveMinCol = 0;
            _battleActiveMaxCol = Mathf.Max(0, grid.Width - 1);
            _battleActiveMinRow = 0;
            _battleActiveMaxRow = Mathf.Max(0, grid.Length - 1);
        }

        _battleActiveBoundsInitialized = true;
        RaiseActiveBattleZoneChanged();
    }

    private void ApplyActiveZoneBoundsFromTurnResult(TurnResultPayload result, HexGrid grid)
    {
        if (result == null || grid == null) return;
        bool sane = result.activeMaxCol >= result.activeMinCol && result.activeMaxRow >= result.activeMinRow
            && result.activeMaxCol < grid.Width && result.activeMaxRow < grid.Length;
        if (!sane) return;
        _battleActiveMinCol = result.activeMinCol;
        _battleActiveMaxCol = result.activeMaxCol;
        _battleActiveMinRow = result.activeMinRow;
        _battleActiveMaxRow = result.activeMaxRow;
        _battleActiveBoundsInitialized = true;
        RaiseActiveBattleZoneChanged();
    }

    /// <summary>After turn-resolve animations: snap units to server hex, then remove obstacles, play falling hex animation, sync active rectangle.</summary>
    private void SnapBattleUnitsToAuthoritativeHexPositions()
    {
        HexGrid grid = CachedHexGrid;
        if (grid == null)
            return;

        Player pl = LocalPlayer;
        if (pl != null && !pl.IsDead && !pl.IsHidden)
            pl.transform.position = grid.GetCellWorldPosition(pl.CurrentCol, pl.CurrentRow);

        foreach (RemoteBattleUnitView remote in _remoteUnits.Values)
        {
            if (remote == null || remote.CurrentHp <= 0)
                continue;
            remote.transform.position = grid.GetCellWorldPosition(remote.CurrentCol, remote.CurrentRow);
        }

        RefreshPvpOccupancyHexHighlights();
    }

    /// <summary>Remove obstacles, play falling hex animation, sync active rectangle from server.</summary>
    private void ApplyZoneShrinkFromTurnResult(TurnResultPayload result)
    {
        if (result == null) return;
        HexGrid grid = CachedHexGrid;
        if (grid == null) return;

        if (result.zoneShrinkCells != null)
        {
            foreach (HexPosition h in result.zoneShrinkCells)
            {
                if (h == null) continue;
                RemoveObstacleAtCell(grid, h.col, h.row);
                HexCell cell = grid.GetCell(h.col, h.row);
                if (cell != null)
                    cell.AnimateZoneShrinkFall(grid);
            }
        }

        ApplyActiveZoneBoundsFromTurnResult(result, grid);
        RefreshEscapeBorderRing();
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

        HexGrid grid = CachedHexGrid;
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

    private IEnumerator HandleLocalEliminationAfterMessage(bool escaped)
    {
        if (_battleFinished) yield break;
        CachedHexGrid?.ClearAllPvpOccupancyHighlights();
        _battleFinished = true;
        HideBattleInspectUnitCard();
        OnNetworkMessage?.Invoke(Loc.T("ui.battle_lost"));
        OnBattleFinished?.Invoke(false);
        _waitingForServerRoundResolve = false;
        var p = LocalPlayer;
        p?.SetTurnTimerPaused(true);
        // Stop timeline immediately to prevent further animations/moves after fatal tick.
        ApplyLocalEliminatedState(escaped, showMessage: false);
        yield break;
    }

    private void ApplyLocalEliminatedState(bool escaped, bool showMessage)
    {
        var local = LocalPlayer;
        if (local == null)
            return;

        local.ForceStopMovement();
        local.SetHidden(true);
        if (showMessage)
            OnNetworkMessage?.Invoke(Loc.T("ui.battle_lost"));
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
            yield return HandleLocalEliminationAfterMessage(escaped: false);
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

    private void RemoveFledRemoteUnitsFromTurnResult(TurnResultPayload result)
    {
        if (result?.results == null)
            return;
        foreach (var r in result.results)
        {
            if (r == null || !r.hasFled || string.IsNullOrEmpty(r.playerId))
                continue;
            if (r.playerId == _playerId)
                continue;
            if (_remoteUnits.TryGetValue(r.playerId, out var rem) && rem != null)
            {
                _remoteUnits.Remove(r.playerId);
                Destroy(rem.gameObject);
            }
        }
    }

    /// <summary>Legacy single-click flee entry removed — use double-click movement onto the orange ring; <see cref="ActionPointsUI.TryShowEscapeStepOntoBorderDialog"/> handles confirmation.</summary>
    public void TryOpenBattleEscapeConfirmFromMapClick()
    {
    }

    /// <summary>HTTP POST /api/battle/.../escape after the player confirms in <see cref="ActionPointsUI"/>.</summary>
    public IEnumerator CoPostBattleEscapeRequest(System.Action<string> onError)
    {
        if (_spectatorMode)
        {
            onError?.Invoke(Loc.T("escape.request_failed"));
            yield break;
        }
        if (_serverConnection == null)
            _serverConnection = FindFirstObjectByType<BattleServerConnection>();
        if (_serverConnection == null || !_serverConnection.IsInBattle)
        {
            onError?.Invoke(Loc.T("escape.request_failed"));
            yield break;
        }

        string url = $"{_serverConnection.ServerUrl.TrimEnd('/')}/api/battle/{HttpSimple.Escape(_battleId)}/escape?playerId={HttpSimple.Escape(_playerId)}";
        string err = null;
        yield return HttpSimple.PostJsonWithAuth(url, "{}", BattleSessionState.AccessToken, _ => { }, e => err = e);
        if (!string.IsNullOrEmpty(err))
        {
            onError?.Invoke(err);
            yield break;
        }

        OnNetworkMessage?.Invoke(Loc.T("battle.escape_started"));
    }

    private void HandleBattleFinishedFromServer(TurnResultPayload result)
    {
        if (_battleFinished) return;

        CachedHexGrid?.ClearAllPvpOccupancyHighlights();

        if (_spectatorMode)
        {
            _battleFinished = true;
            HideBattleInspectUnitCard();
            _waitingForServerRoundResolve = false;
            LocalPlayer?.SetTurnTimerPaused(true);
            _spectatorMode = false;
            BattleSessionState.ClearSpectatorMode();
            OnBattleFinished?.Invoke(true);
            OnNetworkMessage?.Invoke(Loc.T("ui.spectator_battle_ended"));
            return;
        }

        bool localDead = false;
        bool localFled = false;
        if (result != null && result.results != null)
        {
            foreach (var item in result.results)
            {
                if (item == null) continue;
                if (item.playerId == _playerId)
                {
                    localDead = item.isDead;
                    localFled = item.hasFled;
                    break;
                }
            }
        }

        if (localDead)
        {
            // Local elimination was already handled from TurnResult; avoid duplicate conflicting final message.
            return;
        }

        if (localFled || !localDead)
        {
            _battleFinished = true;
            HideBattleInspectUnitCard();
            _waitingForServerRoundResolve = false;
            var p = LocalPlayer;
            p?.SetTurnTimerPaused(true);
            LastBattleEndWasEscape = localFled;
            OnBattleFinished?.Invoke(true);
            OnNetworkMessage?.Invoke(localFled ? Loc.T("ui.battle_escaped") : Loc.T("ui.battle_won"));
        }
    }

    private void AppendServerTurnLogs(TurnResultPayload result)
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
                        string targetLabel = string.IsNullOrEmpty(action.targetUnitId)
                            ? Loc.T("combat.target_fallback")
                            : action.targetUnitId;
                        string bodyPartName = BodyPartIds.DisplayName(action.bodyPart);
                        OnNetworkMessage?.Invoke(Loc.Tf("combat.hit_log", action.unitId, bodyPartName, targetLabel, action.damage));
                        if (action.targetDied)
                            OnNetworkMessage?.Invoke(Loc.Tf("combat.target_died", targetLabel));
                    }
                }
            }
        }
    }

    /// <summary>Сервер присылает p и факт попадания в executedActions; пишем в Unity Console на русском.</summary>
    private static void LogHitRollsFromTurnResult(TurnResultPayload result)
    {
        if (result?.results == null)
            return;

        foreach (var turnResult in result.results)
        {
            if (turnResult?.executedActions == null)
                continue;

            foreach (var action in turnResult.executedActions)
            {
                if (action == null)
                    continue;
                if (!string.Equals(action.actionType, "Attack", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!action.hitProbability.HasValue || !action.hitSucceeded.HasValue)
                    continue;

                string targetLabel = string.IsNullOrEmpty(action.targetUnitId) ? "—" : action.targetUnitId;
                double p = action.hitProbability.Value;
                bool hit = action.hitSucceeded.Value;
                Debug.Log(
                    $"[Combat] Раунд {result.roundIndex}, тик {action.tick}: стрелок {action.unitId} → {targetLabel} — " +
                    $"вероятность попадания {p * 100.0:F1} %, попадание: {(hit ? "да" : "нет")}, урон {action.damage}");
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

        DamagePopupQueue queue = target.GetComponent<DamagePopupQueue>();
        if (queue == null)
            queue = target.gameObject.AddComponent<DamagePopupQueue>();
        queue.ShowDamage(action.damage);
    }

    private void ShowHealPopupForAction(TurnResultPayload result, BattleExecutedAction action)
    {
        if (action == null || !action.succeeded || action.healed <= 0 || string.IsNullOrEmpty(action.unitId))
            return;

        Transform target = ResolveDamagePopupTarget(result, action.unitId);
        if (target == null)
            return;

        DamagePopupQueue queue = target.GetComponent<DamagePopupQueue>();
        if (queue == null)
            queue = target.gameObject.AddComponent<DamagePopupQueue>();
        queue.ShowHeal(action.healed);
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
                Player local = LocalPlayer;
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
                weaponAttackApCost = action.weaponAttackApCost,
                previousMagazineRounds = action.previousMagazineRounds
            };
        }

        return new SubmitTurnPayload
        {
            battleId = _battleId,
            playerId = _playerId,
            roundIndex = roundIndex,
            currentMagazineRounds = LocalPlayer != null ? LocalPlayer.CurrentMagazineRounds : 0,
            actions = payloadActions
        };
    }
}
