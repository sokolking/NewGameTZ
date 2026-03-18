using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// UI для отображения очков действия и кнопки \"Закончить ход\".
/// Онлайн: бар после успешного POST submit; снимается при пуше по WebSocket.
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

    private bool _endTurnInProgress;
    private bool _roundWaitVisible;
    private readonly System.Collections.Generic.Queue<string> _logLines = new System.Collections.Generic.Queue<string>();

    private void Awake()
    {
        if (_endTurnButton != null)
            _endTurnButton.onClick.AddListener(OnEndTurnClicked);

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
        if (_roundWaitVisible && _roundWaitSlider != null)
            _roundWaitSlider.normalizedValue = 0.5f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.5f);

        if (_player == null || _apText == null) return;
        // Пока ждём ответ сервера после досрочного «Закончить ход» — не показывать замороженный таймер (T 22),
        // чтобы не создавать впечатление ожидания окончания раунда на сервере.
        string timerStr = (_gameSession != null && _gameSession.IsWaitingForServerRoundResolve)
            ? "—"
            : Mathf.CeilToInt(Mathf.Max(0f, _player.TurnTimeLeft)).ToString();
        _apText.text = $"Ход {_player.TurnCount + 1} | ОД {_player.CurrentAp} | T {timerStr}";
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
            _gameSession.BeginWaitingForServerRoundResolve();
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

        yield return _player.PlayLastMoveAnimation();

        if (IsOnlineSubmitFlow)
        {
            _gameSession.BeginWaitingForServerRoundResolve();
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
        if (FindFirstObjectByType<BattleSignalRConnection>() == null)
        {
            AppendLog("Ход принят сервером. Добавьте BattleSignalRConnection в сцену для бара до ответа по сокету.");
            return;
        }

        ShowRoundWaitPanel();
        AppendLog("Ход принят. Ожидание результата по сокету…");
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
}
