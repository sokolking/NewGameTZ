using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// UI для отображения очков действия и кнопки \"Закончить ход\".
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

    // Чтобы не запускать завершение хода дважды (через D, E, кнопку или таймер).
    private bool _endTurnInProgress;
    private readonly System.Collections.Generic.Queue<string> _logLines = new System.Collections.Generic.Queue<string>();

    private void Awake()
    {
        if (_endTurnButton != null)
            _endTurnButton.onClick.AddListener(OnEndTurnClicked);

        if (_player != null)
        {
            _player.OnMovedToCell += HandlePlayerMoved;
        }
    }

    private void Start()
    {
        // Здесь Player уже прошёл свой Awake, CurrentAp инициализирован корректно.
        if (_player != null)
        {
            AppendLog($"Ход {_player.TurnCount + 1} начат. ОД: {_player.CurrentAp}.");
        }
    }

    private void OnDestroy()
    {
        if (_player != null)
        {
            _player.OnMovedToCell -= HandlePlayerMoved;
        }
    }

    private void Update()
    {
        if (_player == null || _apText == null) return;
        float timeLeft = Mathf.Max(0f, _player.TurnTimeLeft);
        int secondsLeft = Mathf.CeilToInt(timeLeft);
        _apText.text = $"Ход {_player.TurnCount + 1} | ОД {_player.CurrentAp} | T {secondsLeft}";
        if (Keyboard.current == null) return;

        // D — закончить ход без анимации.
        if (Keyboard.current.dKey.wasPressedThisFrame)
        {
            TryEndTurn(animate: false);
        }
        // E — закончить ход с анимацией.
        else if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            TryEndTurn(animate: true);
        }

        // Авто-завершение хода по таймеру (с анимацией, как при нажатии E/кнопки).
        if (_player.TurnTimeExpired)
        {
            TryEndTurn(animate: true);
        }
    }

    private void OnEndTurnClicked()
    {
        if (_player == null) return;
        TryEndTurn(animate: true);
    }

    /// <summary>
    /// Попробовать завершить ход. Защита от двойных вызовов и завершения во время реального движения игрока.
    /// </summary>
    private void TryEndTurn(bool animate)
    {
        if (_player == null) return;

        // Уже запущен процесс завершения хода — игнорируем повторные запросы (таймер/D/E/кнопка).
        if (_endTurnInProgress) return;

        // Нельзя завершать ход, пока игрок реально двигается по карте (во избежание порчи состояния ОД/штрафов).
        if (_player.IsMoving) return;

        _endTurnInProgress = true;

        if (animate)
            StartCoroutine(EndTurnAnimated());
        else
            EndTurnImmediate();
    }

    private void EndTurnImmediate()
    {
        if (_player == null) return;
        SubmitTurnIfOnline();
        AppendLog("Ход завершён без анимации.");
        _player.EndTurn();
        AppendLog($"Ход {_player.TurnCount + 1} начат. ОД: {_player.CurrentAp}.");
        _endTurnInProgress = false;
    }

    private System.Collections.IEnumerator EndTurnAnimated()
    {
        if (_player == null) yield break;

        // Сначала проигрываем анимацию последнего перемещения (с исходного гекса до выбранного).
        yield return _player.PlayLastMoveAnimation();

        SubmitTurnIfOnline();
        AppendLog("Ход завершён с анимацией.");
        _player.EndTurn();
        AppendLog($"Ход {_player.TurnCount + 1} начат. ОД: {_player.CurrentAp}.");
        _endTurnInProgress = false;
    }

    /// <summary>Отправить данные хода на сервер (или в заглушку), если включён онлайн-режим. Вызывать до _player.EndTurn().</summary>
    private void SubmitTurnIfOnline()
    {
        if (_player == null || _gameSession == null || !_gameSession.IsOnlineMode) return;
        var path = _player.GetTurnPathCopy();
        _gameSession.SubmitTurnLocal(path, _player.ApSpentThisTurn, _player.StepsTakenThisTurn, _player.TurnCount);
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

        // Добавляем новую строку в конец лога (старые сверху, новые снизу).
        _logLines.Enqueue(line);
        while (_logLines.Count > _maxLogLines)
            _logLines.Dequeue();

        _logText.text = string.Join("\n", _logLines);

        // Позицию ScrollRect больше не меняем, чтобы вообще не было «прыжков».
    }
}

