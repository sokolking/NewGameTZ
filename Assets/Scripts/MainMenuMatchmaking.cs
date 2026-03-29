using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>PvP queue mode for socket matchmaking (1v1 / 3v3 / 5v5).</summary>
public enum PvpMatchmakingMode
{
    Pvp1v1 = 0,
    Pvp3v3 = 1,
    Pvp5v5 = 2
}

/// <summary>
/// Матчмейкинг с главного меню: по умолчанию — очередь через <c>/ws/session</c>; опционально HTTP join/poll (1v1).
/// </summary>
[AddComponentMenu("Hex Grid/Main Menu Matchmaking")]
public class MainMenuMatchmaking : MonoBehaviour
{
    [SerializeField] private string _serverUrl = "http://localhost:5000";
    [SerializeField] private string _gameSceneName = "MainScene";
    [Tooltip("If true, use legacy POST /api/battle/join + poll (1v1 only). Otherwise use session WebSocket queue.")]
    [SerializeField] private bool _useHttpMatchmakingFallback;

    [Tooltip("Default PvP mode when using socket matchmaking.")]
    [SerializeField] private PvpMatchmakingMode _pvpMode = PvpMatchmakingMode.Pvp1v1;

    [Tooltip("Optional status line when queue panel is not assigned.")]
    [SerializeField] private Text _statusText;

    [Header("Queue window (optional)")]
    [Tooltip("Usually the MatchmakingQueuePanel GameObject under the menu Canvas.")]
    [SerializeField] private GameObject _queuePanel;
    [SerializeField] private Text _queueProgressText;
    [SerializeField] private Text _queueModeText;
    [SerializeField] private Button _leaveQueueButton;
    [SerializeField] private Button _readyButton;
    [SerializeField] private Text _readyHintText;

    private bool _searching;
    private bool _usingSocketQueue;
    private Coroutine _socketMatchmakingCoroutine;
    private Action _resetMatchTypeTogglesForUi;
    private string _queueBattleId;
    private string _queuePlayerId;
    private string _pendingReadyCheckId;

    /// <summary>When true, <see cref="FindGame"/> only opens the queue panel; socket <c>queueJoin</c> runs when the player picks a mode (toggle).</summary>
    [SerializeField] private bool _socketJoinOnlyAfterMatchTypeToggle;

    private void OnEnable()
    {
        SessionWebSocketConnection.OnMatchmakingQueueStateUpdated += OnQueueStateUpdated;
        SessionWebSocketConnection.OnMatchmakingQueueJoined += OnQueueJoined;
        SessionWebSocketConnection.OnMatchmakingQueueLeft += OnQueueLeftMessage;
        SessionWebSocketConnection.OnMatchmakingReadyCheckStarted += OnReadyCheckStarted;
        SessionWebSocketConnection.OnMatchmakingReadyCheckProgress += OnReadyCheckProgress;
        SessionWebSocketConnection.OnMatchmakingReadyCheckCancelled += OnReadyCheckCancelled;
        SessionWebSocketConnection.OnMatchmakingMatchStarted += OnMatchStarted;
        SessionWebSocketConnection.OnMatchmakingQueueError += OnQueueError;
    }

    private void OnDisable()
    {
        SessionWebSocketConnection.OnMatchmakingQueueStateUpdated -= OnQueueStateUpdated;
        SessionWebSocketConnection.OnMatchmakingQueueJoined -= OnQueueJoined;
        SessionWebSocketConnection.OnMatchmakingQueueLeft -= OnQueueLeftMessage;
        SessionWebSocketConnection.OnMatchmakingReadyCheckStarted -= OnReadyCheckStarted;
        SessionWebSocketConnection.OnMatchmakingReadyCheckProgress -= OnReadyCheckProgress;
        SessionWebSocketConnection.OnMatchmakingReadyCheckCancelled -= OnReadyCheckCancelled;
        SessionWebSocketConnection.OnMatchmakingMatchStarted -= OnMatchStarted;
        SessionWebSocketConnection.OnMatchmakingQueueError -= OnQueueError;
    }

    private void Awake()
    {
        FixRootCanvasScaleIfBroken();

        if (_leaveQueueButton != null)
        {
            _leaveQueueButton.onClick.RemoveListener(OnLeaveQueueClicked);
            _leaveQueueButton.onClick.AddListener(OnLeaveQueueClicked);
        }

        if (_readyButton != null)
        {
            _readyButton.onClick.RemoveListener(OnConfirmReadyClicked);
            _readyButton.onClick.AddListener(OnConfirmReadyClicked);
            _readyButton.gameObject.SetActive(false);
        }

        SetLeaveButtonLabel();
        SetReadyButtonLabel();
        EnsureQueuePanelReference();
        EnsureQueuePanelOverlayCanvas();
        HideQueuePanel();
    }

    private void Start()
    {
        RefreshQueueModeText();
    }

    /// <summary>MainMenuUI registers to uncheck MatchTypes when queue ends from Leave / errors / connection failure.</summary>
    public void SetMatchTypeTogglesResetHandler(Action handler) => _resetMatchTypeTogglesForUi = handler;

    /// <summary>Set from MainMenuUI when 1v1/3v3/5v5 toggles are used (often inside the queue panel).</summary>
    public void SetSocketJoinOnlyAfterMatchTypeToggle(bool value) => _socketJoinOnlyAfterMatchTypeToggle = value;

    private void InvokeMatchTypeTogglesReset()
    {
        try
        {
            _resetMatchTypeTogglesForUi?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MainMenuMatchmaking] Match type UI reset: " + ex.Message);
        }
    }

    public static int RequiredHumansForMode(PvpMatchmakingMode mode) => mode switch
    {
        PvpMatchmakingMode.Pvp1v1 => 2,
        PvpMatchmakingMode.Pvp3v3 => 6,
        PvpMatchmakingMode.Pvp5v5 => 10,
        _ => 2
    };

    /// <summary>Sets queue overlay progress line to <c>0 / required</c> for the mode (until server sends live counts).</summary>
    public void ApplyQueueProgressPlaceholderForMode(PvpMatchmakingMode mode)
    {
        if (_queueProgressText == null)
            return;
        int req = RequiredHumansForMode(mode);
        _queueProgressText.text = Loc.Tf("menu.matchmaking_players_progress", 0, req);
    }

    private void StopSocketMatchmakingCoroutine()
    {
        if (_socketMatchmakingCoroutine == null)
            return;
        StopCoroutine(_socketMatchmakingCoroutine);
        _socketMatchmakingCoroutine = null;
    }

    /// <summary>Leave socket or HTTP waiter queue, hide overlay, clear status/progress, optionally uncheck MatchTypes.</summary>
    public void StopSocketMatchmakingFromUi(bool resetMatchTypeToggles = true)
    {
        StopSocketMatchmakingCoroutine();

        if (_usingSocketQueue)
            SessionWebSocketConnection.SendMatchmakingQueueLeave();

        if (_searching && !_usingSocketQueue && !string.IsNullOrEmpty(_queueBattleId) && !string.IsNullOrEmpty(_queuePlayerId))
        {
            try
            {
                BattleServerConnection.NotifyLeaveBlocking(_serverUrl, _queueBattleId, _queuePlayerId, BattleSessionState.AccessToken);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MainMenuMatchmaking] HTTP queue leave: " + ex.Message);
            }
        }

        _queueBattleId = null;
        _queuePlayerId = null;
        _searching = false;
        _usingSocketQueue = false;
        _pendingReadyCheckId = null;
        if (_readyButton != null)
            _readyButton.gameObject.SetActive(false);
        HideQueuePanel();
        SetStatus("");
        if (_queueProgressText != null)
            _queueProgressText.text = "";
        if (resetMatchTypeToggles)
            InvokeMatchTypeTogglesReset();
    }

    /// <summary>Leaves any active socket queue, then joins <paramref name="mode"/> (used by MatchTypes toggles).</summary>
    public void RequestSocketMatchmakingForMode(PvpMatchmakingMode mode)
    {
        if (_useHttpMatchmakingFallback)
        {
            SetPvpMatchmakingMode(mode);
            if (!_searching)
                FindGame();
            return;
        }

        if (string.IsNullOrEmpty(BattleSessionState.AccessToken))
        {
            SetStatus(Loc.T("menu.session_required"));
            InvokeMatchTypeTogglesReset();
            return;
        }

        _serverUrl = BattleServerRuntime.CurrentBaseUrl;
        SetPvpMatchmakingMode(mode);

        StopSocketMatchmakingCoroutine();
        if (_searching && _usingSocketQueue)
            SessionWebSocketConnection.SendMatchmakingQueueLeave();

        _searching = false;
        _usingSocketQueue = false;
        _pendingReadyCheckId = null;
        if (_readyButton != null)
            _readyButton.gameObject.SetActive(false);

        _searching = true;
        _usingSocketQueue = true;
        ShowQueuePanel();
        RefreshQueueModeText();
        ApplyQueueProgressPlaceholderForMode(mode);
        SetStatus(Loc.T("menu.matchmaking_searching"));
        SessionWebSocketConnection.EnsureStarted();
        _socketMatchmakingCoroutine = StartCoroutine(SocketMatchmakingConnectAndJoinCoroutine());
    }

    private void SetLeaveButtonLabel()
    {
        if (_leaveQueueButton == null)
            return;
        var txt = _leaveQueueButton.GetComponentInChildren<Text>(true);
        if (txt != null)
            txt.text = Loc.T("menu.matchmaking_leave_queue");
    }

    private void SetReadyButtonLabel()
    {
        if (_readyButton == null)
            return;
        var txt = _readyButton.GetComponentInChildren<Text>(true);
        if (txt != null)
            txt.text = Loc.T("menu.matchmaking_confirm_ready");
    }

    /// <summary>Call from UI (dropdown / buttons). Updates <see cref="_queueModeText"/> when assigned.</summary>
    public void SetPvpMatchmakingMode(PvpMatchmakingMode mode)
    {
        _pvpMode = mode;
        RefreshQueueModeText();
    }

    public PvpMatchmakingMode GetPvpMatchmakingMode() => _pvpMode;

    /// <summary>Updates the queue panel mode line from <see cref="_pvpMode"/> (e.g. after changing mode in the menu).</summary>
    public void RefreshQueueModeText()
    {
        if (_queueModeText == null)
            return;
        _queueModeText.text = Loc.Tf("menu.matchmaking_mode_label", ModeDisplayName(_pvpMode));
    }

    private static string ModeToWire(PvpMatchmakingMode mode) => mode switch
    {
        PvpMatchmakingMode.Pvp1v1 => "1v1",
        PvpMatchmakingMode.Pvp3v3 => "3v3",
        PvpMatchmakingMode.Pvp5v5 => "5v5",
        _ => "1v1"
    };

    private static string ModeDisplayName(PvpMatchmakingMode mode) => mode switch
    {
        PvpMatchmakingMode.Pvp1v1 => "1v1",
        PvpMatchmakingMode.Pvp3v3 => "3v3",
        PvpMatchmakingMode.Pvp5v5 => "5v5",
        _ => "1v1"
    };

    private void OnDestroy()
    {
        StopSocketMatchmakingCoroutine();
        if (_searching && _usingSocketQueue)
            SessionWebSocketConnection.SendMatchmakingQueueLeave();
        if (_searching && !_usingSocketQueue && !string.IsNullOrEmpty(_queueBattleId) && !string.IsNullOrEmpty(_queuePlayerId))
            BattleServerConnection.NotifyLeaveBlocking(_serverUrl, _queueBattleId, _queuePlayerId, BattleSessionState.AccessToken);
        _queueBattleId = null;
        _queuePlayerId = null;
    }

    /// <summary>Uses <see cref="BattleSessionState.AccessToken"/> from <c>/api/auth/login</c>.</summary>
    public void FindGame()
    {
        if (string.IsNullOrEmpty(BattleSessionState.AccessToken))
        {
            SetStatus(Loc.T("menu.session_required"));
            return;
        }

        ShowMatchmakingQueuePanel();
        if (_searching)
            return;

        _serverUrl = BattleServerRuntime.CurrentBaseUrl;

        if (_useHttpMatchmakingFallback)
        {
            _usingSocketQueue = false;
            _searching = true;
            SetStatus(Loc.T("menu.matchmaking_searching"));
            StartCoroutine(JoinAndWaitForBattleCoroutine());
            return;
        }

        if (_socketJoinOnlyAfterMatchTypeToggle)
        {
            RefreshQueueModeText();
            ApplyQueueProgressPlaceholderForMode(_pvpMode);
            SetStatus(Loc.T("menu.matchmaking_select_mode"));
            return;
        }

        _usingSocketQueue = true;
        _searching = true;
        _pendingReadyCheckId = null;
        SetStatus(Loc.T("menu.matchmaking_searching"));
        RefreshQueueModeText();
        ApplyQueueProgressPlaceholderForMode(_pvpMode);
        SessionWebSocketConnection.EnsureStarted();
        StopSocketMatchmakingCoroutine();
        _socketMatchmakingCoroutine = StartCoroutine(SocketMatchmakingConnectAndJoinCoroutine());
    }

    /// <summary>Одиночный режим: запросить на сервере одиночный бой (1 игрок + моб) и сразу загрузить сцену.</summary>
    public void StartSinglePlayerServerBattle()
    {
        if (_searching)
            return;
        if (string.IsNullOrEmpty(BattleSessionState.AccessToken))
        {
            SetStatus(Loc.T("menu.session_required"));
            return;
        }

        _serverUrl = BattleServerRuntime.CurrentBaseUrl;
        _searching = true;
        SetStatus("Starting single-player battle...");
        StartCoroutine(JoinSoloAndStartBattleCoroutine());
    }

    private IEnumerator SocketMatchmakingConnectAndJoinCoroutine()
    {
        // Let a prior queueLeave reach the server before queueJoin when switching modes.
        yield return null;

        float timeout = 15f;
        while (timeout > 0f && !SessionWebSocketConnection.IsSessionSocketConnected)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (!SessionWebSocketConnection.IsSessionSocketConnected)
        {
            SetStatus(Loc.T("menu.matchmaking_session_connect_failed"));
            StopSocketMatchmakingCoroutine();
            _searching = false;
            _usingSocketQueue = false;
            HideQueuePanel();
            if (_queueProgressText != null)
                _queueProgressText.text = "";
            InvokeMatchTypeTogglesReset();
            yield break;
        }

        SessionWebSocketConnection.SendMatchmakingQueueJoin(ModeToWire(_pvpMode));
        _socketMatchmakingCoroutine = null;
    }

    private void OnQueueStateUpdated(string mode, int current, int required)
    {
        if (!_searching || !_usingSocketQueue)
            return;
        if (!string.IsNullOrEmpty(mode) && mode != ModeToWire(_pvpMode))
            return;
        string line = Loc.Tf("menu.matchmaking_players_progress", current, required);
        SetStatus(line);
        if (_queueProgressText != null)
            _queueProgressText.text = line;
    }

    private void OnQueueJoined()
    {
        if (!_searching || !_usingSocketQueue)
            return;
    }

    private void OnQueueLeftMessage(string mode)
    {
        // Other players also receive broadcasts when someone leaves; ignore if we are not searching.
    }

    private void OnReadyCheckStarted(string readyCheckId, string mode, long deadlineUtcMs, int required)
    {
        if (!_searching || !_usingSocketQueue)
            return;
        _pendingReadyCheckId = readyCheckId;
        if (_readyButton != null)
            _readyButton.gameObject.SetActive(true);
        string hint = Loc.Tf("menu.matchmaking_ready_prompt", 0, required);
        SetStatus(hint);
        if (_readyHintText != null)
            _readyHintText.text = hint;
    }

    private void OnReadyCheckProgress(string readyCheckId, int confirmed, int required)
    {
        if (!_searching || !_usingSocketQueue)
            return;
        if (!string.IsNullOrEmpty(_pendingReadyCheckId) && _pendingReadyCheckId != readyCheckId)
            return;
        string hint = Loc.Tf("menu.matchmaking_ready_prompt", confirmed, required);
        SetStatus(hint);
        if (_readyHintText != null)
            _readyHintText.text = hint;
    }

    private void OnReadyCheckCancelled(string readyCheckId, string mode, string reason)
    {
        if (!_searching || !_usingSocketQueue)
            return;
        _pendingReadyCheckId = null;
        if (_readyButton != null)
            _readyButton.gameObject.SetActive(false);
        SetStatus(Loc.Tf("menu.matchmaking_ready_cancelled", reason ?? ""));
    }

    private void OnMatchStarted(MatchmakingMatchStartedMessage msg)
    {
        if (!_searching || !_usingSocketQueue || msg == null)
            return;
        if (msg.battleStarted == null || string.IsNullOrEmpty(msg.battleId))
            return;

        StopSocketMatchmakingCoroutine();
        _searching = false;
        _usingSocketQueue = false;
        _pendingReadyCheckId = null;
        HideQueuePanel();
        BattleSessionState.SetPending(msg.battleId, msg.playerId, _serverUrl, msg.battleStarted);
        SceneManager.LoadScene(_gameSceneName);
    }

    private void OnQueueError(string code)
    {
        if (!_searching || !_usingSocketQueue)
            return;
        string key = code switch
        {
            "already_in_battle" => "menu.matchmaking_error_already_in_battle",
            "invalid_mode" => "menu.matchmaking_error_invalid_mode",
            _ => "menu.matchmaking_error_generic"
        };
        SetStatus(Loc.T(key));
        StopSocketMatchmakingCoroutine();
        _searching = false;
        _usingSocketQueue = false;
        HideQueuePanel();
        if (_queueProgressText != null)
            _queueProgressText.text = "";
        InvokeMatchTypeTogglesReset();
    }

    private void OnLeaveQueueClicked()
    {
        StopSocketMatchmakingFromUi(true);
    }

    private void OnConfirmReadyClicked()
    {
        if (string.IsNullOrEmpty(_pendingReadyCheckId))
            return;
        SessionWebSocketConnection.SendMatchmakingReadyConfirm(_pendingReadyCheckId);
    }

    const string MatchmakingQueuePanelName = "MatchmakingQueuePanel";
    const int QueuePanelCanvasSortOrder = 2000;

    /// <summary>
    /// If the menu root Canvas RectTransform has zero scale (broken scene / editor glitch), the whole UI including
    /// <see cref="_queuePanel"/> is invisible. Force (1,1,1) on the root overlay canvas that owns this menu.
    /// </summary>
    void FixRootCanvasScaleIfBroken()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        var rt = canvas.transform as RectTransform;
        if (rt == null)
            return;
        if (rt.localScale.sqrMagnitude < 1e-6f)
        {
            rt.localScale = Vector3.one;
            Debug.LogWarning(
                "[MainMenuMatchmaking] Root Canvas had zero localScale — fixed to (1,1,1). Save the MainMenu scene so Scale stays correct.");
        }
    }

    void EnsureQueuePanelReference()
    {
        if (_queuePanel != null)
            return;
#if UNITY_2023_1_OR_NEWER
        foreach (RectTransform rt in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#else
        foreach (RectTransform rt in UnityEngine.Object.FindObjectsOfType<RectTransform>(true))
#endif
        {
            if (rt != null && rt.gameObject.name == MatchmakingQueuePanelName)
            {
                _queuePanel = rt.gameObject;
                break;
            }
        }
    }

    /// <summary>Nested canvas so the panel draws above other menu UI (e.g. prefabs with their own canvases).</summary>
    void EnsureQueuePanelOverlayCanvas()
    {
        if (_queuePanel == null)
            return;
        var cv = _queuePanel.GetComponent<Canvas>();
        if (cv == null)
        {
            cv = _queuePanel.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = QueuePanelCanvasSortOrder;
        }
        else if (!cv.overrideSorting || cv.sortingOrder < 100)
        {
            cv.overrideSorting = true;
            cv.sortingOrder = QueuePanelCanvasSortOrder;
        }

        if (_queuePanel.GetComponent<GraphicRaycaster>() == null)
            _queuePanel.AddComponent<GraphicRaycaster>();
    }

    /// <summary>Shows <c>MatchmakingQueuePanel</c> (e.g. after <c>Button_FindGame</c>).</summary>
    public void ShowMatchmakingQueuePanel()
    {
        FixRootCanvasScaleIfBroken();
        EnsureQueuePanelReference();
        EnsureQueuePanelOverlayCanvas();
        if (_queuePanel == null)
        {
            Debug.LogWarning(
                "[MainMenuMatchmaking] Queue panel is null: on MainMenuMatchmaking assign Queue Panel to the GameObject \"MatchmakingQueuePanel\" (full-screen overlay root, not the Box child).");
            return;
        }

        _queuePanel.SetActive(true);
        _queuePanel.transform.SetAsLastSibling();
    }

    private void ShowQueuePanel() => ShowMatchmakingQueuePanel();

    private void HideQueuePanel()
    {
        if (_queuePanel != null)
            _queuePanel.SetActive(false);
        if (_readyButton != null)
            _readyButton.gameObject.SetActive(false);
    }

    private void SetStatus(string message)
    {
        if (_statusText != null)
            _statusText.text = message ?? "";
    }

    /// <summary>Status line under menu buttons (e.g. «select match type»).</summary>
    public void ShowMenuStatus(string message) => SetStatus(message ?? "");

    private IEnumerator JoinAndWaitForBattleCoroutine()
    {
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = false, characterLevel = 1 };
        var json = JsonConvert.SerializeObject(body);
        string url = _serverUrl.TrimEnd('/') + "/api/battle/join";
        string responseText = null;
        string errBody = null;
        yield return HttpSimple.PostJsonWithAuth(url, json, BattleSessionState.AccessToken, b => responseText = b, e => errBody = e);

        if (errBody != null)
        {
            SetStatus(ExtractRequestErrorFromBody(errBody, "Connection failed. Check server."));
            _searching = false;
            yield break;
        }

        if (string.IsNullOrEmpty(responseText))
        {
            SetStatus("Connection failed. Check server.");
            _searching = false;
            yield break;
        }

        {
            var response = ParseJoinResponse(responseText);
            string battleId = response.battleId;
            string playerId = response.playerId;

            if (response.status == "battle" && response.battleStarted != null)
            {
                BattleSessionState.SetPending(battleId, playerId, _serverUrl, response.battleStarted);
                _searching = false;
                SceneManager.LoadScene(_gameSceneName);
                yield break;
            }

            if (response.status == "waiting")
            {
                _queueBattleId = battleId;
                _queuePlayerId = playerId;
                yield return PollUntilBattleStartedCoroutine(battleId, playerId);
                _queueBattleId = null;
                _queuePlayerId = null;
            }
            else
            {
                SetStatus("Unexpected response.");
                _searching = false;
            }
        }
    }

    private IEnumerator JoinSoloAndStartBattleCoroutine()
    {
        var body = new JoinRequest { startCol = 0, startRow = 0, solo = true, characterLevel = 1 };
        var json = JsonConvert.SerializeObject(body);
        string url = _serverUrl.TrimEnd('/') + "/api/battle/join";
        string responseText = null;
        string errBody = null;
        yield return HttpSimple.PostJsonWithAuth(url, json, BattleSessionState.AccessToken, b => responseText = b, e => errBody = e);

        if (errBody != null)
        {
            SetStatus(ExtractRequestErrorFromBody(errBody, "Connection failed. Check server."));
            _searching = false;
            yield break;
        }

        if (string.IsNullOrEmpty(responseText))
        {
            SetStatus("Connection failed. Check server.");
            _searching = false;
            yield break;
        }

        var response = ParseJoinResponse(responseText);
        string battleId = response.battleId;
        string playerId = response.playerId;

        if (response.status == "battle" && response.battleStarted != null)
        {
            BattleSessionState.SetPending(battleId, playerId, _serverUrl, response.battleStarted);
            _searching = false;
            SceneManager.LoadScene(_gameSceneName);
            yield break;
        }

        SetStatus("Unexpected response for solo battle.");
        _searching = false;
    }

    private static readonly WaitForSeconds _pollWait = new WaitForSeconds(0.5f);

    private IEnumerator PollUntilBattleStartedCoroutine(string battleId, string playerId)
    {
        while (_searching && !string.IsNullOrEmpty(battleId))
        {
            yield return _pollWait;
            var url = _serverUrl.TrimEnd('/') + "/api/battle/" + HttpSimple.Escape(battleId) + "/poll?playerId=" + HttpSimple.Escape(playerId);
            int status = 0;
            string body = null;
            string transportErr = null;
            yield return HttpSimple.GetStringWithStatusAndAuth(url, BattleSessionState.AccessToken, (code, b) => { status = code; body = b; }, e => transportErr = e);

            if (transportErr != null)
                continue;

            if (status == 404)
            {
                _queueBattleId = null;
                _queuePlayerId = null;
                SetStatus(Loc.T("menu.search_cancelled"));
                _searching = false;
                yield break;
            }

            if (status == 401 || status == 403)
            {
                _queueBattleId = null;
                _queuePlayerId = null;
                SetStatus(Loc.T("menu.session_required"));
                _searching = false;
                yield break;
            }

            if (status < 200 || status >= 300 || string.IsNullOrEmpty(body))
                continue;

            var poll = ParsePollResponse(body);
            if (poll.status == "battle" && poll.battleStarted != null)
            {
                BattleSessionState.SetPending(battleId, playerId, _serverUrl, poll.battleStarted);
                _searching = false;
                SceneManager.LoadScene(_gameSceneName);
                yield break;
            }
        }
    }

    [Serializable]
    private class JoinRequest
    {
        public int startCol;
        public int startRow;
        public bool solo;
        public int characterLevel;
    }

    [Serializable]
    private class JoinResponse
    {
        public string battleId;
        public string playerId;
        public string status;
        public BattleStartedPayload battleStarted;
    }

    [Serializable]
    private class PollResponse
    {
        public string status;
        public BattleStartedPayload battleStarted;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string error;
    }

    private static JoinResponse ParseJoinResponse(string responseText)
    {
        if (string.IsNullOrEmpty(responseText))
            return null;
        try
        {
            return JsonConvert.DeserializeObject<JoinResponse>(responseText);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MainMenuMatchmaking] Join JSON Newtonsoft failed: " + ex.Message + "; JsonUtility fallback");
            return JsonUtility.FromJson<JoinResponse>(responseText);
        }
    }

    private static PollResponse ParsePollResponse(string body)
    {
        if (string.IsNullOrEmpty(body))
            return null;
        try
        {
            return JsonConvert.DeserializeObject<PollResponse>(body);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MainMenuMatchmaking] Poll JSON Newtonsoft failed: " + ex.Message + "; JsonUtility fallback");
            return JsonUtility.FromJson<PollResponse>(body);
        }
    }

    private static string ExtractRequestErrorFromBody(string responseText, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                var error = JsonUtility.FromJson<ErrorResponse>(responseText);
                if (error != null && !string.IsNullOrWhiteSpace(error.error))
                    return error.error;
            }
            catch
            {
                // Ignore malformed error body and fall back to generic text.
            }
        }

        return fallback;
    }
}
