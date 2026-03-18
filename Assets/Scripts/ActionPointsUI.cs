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
    [SerializeField] private Text _apText;
    [SerializeField] private Button _endTurnButton;
    [Header("Лог ходов")]
    [SerializeField] private Text _logText;
    [SerializeField] private int _maxLogLines = 50;
    [SerializeField] private ScrollRect _logScrollRect;

    [Header("Онлайн (сервер)")]
    [SerializeField] private GameSession _gameSession;

    [Header("Ожидание результата раунда")]
    [Tooltip("Панель на весь экран: Image с Raycast Target, дочерний Slider (опционально).")]
    [SerializeField] private GameObject _roundWaitPanel;
    [SerializeField] private Slider _roundWaitSlider;
    [Tooltip("Секунды на один цикл заполнения бара (неопределённый прогресс).")]
    [SerializeField] private float _roundWaitBarCycleSeconds = 1.25f;

    [Header("Миникарта")]
    [SerializeField] private RectTransform _miniMapPanel;
    [Tooltip("Максимальный размер миникарты. Фактический размер подстраивается под пропорции поля.")]
    private Vector2 _miniMapSize = new Vector2(182f, 98f);
    [SerializeField] private Vector2 _miniMapMargin = new Vector2(16f, 16f);
    private float _miniMapPadding = 4f;
    private float _miniMapMarkerSize = 7f;
    [SerializeField] private Color _miniMapPlayerColor = Color.white;
    [SerializeField] private Color _miniMapEnemyColor = Color.red;
    [SerializeField] private Color _miniMapMobColor = new Color(1f, 0.55f, 0.15f, 1f);

    private bool _endTurnInProgress;
    private bool _roundWaitVisible;
    private readonly System.Collections.Generic.Queue<string> _logLines = new System.Collections.Generic.Queue<string>();
    private readonly Dictionary<string, Image> _miniMapRemoteMarkers = new();
    private Image _miniMapLocalMarker;
    private static Sprite _miniMapCircleSprite;

    private void Awake()
    {
        if (_endTurnButton != null)
            _endTurnButton.onClick.AddListener(OnEndTurnClicked);

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
        EnsureMiniMap();
        if (_player != null)
            AppendLog($"Ход {_player.TurnCount + 1} начат. ОД: {_player.CurrentAp}.");
    }

    private void OnDestroy()
    {
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

        if (_player == null || _apText == null) return;
        // Пока ждём ответ сервера после досрочного «Закончить ход» — не показывать замороженный таймер (T 22),
        // чтобы не создавать впечатление ожидания окончания раунда на сервере.
        string timerStr = (_gameSession != null && _gameSession.IsWaitingForServerRoundResolve)
            ? "—"
            : Mathf.CeilToInt(Mathf.Max(0f, _player.TurnTimeLeft)).ToString();
        _apText.text = $"Ход {_player.TurnCount + 1} | ОД {_player.CurrentAp} | T {timerStr}";
        UpdateMiniMap();
        if (Keyboard.current == null) return;

        if (_roundWaitVisible) return;

        if (Keyboard.current.dKey.wasPressedThisFrame)
            TryEndTurn(animate: false);
        else if (Keyboard.current.eKey.wasPressedThisFrame)
            TryEndTurn(animate: true);

        if (_player.TurnTimeExpired)
            TryEndTurn(animate: true);
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
        if (_player.IsMoving) return;
        if (_gameSession != null && _gameSession.IsBattleAnimationPlaying) return;
        if (_roundWaitVisible) return;
        if (_gameSession != null && _gameSession.IsWaitingForServerRoundResolve) return;

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
            AppendLog("Отправка хода на сервер…");
            _endTurnInProgress = false;
            return;
        }

        // Оффлайн-режим без сервера больше не используется: одиночка тоже идёт через сервер.
        // Для совместимости оставляем только локальный лог без изменения состояния ОД/штрафов.
        AppendLog("Локальный конец хода без сервера (устаревший режим).");
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
            AppendLog("Отправка хода на сервер…");
            _endTurnInProgress = false;
            yield break;
        }

        // Оффлайн-режим без сервера больше не используется: одиночка тоже идёт через сервер.
        AppendLog("Локальный анимированный конец хода без сервера (устаревший режим).");
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
        AppendLog("Ход принят сервером. Ожидание результата раунда…");
    }

    private void SubmitTurnIfOnline()
    {
        if (_player == null || _gameSession == null || !_gameSession.IsOnlineMode) return;
        if (!_gameSession.IsInBattleWithServer()) return;
        var path = _player.GetTurnPathCopy();
        _gameSession.SubmitTurnLocal(path, _player.ApSpentThisTurn, _player.StepsTakenThisTurn, _gameSession.ServerRoundIndexForSubmit);
    }

    private void HandlePlayerMoved(HexCell cell)
    {
        if (cell == null) return;
        string tag = $"{cell.ColLabel}{cell.RowLabel}";
        AppendLog($"Player перешёл на {tag}");
    }

    private void AppendLog(string line)
    {
        if (_logText == null) return;
        _logLines.Enqueue(line);
        while (_logLines.Count > _maxLogLines)
            _logLines.Dequeue();
        _logText.text = string.Join("\n", _logLines);
    }

    private void EnsureMiniMap()
    {
        if (_miniMapPanel == null)
        {
            Transform existing = transform.Find("MiniMapPanel");
            if (existing != null)
                _miniMapPanel = existing as RectTransform;
        }

        if (_miniMapPanel == null)
        {
            GameObject panelGo = new GameObject("MiniMapPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(transform, false);
            _miniMapPanel = panelGo.GetComponent<RectTransform>();
            _miniMapPanel.anchorMin = new Vector2(1f, 1f);
            _miniMapPanel.anchorMax = new Vector2(1f, 1f);
            _miniMapPanel.pivot = new Vector2(1f, 1f);
            _miniMapPanel.anchoredPosition = new Vector2(-_miniMapMargin.x, -_miniMapMargin.y);
            _miniMapPanel.sizeDelta = _miniMapSize;

            Image bg = panelGo.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;
        }

        if (_miniMapLocalMarker == null)
            _miniMapLocalMarker = CreateMiniMapMarker("LocalPlayerMarker", _miniMapPlayerColor);
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

    private void UpdateMiniMap()
    {
        EnsureMiniMap();
        if (_miniMapPanel == null || _gameSession == null)
            return;

        Player local = _gameSession.LocalPlayer;
        HexGrid grid = local != null ? local.Grid : FindFirstObjectByType<HexGrid>();
        if (grid == null)
            return;

        Camera miniMapCamera = ResolveMiniMapCamera();
        if (miniMapCamera == null)
            return;
        if (!TryGetMiniMapViewportBounds(grid, miniMapCamera, out Vector2 minViewport, out Vector2 maxViewport))
            return;

        float width = Mathf.Max(0.001f, maxViewport.x - minViewport.x);
        float height = Mathf.Max(0.001f, maxViewport.y - minViewport.y);
        UpdateMiniMapPanelLayout(grid.Width, grid.Length);
        float usableWidth = Mathf.Max(1f, _miniMapPanel.rect.width - _miniMapPadding * 2f);
        float usableHeight = Mathf.Max(1f, _miniMapPanel.rect.height - _miniMapPadding * 2f);

        if (_miniMapLocalMarker != null && local != null)
        {
            _miniMapLocalMarker.gameObject.SetActive(true);
            SetMiniMapMarkerPosition(_miniMapLocalMarker.rectTransform, local.transform.position, miniMapCamera, minViewport, width, height, usableWidth, usableHeight);
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
                SetMiniMapMarkerPosition(marker.rectTransform, remote.transform.position, miniMapCamera, minViewport, width, height, usableWidth, usableHeight);
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

    private bool TryGetMiniMapViewportBounds(HexGrid grid, Camera cam, out Vector2 minViewport, out Vector2 maxViewport)
    {
        minViewport = Vector2.zero;
        maxViewport = Vector2.one;
        if (grid == null || cam == null || grid.Width <= 0 || grid.Length <= 0)
            return false;

        Vector2[] points =
        {
            ProjectToViewport(cam, grid.GetCellWorldPosition(0, 0)),
            ProjectToViewport(cam, grid.GetCellWorldPosition(0, grid.Length - 1)),
            ProjectToViewport(cam, grid.GetCellWorldPosition(grid.Width - 1, 0)),
            ProjectToViewport(cam, grid.GetCellWorldPosition(grid.Width - 1, grid.Length - 1)),
        };

        float minViewX = float.MaxValue;
        float maxViewX = float.MinValue;
        float minViewY = float.MaxValue;
        float maxViewY = float.MinValue;
        foreach (Vector2 p in points)
        {
            minViewX = Mathf.Min(minViewX, p.x);
            maxViewX = Mathf.Max(maxViewX, p.x);
            minViewY = Mathf.Min(minViewY, p.y);
            maxViewY = Mathf.Max(maxViewY, p.y);
        }

        if (maxViewX - minViewX < 0.0001f || maxViewY - minViewY < 0.0001f)
            return false;

        minViewport = new Vector2(minViewX, minViewY);
        maxViewport = new Vector2(maxViewX, maxViewY);
        return true;
    }

    private static Vector2 ProjectToViewport(Camera cam, Vector3 worldPosition)
    {
        Vector3 viewport = cam.WorldToViewportPoint(worldPosition);
        return new Vector2(viewport.x, viewport.y);
    }

    private void UpdateMiniMapPanelLayout(int gridWidth, int gridLength)
    {
        if (_miniMapPanel == null)
            return;

        float maxWidth = Mathf.Max(60f, _miniMapSize.x);
        float maxHeight = Mathf.Max(60f, _miniMapSize.y);
        float aspect = Mathf.Max(1f, gridWidth) / Mathf.Max(1f, gridLength);

        float panelWidth;
        float panelHeight;

        if (aspect >= 1f)
        {
            panelWidth = maxWidth;
            panelHeight = panelWidth / aspect;
            if (panelHeight > maxHeight)
            {
                panelHeight = maxHeight;
                panelWidth = panelHeight * aspect;
            }
        }
        else
        {
            panelHeight = maxHeight;
            panelWidth = panelHeight * aspect;
            if (panelWidth > maxWidth)
            {
                panelWidth = maxWidth;
                panelHeight = panelWidth / aspect;
            }
        }

        _miniMapPanel.sizeDelta = new Vector2(panelHeight, panelWidth);
    }

    private void SetMiniMapMarkerPosition(
        RectTransform marker,
        Vector3 worldPosition,
        Camera cam,
        Vector2 minViewport,
        float viewportWidth,
        float viewportHeight,
        float usableWidth,
        float usableHeight)
    {
        if (marker == null || cam == null)
            return;

        Vector2 viewport = ProjectToViewport(cam, worldPosition);
        float xNorm = Mathf.Clamp01((viewport.x - minViewport.x) / viewportWidth);
        float yNorm = Mathf.Clamp01((viewport.y - minViewport.y) / viewportHeight);
        marker.anchoredPosition = new Vector2(
            _miniMapPadding + xNorm * usableWidth,
            _miniMapPadding + yNorm * usableHeight);
    }
}
