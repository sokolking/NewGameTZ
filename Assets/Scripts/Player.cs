using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Игрок на гекс-сетке: текущая ячейка, очки действия и счётчик ходов.
/// </summary>
public class Player : MonoBehaviour
{
    private const int ChangePostureCost = 2;
    private const float RunCostMultiplier = 0.5f;
    private const float SitCostMultiplier = 1.5f;
    private const float RunPenaltyThresholdFraction = 0.85f;
    private const float MaxPenaltyFraction = 0.95f;

    [Header("Сетка")]
    [SerializeField] private HexGrid _grid;

    [Header("Движение")]
    [SerializeField] private float _moveDurationPerHex = 0.2f;

    [Header("Очки действия")]
    [SerializeField] private int _maxAp = 100;
    [Header("Жизни")]
    [SerializeField] private int _maxHp = 10;

    [Header("Таймер хода, сек")]
    [SerializeField] private float _turnDurationSeconds = 100f;

    private int _currentCol;
    private int _currentRow;
    private bool _isMoving;
    private int _currentAp;
    private int _turnCount;
    private int _currentHp;
    private int _runMovementApSpentThisTurn;
    private MovementPosture _currentPosture = MovementPosture.Walk;

    // Накопленный штраф в долях от MaxAp (0–1): 0 = нет штрафа, 0.1 = -10% от MaxAp.
    // Сколько ОД уже потрачено в текущем ходу (используется для штрафа и подсчёта шагов).
    private int _apSpentThisTurn;
    // Сколько "шагов" уже совершено в этом ходу (независимо от направления).
    private int _stepsTakenThisTurn;
    // Эффективный максимум ОД в начале текущего хода (до трат в этом ходу).
    // Полный путь за текущий ход (все переходы по гексам, в порядке кликов).
    private List<(int col, int row)> _turnPath;
    private List<BattleQueuedAction> _turnActions;
    // Остаток времени текущего хода (сек).
    private float _turnTimeLeft;
    // Абсолютный момент окончания хода по UTC timestamp от сервера.
    private long _turnDeadlineUtcMs;
    // Флаг, что таймер хода уже истёк (для внешней логики автозавершения).
    private bool _turnTimeExpired;
    private bool _turnTimerPaused;
    private bool _isHidden;
    private int _movementInterruptVersion;

    /// <summary>Код оружия (сервер / локальный каталог).</summary>
    private string _weaponCode = WeaponCatalog.FistCode;
    private int _weaponDamage = 1;
    /// <summary>Макс. дистанция атаки в шагах гекса (как на сервере <see cref="UnitStateDto.WeaponRange"/>).</summary>
    private int _weaponRangeHexes = 1;

    public int CurrentCol => _currentCol;
    public int CurrentRow => _currentRow;
    public bool IsMoving => _isMoving;
    public int MaxAp => _maxAp;
    public int MaxHp => _maxHp;
    public int CurrentHp => _currentHp;
    public bool IsDead => _currentHp <= 0;
    public MovementPosture CurrentMovementPosture => _currentPosture;
    public MovementPosture PreviewMovementPosture => MovementPostureUtility.GetPreviewMovementPosture(_currentPosture);
    /// <summary>Текущие ОД.</summary>
    public int CurrentAp => _currentAp;
    public int TurnCount => _turnCount;
    /// <summary>Сколько ОД уже потрачено в текущем ходу.</summary>
    public int ApSpentThisTurn => _apSpentThisTurn;
    /// <summary>Оставшееся время текущего хода (сек).</summary>
    public float TurnTimeLeft => _turnTimeLeft;
    /// <summary>Длительность хода (сек).</summary>
    public float TurnDurationSeconds => _turnDurationSeconds;
    public bool IsTurnTimerPaused => _turnTimerPaused;
    public bool IsHidden => _isHidden;
    /// <summary>Истёк ли таймер хода.</summary>
    public bool TurnTimeExpired => _turnTimeExpired;
    /// <summary>Сколько "шагов" уже совершено в этом ходу.</summary>
    public int StepsTakenThisTurn => _stepsTakenThisTurn;
    public HexGrid Grid => _grid;
    public float MoveDurationPerHex => _moveDurationPerHex;

    public string WeaponCode => _weaponCode;
    public int WeaponDamage => _weaponDamage;
    /// <summary>Радиус атаки в гексах (hex distance), как у текущего оружия.</summary>
    public int WeaponRangeHexes => Mathf.Max(0, _weaponRangeHexes);

    /// <summary>Синхронизация с сервером или локальная смена (кулак / камень и т.д.).</summary>
    public void SetEquippedWeapon(string code, int damage, int rangeHexes)
    {
        _weaponCode = string.IsNullOrWhiteSpace(code) ? WeaponCatalog.FistCode : code.Trim().ToLowerInvariant();
        _weaponDamage = Mathf.Max(0, damage);
        _weaponRangeHexes = Mathf.Max(0, rangeHexes);
    }

    public delegate void PlayerMovedHandler(HexCell cell);
    /// <summary>Событие: игрок завершил перемещение в новую ячейку (после MoveAlongPath, телепорта или анимации).</summary>
    public event PlayerMovedHandler OnMovedToCell;
    public event Action<MovementPosture> OnMovementPostureChanged;

    private void Awake()
    {
        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;
        _currentAp = _maxAp;
        _currentHp = _maxHp;
        _currentPosture = MovementPosture.Walk;
        _runMovementApSpentThisTurn = 0;
        _turnTimeLeft = _turnDurationSeconds;
        _turnDeadlineUtcMs = BuildRoundDeadlineUtcMs(_turnDurationSeconds);
        _turnTimeExpired = false;
        _turnPath = new List<(int col, int row)>();
        _turnActions = new List<BattleQueuedAction>();
    }

    private void Start()
    {
        if (GetComponentInChildren<MeshFilter>() == null && GetComponentInChildren<MeshRenderer>() == null)
            CreateDefaultVisual();

        // В онлайн-бою позицию задаёт сервер (ApplyBattleStarted); иначе Player.Start перезапишет спавн в (0,0).
        if (GameSession.Active != null && GameSession.Active.IsInBattleWithServer())
            return;
        if (_grid != null && _grid.GetCell(0, 0) != null)
        {
            _currentCol = 0;
            _currentRow = 0;
            transform.position = _grid.GetCellWorldPosition(0, 0);
        }
    }

    private void Update()
    {
        // Таймер хода: считаем только время, авто-завершение делает UI (чтобы можно было показать анимацию).
        if (!_turnTimerPaused && _turnTimeLeft > 0f)
        {
            _turnTimeLeft = GetRemainingTimeFromDeadline();
            if (_turnTimeLeft <= 0f)
            {
                _turnTimeLeft = 0f;
                _turnTimeExpired = true;
            }
        }
    }

    private void CreateDefaultVisual()
    {
        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.name = "Visual";
        cap.transform.SetParent(transform);
        cap.transform.localPosition = Vector3.zero;
        cap.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);
        UnityEngine.Object.Destroy(cap.GetComponent<Collider>());
    }

    /// <summary>Запускает движение по пути (список (col, row)), ограничивая длину по ОД. animate=false – телепорт.
    /// Стоимость перехода на L клеток при уже сделанных K шагах = GetStepCost(K+L) − GetStepCost(K).</summary>
    public void MoveAlongPath(List<(int col, int row)> path, bool animate)
    {
        if (path == null || path.Count < 2 || _isMoving || _grid == null) return;
        if (IsDead || _isHidden) return;
        if (!MovementPostureUtility.CanMove(_currentPosture)) return;

        int stepsToMove = path.Count - 1;
        if (stepsToMove <= 0) return;

        int stepsAlready = _stepsTakenThisTurn;
        int allowedSteps = GetAllowedSteps(PreviewMovementPosture, stepsAlready, stepsToMove, _currentAp);
        if (allowedSteps <= 0) return;

        MovementPosture posture = PreviewMovementPosture;
        int moveCost = GetMoveCost(posture, stepsAlready, allowedSteps);
        _apSpentThisTurn += moveCost;
        _stepsTakenThisTurn = stepsAlready + allowedSteps;
        _currentAp -= moveCost;
        if (posture == MovementPosture.Run)
            _runMovementApSpentThisTurn += moveCost;

        var subPath = path.GetRange(0, allowedSteps + 1);

        // Накопить полный путь за ход:
        // при первом перемещении добавляем стартовую клетку, далее — только новые шаги.
        if (_turnPath == null)
            _turnPath = new List<(int col, int row)>();
        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();
        if (_turnPath.Count == 0)
            _turnPath.Add((_currentCol, _currentRow));
        for (int i = 1; i < subPath.Count; i++)
        {
            _turnPath.Add(subPath[i]);
            int stepCost = GetMoveCost(posture, stepsAlready + i - 1, 1);
            _turnActions.Add(new BattleQueuedAction
            {
                actionType = "MoveStep",
                targetPosition = new HexPosition(subPath[i].col, subPath[i].row),
                posture = MovementPostureUtility.ToId(posture),
                cost = stepCost
            });
        }

        if (!animate)
        {
            // Без анимации — сразу телепортируем в последний гекс подотрезка.
            var last = subPath[subPath.Count - 1];
            _currentCol = last.col;
            _currentRow = last.row;
            transform.position = _grid.GetCellWorldPosition(last.col, last.row);

            HexCell cell = _grid.GetCell(_currentCol, _currentRow);
            OnMovedToCell?.Invoke(cell);
        }
        else
        {
            StartCoroutine(MoveAlongPathCoroutine(subPath));
        }
    }

    private IEnumerator MoveAlongPathCoroutine(List<(int col, int row)> path)
    {
        int interruptVersion = _movementInterruptVersion;
        _isMoving = true;

        foreach (var step in path)
        {
            if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
            {
                _isMoving = false;
                yield break;
            }

            if (step.col == _currentCol && step.row == _currentRow)
                continue;

            Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
            float elapsed = 0f;

            while (elapsed < _moveDurationPerHex)
            {
                if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
                {
                    _isMoving = false;
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _moveDurationPerHex);
                transform.position = Vector3.Lerp(transform.position, target, t);
                yield return null;
            }

            _currentCol = step.col;
            _currentRow = step.row;
            transform.position = target;
        }

        if (_grid != null)
        {
            HexCell cell = _grid.GetCell(_currentCol, _currentRow);
            OnMovedToCell?.Invoke(cell);
        }

        _isMoving = false;
    }

    /// <summary>
    /// Закончить ход.
    /// Штрафный ход — финиш на предпоследнем или последнем шаге при макс. ОД.
    /// Если ход не штрафной — восстановление усталости на 5%. Новый ход начинается с EffectiveMaxAp ОД.
    /// </summary>
    public void EndTurn()
    {
        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;
        _runMovementApSpentThisTurn = 0;
        _currentAp = _maxAp;
        SetMovementPostureInternal(MovementPosture.Walk, notify: true);

        _turnPath.Clear();
        _turnActions.Clear();
        _turnCount++;
        _turnDeadlineUtcMs = BuildRoundDeadlineUtcMs(_turnDurationSeconds);
        _turnTimeLeft = GetRemainingTimeFromDeadline();
        _turnTimeExpired = false;
    }

    /// <summary>Стоимость n-го шага в рамках одного перемещения.</summary>
    public int GetStepCost(int stepIndex)
    {
        if (stepIndex <= 0)
            return 0;

        float n = stepIndex;
        float value = (5f * n * n - 8f * n + 21f) / 3f;
        return Mathf.Max(1, Mathf.RoundToInt(value));
    }

    public int GetMoveStepCost(MovementPosture posture, int stepIndex)
    {
        int baseStepCost = GetStepCost(stepIndex) - GetStepCost(stepIndex - 1);
        return posture switch
        {
            MovementPosture.Run => Mathf.Max(1, Mathf.CeilToInt(baseStepCost * RunCostMultiplier)),
            MovementPosture.Sit or MovementPosture.Hide => Mathf.Max(1, Mathf.FloorToInt(baseStepCost * SitCostMultiplier)),
            _ => Mathf.Max(1, baseStepCost)
        };
    }

    /// <summary>Стоимость перехода на steps шагов, если уже сделано fromStepIndex шагов в этом ходу.</summary>
    public int GetMoveCost(int fromStepIndex, int steps)
    {
        return GetMoveCost(PreviewMovementPosture, fromStepIndex, steps);
    }

    public int GetMoveCost(MovementPosture posture, int fromStepIndex, int steps)
    {
        if (steps <= 0)
            return 0;

        int total = 0;
        for (int i = 1; i <= steps; i++)
            total += GetMoveStepCost(posture, fromStepIndex + i);
        return total;
    }

    /// <summary>Копия пути за текущий ход (для отправки на сервер). Вызывать до EndTurn().</summary>
    public List<(int col, int row)> GetTurnPathCopy()
    {
        if (_turnPath == null) return new List<(int col, int row)>();
        return new List<(int col, int row)>(_turnPath);
    }

    public BattleQueuedAction[] GetTurnActionsCopy()
    {
        if (_turnActions == null || _turnActions.Count == 0)
            return Array.Empty<BattleQueuedAction>();

        var copy = new BattleQueuedAction[_turnActions.Count];
        for (int i = 0; i < _turnActions.Count; i++)
        {
            var src = _turnActions[i];
            copy[i] = new BattleQueuedAction
            {
                actionType = src.actionType,
                targetPosition = src.targetPosition != null ? new HexPosition(src.targetPosition.col, src.targetPosition.row) : null,
                targetUnitId = src.targetUnitId,
                bodyPart = src.bodyPart,
                posture = src.posture,
                previousPosture = src.previousPosture,
                cost = src.cost
            };
        }
        return copy;
    }

    public bool QueuePostureChange(MovementPosture posture)
    {
        if (_currentPosture == posture)
            return true;
        if (_currentAp < ChangePostureCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "ChangePosture",
            posture = MovementPostureUtility.ToId(posture),
            previousPosture = MovementPostureUtility.ToId(_currentPosture),
            cost = ChangePostureCost
        });
        _apSpentThisTurn += ChangePostureCost;
        _currentAp -= ChangePostureCost;
        SetMovementPostureInternal(posture, notify: true);
        return true;
    }

    public bool QueueWaitAction(int cost)
    {
        int safeCost = Mathf.Max(1, cost);
        if (_currentAp < safeCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "Wait",
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        return true;
    }

    public bool EnsureMovablePostureForMovement()
    {
        if (_currentPosture != MovementPosture.Hide)
            return true;
        return QueuePostureChange(MovementPosture.Sit);
    }

    public bool QueueAttackAction(string targetUnitId, string bodyPart, int cost = 1)
    {
        int safeCost = Mathf.Max(1, cost);
        if (_currentAp < safeCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "Attack",
            targetUnitId = targetUnitId,
            bodyPart = bodyPart,
            posture = MovementPostureUtility.ToId(_currentPosture),
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        return true;
    }

    /// <summary>Отменить последнее действие в очереди текущего хода (движение, атака, смена позы, ожидание).</summary>
    public bool TryUndoLastQueuedAction(out string failureReason)
    {
        failureReason = null;
        if (IsDead || _isHidden)
        {
            failureReason = "Недоступно";
            return false;
        }

        if (_isMoving)
        {
            failureReason = "Дождитесь окончания движения";
            return false;
        }

        if (_turnActions == null || _turnActions.Count == 0)
        {
            failureReason = "Нет действий для отмены";
            return false;
        }

        BattleQueuedAction last = _turnActions[_turnActions.Count - 1];
        _turnActions.RemoveAt(_turnActions.Count - 1);

        string type = last.actionType ?? string.Empty;
        if (string.Equals(type, "MoveStep", StringComparison.OrdinalIgnoreCase))
        {
            int cost = Mathf.Max(0, last.cost);
            _currentAp += cost;
            _apSpentThisTurn -= cost;
            if (_stepsTakenThisTurn > 0)
                _stepsTakenThisTurn--;

            MovementPosture stepPosture = MovementPostureUtility.FromId(last.posture);
            if (stepPosture == MovementPosture.Run && cost > 0)
                _runMovementApSpentThisTurn = Mathf.Max(0, _runMovementApSpentThisTurn - cost);

            if (_turnPath != null && _turnPath.Count > 1)
                _turnPath.RemoveAt(_turnPath.Count - 1);

            if (_turnPath != null && _turnPath.Count > 0)
            {
                var back = _turnPath[_turnPath.Count - 1];
                _currentCol = back.col;
                _currentRow = back.row;
                if (_grid != null)
                {
                    transform.position = _grid.GetCellWorldPosition(_currentCol, _currentRow);
                    HexCell cell = _grid.GetCell(_currentCol, _currentRow);
                    OnMovedToCell?.Invoke(cell);
                }
            }

            return true;
        }

        if (string.Equals(type, "ChangePosture", StringComparison.OrdinalIgnoreCase))
        {
            int cost = Mathf.Max(0, last.cost);
            _currentAp += cost;
            _apSpentThisTurn -= cost;
            MovementPosture restore = MovementPostureUtility.FromId(last.previousPosture);
            SetMovementPostureInternal(restore, notify: true);
            return true;
        }

        if (string.Equals(type, "Wait", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            int cost = Mathf.Max(0, last.cost);
            _currentAp += cost;
            _apSpentThisTurn -= cost;
            return true;
        }

        _turnActions.Add(last);
        failureReason = "Неизвестный тип действия";
        return false;
    }

    public bool IsPenaltyHexAtDistance(int distanceFromCurrent)
    {
        if (distanceFromCurrent <= 0)
            return false;

        MovementPosture posture = PreviewMovementPosture;
        if (posture != MovementPosture.Run)
            return false;

        int totalRunCost = _runMovementApSpentThisTurn + GetMoveCost(MovementPosture.Run, _stepsTakenThisTurn, distanceFromCurrent);
        int threshold = Mathf.Max(1, Mathf.CeilToInt(_maxAp * RunPenaltyThresholdFraction));
        return totalRunCost >= threshold;
    }

    /// <summary>Максимум шагов, которые можно пройти при текущих ОД (уже сделано stepsAlready, лимит stepsLimit).</summary>
    private int GetAllowedSteps(MovementPosture posture, int stepsAlready, int stepsLimit, int currentAp)
    {
        int allowed = 0;
        for (int steps = 1; steps <= stepsLimit; steps++)
        {
            if (GetMoveCost(posture, stepsAlready, steps) > currentAp)
                break;
            allowed = steps;
        }
        return allowed;
    }

    /// <summary>Установить штраф с сервера (доля 0–0.9).</summary>
    public void SetPenaltyFraction(float value)
    {
        value = Mathf.Clamp(value, 0f, MaxPenaltyFraction);
    }

    public void SetMovementPostureFromServer(string postureId)
    {
        SetMovementPostureInternal(MovementPostureUtility.FromId(postureId), notify: true);
    }

    /// <summary>Пауза таймера хода (ожидание сервера после «Завершить ход»).</summary>
    public void SetTurnTimerPaused(bool paused)
    {
        if (_turnTimerPaused == paused) return;

        if (paused)
            _turnTimeLeft = GetRemainingTimeFromDeadline();
        else if (!_turnTimeExpired)
            _turnDeadlineUtcMs = BuildRoundDeadlineUtcMs(_turnTimeLeft);

        _turnTimerPaused = paused;
        if (paused) _turnTimeExpired = false;
    }

    /// <summary>Синхронизировать номер раунда и дедлайн таймера с сервером.</summary>
    public void SetRoundState(int roundIndex, long roundDeadlineUtcMs)
    {
        _turnCount = roundIndex;
        _turnDeadlineUtcMs = roundDeadlineUtcMs > 0 ? roundDeadlineUtcMs : BuildRoundDeadlineUtcMs(_turnDurationSeconds);
        _turnTimeLeft = GetRemainingTimeFromDeadline();
        _turnTimeExpired = _turnTimeLeft <= 0f;
    }

    public static long BuildRoundDeadlineUtcMs(float durationSeconds)
    {
        long durationMs = (long)Mathf.Round(Mathf.Max(0f, durationSeconds) * 1000f);
        return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + durationMs;
    }

    private float GetRemainingTimeFromDeadline()
    {
        if (_turnDeadlineUtcMs <= 0)
            return Mathf.Max(0f, _turnTimeLeft);

        long remainingMs = _turnDeadlineUtcMs - System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Mathf.Max(0f, remainingMs / 1000f);
    }

    /// <summary>Применить результат хода с сервера: ОД, штраф, финальная позиция; визуал ставится в начало пути для последующей анимации.</summary>
    public void ApplyServerTurnResult(HexPosition finalPosition, HexPosition[] actualPath, int currentAp, float penaltyFraction, string currentPosture, bool prepareForAnimation = true)
    {
        _currentAp = currentAp;
        _currentCol = finalPosition != null ? finalPosition.col : _currentCol;
        _currentRow = finalPosition != null ? finalPosition.row : _currentRow;
        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;
        _runMovementApSpentThisTurn = 0;
        SetMovementPostureInternal(MovementPostureUtility.FromId(currentPosture), notify: true);
        if (_turnPath != null) _turnPath.Clear();
        if (_turnActions != null) _turnActions.Clear();

        if (_grid != null && actualPath != null && actualPath.Length > 0)
        {
            var pos = prepareForAnimation ? actualPath[0] : actualPath[actualPath.Length - 1];
            transform.position = _grid.GetCellWorldPosition(pos.col, pos.row);
        }
        else if (_grid != null)
            transform.position = _grid.GetCellWorldPosition(_currentCol, _currentRow);
    }

    /// <summary>Проиграть анимацию движения по пути с сервера (actualPath). Запускать после ApplyServerTurnResult. Не меняет состояние.</summary>
    public IEnumerator PlayPathAnimation(HexPosition[] path)
    {
        if (_grid == null || path == null || path.Length < 2)
        {
            if (_grid != null && path != null && path.Length == 1)
                transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);
            yield break;
        }

        int interruptVersion = _movementInterruptVersion;
        _isMoving = true;
        transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);

        for (int i = 1; i < path.Length; i++)
        {
            if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
            {
                _isMoving = false;
                yield break;
            }

            var step = path[i];
            Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
            float elapsed = 0f;

            while (elapsed < _moveDurationPerHex)
            {
                if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
                {
                    _isMoving = false;
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _moveDurationPerHex);
                transform.position = Vector3.Lerp(transform.position, target, t);
                yield return null;
            }

            transform.position = target;
        }

        _isMoving = false;
    }

    /// <summary>Проиграть анимацию всего пути за ход (без изменения ОД и штрафов), используется при завершении хода с анимацией.</summary>
    public IEnumerator PlayLastMoveAnimation()
    {
        if (_grid == null || _turnPath == null || _turnPath.Count < 2) yield break;

        _isMoving = true;

        // Стартуем с исходной клетки хода.
        Vector3 startPos = _grid.GetCellWorldPosition(_turnPath[0].col, _turnPath[0].row);
        transform.position = startPos;

        for (int i = 1; i < _turnPath.Count; i++)
        {
            var step = _turnPath[i];
            Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
            float elapsed = 0f;

            while (elapsed < _moveDurationPerHex)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _moveDurationPerHex);
                transform.position = Vector3.Lerp(transform.position, target, t);
                yield return null;
            }

            transform.position = target;
        }

        _isMoving = false;
    }

    public void ForceStopMovement()
    {
        _movementInterruptVersion++;
        StopAllCoroutines();
        _isMoving = false;
    }

    public void SetHidden(bool hidden)
    {
        if (_isHidden == hidden)
            return;

        _isHidden = hidden;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = !hidden;

        foreach (var collider in GetComponentsInChildren<Collider>(true))
            collider.enabled = !hidden;
    }

    public void SetHealth(int currentHp, int maxHp)
    {
        _maxHp = Mathf.Max(1, maxHp);
        _currentHp = Mathf.Clamp(currentHp, 0, _maxHp);
    }

    private void SetMovementPostureInternal(MovementPosture posture, bool notify)
    {
        if (_currentPosture == posture)
            return;

        _currentPosture = posture;
        if (notify)
            OnMovementPostureChanged?.Invoke(_currentPosture);
    }
}
