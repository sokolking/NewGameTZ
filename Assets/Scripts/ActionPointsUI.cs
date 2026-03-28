using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// UI для отображения очков действия и кнопки \"Закончить ход\".
/// Онлайн: полноэкранный бар и блокировка UI сразу после «конец хода» до submitAck по сокету;
/// панель остаётся до roundResolved по WebSocket.
/// </summary>
public class ActionPointsUI : MonoBehaviour
{
    [SerializeField] private Player _player;
    [SerializeField] private Button _endTurnButton;
    [Header("Movement posture")]
    [SerializeField] private Button _walkButton;
    [SerializeField] private Button _runButton;
    [SerializeField] private Button _sitButton;
    [SerializeField] private Button _hideButton;
    [SerializeField] private Button _skipButton;
    [SerializeField] private Button _stepBackButton;
    [Tooltip("On: step-by-step movement animation while planning; off: teleport. Scene name: ToggleShowAnimation.")]
    [SerializeField] private Toggle _toggleShowAnimation;
    [SerializeField] private Image _walkBG;
    [SerializeField] private Image _runBG;
    [SerializeField] private Image _sitBG;
    [SerializeField] private Image _hideBG;
    [SerializeField] private Image _skipBG;
    [Header("Skip AP dialog")]
    [Tooltip("Root: UiHierarchyNames.SkipDialogPanel. Children: question, input, OK/Cancel (names in UiHierarchyNames).")]
    [SerializeField] private GameObject _skipDialogPanel;
    [SerializeField] private Text _skipDialogQuestionText;
    [Tooltip("TMP variant for the question (if not using UI.Text).")]
    [SerializeField] private TextMeshProUGUI _skipDialogQuestionTmpText;
    [SerializeField] private InputField _skipDialogInput;
    [Tooltip("TMP input field (if not using legacy InputField).")]
    [SerializeField] private TMP_InputField _skipDialogTmpInput;
    [SerializeField] private Button _skipDialogOkButton;
    [SerializeField] private Button _skipDialogCancelButton;
    [Tooltip("Default value placed in the field when the dialog opens.")]
    [SerializeField] private string _skipDialogInitialInput = "1";
    [Header("Turn log")]
    [SerializeField] private Text _loggerText;
    [SerializeField] private Button _loggerUpButton;
    [SerializeField] private Button _loggerDownButton;
    [SerializeField] private int _maxLogLines = 50;
    [SerializeField] private int _loggerVisibleLines = 6;

    [Header("Online (server)")]
    [SerializeField] private GameSession _gameSession;
    [SerializeField] private InventoryUI _inventoryUi;

    [Header("Round result wait")]
    [Tooltip("Full-screen panel: Image with Raycast Target, optional child Slider.")]
    [SerializeField] private GameObject _roundWaitPanel;
    [SerializeField] private Slider _roundWaitSlider;
    [Tooltip("Seconds for one fill cycle of the bar (indeterminate progress).")]
    [SerializeField] private float _roundWaitBarCycleSeconds = 1.25f;
    [Tooltip("Seconds before live turn ends to auto-submit draft while viewing history.")]
    [SerializeField] private float _historicalViewAutoSubmitLeadSeconds = 1f;

    [Header("Minimap")]
    [Tooltip("Minimap panel from Canvas.")]
    [SerializeField] private RectTransform _miniMapPanel;
    [Tooltip("Panel under minimap for time and AP.")]
    [SerializeField] private RectTransform _miniMapStatsPanel;
    [Tooltip("UI Text for timer under minimap.")]
    [SerializeField] private Text _miniMapTimeText;
    [Tooltip("UI Text for AP under minimap.")]
    [SerializeField] private Text _miniMapApText;
    [SerializeField] private Color _miniMapTimeNormalColor = Color.white;
    [SerializeField] private Color _miniMapTimeWarningColor = Color.red;
    [SerializeField] private float _miniMapTimeWarningThresholdSeconds = 5f;
    [Header("Turn tracking")]
    [SerializeField] private Text _turnTrackerText;
    [Tooltip("Use if the label is TextMeshPro instead of UI.Text.")]
    [SerializeField] private TextMeshProUGUI _turnTrackerTmpText;
    [SerializeField] private Button _turnTrackerPrevButton;
    [SerializeField] private Button _turnTrackerNextButton;
    private float _miniMapPadding = 4f;
    private float _miniMapMarkerSize = 7f;
    [SerializeField] private Color _miniMapPlayerColor = Color.white;
    [SerializeField] private Color _miniMapEnemyColor = Color.red;
    [SerializeField] private Color _miniMapMobColor = new Color(1f, 0.55f, 0.15f, 1f);
    [Tooltip("Color of the camera viewport rectangle on the minimap.")]
    [SerializeField] private Color _miniMapViewportRectColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Thickness of viewport rectangle edges (px).")]
    [SerializeField] private float _miniMapViewportRectThickness = 1f;
    [Header("Battle Start Intro")]
    [Tooltip("Show intro on first appearance of the battlefield (HexGrid).")]
    [SerializeField] private bool _playBattleBeginOnFieldAppear = true;
    [Tooltip("Intro text before auto-zoom.")]
    [SerializeField] private string _battleBeginText = "your battle begin";
    [Tooltip("Intro text duration (seconds).")]
    [SerializeField] private float _battleBeginTextDuration = 3f;
    [Tooltip("Zoom factor after intro text.")]
    [SerializeField] private float _battleBeginZoomFactor = 2f;
    [Tooltip("Smooth auto-zoom duration (seconds).")]
    [SerializeField] private float _battleBeginZoomDuration = 0.9f;
    [Tooltip("Optional UI Text; if unset, one is created at screen center.")]
    [SerializeField] private Text _battleBeginOverlayText;
    [Header("Battle end panel")]
    [SerializeField] private GameObject _battleEndPanel;
    [SerializeField] private Text _battleEndTitleText;
    [SerializeField] private Button _battleEndCloseButton;
    [SerializeField] private Button _battleEndMainMenuButton;

    private bool _endTurnInProgress;
    private bool _roundWaitVisible;
    private readonly List<string> _logLines = new();
    private int _loggerStartIndex;
    private readonly Dictionary<string, Image> _miniMapRemoteMarkers = new();
    private Image _miniMapLocalMarker;
    private RectTransform _miniMapViewportRect;
    private static Sprite _miniMapCircleSprite;
    private static Sprite _miniMapWhiteSprite;
    private bool _battleBeginSequencePlayed;
    private bool _battleBeginSequenceRunning;
    private MovementPosture _skipReturnPosture = MovementPosture.Walk;

    /// <summary>Кэш корня Front Content Maker — без повторного Resources.FindObjectsOfTypeAll каждый кадр.</summary>
    private Transform _cachedFrontContentMaker;

    /// <summary>Следующий разрешённый момент полного поиска Front Content (если ещё не найден).</summary>
    private float _nextFrontContentSearchUnscaledTime = float.NegativeInfinity;

    private int _lastFrontContentSearchFrame = -1;
    private int _frontContentSearchAttemptsInFrame;

    /// <summary>Fallback BindGlobalUiFallbacks уже выполнялся при отсутствии Front Content Maker.</summary>
    private bool _fallbackBindDoneWhenNoFrontContent;

    /// <summary>BindUiCallbacks уже выполнен — не вызывать каждый кадр (UnityEvent + RefreshLoggerView давали ~100+ KB GC/кадр).</summary>
    private bool _uiCallbacksBound;

    /// <summary>Кэш для миникарты (без FindFirstObjectByType каждый кадр).</summary>
    private HexGridCamera _cachedHexGridCamera;

    /// <summary>Используется только если <see cref="GameSession.LocalPlayer"/> ещё null.</summary>
    private HexGrid _cachedHexGridWhenNoLocalPlayer;

    private readonly List<RemoteBattleUnitView> _miniMapRemoteUnitsBuffer = new();
    private readonly HashSet<string> _miniMapActiveRemoteIds = new();
    private readonly List<string> _miniMapStaleRemoteIds = new();

    private bool _lastMiniMapTimerWaitMode;
    private int _lastMiniMapTimerCeilSeconds = -1;
    private bool _lastMiniMapTimeWarningState;
    private int _lastMiniMapAp = int.MinValue;
    private int _lastTurnTrackerDisplayedTurn = int.MinValue;

    /// <summary>Подавление рекурсии при подрезке текста в диалоге пропуска ОД.</summary>
    private bool _skipDialogInputClampSuppressed;

    public static bool IsModalDialogOpen { get; private set; }

    /// <summary>Ожидание раунда (RoundWaitPanel) — блокирует ввод по карте вместе с <see cref="GameplayMapInputBlock"/>.</summary>
    public static bool IsRoundWaitPanelVisible { get; private set; }

    private void Awake()
    {
        // Полная привязка UI — в Start (один раз), здесь только то, что нужно до Start для подписок.
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        if (_gameSession == null)
            _gameSession = FindFirstObjectByType<GameSession>();

        if (_roundWaitBarCycleSeconds <= 0.05f)
            _roundWaitBarCycleSeconds = 1.25f;

        if (_player != null)
            _player.OnMovedToCell += HandlePlayerMoved;
        if (_player != null)
            _player.OnMovementPostureChanged += HandleMovementPostureChanged;

        GameSession.OnNetworkMessage += AppendLog;
        GameSession.OnSubmitTurnDeliveredToServer += ShowRoundWaitAfterSubmitDelivered;
        GameSession.OnWebSocketRoundPushReceived += HideRoundWaitPanel;
        GameSession.OnServerRoundWaitCancelled += HideRoundWaitPanel;
        GameSession.OnBattleFinished += HandleBattleFinished;
        if (_roundWaitPanel != null) _roundWaitPanel.SetActive(false);
        if (_battleEndPanel != null) _battleEndPanel.SetActive(false);
        if (_skipDialogPanel != null) _skipDialogPanel.SetActive(false);
        IsModalDialogOpen = false;
        IsRoundWaitPanelVisible = false;

        EnsureUiBlockOverlaySync();
    }

    /// <summary>
    /// BlockOverlay + UiBlockOverlaySync часто не добавляют в сцену — без них оверлей не показывается.
    /// </summary>
    private void EnsureUiBlockOverlaySync()
    {
        if (!TryGetComponent<UiBlockOverlaySync>(out _))
            gameObject.AddComponent<UiBlockOverlaySync>();
    }

    private void Start()
    {
        AutoBindSceneReferences();
        EnsureMiniMap();
        if (_player != null)
        {
            _player.OnMovedToCell -= HandlePlayerMoved;
            _player.OnMovedToCell += HandlePlayerMoved;
            _player.OnMovementPostureChanged -= HandleMovementPostureChanged;
            _player.OnMovementPostureChanged += HandleMovementPostureChanged;
        }
        RefreshMovementButtons(_player != null ? _player.CurrentMovementPosture : MovementPosture.Walk, skipSelected: false);
        if (_player != null)
            AppendLog(Loc.Tf("battle_log.turn_started", _player.TurnCount + 1, _player.CurrentAp));
    }

    private void OnDestroy()
    {
        if (_turnTrackerPrevButton != null)
            _turnTrackerPrevButton.onClick.RemoveListener(OnTurnTrackerPrevClicked);
        if (_turnTrackerNextButton != null)
            _turnTrackerNextButton.onClick.RemoveListener(OnTurnTrackerNextClicked);
        if (_loggerUpButton != null)
            _loggerUpButton.onClick.RemoveListener(OnLoggerUpClicked);
        if (_loggerDownButton != null)
            _loggerDownButton.onClick.RemoveListener(OnLoggerDownClicked);
        if (_walkButton != null)
            _walkButton.onClick.RemoveListener(OnWalkClicked);
        if (_runButton != null)
            _runButton.onClick.RemoveListener(OnRunClicked);
        if (_sitButton != null)
            _sitButton.onClick.RemoveListener(OnSitClicked);
        if (_hideButton != null)
            _hideButton.onClick.RemoveListener(OnHideClicked);
        if (_skipButton != null)
            _skipButton.onClick.RemoveListener(OnSkipClicked);
        if (_stepBackButton != null)
            _stepBackButton.onClick.RemoveListener(OnStepBackClicked);
        if (_toggleShowAnimation != null)
            _toggleShowAnimation.onValueChanged.RemoveListener(OnPlanningMovementAnimationToggle);
        if (_skipDialogOkButton != null)
            _skipDialogOkButton.onClick.RemoveListener(OnSkipDialogOkClicked);
        if (_skipDialogCancelButton != null)
            _skipDialogCancelButton.onClick.RemoveListener(OnSkipDialogCancelClicked);
        if (_skipDialogInput != null)
        {
            _skipDialogInput.onValueChanged.RemoveListener(OnSkipDialogLegacyInputValueChanged);
            _skipDialogInput.onValidateInput = null;
        }
        if (_skipDialogTmpInput != null)
            _skipDialogTmpInput.onValueChanged.RemoveListener(OnSkipDialogTmpInputValueChanged);
        GameSession.OnNetworkMessage -= AppendLog;
        GameSession.OnSubmitTurnDeliveredToServer -= ShowRoundWaitAfterSubmitDelivered;
        GameSession.OnWebSocketRoundPushReceived -= HideRoundWaitPanel;
        GameSession.OnServerRoundWaitCancelled -= HideRoundWaitPanel;
        GameSession.OnBattleFinished -= HandleBattleFinished;
        if (_player != null)
            _player.OnMovedToCell -= HandlePlayerMoved;
        if (_player != null)
            _player.OnMovementPostureChanged -= HandleMovementPostureChanged;
        IsModalDialogOpen = false;
    }

    private void Update()
    {
        if (_roundWaitVisible && _roundWaitSlider != null && _roundWaitBarCycleSeconds > 0.05f)
        {
            float t = Mathf.Repeat(Time.unscaledTime / _roundWaitBarCycleSeconds, 1f);
            _roundWaitSlider.normalizedValue = t;
        }

        // Проверяем интро до раннего return, чтобы оно не пропускалось,
        // даже если _player ещё не был проставлен в момент старта кадра.
        TryRunBattleBeginSequence();

        if (_player == null) return;
        UpdateMiniMapStats(_player);
        UpdateTurnTrackerUi();
        UpdateMiniMap();

        bool isViewingHistory = _gameSession != null && (_gameSession.IsViewingHistoricalTurn || _gameSession.IsTurnHistoryReplayPlaying);
        if (isViewingHistory && !_roundWaitVisible && _gameSession != null && !_gameSession.IsWaitingForServerRoundResolve)
        {
            float leadTime = Mathf.Max(0.05f, _historicalViewAutoSubmitLeadSeconds);
            if (!_gameSession.IsBattleFinished && _player.TurnTimeLeft <= leadTime)
            {
                if (_gameSession.TryAutoSubmitTimedOutLiveTurn(animateResolvedRound: true))
                {
                    ShowRoundWaitPanel();
                    AppendLog(Loc.T("battle_log.auto_submit_live"));
                }
            }
        }

        if (Keyboard.current == null) return;

        if (_roundWaitVisible) return;
        if (_gameSession != null && _gameSession.IsBattleFinished) return;

        // 1–4: режимы передвижения (как кнопки ходьба/бег/присед/укрытие).
        if (!IsKeyboardInputFocusedOnTextField() && CanInteractWithMovementUi())
        {
            Keyboard kb = Keyboard.current;
            if (kb[Key.Digit1].wasPressedThisFrame)
                ChangeMovementPosture(MovementPosture.Walk);
            else if (kb[Key.Digit2].wasPressedThisFrame)
                ChangeMovementPosture(MovementPosture.Run);
            else if (kb[Key.Digit3].wasPressedThisFrame)
                ChangeMovementPosture(MovementPosture.Sit);
            else if (kb[Key.Digit4].wasPressedThisFrame)
                ChangeMovementPosture(MovementPosture.Hide);
        }

        if (Keyboard.current.dKey.wasPressedThisFrame)
            TryEndTurn(animate: false);
        else if (Keyboard.current.eKey.wasPressedThisFrame)
            TryEndTurn(animate: true);
        else if (Keyboard.current.rKey.wasPressedThisFrame)
            TryQueueReload();
        else if (Keyboard.current.hKey.wasPressedThisFrame)
            TryQueueUseItem();

        if (_player.TurnTimeExpired)
        {
            if (_gameSession != null && isViewingHistory)
            {
                if (_gameSession.TryAutoSubmitTimedOutLiveTurn(animateResolvedRound: true))
                {
                    ShowRoundWaitPanel();
                    AppendLog(Loc.T("battle_log.turn_time_expired"));
                }
            }
            else
                TryEndTurn(animate: true);
        }
    }

    private void TryRunBattleBeginSequence()
    {
        if (_battleBeginSequencePlayed || _battleBeginSequenceRunning)
            return;
        if (!_playBattleBeginOnFieldAppear)
            return;

        if (_gameSession == null)
            _gameSession = FindFirstObjectByType<GameSession>();

        if (_player == null)
            _player = _gameSession != null ? _gameSession.LocalPlayer : FindFirstObjectByType<Player>();
        if (_player == null)
            return;

        HexGrid grid = _player.Grid != null ? _player.Grid : FindFirstObjectByType<HexGrid>();
        if (grid == null || grid.transform.childCount == 0)
            return;

        StartCoroutine(BattleBeginSequenceCoroutine());
    }

    /// <summary>Интро боя: текст по центру → пауза → плавный зум камеры к игроку (<see cref="HexGridCamera.FocusAndZoomSmooth"/>).</summary>
    private System.Collections.IEnumerator BattleBeginSequenceCoroutine()
    {
        _battleBeginSequenceRunning = true;

        Text overlay = EnsureBattleBeginOverlayText();
        if (overlay != null)
        {
            overlay.text = _battleBeginText;
            overlay.gameObject.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, _battleBeginTextDuration));

        if (overlay != null)
            overlay.gameObject.SetActive(false);

        HexGridCamera camController = GetCachedHexGridCamera();
        if (camController != null && _player != null)
        {
            yield return camController.StartCoroutine(
                camController.FocusAndZoomSmooth(
                    _player.transform.position,
                    Mathf.Max(1f, _battleBeginZoomFactor),
                    Mathf.Max(0.05f, _battleBeginZoomDuration)));
        }

        _battleBeginSequencePlayed = true;
        _battleBeginSequenceRunning = false;
    }

    private Text EnsureBattleBeginOverlayText()
    {
        if (_battleBeginOverlayText != null)
            return _battleBeginOverlayText;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return null;

        GameObject go = new GameObject("BattleBeginOverlayText", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(canvas.transform, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(700f, 120f);

        Text txt = go.GetComponent<Text>();
        txt.alignment = TextAnchor.MiddleCenter;
        txt.fontSize = 48;
        txt.color = Color.white;
        txt.raycastTarget = false;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) txt.font = font;
        txt.text = _battleBeginText;
        txt.gameObject.SetActive(false);

        _battleBeginOverlayText = txt;
        return txt;
    }

    private void OnEndTurnClicked()
    {
        if (_player == null) return;
        TryEndTurn(animate: true);
    }

    private void OnWalkClicked()
    {
        ChangeMovementPosture(MovementPosture.Walk);
    }

    private void OnRunClicked()
    {
        ChangeMovementPosture(MovementPosture.Run);
    }

    private void OnSitClicked()
    {
        ChangeMovementPosture(MovementPosture.Sit);
    }

    private void OnHideClicked()
    {
        ChangeMovementPosture(MovementPosture.Hide);
    }

    private void OnSkipClicked()
    {
        AutoBindSceneReferences();

        if (!CanInteractWithMovementUi())
            return;

        if (_player == null)
            return;

        if (!HasSkipDialogUi())
        {
            AppendLog(Loc.Tf("battle_log.skip_dialog_misconfigured", UiHierarchyNames.SkipDialogPanel));
            return;
        }

        _skipReturnPosture = _player.CurrentMovementPosture;
        RefreshMovementButtons(_skipReturnPosture, skipSelected: true);
        SetSkipDialogInputText(_skipDialogInitialInput);
        ClampSkipDialogInputsToCurrentAp();
        _skipDialogPanel.SetActive(true);
        IsModalDialogOpen = true;
        ActivateSkipInputField();
    }

    private void OnSkipDialogOkClicked()
    {
        AutoBindSceneReferences();

        if (_player == null)
        {
            CloseSkipDialog(restoreSelection: true);
            return;
        }

        if (!int.TryParse(GetSkipDialogInputText(), out int skipCost) || skipCost <= 0)
        {
            AppendLog(Loc.T("battle_log.enter_positive_ap"));
            return;
        }

        if (!_player.QueueWaitAction(skipCost))
        {
            AppendLog(Loc.T("battle_log.not_enough_ap_skip"));
            return;
        }

        AppendLog(Loc.Tf("battle_log.skip_ap_cost", skipCost));
        CloseSkipDialog(restoreSelection: true);
    }

    private void OnSkipDialogCancelClicked()
    {
        CloseSkipDialog(restoreSelection: true);
    }

    private void OnStepBackClicked()
    {
        if (ActionPointsUI.IsModalDialogOpen)
            return;
        if (!CanInteractWithMovementUi() || _player == null)
            return;

        if (!_player.TryUndoLastQueuedAction(out string reason))
        {
            if (_player.TryClearMovementFlagOnStepBackAtFullAp())
                return;
            if (!string.IsNullOrEmpty(reason))
                AppendLog(reason);
            return;
        }

        RefreshMovementButtons(_player.CurrentMovementPosture, skipSelected: false);
    }

    private static void OnPlanningMovementAnimationToggle(bool showAnimation)
    {
        MovementPlanningVisualSettings.ShowMovementAnimation = showAnimation;
    }

    private void ChangeMovementPosture(MovementPosture posture)
    {
        if (!CanInteractWithMovementUi() || _player == null)
            return;

        if (!_player.QueuePostureChange(posture))
        {
            AppendLog(Loc.T("battle_log.not_enough_ap_posture"));
            return;
        }

        RefreshMovementButtons(_player.CurrentMovementPosture, skipSelected: false);
    }

    private void TryEndTurn(bool animate)
    {
        if (_player == null) return;
        if (_endTurnInProgress) return;
        if (_roundWaitVisible) return;
        if (_gameSession != null && _gameSession.IsBattleFinished) return;
        if (_gameSession != null && _gameSession.IsWaitingForServerRoundResolve) return;

        bool isViewingHistory = _gameSession != null && (_gameSession.IsViewingHistoricalTurn || _gameSession.IsTurnHistoryReplayPlaying);
        if (isViewingHistory)
        {
            _endTurnInProgress = true;
            if (_gameSession.TrySubmitCurrentLiveTurnDraft(animate))
            {
                ShowRoundWaitPanel();
                AppendLog(Loc.T("battle_log.submitting_turn"));
            }
            _endTurnInProgress = false;
            return;
        }

        if (_player != null && _player.IsMoving)
        {
            bool inViewThirdPerson = HexGridCamera.ThirdPersonFollowActive;
            _player.ForceStopMovement(exitThirdPersonCamera: !inViewThirdPerson);
        }

        if (_gameSession != null && _gameSession.IsBattleAnimationPlaying) return;

        _endTurnInProgress = true;

        if (animate)
            StartCoroutine(EndTurnAnimated());
        else
            EndTurnImmediate();
    }

    private void TryQueueReload()
    {
        if (_player == null || _roundWaitVisible || IsModalDialogOpen)
            return;
        if (_gameSession != null && (_gameSession.IsWaitingForServerRoundResolve || _gameSession.IsBattleFinished))
            return;
        if (_inventoryUi == null)
            _inventoryUi = FindFirstObjectByType<InventoryUI>();
        int reloadCost = _inventoryUi != null ? _inventoryUi.GetCurrentWeaponReloadApCost() : 1;
        if (!_player.QueueReloadAction(reloadCost))
            AppendLog(Loc.T("ui.not_enough_ap"));
    }

    private void TryQueueUseItem()
    {
        if (_player == null || _roundWaitVisible || IsModalDialogOpen)
            return;
        if (_gameSession != null && (_gameSession.IsWaitingForServerRoundResolve || _gameSession.IsBattleFinished))
            return;
        if (_inventoryUi == null)
            _inventoryUi = FindFirstObjectByType<InventoryUI>();
        if (_inventoryUi == null || !_inventoryUi.IsActiveItemMedicine())
            return;
        int useCost = _inventoryUi.GetCurrentActiveItemUseApCost();
        if (!_player.QueueUseItemAction(useCost))
            AppendLog(Loc.T("ui.not_enough_ap"));
    }

    private bool IsOnlineSubmitFlow =>
        _gameSession != null && _gameSession.IsOnlineMode && _gameSession.IsInBattleWithServer();

    private void EndTurnImmediate()
    {
        if (_player == null) return;

        if (IsOnlineSubmitFlow)
        {
            ShowRoundWaitPanel();
            _gameSession.SubmitTurnOnlineWithOptionalRangedFacing(
                _player.GetTurnActionsCopy(),
                _gameSession.ServerRoundIndexForSubmit,
                animateResolvedRound: false);
            AppendLog(Loc.T("battle_log.submitting_turn"));
            _endTurnInProgress = false;
            return;
        }

        // Оффлайн-режим без сервера больше не используется: одиночка тоже идёт через сервер.
        // Для совместимости оставляем только локальный лог без изменения состояния ОД/штрафов.
        AppendLog(Loc.T("battle_log.local_end_turn"));
        _endTurnInProgress = false;
    }

    private System.Collections.IEnumerator EndTurnAnimated()
    {
        if (_player == null) yield break;

        if (IsOnlineSubmitFlow)
        {
            ShowRoundWaitPanel();
            _gameSession.SubmitTurnOnlineWithOptionalRangedFacing(
                _player.GetTurnActionsCopy(),
                _gameSession.ServerRoundIndexForSubmit,
                animateResolvedRound: true);
            AppendLog(Loc.T("battle_log.submitting_turn"));
            _endTurnInProgress = false;
            yield break;
        }

        // Оффлайн-режим без сервера больше не используется: одиночка тоже идёт через сервер.
        AppendLog(Loc.T("battle_log.local_anim_end"));
        _endTurnInProgress = false;
    }

    private void ShowRoundWaitPanel()
    {
        if (_roundWaitPanel == null) return;
        _roundWaitPanel.SetActive(true);
        UiModalBackdropUtility.SendBackdropToBack(_roundWaitPanel.transform);
        _roundWaitVisible = true;
        IsRoundWaitPanelVisible = true;
        if (_endTurnButton != null) _endTurnButton.interactable = false;
    }

    private void HideRoundWaitPanel()
    {
        if (_roundWaitPanel != null) _roundWaitPanel.SetActive(false);
        _roundWaitVisible = false;
        IsRoundWaitPanelVisible = false;
        if (_endTurnButton != null) _endTurnButton.interactable = true;
    }

    private void ShowRoundWaitAfterSubmitDelivered()
    {
        // Панель уже показана при нажатии «конец хода»; здесь только подтверждение по сокету.
        if (!_roundWaitVisible)
            ShowRoundWaitPanel();
        AppendLog(Loc.T("battle_log.turn_accepted_waiting"));
    }

    private void OnTurnTrackerPrevClicked()
    {
        _gameSession?.TryStepViewedTurn(-1);
        UpdateTurnTrackerUi();
    }

    private void OnTurnTrackerNextClicked()
    {
        _gameSession?.TryStepViewedTurn(1);
        UpdateTurnTrackerUi();
    }

    private void HandlePlayerMoved(HexCell cell)
    {
        if (cell == null) return;
        string tag = $"{cell.ColLabel}{cell.RowLabel}";
        AppendLog(Loc.Tf("battle_log.player_to_hex", tag));
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        _logLines.Add(line);
        while (_logLines.Count > _maxLogLines)
            _logLines.RemoveAt(0);

        // При новом логе всегда прокручиваем к последним строкам.
        _loggerStartIndex = Mathf.Max(0, _logLines.Count - Mathf.Max(1, _loggerVisibleLines));
        RefreshLoggerView();
    }

    private void OnLoggerUpClicked()
    {
        _loggerStartIndex = Mathf.Max(0, _loggerStartIndex - 1);
        RefreshLoggerView();
    }

    private void OnLoggerDownClicked()
    {
        int visible = Mathf.Max(1, _loggerVisibleLines);
        int maxStart = Mathf.Max(0, _logLines.Count - visible);
        _loggerStartIndex = Mathf.Min(maxStart, _loggerStartIndex + 1);
        RefreshLoggerView();
    }

    private void RefreshLoggerView()
    {
        if (_loggerText == null) return;

        int visible = Mathf.Max(1, _loggerVisibleLines);
        int maxStart = Mathf.Max(0, _logLines.Count - visible);
        _loggerStartIndex = Mathf.Clamp(_loggerStartIndex, 0, maxStart);

        int count = Mathf.Min(visible, Mathf.Max(0, _logLines.Count - _loggerStartIndex));
        if (count <= 0)
        {
            _loggerText.text = string.Empty;
        }
        else
        {
            _loggerText.text = string.Join("\n", _logLines.GetRange(_loggerStartIndex, count));
        }

        if (_loggerUpButton != null)
            _loggerUpButton.interactable = _loggerStartIndex > 0;
        if (_loggerDownButton != null)
            _loggerDownButton.interactable = _loggerStartIndex < maxStart;
    }

    private void HandleMovementPostureChanged(MovementPosture posture)
    {
        if (!IsModalDialogOpen)
            RefreshMovementButtons(posture, skipSelected: false);
    }

    private bool CanInteractWithMovementUi()
    {
        if (IsModalDialogOpen)
            return false;
        if (_roundWaitVisible)
            return false;
        if (_gameSession != null && (_gameSession.IsWaitingForServerRoundResolve || _gameSession.IsBattleFinished))
            return false;
        return _player != null;
    }

    /// <summary>Чтобы цифры 1–4 не переключали позу во время ввода в поле (например, диалог пропуска ОД).</summary>
    private static bool IsKeyboardInputFocusedOnTextField()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
            return false;

        GameObject go = EventSystem.current.currentSelectedGameObject;
        // TryGetComponent — без внутреннего GetComponentNullErrorMessage при отсутствии компонента (профайлер).
        return go.TryGetComponent<InputField>(out _) || go.TryGetComponent<TMP_InputField>(out _);
    }

    private void RefreshMovementButtons(MovementPosture posture, bool skipSelected)
    {
        SetMovementBg(_walkBG, !skipSelected && posture == MovementPosture.Walk);
        SetMovementBg(_runBG, !skipSelected && posture == MovementPosture.Run);
        SetMovementBg(_sitBG, !skipSelected && posture == MovementPosture.Sit);
        SetMovementBg(_hideBG, !skipSelected && posture == MovementPosture.Hide);
        SetMovementBg(_skipBG, skipSelected);
    }

    private static void SetMovementBg(Image image, bool selected)
    {
        if (image == null)
            return;

        Color color = image.color;
        color.r = selected ? 1f : 0f;
        color.g = selected ? 1f : 0f;
        color.b = selected ? 1f : 0f;
        image.color = color;
    }

    private void CloseSkipDialog(bool restoreSelection)
    {
        if (_skipDialogPanel != null)
            _skipDialogPanel.SetActive(false);
        IsModalDialogOpen = false;
        if (restoreSelection)
        {
            MovementPosture posture = _player != null ? _player.CurrentMovementPosture : _skipReturnPosture;
            RefreshMovementButtons(posture, skipSelected: false);
        }
    }

    private bool HasSkipDialogUi()
    {
        if (_skipDialogPanel == null || _skipDialogOkButton == null || _skipDialogCancelButton == null)
            return false;
        return _skipDialogInput != null || _skipDialogTmpInput != null;
    }

    private string GetSkipDialogInputText()
    {
        if (_skipDialogInput != null)
        {
            string main = (_skipDialogInput.text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(main))
                return main;
            // В части сцен значение задаётся только на дочернем Placeholder (Legacy), а text остаётся пустым.
            return GetLegacyInputFieldPlaceholderText(_skipDialogInput);
        }

        if (_skipDialogTmpInput != null)
        {
            string main = (_skipDialogTmpInput.text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(main))
                return main;
            if (_skipDialogTmpInput.placeholder != null)
            {
                if (_skipDialogTmpInput.placeholder is TMP_Text tmpPh)
                    return (tmpPh.text ?? string.Empty).Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// На объекте SkipDialogInput часто два InputField: оболочка без Text Component и дочерний «InputField (Legacy)» с Text/Placeholder.
    /// Без textComponent ввод с клавиатуры не работает.
    /// </summary>
    private static InputField ResolveBestInputFieldUnderSkipDialogInput(Transform skipDialogInputRoot)
    {
        if (skipDialogInputRoot == null)
            return null;

        InputField[] fields = skipDialogInputRoot.GetComponentsInChildren<InputField>(true);
        foreach (InputField f in fields)
        {
            if (f != null && f.textComponent != null)
                return f;
        }

        return skipDialogInputRoot.GetComponent<InputField>();
    }

    private static string GetLegacyInputFieldPlaceholderText(InputField input)
    {
        if (input == null)
            return string.Empty;

        if (input.placeholder != null)
        {
            if (input.placeholder is Text t)
                return (t.text ?? string.Empty).Trim();
            Text onGraphic = input.placeholder.GetComponent<Text>();
            if (onGraphic != null)
                return (onGraphic.text ?? string.Empty).Trim();
        }

        // Если ссылка Placeholder в инспекторе не задана — ищем дочерний объект по имени.
        Transform ph = input.transform.Find("Placeholder");
        if (ph != null)
        {
            Text childText = ph.GetComponent<Text>();
            if (childText != null)
                return (childText.text ?? string.Empty).Trim();
        }

        return string.Empty;
    }

    private void SetSkipDialogInputText(string value)
    {
        string v = value ?? string.Empty;
        if (_skipDialogInput != null)
            SetSkipDialogLegacyTextWithoutNotify(v);
        if (_skipDialogTmpInput != null)
            SetSkipDialogTmpTextWithoutNotify(v);
    }

    private void SetSkipDialogLegacyTextWithoutNotify(string value)
    {
        if (_skipDialogInput == null)
            return;
        _skipDialogInputClampSuppressed = true;
        try
        {
            _skipDialogInput.SetTextWithoutNotify(value ?? string.Empty);
        }
        finally
        {
            _skipDialogInputClampSuppressed = false;
        }
    }

    private void SetSkipDialogTmpTextWithoutNotify(string value)
    {
        if (_skipDialogTmpInput == null)
            return;
        _skipDialogInputClampSuppressed = true;
        try
        {
            _skipDialogTmpInput.SetTextWithoutNotify(value ?? string.Empty);
        }
        finally
        {
            _skipDialogInputClampSuppressed = false;
        }
    }

    /// <summary>Только символы 0–9, подрезка до текущих ОД при вводе.</summary>
    private void ApplySkipDialogInputFieldRules()
    {
        if (_skipDialogInput != null)
        {
            _skipDialogInput.contentType = InputField.ContentType.IntegerNumber;
            _skipDialogInput.characterValidation = InputField.CharacterValidation.Integer;
            _skipDialogInput.lineType = InputField.LineType.SingleLine;
            _skipDialogInput.onValidateInput = ValidateSkipDialogDigitOnlyChar;

            _skipDialogInput.onValueChanged.RemoveListener(OnSkipDialogLegacyInputValueChanged);
            _skipDialogInput.onValueChanged.AddListener(OnSkipDialogLegacyInputValueChanged);
        }

        if (_skipDialogTmpInput != null)
        {
            _skipDialogTmpInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            _skipDialogTmpInput.characterValidation = TMP_InputField.CharacterValidation.Integer;
            _skipDialogTmpInput.lineType = TMP_InputField.LineType.SingleLine;

            _skipDialogTmpInput.onValueChanged.RemoveListener(OnSkipDialogTmpInputValueChanged);
            _skipDialogTmpInput.onValueChanged.AddListener(OnSkipDialogTmpInputValueChanged);
        }
    }

    private static char ValidateSkipDialogDigitOnlyChar(string text, int charIndex, char addedChar)
    {
        return char.IsDigit(addedChar) ? addedChar : '\0';
    }

    private void OnSkipDialogLegacyInputValueChanged(string _)
    {
        if (_skipDialogInputClampSuppressed)
            return;
        ClampSkipDialogInputsToCurrentAp();
    }

    private void OnSkipDialogTmpInputValueChanged(string _)
    {
        if (_skipDialogInputClampSuppressed)
            return;
        ClampSkipDialogInputsToCurrentAp();
    }

    /// <summary>Если введено число больше текущих ОД — подставляем текущие ОД.</summary>
    private void ClampSkipDialogInputsToCurrentAp()
    {
        if (_player == null)
            return;

        int maxAp = Mathf.Max(0, _player.CurrentAp);

        if (_skipDialogInput != null)
        {
            string t = (_skipDialogInput.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t))
                return;
            if (!int.TryParse(t, out int v))
                return;
            if (v > maxAp)
                SetSkipDialogLegacyTextWithoutNotify(maxAp.ToString());
        }

        if (_skipDialogTmpInput != null)
        {
            string t = (_skipDialogTmpInput.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t))
                return;
            if (!int.TryParse(t, out int v))
                return;
            if (v > maxAp)
                SetSkipDialogTmpTextWithoutNotify(maxAp.ToString());
        }
    }

    private void ActivateSkipInputField()
    {
        if (_skipDialogInput != null)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_skipDialogInput.gameObject);
            _skipDialogInput.Select();
            _skipDialogInput.ActivateInputField();
            return;
        }

        if (_skipDialogTmpInput != null)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_skipDialogTmpInput.gameObject);
            _skipDialogTmpInput.Select();
            _skipDialogTmpInput.ActivateInputField();
        }
    }

    private void EnsureMiniMap()
    {
        if (_miniMapPanel == null || _miniMapStatsPanel == null || _miniMapTimeText == null || _miniMapApText == null)
            return;

        if (_miniMapLocalMarker == null)
            _miniMapLocalMarker = CreateMiniMapMarker("LocalPlayerMarker", _miniMapPlayerColor);

        if (_miniMapViewportRect == null)
            _miniMapViewportRect = CreateMiniMapViewportRect();
    }

    private RectTransform CreateMiniMapViewportRect()
    {
        if (_miniMapPanel == null) return null;

        GameObject go = new GameObject("MiniMapViewportRect", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_miniMapPanel, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(50f, 50f);

        Image img = go.GetComponent<Image>();
        img.sprite = GetMiniMapWhiteSprite();
        img.color = Color.clear;
        img.raycastTarget = false;

        float thickness = Mathf.Max(1f, _miniMapViewportRectThickness);
        CreateMiniMapViewportTopBottomEdge(go.transform, "Top", _miniMapViewportRectColor, thickness, true);
        CreateMiniMapViewportTopBottomEdge(go.transform, "Bottom", _miniMapViewportRectColor, thickness, false);
        CreateMiniMapViewportLeftRightEdge(go.transform, "Left", _miniMapViewportRectColor, thickness, true);
        CreateMiniMapViewportLeftRightEdge(go.transform, "Right", _miniMapViewportRectColor, thickness, false);

        rt.SetAsLastSibling();
        return rt;
    }

    private static void CreateMiniMapViewportTopBottomEdge(
        Transform parent,
        string edgeName,
        Color color,
        float thickness,
        bool isTop)
    {
        GameObject edgeGo = new GameObject($"MiniMapViewport{edgeName}", typeof(RectTransform), typeof(Image));
        edgeGo.transform.SetParent(parent, false);
        RectTransform edgeRt = edgeGo.GetComponent<RectTransform>();
        edgeRt.anchorMin = new Vector2(0f, isTop ? 1f : 0f);
        edgeRt.anchorMax = new Vector2(1f, isTop ? 1f : 0f);
        edgeRt.pivot = new Vector2(0.5f, isTop ? 1f : 0f);
        edgeRt.anchoredPosition = Vector2.zero;
        // Вписываем верх/низ между левым и правым краем, чтобы не было "усиков" в углах.
        edgeRt.sizeDelta = new Vector2(-2f * thickness, thickness);

        Image edgeImg = edgeGo.GetComponent<Image>();
        edgeImg.sprite = GetMiniMapWhiteSprite();
        edgeImg.color = color;
        edgeImg.raycastTarget = false;
    }

    private static void CreateMiniMapViewportLeftRightEdge(
        Transform parent,
        string edgeName,
        Color color,
        float thickness,
        bool isLeft)
    {
        GameObject edgeGo = new GameObject($"MiniMapViewport{edgeName}", typeof(RectTransform), typeof(Image));
        edgeGo.transform.SetParent(parent, false);
        RectTransform edgeRt = edgeGo.GetComponent<RectTransform>();
        edgeRt.anchorMin = new Vector2(isLeft ? 0f : 1f, 0f);
        edgeRt.anchorMax = new Vector2(isLeft ? 0f : 1f, 1f);
        edgeRt.pivot = new Vector2(isLeft ? 0f : 1f, 0.5f);
        edgeRt.anchoredPosition = Vector2.zero;
        edgeRt.sizeDelta = new Vector2(thickness, 0f);

        Image edgeImg = edgeGo.GetComponent<Image>();
        edgeImg.sprite = GetMiniMapWhiteSprite();
        edgeImg.color = color;
        edgeImg.raycastTarget = false;
    }

    private static Sprite GetMiniMapWhiteSprite()
    {
        if (_miniMapWhiteSprite != null) return _miniMapWhiteSprite;
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _miniMapWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        return _miniMapWhiteSprite;
    }

    private static Sprite GetMiniMapCircleSprite()
    {
        if (_miniMapCircleSprite != null)
            return _miniMapCircleSprite;

        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f - 1f;
        float radiusSqr = radius * radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 delta = new Vector2(x, y) - center;
                bool inside = delta.sqrMagnitude <= radiusSqr;
                texture.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        }

        texture.Apply();
        _miniMapCircleSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        return _miniMapCircleSprite;
    }

    private Image CreateMiniMapMarker(string name, Color color)
    {
        if (_miniMapPanel == null)
            return null;

        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_miniMapPanel, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(_miniMapMarkerSize, _miniMapMarkerSize);

        Image image = go.GetComponent<Image>();
        image.sprite = GetMiniMapCircleSprite();
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void UpdateMiniMapStats(Player player)
    {
        EnsureMiniMap();
        if (_miniMapPanel == null || _miniMapStatsPanel == null)
            return;

        bool waitingForRound = _gameSession != null && _gameSession.IsWaitingForServerRoundResolve;
        int ceilSec = Mathf.Max(0, Mathf.CeilToInt(player.TurnTimeLeft));
        bool timerTextDirty = waitingForRound != _lastMiniMapTimerWaitMode
            || (!waitingForRound && ceilSec != _lastMiniMapTimerCeilSeconds);

        bool isWarning = player.TurnTimeLeft <= Mathf.Max(0f, _miniMapTimeWarningThresholdSeconds)
            && !waitingForRound;
        bool colorDirty = isWarning != _lastMiniMapTimeWarningState;

        if (_miniMapTimeText != null && (timerTextDirty || colorDirty))
        {
            if (timerTextDirty)
            {
                _miniMapTimeText.text = waitingForRound ? "--:--" : FormatRoundTime(player.TurnTimeLeft);
                _lastMiniMapTimerWaitMode = waitingForRound;
                _lastMiniMapTimerCeilSeconds = ceilSec;
            }

            _miniMapTimeText.color = isWarning ? _miniMapTimeWarningColor : _miniMapTimeNormalColor;
            _lastMiniMapTimeWarningState = isWarning;
        }

        int currentAp = player.CurrentAp;
        if (_miniMapApText != null && currentAp != _lastMiniMapAp)
        {
            _miniMapApText.text = Loc.Tf("ui.minimap_ap", currentAp);
            _lastMiniMapAp = currentAp;
        }
    }

    private void UpdateTurnTrackerUi()
    {
        int displayedTurn = 1;
        if (_gameSession != null)
            displayedTurn = Mathf.Max(1, _gameSession.DisplayedTurnNumber);
        else if (_player != null)
            displayedTurn = _player.TurnCount + 1;

        if (displayedTurn != _lastTurnTrackerDisplayedTurn)
        {
            string turnLabel = Loc.Tf("ui.turn_number", displayedTurn);
            if (_turnTrackerText != null)
                _turnTrackerText.text = turnLabel;
            if (_turnTrackerTmpText != null)
                _turnTrackerTmpText.text = turnLabel;
            _lastTurnTrackerDisplayedTurn = displayedTurn;
        }

        if (_turnTrackerPrevButton != null)
            _turnTrackerPrevButton.interactable = _gameSession != null && _gameSession.CanViewPreviousTurn;
        if (_turnTrackerNextButton != null)
            _turnTrackerNextButton.interactable = _gameSession != null && _gameSession.CanViewNextTurn;
    }

    private void UpdateMiniMap()
    {
        EnsureMiniMap();
        if (_miniMapPanel == null || _gameSession == null)
            return;

        Player local = _gameSession.LocalPlayer;

        HexGrid grid;
        if (local != null)
        {
            grid = local.Grid;
            _cachedHexGridWhenNoLocalPlayer = null;
        }
        else
        {
            if (_cachedHexGridWhenNoLocalPlayer == null)
                _cachedHexGridWhenNoLocalPlayer = FindFirstObjectByType<HexGrid>();
            grid = _cachedHexGridWhenNoLocalPlayer;
        }

        if (grid == null)
            return;

        HexGridCamera gridCamController = GetCachedHexGridCamera();
        Camera mainCam = null;
        if (gridCamController != null && gridCamController.TryGetComponent<Camera>(out Camera camOnController))
            mainCam = camOnController;
        if (mainCam == null)
            mainCam = Camera.main;
        if (mainCam == null)
            return;

        grid.GetGridBoundsWorld(out float mapMinX, out float mapMaxX, out float mapMinZ, out float mapMaxZ);
        float mapW = Mathf.Max(0.001f, mapMaxX - mapMinX);
        float mapH = Mathf.Max(0.001f, mapMaxZ - mapMinZ);
        float markerUsableWidth = Mathf.Max(1f, _miniMapPanel.rect.width - _miniMapPadding * 2f);
        float markerUsableHeight = Mathf.Max(1f, _miniMapPanel.rect.height - _miniMapPadding * 2f);
        float rectUsableWidth = Mathf.Max(1f, _miniMapPanel.rect.width);
        float rectUsableHeight = Mathf.Max(1f, _miniMapPanel.rect.height);

        // Прямоугольник видимости: текущий вид камеры в координатах всей карты
        if (_miniMapViewportRect != null && mainCam.orthographic)
        {
            float halfW = mainCam.orthographicSize * mainCam.aspect;
            float halfH = mainCam.orthographicSize;
            Vector3 camPos = mainCam.transform.position;
            float visMinX = camPos.x - halfW;
            float visMaxX = camPos.x + halfW;
            float visMinZ = camPos.z - halfH;
            float visMaxZ = camPos.z + halfH;

            float nMinX = Mathf.Clamp01((visMinX - mapMinX) / mapW);
            float nMaxX = Mathf.Clamp01((visMaxX - mapMinX) / mapW);
            float nMinY = Mathf.Clamp01((visMinZ - mapMinZ) / mapH);
            float nMaxY = Mathf.Clamp01((visMaxZ - mapMinZ) / mapH);

            float rectW = Mathf.Max(2f, (nMaxX - nMinX) * rectUsableWidth);
            float rectH = Mathf.Max(2f, (nMaxY - nMinY) * rectUsableHeight);
            float left = nMinX * rectUsableWidth;
            float bottom = nMinY * rectUsableHeight;

            _miniMapViewportRect.anchoredPosition = new Vector2(left, bottom);
            _miniMapViewportRect.sizeDelta = new Vector2(rectW, rectH);
            bool shouldShowRect = gridCamController != null
                ? gridCamController.IsZoomApplied
                : mainCam.orthographic;
            _miniMapViewportRect.gameObject.SetActive(shouldShowRect);
        }
        else if (_miniMapViewportRect != null)
            _miniMapViewportRect.gameObject.SetActive(false);

        // Маркеры в координатах всей карты (0–1 по карте → позиция на миникарте)
        if (_miniMapLocalMarker != null && local != null)
        {
            _miniMapLocalMarker.gameObject.SetActive(true);
            SetMiniMapMarkerPositionFullMap(_miniMapLocalMarker.rectTransform, local.transform.position,
                mapMinX, mapMaxX, mapMinZ, mapMaxZ, markerUsableWidth, markerUsableHeight);
        }

        _gameSession.CopyRemoteUnitsTo(_miniMapRemoteUnitsBuffer);
        _miniMapActiveRemoteIds.Clear();
        foreach (var remote in _miniMapRemoteUnitsBuffer)
        {
            if (remote == null || string.IsNullOrEmpty(remote.NetworkPlayerId))
                continue;

            _miniMapActiveRemoteIds.Add(remote.NetworkPlayerId);
            if (!_miniMapRemoteMarkers.TryGetValue(remote.NetworkPlayerId, out var marker) || marker == null)
            {
                Color color = remote.IsMob ? _miniMapMobColor : _miniMapEnemyColor;
                marker = CreateMiniMapMarker($"Marker_{remote.NetworkPlayerId}", color);
                _miniMapRemoteMarkers[remote.NetworkPlayerId] = marker;
            }

            if (marker != null)
            {
                marker.gameObject.SetActive(true);
                SetMiniMapMarkerPositionFullMap(marker.rectTransform, remote.transform.position,
                    mapMinX, mapMaxX, mapMinZ, mapMaxZ, markerUsableWidth, markerUsableHeight);
            }
        }

        _miniMapStaleRemoteIds.Clear();
        foreach (var kv in _miniMapRemoteMarkers)
        {
            if (_miniMapActiveRemoteIds.Contains(kv.Key))
                continue;
            if (kv.Value != null)
                Destroy(kv.Value.gameObject);
            _miniMapStaleRemoteIds.Add(kv.Key);
        }

        foreach (string id in _miniMapStaleRemoteIds)
            _miniMapRemoteMarkers.Remove(id);
    }

    private void SetMiniMapMarkerPositionFullMap(
        RectTransform marker,
        Vector3 worldPosition,
        float mapMinX, float mapMaxX, float mapMinZ, float mapMaxZ,
        float usableWidth, float usableHeight)
    {
        if (marker == null) return;

        float mapW = Mathf.Max(0.001f, mapMaxX - mapMinX);
        float mapH = Mathf.Max(0.001f, mapMaxZ - mapMinZ);
        float normX = Mathf.Clamp01((worldPosition.x - mapMinX) / mapW);
        float normY = Mathf.Clamp01((worldPosition.z - mapMinZ) / mapH);

        marker.anchoredPosition = new Vector2(
            _miniMapPadding + normX * usableWidth,
            _miniMapPadding + normY * usableHeight);
    }

    private HexGridCamera GetCachedHexGridCamera()
    {
        if (_cachedHexGridCamera == null)
            _cachedHexGridCamera = FindFirstObjectByType<HexGridCamera>();
        return _cachedHexGridCamera;
    }

    private static string FormatRoundTime(float secondsLeft)
    {
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(secondsLeft));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    /// <summary>
    /// Разрешает Transform Front Content Maker с кэшем и троттлингом дорогого FindNamedTransform (в т.ч. по всей сцене).
    /// </summary>
    /// <returns>false — в этом кадре поиск пропущен (троттлинг), вызывающий код не должен выполнять остальной AutoBind.</returns>
    private bool TryResolveFrontContentTransform(out Transform frontContent)
    {
        frontContent = _cachedFrontContentMaker;
        if (frontContent != null)
            return true;

        const float frontContentSearchInterval = 0.25f;

        int frame = Time.frameCount;
        if (frame != _lastFrontContentSearchFrame)
            _frontContentSearchAttemptsInFrame = 0;

        // Между кадрами — не чаще чем раз в frontContentSearchInterval; в одном кадре — до 2 поисков (Awake + Start).
        if (Time.unscaledTime < _nextFrontContentSearchUnscaledTime)
        {
            if (frame != _lastFrontContentSearchFrame || _frontContentSearchAttemptsInFrame >= 2)
                return false;
        }

        if (frame == _lastFrontContentSearchFrame && _frontContentSearchAttemptsInFrame >= 2)
            return false;

        _lastFrontContentSearchFrame = frame;
        _frontContentSearchAttemptsInFrame++;

        _cachedFrontContentMaker = FindNamedTransform(UiHierarchyNames.FrontContentMaker);
        frontContent = _cachedFrontContentMaker;
        if (_cachedFrontContentMaker == null)
            _nextFrontContentSearchUnscaledTime = Time.unscaledTime + frontContentSearchInterval;

        return true;
    }

    private void AutoBindSceneReferences()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        if (_gameSession == null)
            _gameSession = FindFirstObjectByType<GameSession>();

        if (!TryResolveFrontContentTransform(out Transform frontContent))
            return;

        if (frontContent == null)
        {
            if (!_fallbackBindDoneWhenNoFrontContent)
            {
                BindSkipDialogReferences(null);
                BindGlobalUiFallbacks();
                BindUiCallbacks();
                _fallbackBindDoneWhenNoFrontContent = true;
            }

            return;
        }

        _fallbackBindDoneWhenNoFrontContent = false;

        if (_miniMapPanel == null)
            _miniMapPanel = FindChildComponent<RectTransform>(frontContent, UiHierarchyNames.MiniMapPanel);
        if (_miniMapStatsPanel == null)
            _miniMapStatsPanel = FindChildComponent<RectTransform>(frontContent, UiHierarchyNames.MiniMapStatsPanel);
        if (_miniMapTimeText == null)
            _miniMapTimeText = FindChildComponent<Text>(frontContent, UiHierarchyNames.MiniMapTimeText);
        if (_miniMapApText == null)
            _miniMapApText = FindChildComponent<Text>(frontContent, UiHierarchyNames.MiniMapApText);
        BindTurnTrackerTextReferences(frontContent);
        BindSkipDialogReferences(frontContent);
        if (_turnTrackerPrevButton == null)
            _turnTrackerPrevButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.TurnTrackerPrevButton);
        if (_turnTrackerNextButton == null)
            _turnTrackerNextButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.TurnTrackerNextButton);
        if (_endTurnButton == null)
        {
            _endTurnButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.EndTurnButton)
                ?? FindChildComponent<Button>(frontContent, UiHierarchyNames.EndTurnButtonCompact);
        }
        if (_walkButton == null)
            _walkButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.WalkButton)
                ?? FindChildComponent<Button>(frontContent, UiHierarchyNames.WalkButtonPascal);
        if (_runButton == null)
            _runButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.RunButton)
                ?? FindChildComponent<Button>(frontContent, UiHierarchyNames.RunButtonPascal);
        if (_sitButton == null)
            _sitButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.SitButton)
                ?? FindChildComponent<Button>(frontContent, UiHierarchyNames.SitButtonPascal);
        if (_hideButton == null)
            _hideButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.HideButton)
                ?? FindChildComponent<Button>(frontContent, UiHierarchyNames.HideButtonPascal);
        if (_skipButton == null)
            _skipButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.SkipButton)
                ?? FindChildComponent<Button>(frontContent, UiHierarchyNames.SkipButtonPascal);
        if (_stepBackButton == null)
            _stepBackButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.StepBackButton)
                ?? FindChildComponent<Button>(frontContent, UiHierarchyNames.StepBackButtonCamel);
        if (_toggleShowAnimation == null)
            _toggleShowAnimation = FindChildComponent<Toggle>(frontContent, UiHierarchyNames.ToggleShowAnimation);
        if (_toggleShowAnimation == null)
        {
            Transform tAnim = FindNamedTransform(UiHierarchyNames.ToggleShowAnimation);
            if (tAnim != null)
                _toggleShowAnimation = tAnim.GetComponent<Toggle>();
        }
        if (_walkBG == null)
            _walkBG = FindChildComponent<Image>(frontContent, UiHierarchyNames.WalkBg)
                ?? FindChildComponent<Image>(frontContent, UiHierarchyNames.WalkBgPascal);
        if (_runBG == null)
            _runBG = FindChildComponent<Image>(frontContent, UiHierarchyNames.RunBg)
                ?? FindChildComponent<Image>(frontContent, UiHierarchyNames.RunBgPascal);
        if (_sitBG == null)
            _sitBG = FindChildComponent<Image>(frontContent, UiHierarchyNames.SitBg)
                ?? FindChildComponent<Image>(frontContent, UiHierarchyNames.SitBgPascal);
        if (_hideBG == null)
            _hideBG = FindChildComponent<Image>(frontContent, UiHierarchyNames.HideBg)
                ?? FindChildComponent<Image>(frontContent, UiHierarchyNames.HideBgPascal);
        if (_skipBG == null)
            _skipBG = FindChildComponent<Image>(frontContent, UiHierarchyNames.SkipBg)
                ?? FindChildComponent<Image>(frontContent, UiHierarchyNames.SkipBgPascal);
        if (_loggerText == null)
            _loggerText = FindChildComponent<Text>(frontContent, UiHierarchyNames.LoggerText);
        if (_loggerText == null)
            _loggerText = FindChildComponent<Text>(frontContent, UiHierarchyNames.LogTextLegacy);
        if (_loggerUpButton == null)
            _loggerUpButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.LoggerUp);
        if (_loggerUpButton == null)
            _loggerUpButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.LoggerUpButton);
        if (_loggerDownButton == null)
            _loggerDownButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.LoggerDown);
        if (_loggerDownButton == null)
            _loggerDownButton = FindChildComponent<Button>(frontContent, UiHierarchyNames.LoggerDownButton);

        // Глобальный fallback, если логгер находится вне Front Content Maker.
        if (_loggerText == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.LoggerText);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.LogTextLegacy);
            if (t != null) _loggerText = t.GetComponent<Text>();
        }
        if (_loggerUpButton == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.LoggerUp);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.LoggerUpButton);
            if (t != null) _loggerUpButton = t.GetComponent<Button>();
        }
        if (_loggerDownButton == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.LoggerDown);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.LoggerDownButton);
            if (t != null) _loggerDownButton = t.GetComponent<Button>();
        }

        // Дополнительный fallback: если компонент Button/Text висит на дочернем объекте внутри Logger*.
        if (_loggerText == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.LoggerText);
            if (t != null)
                _loggerText = t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
        }
        if (_loggerUpButton == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.LoggerUp);
            if (t != null)
                _loggerUpButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }
        if (_loggerDownButton == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.LoggerDown);
            if (t != null)
                _loggerDownButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }

        BindGlobalUiFallbacks();
        BindUiCallbacks();
    }

    /// <summary>
    /// Глобальный поиск по сцене (MainScene: кнопки могут быть не под Front Content Maker).
    /// </summary>
    private void BindGlobalUiFallbacks()
    {
        if (_skipButton == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.SkipButton);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.SkipButtonPascal);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.SkipGlobalNameShort);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.SkipGlobalNameBtn);
            if (t != null)
                _skipButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }

        if (_skipBG == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.SkipBg);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.SkipBgPascal);
            if (t != null)
                _skipBG = t.GetComponent<Image>();
        }

        if (_endTurnButton == null)
        {
            Transform t = FindNamedTransform(UiHierarchyNames.EndTurnButton);
            if (t == null) t = FindNamedTransform(UiHierarchyNames.EndTurnButtonCompact);
            if (t != null)
                _endTurnButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }
    }

    /// <summary>
    /// UI.Text ищется только на самом объекте; подпись часто лежит на дочернем «Text» или в TMP — подхватываем и то и другое.
    /// </summary>
    private void BindTurnTrackerTextReferences(Transform frontContent)
    {
        if (_turnTrackerText != null && _turnTrackerTmpText != null)
            return;

        Transform t = FindNamedTransform(UiHierarchyNames.TurnTrackerText, frontContent);
        if (t == null)
            t = FindNamedTransform(UiHierarchyNames.TurnTrackerText);
        if (t == null)
            return;

        if (_turnTrackerText == null)
            _turnTrackerText = t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
        if (_turnTrackerTmpText == null)
            _turnTrackerTmpText = t.GetComponent<TextMeshProUGUI>() ?? t.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private Transform FindSkipDialogNamedTransform(string objectName, Transform frontContent, Transform skipPanelTransform)
    {
        if (skipPanelTransform != null)
        {
            Transform t = FindNamedTransform(objectName, skipPanelTransform);
            if (t != null)
                return t;
        }

        if (frontContent != null)
        {
            Transform t = FindNamedTransform(objectName, frontContent);
            if (t != null)
                return t;
        }

        return FindNamedTransform(objectName);
    }

    private void BindSkipDialogReferences(Transform frontContent)
    {
        if (_skipDialogPanel != null && _skipDialogOkButton != null && _skipDialogCancelButton != null)
            return;

        // Корень должен называться SkipDialogPanel (полноэкранный Canvas). Частая ошибка в инспекторе —
        // ссылка на дочерний SkipDialogDialog: тогда SetActive не включает родителя и диалог не в иерархии.
        Transform skipRoot = _skipDialogPanel == null
            ? FindNamedTransform(UiHierarchyNames.SkipDialogPanel)
            : _skipDialogPanel.transform;
        if (skipRoot != null)
            _skipDialogPanel = skipRoot.gameObject;
        else if (_skipDialogPanel != null && _skipDialogPanel.name != UiHierarchyNames.SkipDialogPanel)
        {
            Transform t = _skipDialogPanel.transform;
            while (t != null)
            {
                if (t.name == UiHierarchyNames.SkipDialogPanel)
                {
                    _skipDialogPanel = t.gameObject;
                    break;
                }
                t = t.parent;
            }
        }
        else if (_skipDialogPanel == null)
        {
            Transform t = frontContent != null ? FindNamedTransform(UiHierarchyNames.SkipDialogPanel, frontContent) : null;
            if (t != null)
                _skipDialogPanel = t.gameObject;
        }

        Transform skipPanelTransform = _skipDialogPanel != null ? _skipDialogPanel.transform : null;

        if (_skipDialogQuestionText == null)
        {
            Transform t = FindSkipDialogNamedTransform(UiHierarchyNames.SkipDialogQuestionText, frontContent, skipPanelTransform);
            if (t != null)
                _skipDialogQuestionText = t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
        }

        if (_skipDialogQuestionTmpText == null)
        {
            Transform t = FindSkipDialogNamedTransform(UiHierarchyNames.SkipDialogQuestionText, frontContent, skipPanelTransform);
            if (t != null)
                _skipDialogQuestionTmpText = t.GetComponent<TextMeshProUGUI>() ?? t.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        Transform tSkipIn = FindSkipDialogNamedTransform(UiHierarchyNames.SkipDialogInput, frontContent, skipPanelTransform);
        if (tSkipIn != null)
        {
            InputField resolved = ResolveBestInputFieldUnderSkipDialogInput(tSkipIn);
            if (resolved != null && (_skipDialogInput == null || _skipDialogInput.textComponent == null))
                _skipDialogInput = resolved;
        }

        if (_skipDialogTmpInput == null)
        {
            Transform t = FindSkipDialogNamedTransform(UiHierarchyNames.SkipDialogInput, frontContent, skipPanelTransform);
            if (t != null)
                _skipDialogTmpInput = t.GetComponent<TMP_InputField>() ?? t.GetComponentInChildren<TMP_InputField>(true);
        }

        if (_skipDialogOkButton == null)
        {
            Transform t = FindSkipDialogNamedTransform(UiHierarchyNames.SkipDialogOkButton, frontContent, skipPanelTransform);
            if (t != null)
                _skipDialogOkButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }

        if (_skipDialogCancelButton == null)
        {
            Transform t = FindSkipDialogNamedTransform(UiHierarchyNames.SkipDialogCancelButton, frontContent, skipPanelTransform);
            if (t != null)
                _skipDialogCancelButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }
    }

    private void BindUiCallbacks()
    {
        if (_uiCallbacksBound)
            return;

        if (_endTurnButton != null)
        {
            _endTurnButton.onClick.RemoveListener(OnEndTurnClicked);
            _endTurnButton.onClick.AddListener(OnEndTurnClicked);
        }

        if (_walkButton != null)
        {
            _walkButton.onClick.RemoveListener(OnWalkClicked);
            _walkButton.onClick.AddListener(OnWalkClicked);
        }
        if (_runButton != null)
        {
            _runButton.onClick.RemoveListener(OnRunClicked);
            _runButton.onClick.AddListener(OnRunClicked);
        }
        if (_sitButton != null)
        {
            _sitButton.onClick.RemoveListener(OnSitClicked);
            _sitButton.onClick.AddListener(OnSitClicked);
        }
        if (_hideButton != null)
        {
            _hideButton.onClick.RemoveListener(OnHideClicked);
            _hideButton.onClick.AddListener(OnHideClicked);
        }
        if (_skipButton != null)
        {
            _skipButton.onClick.RemoveListener(OnSkipClicked);
            _skipButton.onClick.AddListener(OnSkipClicked);
        }

        if (_stepBackButton != null)
        {
            _stepBackButton.onClick.RemoveListener(OnStepBackClicked);
            _stepBackButton.onClick.AddListener(OnStepBackClicked);
        }

        if (_turnTrackerPrevButton != null)
        {
            _turnTrackerPrevButton.onClick.RemoveListener(OnTurnTrackerPrevClicked);
            _turnTrackerPrevButton.onClick.AddListener(OnTurnTrackerPrevClicked);
        }

        if (_turnTrackerNextButton != null)
        {
            _turnTrackerNextButton.onClick.RemoveListener(OnTurnTrackerNextClicked);
            _turnTrackerNextButton.onClick.AddListener(OnTurnTrackerNextClicked);
        }

        if (_loggerUpButton != null)
        {
            _loggerUpButton.onClick.RemoveListener(OnLoggerUpClicked);
            _loggerUpButton.onClick.AddListener(OnLoggerUpClicked);
        }

        if (_loggerDownButton != null)
        {
            _loggerDownButton.onClick.RemoveListener(OnLoggerDownClicked);
            _loggerDownButton.onClick.AddListener(OnLoggerDownClicked);
        }

        if (_battleEndCloseButton != null)
        {
            _battleEndCloseButton.onClick.RemoveListener(OnBattleEndCloseClicked);
            _battleEndCloseButton.onClick.AddListener(OnBattleEndCloseClicked);
        }

        if (_battleEndMainMenuButton != null)
        {
            _battleEndMainMenuButton.onClick.RemoveListener(OnBattleEndMainMenuClicked);
            _battleEndMainMenuButton.onClick.AddListener(OnBattleEndMainMenuClicked);
        }

        if (_skipDialogOkButton != null)
        {
            _skipDialogOkButton.onClick.RemoveListener(OnSkipDialogOkClicked);
            _skipDialogOkButton.onClick.AddListener(OnSkipDialogOkClicked);
        }
        if (_skipDialogCancelButton != null)
        {
            _skipDialogCancelButton.onClick.RemoveListener(OnSkipDialogCancelClicked);
            _skipDialogCancelButton.onClick.AddListener(OnSkipDialogCancelClicked);
        }

        if (_toggleShowAnimation != null)
        {
            _toggleShowAnimation.onValueChanged.RemoveListener(OnPlanningMovementAnimationToggle);
            _toggleShowAnimation.onValueChanged.AddListener(OnPlanningMovementAnimationToggle);
            MovementPlanningVisualSettings.ShowMovementAnimation = _toggleShowAnimation.isOn;
        }

        ApplySkipDialogInputFieldRules();

        RefreshLoggerView();
        // Повторяем привязку, пока не появятся основные кнопки хода (иначе UI подгрузился позже).
        _uiCallbacksBound = _endTurnButton != null || _walkButton != null;
    }

    private void HandleBattleFinished(bool victory)
    {
        EnsureBattleEndPanel();
        if (_battleEndTitleText != null)
            _battleEndTitleText.text = victory ? Loc.T("ui.battle_won") : Loc.T("ui.battle_lost");
        if (_battleEndPanel != null)
            _battleEndPanel.SetActive(true);
        HideRoundWaitPanel();
    }

    private void OnBattleEndCloseClicked()
    {
        if (_battleEndPanel != null)
            _battleEndPanel.SetActive(false);
    }

    private void OnBattleEndMainMenuClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }

    private void EnsureBattleEndPanel()
    {
        if (_battleEndPanel != null && _battleEndTitleText != null && _battleEndCloseButton != null && _battleEndMainMenuButton != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        if (_battleEndPanel == null)
        {
            _battleEndPanel = new GameObject("BattleEndPanel", typeof(RectTransform), typeof(Image));
            _battleEndPanel.transform.SetParent(canvas.transform, false);
            RectTransform panelRt = _battleEndPanel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(420f, 220f);
            Image bg = _battleEndPanel.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.86f);
        }

        if (_battleEndTitleText == null)
        {
            var go = new GameObject("BattleEndTitle", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_battleEndPanel.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.60f);
            rt.anchorMax = new Vector2(0.9f, 0.95f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _battleEndTitleText = go.GetComponent<Text>();
            _battleEndTitleText.alignment = TextAnchor.MiddleCenter;
            _battleEndTitleText.color = Color.white;
            _battleEndTitleText.fontSize = 36;
            _battleEndTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        if (_battleEndCloseButton == null)
            _battleEndCloseButton = CreateBattleEndButton("BattleEndCloseButton", Loc.T("ui.battle_end_close"), new Vector2(-90f, -70f), OnBattleEndCloseClicked);
        if (_battleEndMainMenuButton == null)
            _battleEndMainMenuButton = CreateBattleEndButton("BattleEndMainMenuButton", Loc.T("ui.battle_end_main_menu"), new Vector2(90f, -70f), OnBattleEndMainMenuClicked);
    }

    private Button CreateBattleEndButton(string name, string caption, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_battleEndPanel.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(160f, 44f);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        GameObject labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        Text txt = labelGo.GetComponent<Text>();
        txt.text = caption;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontSize = 20;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return btn;
    }

    private static T FindChildComponent<T>(Transform root, string childName) where T : Component
    {
        return UiHierarchyFind.FindChildComponent<T>(root, childName);
    }

    private static Transform FindNamedTransform(string objectName)
    {
        return UiHierarchyFind.FindNamedTransform(objectName);
    }

    private static Transform FindNamedTransform(string objectName, Transform root)
    {
        return UiHierarchyFind.FindNamedTransform(objectName, root);
    }
}
