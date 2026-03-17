using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Игрок на гекс-сетке: текущая ячейка, очки действия и счётчик ходов.
/// </summary>
public class Player : MonoBehaviour
{
    [Header("Сетка")]
    [SerializeField] private HexGrid _grid;

    [Header("Движение")]
    [SerializeField] private float _moveDurationPerHex = 0.2f;

    [Header("Очки действия")]
    [SerializeField] private int _maxAp = 100;

    [Header("Таймер хода, сек")]
    [SerializeField] private float _turnDurationSeconds = 10f;

    private const float PrelastPenaltyFraction = 0.05f;  // штраф за предпоследний шаг при макс. ОД
    private const float LastPenaltyFraction = 0.08f;      // штраф за последний шаг при макс. ОД
    private const float RecoveryFraction = 0.05f;         // восстановление при завершении хода не на штрафном гексе

    private int _currentCol;
    private int _currentRow;
    private bool _isMoving;
    private int _currentAp;
    private int _turnCount;

    // Накопленный штраф в долях от MaxAp (0–1): 0 = нет штрафа, 0.1 = -10% от MaxAp.
    private float _penaltyFraction;
    // Сколько ОД уже потрачено в текущем ходу (используется для штрафа и подсчёта шагов).
    private int _apSpentThisTurn;
    // Сколько "шагов" уже совершено в этом ходу (независимо от направления).
    private int _stepsTakenThisTurn;
    // Эффективный максимум ОД в начале текущего хода (до трат в этом ходу).
    private int _turnStartMaxAp;
    // Полный путь за текущий ход (все переходы по гексам, в порядке кликов).
    private List<(int col, int row)> _turnPath;
    // Остаток времени текущего хода (сек).
    private float _turnTimeLeft;
    // Флаг, что таймер хода уже истёк (для внешней логики автозавершения).
    private bool _turnTimeExpired;

    public int CurrentCol => _currentCol;
    public int CurrentRow => _currentRow;
    public bool IsMoving => _isMoving;
    public int MaxAp => _maxAp;
    /// <summary>Текущие ОД.</summary>
    public int CurrentAp => _currentAp;
    public int TurnCount => _turnCount;
    /// <summary>Сколько ОД уже потрачено в текущем ходу.</summary>
    public int ApSpentThisTurn => _apSpentThisTurn;
    /// <summary>Оставшееся время текущего хода (сек).</summary>
    public float TurnTimeLeft => _turnTimeLeft;
    /// <summary>Длительность хода (сек).</summary>
    public float TurnDurationSeconds => _turnDurationSeconds;
    /// <summary>Истёк ли таймер хода.</summary>
    public bool TurnTimeExpired => _turnTimeExpired;
    /// <summary>Сколько "шагов" уже совершено в этом ходу.</summary>
    public int StepsTakenThisTurn => _stepsTakenThisTurn;

    public delegate void PlayerMovedHandler(HexCell cell);
    /// <summary>Событие: игрок завершил перемещение в новую ячейку (после MoveAlongPath, телепорта или анимации).</summary>
    public event PlayerMovedHandler OnMovedToCell;

    private void Awake()
    {
        _penaltyFraction = 0f;
        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;
        _turnStartMaxAp = GetEffectiveMaxAp();
        _currentAp = _turnStartMaxAp;
        _turnTimeLeft = _turnDurationSeconds;
        _turnTimeExpired = false;
        _turnPath = new List<(int col, int row)>();
    }

    private void Start()
    {
        if (GetComponentInChildren<MeshFilter>() == null && GetComponentInChildren<MeshRenderer>() == null)
            CreateDefaultVisual();

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
        if (_turnTimeLeft > 0f)
        {
            _turnTimeLeft -= Time.deltaTime;
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
        Object.Destroy(cap.GetComponent<Collider>());
    }

    /// <summary>Запускает движение по пути (список (col, row)), ограничивая длину по ОД. animate=false – телепорт.
    /// Стоимость перехода на L клеток при уже сделанных K шагах = GetStepCost(K+L) − GetStepCost(K).</summary>
    public void MoveAlongPath(List<(int col, int row)> path, bool animate)
    {
        if (path == null || path.Count < 2 || _isMoving || _grid == null) return;

        int stepsToMove = path.Count - 1;
        if (stepsToMove <= 0) return;

        int stepsAlready = _stepsTakenThisTurn;
        int allowedSteps = GetAllowedSteps(stepsAlready, stepsToMove, _currentAp);
        if (allowedSteps <= 0) return;

        int moveCost = GetMoveCost(stepsAlready, allowedSteps);
        int prevSpent = _apSpentThisTurn;
        int newSpent = prevSpent + moveCost;

        GetPenaltyStepCosts(out int prelastCost, out int lastCost);
        if (prelastCost > 0 && newSpent == prelastCost)
            AddPenalty(PrelastPenaltyFraction);
        if (lastCost > 0 && prevSpent < lastCost && newSpent >= lastCost)
            AddPenalty(LastPenaltyFraction);

        _apSpentThisTurn = newSpent;
        _stepsTakenThisTurn = stepsAlready + allowedSteps;
        _currentAp -= moveCost;

        var subPath = path.GetRange(0, allowedSteps + 1);

        // Накопить полный путь за ход:
        // при первом перемещении добавляем стартовую клетку, далее — только новые шаги.
        if (_turnPath == null)
            _turnPath = new List<(int col, int row)>();
        if (_turnPath.Count == 0)
            _turnPath.Add((_currentCol, _currentRow));
        for (int i = 1; i < subPath.Count; i++)
            _turnPath.Add(subPath[i]);

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
        _isMoving = true;

        foreach (var step in path)
        {
            if (step.col == _currentCol && step.row == _currentRow)
                continue;

            Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
            float elapsed = 0f;

            while (elapsed < _moveDurationPerHex)
            {
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
        // Штрафный ход — закончили на предпоследнем или последнем шаге при макс. ОД.
        GetPenaltyStepCosts(out int prelastCost, out int lastCost);
        bool endedOnPenaltyHex = _apSpentThisTurn == prelastCost || _apSpentThisTurn == lastCost;

        if (!endedOnPenaltyHex)
            AddPenalty(-RecoveryFraction);

        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;

        // Новый эффективный максимум на следующий ход с учётом изменённого штрафа.
        int newTurnMaxAp = GetEffectiveMaxAp();
        _turnStartMaxAp = newTurnMaxAp;
        _currentAp = newTurnMaxAp;

        _turnPath.Clear();
        _turnCount++;
        _turnTimeLeft = _turnDurationSeconds;
        _turnTimeExpired = false;
    }

    /// <summary>Стоимость n-го шага в рамках одного перемещения.</summary>
    public int GetStepCost(int stepIndex)
    {
        if (stepIndex <= 0) return 0;
        float n = stepIndex;
        float val = (5f * n * n - 8f * n + 21f) / 3f;
        return Mathf.Max(1, Mathf.RoundToInt(val));
    }

    /// <summary>Стоимость перехода на steps шагов, если уже сделано fromStepIndex шагов в этом ходу.</summary>
    public int GetMoveCost(int fromStepIndex, int steps)
    {
        if (steps <= 0) return 0;
        return GetStepCost(fromStepIndex + steps) - GetStepCost(fromStepIndex);
    }

    /// <summary>Копия пути за текущий ход (для отправки на сервер). Вызывать до EndTurn().</summary>
    public List<(int col, int row)> GetTurnPathCopy()
    {
        if (_turnPath == null) return new List<(int col, int row)>();
        return new List<(int col, int row)>(_turnPath);
    }

    /// <summary>При максимальных ОД — стоимость предпоследнего и последнего достижимого шага (для штрафных гексов).</summary>
    public void GetPenaltyStepCosts(out int prelastCost, out int lastCost)
    {
        int N = 0;
        while (GetStepCost(N + 1) <= _maxAp)
            N++;
        if (N <= 0)
        {
            prelastCost = 0;
            lastCost = 0;
            return;
        }
        lastCost = GetStepCost(N);
        prelastCost = N >= 2 ? GetStepCost(N - 1) : 0;
    }

    /// <summary>Максимум шагов, которые можно пройти при текущих ОД (уже сделано stepsAlready, лимит stepsLimit).</summary>
    private int GetAllowedSteps(int stepsAlready, int stepsLimit, int currentAp)
    {
        int allowed = 0;
        for (int L = 1; L <= stepsLimit; L++)
        {
            if (GetMoveCost(stepsAlready, L) > currentAp)
                break;
            allowed = L;
        }
        return allowed;
    }

    /// <summary>Текущий максимальный ОД с учётом накопленного штрафа.</summary>
    private int GetEffectiveMaxAp()
    {
        float factor = Mathf.Clamp01(1f - _penaltyFraction);
        return Mathf.Max(0, Mathf.RoundToInt(_maxAp * factor));
    }

    /// <summary>Изменить штраф на delta (доля от MaxAp), с ограничением 0–0.9.</summary>
    private void AddPenalty(float deltaFraction)
    {
        _penaltyFraction = Mathf.Clamp(_penaltyFraction + deltaFraction, 0f, 0.9f);
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
}
