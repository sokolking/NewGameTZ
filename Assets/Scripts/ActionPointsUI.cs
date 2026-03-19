using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// UI для отображения очков действия и кнопки \"Закончить ход\".
/// Онлайн: полноэкранный бар и блокировка UI сразу после «конец хода» до submitAck по сокету;
/// панель остаётся до roundResolved по WebSocket.
/// </summary>
public class ActionPointsUI : MonoBehaviour
{
    [SerializeField] private Player _player;
    [SerializeField] private Button _endTurnButton;
    [Header("Лог ходов")]
    [SerializeField] private Text _loggerText;
    [SerializeField] private Button _loggerUpButton;
    [SerializeField] private Button _loggerDownButton;
    [SerializeField] private int _maxLogLines = 50;
    [SerializeField] private int _loggerVisibleLines = 6;

    [Header("Онлайн (сервер)")]
    [SerializeField] private GameSession _gameSession;

    [Header("Ожидание результата раунда")]
    [Tooltip("Панель на весь экран: Image с Raycast Target, дочерний Slider (опционально).")]
    [SerializeField] private GameObject _roundWaitPanel;
    [SerializeField] private Slider _roundWaitSlider;
    [Tooltip("Секунды на один цикл заполнения бара (неопределённый прогресс).")]
    [SerializeField] private float _roundWaitBarCycleSeconds = 1.25f;
    [Tooltip("За сколько секунд до конца live-хода автосабмитить draft, пока открыт просмотр истории.")]
    [SerializeField] private float _historicalViewAutoSubmitLeadSeconds = 1f;

    [Header("Миникарта")]
    [Tooltip("Панель миникарты из Canvas.")]
    [SerializeField] private RectTransform _miniMapPanel;
    [Tooltip("Панель под миникартой для времени и ОД.")]
    [SerializeField] private RectTransform _miniMapStatsPanel;
    [Tooltip("UI Text для таймера под миникартой.")]
    [SerializeField] private Text _miniMapTimeText;
    [Tooltip("UI Text для ОД под миникартой.")]
    [SerializeField] private Text _miniMapApText;
    [SerializeField] private Color _miniMapTimeNormalColor = Color.white;
    [SerializeField] private Color _miniMapTimeWarningColor = Color.red;
    [SerializeField] private float _miniMapTimeWarningThresholdSeconds = 5f;
    [Header("Трекинг ходов")]
    [SerializeField] private Text _turnTrackerText;
    [SerializeField] private Button _turnTrackerPrevButton;
    [SerializeField] private Button _turnTrackerNextButton;
    private float _miniMapPadding = 4f;
    private float _miniMapMarkerSize = 7f;
    [SerializeField] private Color _miniMapPlayerColor = Color.white;
    [SerializeField] private Color _miniMapEnemyColor = Color.red;
    [SerializeField] private Color _miniMapMobColor = new Color(1f, 0.55f, 0.15f, 1f);
    [Tooltip("Цвет рамки прямоугольника видимости камеры на миникарте.")]
    [SerializeField] private Color _miniMapViewportRectColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Толщина сторон прямоугольника видимости (в px).")]
    [SerializeField] private float _miniMapViewportRectThickness = 1f;
    [Header("Battle Start Intro")]
    [Tooltip("Показывать интро при первом появлении игрового поля (HexGrid).")]
    [SerializeField] private bool _playBattleBeginOnFieldAppear = true;
    [Tooltip("Текст вступления перед авто-зумом.")]
    [SerializeField] private string _battleBeginText = "your battle begin";
    [Tooltip("Длительность показа вступительного текста (сек).")]
    [SerializeField] private float _battleBeginTextDuration = 3f;
    [Tooltip("Во сколько раз приблизить камеру после вступительного текста.")]
    [SerializeField] private float _battleBeginZoomFactor = 2f;
    [Tooltip("Длительность плавного авто-зума (сек).")]
    [SerializeField] private float _battleBeginZoomDuration = 0.9f;
    [Tooltip("Опциональный UI Text. Если не задан, создаётся автоматически по центру экрана.")]
    [SerializeField] private Text _battleBeginOverlayText;

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

    private void Awake()
    {
        AutoBindSceneReferences();
        BindUiCallbacks();

        if (_roundWaitBarCycleSeconds <= 0.05f)
            _roundWaitBarCycleSeconds = 1.25f;

        if (_player != null)
            _player.OnMovedToCell += HandlePlayerMoved;

        GameSession.OnNetworkMessage += AppendLog;
        GameSession.OnSubmitTurnDeliveredToServer += ShowRoundWaitAfterSubmitDelivered;
        GameSession.OnWebSocketRoundPushReceived += HideRoundWaitPanel;
        GameSession.OnServerRoundWaitCancelled += HideRoundWaitPanel;
        if (_roundWaitPanel != null) _roundWaitPanel.SetActive(false);
    }

    private void Start()
    {
        AutoBindSceneReferences();
        BindUiCallbacks();
        EnsureMiniMap();
        if (_player != null)
            AppendLog($"Ход {_player.TurnCount + 1} начат. ОД: {_player.CurrentAp}.");
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
        GameSession.OnNetworkMessage -= AppendLog;
        GameSession.OnSubmitTurnDeliveredToServer -= ShowRoundWaitAfterSubmitDelivered;
        GameSession.OnWebSocketRoundPushReceived -= HideRoundWaitPanel;
        GameSession.OnServerRoundWaitCancelled -= HideRoundWaitPanel;
        if (_player != null)
            _player.OnMovedToCell -= HandlePlayerMoved;
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
        string timerStr = (_gameSession != null && _gameSession.IsWaitingForServerRoundResolve)
            ? "--:--"
            : FormatRoundTime(_player.TurnTimeLeft);
        UpdateMiniMapStats(timerStr, _player.CurrentAp);
        UpdateTurnTrackerUi();
        UpdateMiniMap();

        bool isViewingHistory = _gameSession != null && (_gameSession.IsViewingHistoricalTurn || _gameSession.IsTurnHistoryReplayPlaying);
        if (isViewingHistory && !_roundWaitVisible && _gameSession != null && !_gameSession.IsWaitingForServerRoundResolve)
        {
            float leadTime = Mathf.Max(0.05f, _historicalViewAutoSubmitLeadSeconds);
            if (_player.TurnTimeLeft <= leadTime)
            {
                if (_gameSession.TryAutoSubmitTimedOutLiveTurn(animateResolvedRound: true))
                {
                    ShowRoundWaitPanel();
                    AppendLog("Автосабмит live хода");
                }
            }
        }

        if (Keyboard.current == null) return;

        if (_roundWaitVisible) return;

        if (Keyboard.current.dKey.wasPressedThisFrame)
            TryEndTurn(animate: false);
        else if (Keyboard.current.eKey.wasPressedThisFrame)
            TryEndTurn(animate: true);

        if (_player.TurnTimeExpired)
        {
            if (_gameSession != null && isViewingHistory)
            {
                if (_gameSession.TryAutoSubmitTimedOutLiveTurn(animateResolvedRound: true))
                {
                    ShowRoundWaitPanel();
                    AppendLog("Время хода истекло");
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

        HexGridCamera camController = FindFirstObjectByType<HexGridCamera>();
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

    private void TryEndTurn(bool animate)
    {
        if (_player == null) return;
        if (_endTurnInProgress) return;
        if (_roundWaitVisible) return;
        if (_gameSession != null && _gameSession.IsWaitingForServerRoundResolve) return;

        bool isViewingHistory = _gameSession != null && (_gameSession.IsViewingHistoricalTurn || _gameSession.IsTurnHistoryReplayPlaying);
        if (isViewingHistory)
        {
            _endTurnInProgress = true;
            if (_gameSession.TrySubmitCurrentLiveTurnDraft(animate))
            {
                ShowRoundWaitPanel();
                AppendLog("Отправка хода...");
            }
            _endTurnInProgress = false;
            return;
        }

        if (_player.IsMoving) return;
        if (_gameSession != null && _gameSession.IsBattleAnimationPlaying) return;

        _endTurnInProgress = true;

        if (animate)
            StartCoroutine(EndTurnAnimated());
        else
            EndTurnImmediate();
    }

    private bool IsOnlineSubmitFlow =>
        _gameSession != null && _gameSession.IsOnlineMode && _gameSession.IsInBattleWithServer();

    private void EndTurnImmediate()
    {
        if (_player == null) return;

        if (IsOnlineSubmitFlow)
        {
            _gameSession.BeginWaitingForServerRoundResolve(animateResolvedRound: false);
            ShowRoundWaitPanel();
            SubmitTurnIfOnline();
            AppendLog("Отправка хода...");
            _endTurnInProgress = false;
            return;
        }

        // Оффлайн-режим без сервера больше не используется: одиночка тоже идёт через сервер.
        // Для совместимости оставляем только локальный лог без изменения состояния ОД/штрафов.
        AppendLog("Локальный конец хода");
        _endTurnInProgress = false;
    }

    private System.Collections.IEnumerator EndTurnAnimated()
    {
        if (_player == null) yield break;

        if (IsOnlineSubmitFlow)
        {
            _gameSession.BeginWaitingForServerRoundResolve(animateResolvedRound: true);
            ShowRoundWaitPanel();
            SubmitTurnIfOnline();
            AppendLog("Отправка хода...");
            _endTurnInProgress = false;
            yield break;
        }

        // Оффлайн-режим без сервера больше не используется: одиночка тоже идёт через сервер.
        AppendLog("Локал. аним. конец");
        _endTurnInProgress = false;
    }

    private void ShowRoundWaitPanel()
    {
        if (_roundWaitPanel == null) return;
        _roundWaitPanel.SetActive(true);
        _roundWaitVisible = true;
        if (_endTurnButton != null) _endTurnButton.interactable = false;
    }

    private void HideRoundWaitPanel()
    {
        if (_roundWaitPanel != null) _roundWaitPanel.SetActive(false);
        _roundWaitVisible = false;
        if (_endTurnButton != null) _endTurnButton.interactable = true;
    }

    private void ShowRoundWaitAfterSubmitDelivered()
    {
        // Панель уже показана при нажатии «конец хода»; здесь только подтверждение по сокету.
        if (!_roundWaitVisible)
            ShowRoundWaitPanel();
        AppendLog("Ход принят, ждём раунд");
    }

    private void SubmitTurnIfOnline()
    {
        if (_player == null || _gameSession == null || !_gameSession.IsOnlineMode) return;
        if (!_gameSession.IsInBattleWithServer()) return;
        var path = _player.GetTurnPathCopy();
        _gameSession.SubmitTurnLocal(path, _player.ApSpentThisTurn, _player.StepsTakenThisTurn, _gameSession.ServerRoundIndexForSubmit);
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
        AppendLog($"Игрок -> {tag}");
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

    private void EnsureMiniMap()
    {
        AutoBindSceneReferences();
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

    private void UpdateMiniMapStats(string timerText, int currentAp)
    {
        EnsureMiniMap();
        if (_miniMapPanel == null || _miniMapStatsPanel == null)
            return;

        if (_miniMapTimeText != null)
        {
            _miniMapTimeText.text = timerText;
            bool isWarning = _player != null
                && _player.TurnTimeLeft <= Mathf.Max(0f, _miniMapTimeWarningThresholdSeconds)
                && (_gameSession == null || !_gameSession.IsWaitingForServerRoundResolve);
            _miniMapTimeText.color = isWarning ? _miniMapTimeWarningColor : _miniMapTimeNormalColor;
        }
        if (_miniMapApText != null)
            _miniMapApText.text = $"ОД {currentAp}";
    }

    private void UpdateTurnTrackerUi()
    {
        AutoBindSceneReferences();
        int displayedTurn = 1;
        if (_gameSession != null)
            displayedTurn = Mathf.Max(1, _gameSession.DisplayedTurnNumber);
        else if (_player != null)
            displayedTurn = _player.TurnCount + 1;

        if (_turnTrackerText != null)
            _turnTrackerText.text = $"Ход {displayedTurn}";

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
        HexGrid grid = local != null ? local.Grid : FindFirstObjectByType<HexGrid>();
        if (grid == null)
            return;

        HexGridCamera gridCamController = FindFirstObjectByType<HexGridCamera>();
        Camera mainCam = ResolveMiniMapCamera();
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

        List<RemoteBattleUnitView> remotes = _gameSession.GetRemoteUnitsSnapshot();
        var activeIds = new HashSet<string>();
        foreach (var remote in remotes)
        {
            if (remote == null || string.IsNullOrEmpty(remote.NetworkPlayerId))
                continue;

            activeIds.Add(remote.NetworkPlayerId);
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

        var staleIds = new List<string>();
        foreach (var kv in _miniMapRemoteMarkers)
        {
            if (activeIds.Contains(kv.Key))
                continue;
            if (kv.Value != null)
                Destroy(kv.Value.gameObject);
            staleIds.Add(kv.Key);
        }

        foreach (string id in staleIds)
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

    private Camera ResolveMiniMapCamera()
    {
        HexGridCamera gridCamera = FindFirstObjectByType<HexGridCamera>();
        if (gridCamera != null)
        {
            Camera cam = gridCamera.GetComponent<Camera>();
            if (cam != null)
                return cam;
        }
        return Camera.main;
    }


    private static string FormatRoundTime(float secondsLeft)
    {
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(secondsLeft));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private void AutoBindSceneReferences()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        if (_gameSession == null)
            _gameSession = FindFirstObjectByType<GameSession>();

        Transform frontContent = FindNamedTransform("Front Content Maker");
        if (frontContent == null)
            return;

        if (_miniMapPanel == null)
            _miniMapPanel = FindChildComponent<RectTransform>(frontContent, "MiniMapPanel");
        if (_miniMapStatsPanel == null)
            _miniMapStatsPanel = FindChildComponent<RectTransform>(frontContent, "MiniMapStatsPanel");
        if (_miniMapTimeText == null)
            _miniMapTimeText = FindChildComponent<Text>(frontContent, "MiniMapTimeText");
        if (_miniMapApText == null)
            _miniMapApText = FindChildComponent<Text>(frontContent, "MiniMapApText");
        if (_turnTrackerText == null)
            _turnTrackerText = FindChildComponent<Text>(frontContent, "TurnTrackerText");
        if (_turnTrackerPrevButton == null)
            _turnTrackerPrevButton = FindChildComponent<Button>(frontContent, "TurnTrackerPrevButton");
        if (_turnTrackerNextButton == null)
            _turnTrackerNextButton = FindChildComponent<Button>(frontContent, "TurnTrackerNextButton");
        if (_loggerText == null)
            _loggerText = FindChildComponent<Text>(frontContent, "LoggerText");
        if (_loggerText == null)
            _loggerText = FindChildComponent<Text>(frontContent, "LogText"); // fallback: старое имя
        if (_loggerUpButton == null)
            _loggerUpButton = FindChildComponent<Button>(frontContent, "LoggerUp");
        if (_loggerUpButton == null)
            _loggerUpButton = FindChildComponent<Button>(frontContent, "LoggerUpButton");
        if (_loggerDownButton == null)
            _loggerDownButton = FindChildComponent<Button>(frontContent, "LoggerDown");
        if (_loggerDownButton == null)
            _loggerDownButton = FindChildComponent<Button>(frontContent, "LoggerDownButton");

        // Глобальный fallback, если логгер находится вне Front Content Maker.
        if (_loggerText == null)
        {
            Transform t = FindNamedTransform("LoggerText");
            if (t == null) t = FindNamedTransform("LogText");
            if (t != null) _loggerText = t.GetComponent<Text>();
        }
        if (_loggerUpButton == null)
        {
            Transform t = FindNamedTransform("LoggerUp");
            if (t == null) t = FindNamedTransform("LoggerUpButton");
            if (t != null) _loggerUpButton = t.GetComponent<Button>();
        }
        if (_loggerDownButton == null)
        {
            Transform t = FindNamedTransform("LoggerDown");
            if (t == null) t = FindNamedTransform("LoggerDownButton");
            if (t != null) _loggerDownButton = t.GetComponent<Button>();
        }

        // Дополнительный fallback: если компонент Button/Text висит на дочернем объекте внутри Logger*.
        if (_loggerText == null)
        {
            Transform t = FindNamedTransform("LoggerText");
            if (t != null)
                _loggerText = t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
        }
        if (_loggerUpButton == null)
        {
            Transform t = FindNamedTransform("LoggerUp");
            if (t != null)
                _loggerUpButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }
        if (_loggerDownButton == null)
        {
            Transform t = FindNamedTransform("LoggerDown");
            if (t != null)
                _loggerDownButton = t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }

        // Если привязали LoggerText поздно, сразу отрисуем уже накопленные логи.
        if (_loggerText != null)
            RefreshLoggerView();
    }

    private void BindUiCallbacks()
    {
        if (_endTurnButton != null)
        {
            _endTurnButton.onClick.RemoveListener(OnEndTurnClicked);
            _endTurnButton.onClick.AddListener(OnEndTurnClicked);
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

        RefreshLoggerView();
    }

    private static T FindChildComponent<T>(Transform root, string childName) where T : Component
    {
        Transform child = FindNamedTransform(childName, root);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static Transform FindNamedTransform(string objectName)
    {
        return FindNamedTransform(objectName, null);
    }

    private static Transform FindNamedTransform(string objectName, Transform root)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        if (root != null)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child.name == objectName)
                    return child;
            }

            return null;
        }

        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || candidate.hideFlags != HideFlags.None)
                continue;
            if (!candidate.gameObject.scene.IsValid())
                continue;
            if (candidate.name == objectName)
                return candidate;
        }

        return null;
    }
}
