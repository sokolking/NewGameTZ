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
    [SerializeField] private int _apPerStep = 10;

    [Header("Таймер хода, сек")]
    [SerializeField] private float _turnDurationSeconds = 10f;

    private int _currentCol;
    private int _currentRow;
    private bool _isMoving;
    private int _currentAp;
    private int _turnCount;

    // Накопленный штраф в долях от MaxAp (0–1): 0 = нет штрафа, 0.1 = -10% от MaxAp.
    private float _penaltyFraction;
    // Сколько ОД уже потрачено в текущем ходу (используется только для расчёта штрафа).
    private int _apSpentThisTurn;
    // Эффективный максимум ОД в начале текущего хода (до трат в этом ходу).
    private int _turnStartMaxAp;
    // Последний путь за этот ход (для анимации при завершении хода).
    private List<(int col, int row)> _lastMovePath;
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
    public int StepCostPerHex => _apPerStep;
    public int TurnCount => _turnCount;
    /// <summary>Сколько ОД уже потрачено в текущем ходу.</summary>
    public int ApSpentThisTurn => _apSpentThisTurn;
    /// <summary>Оставшееся время текущего хода (сек).</summary>
    public float TurnTimeLeft => _turnTimeLeft;
    /// <summary>Длительность хода (сек).</summary>
    public float TurnDurationSeconds => _turnDurationSeconds;
    /// <summary>Истёк ли таймер хода.</summary>
    public bool TurnTimeExpired => _turnTimeExpired;

    public delegate void PlayerMovedHandler(HexCell cell);
    /// <summary>Событие: игрок завершил перемещение в новую ячейку (после MoveAlongPath, телепорта или анимации).</summary>
    public event PlayerMovedHandler OnMovedToCell;

    private void Awake()
    {
        _penaltyFraction = 0f;
        _apSpentThisTurn = 0;
        _turnStartMaxAp = GetEffectiveMaxAp();
        _currentAp = _turnStartMaxAp;
        _turnTimeLeft = _turnDurationSeconds;
        _turnTimeExpired = false;
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

    /// <summary>Запускает движение по пути (список (col, row)), ограничивая длину по ОД. animate=false – телепорт.</summary>
    public void MoveAlongPath(List<(int col, int row)> path, bool animate)
    {
        if (path == null || path.Count < 2 || _isMoving || _grid == null) return;

        int steps = path.Count - 1;
        int maxStepsByAp = _apPerStep > 0 ? _currentAp / _apPerStep : steps;
        int allowedSteps = Mathf.Min(steps, maxStepsByAp);
        if (allowedSteps <= 0) return;

        // Сколько ОД потратим этим движением
        int moveAp = allowedSteps * _apPerStep;
        int prevSpent = _apSpentThisTurn;
        int newSpent = prevSpent + moveAp;

        // Пороги 90% и 100% считаем от БАЗОВОГО максимума ОД (_maxAp), а не от урезанного.
        int threshold90 = Mathf.RoundToInt(_maxAp * 0.9f);
        int threshold100 = _maxAp;

        // Штраф начисляется только за трату ОД >= 90% от максимума:
        // если за ход добежали до 100% или больше — штраф 10%, иначе, если хотя бы до 90% — штраф 7%.
        if (prevSpent < threshold90 && newSpent >= threshold100)
        {
            AddPenalty(0.10f);
        }
        else if (prevSpent < threshold90 && newSpent >= threshold90)
        {
            AddPenalty(0.07f);
        }

        _apSpentThisTurn = newSpent;

        var subPath = path.GetRange(0, allowedSteps + 1);
        _lastMovePath = subPath;
        _currentAp -= moveAp;

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
    /// - Если за ход потрачено &gt;= 90% от максимума ОД этого хода — ход считается штрафным (финиш на штрафном гексе).
    /// - Если ход НЕ штрафной — штраф уменьшается на 5% (игрок \"отдохнул\").
    /// - В любом случае новый ход начинается с EffectiveMaxAp ОД.
    /// </summary>
    public void EndTurn()
    {
        // Проверяем, был ли ход штрафным: суммарно потратили >= 90% от БАЗОВОГО максимума ОД.
        int threshold90 = Mathf.RoundToInt(_maxAp * 0.9f);
        bool endedOnPenaltyHex = _apSpentThisTurn >= threshold90;

        // Если ход НЕ штрафной — уменьшаем накопленный штраф на 5% (игрок отдохнул).
        if (!endedOnPenaltyHex)
        {
            AddPenalty(-0.05f);
        }

        _apSpentThisTurn = 0;

        // Новый эффективный максимум на следующий ход с учётом изменённого штрафа.
        int newTurnMaxAp = GetEffectiveMaxAp();
        _turnStartMaxAp = newTurnMaxAp;
        _currentAp = newTurnMaxAp;

        _lastMovePath = null;
        _turnCount++;
        _turnTimeLeft = _turnDurationSeconds;
        _turnTimeExpired = false;
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

    /// <summary>Проиграть анимацию последнего перемещения (без изменения ОД и координат), используется при завершении хода с анимацией.</summary>
    public IEnumerator PlayLastMoveAnimation()
    {
        if (_grid == null || _lastMovePath == null || _lastMovePath.Count < 2) yield break;

        _isMoving = true;

        // Стартуем с исходной клетки пути.
        Vector3 startPos = _grid.GetCellWorldPosition(_lastMovePath[0].col, _lastMovePath[0].row);
        transform.position = startPos;

        for (int i = 1; i < _lastMovePath.Count; i++)
        {
            var step = _lastMovePath[i];
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
