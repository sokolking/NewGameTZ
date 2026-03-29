using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Подсвечивает достижимые за текущие ОД гексы.
/// Серые — все клетки в радиусе оставшихся ОД.
/// Красные — предпоследний и последний шаг при максимальных ОД (если достижимы). Подсветка непрозрачная (только цвет).
/// </summary>
public class MovementRangeHighlighter : MonoBehaviour
{
    [SerializeField] private HexGrid _grid;
    [SerializeField] private Player _player;
    [Tooltip("Near cells — light gray (opaque fill over semi-transparent grid).")]
    [SerializeField] private Color _nearColor = new Color(0.92f, 0.92f, 0.92f, 1f);
    [Tooltip("Far cells — red penalty ring (opaque).")]
    [SerializeField] private Color _farColor = new Color(1f, 0f, 0f, 1f);

    private int _lastCol = int.MinValue;
    private int _lastRow = int.MinValue;
    private int _lastAp = int.MinValue;
    private int _lastStepsTaken = int.MinValue;
    private MovementPosture? _lastPreviewPosture;
    private bool _wasBlocked;
    private bool _wasMoving;

    private HexCell[] _cachedAllCells;
    /// <summary>Max AP remaining after reaching (col,row) in exactly k steps from current cell.</summary>
    private readonly Dictionary<(int col, int row, int k), int> _bestApByCellAndSteps = new();
    private readonly Queue<(int col, int row, int apRem, int k)> _queueApBfs = new();
    private readonly Dictionary<(int col, int row), int> _minStepsToReachCell = new();

    private void OnValidate()
    {
#if UNITY_EDITOR
        TryRefreshMaskAfterInspectorChange();
#endif
    }

#if UNITY_EDITOR
    /// <summary>В Play Mode сразу перерисовать маску после изменения ползунка/цветов в инспекторе.</summary>
    private void TryRefreshMaskAfterInspectorChange()
    {
        if (!Application.isPlaying || !enabled || _grid == null || _player == null) return;
        if (_player.IsDead || _player.IsHidden) return;
        if (GameplayMapInputBlock.IsBlocked
            || (GameSession.Active != null && GameSession.Active.BlockPlayerInput)) return;
        if (_player.IsMoving) return;
        RebuildMask();
    }
#endif

    private void Update()
    {
        if (_grid == null || _player == null) return;
        if (_player.IsDead || _player.IsHidden)
        {
            ClearMask();
            _wasBlocked = false;
            _wasMoving = false;
            return;
        }
        bool isBlocked = GameplayMapInputBlock.IsBlocked
            || (GameSession.Active != null && GameSession.Active.BlockPlayerInput);
        if (isBlocked)
        {
            ClearMask();
            _wasBlocked = true;
            return;
        }
        // Сняли блокировку ввода — нужно восстановить подсветку даже если ОД/позиция не менялись.
        if (_wasBlocked)
        {
            _wasBlocked = false;
            RebuildMask();
        }

        bool isMoving = _player.IsMoving;
        if (isMoving)
        {
            ClearMask();
            _wasMoving = true;
            return;
        }
        if (_wasMoving)
        {
            _wasMoving = false;
            RebuildMask();
        }

        bool stateDirty = _player.CurrentCol != _lastCol
            || _player.CurrentRow != _lastRow
            || _player.CurrentAp != _lastAp
            || _player.StepsTakenThisTurn != _lastStepsTaken
            || !_lastPreviewPosture.HasValue
            || _lastPreviewPosture.Value != _player.PreviewMovementPosture;
        if (stateDirty)
        {
            _lastCol = _player.CurrentCol;
            _lastRow = _player.CurrentRow;
            _lastAp = _player.CurrentAp;
            _lastStepsTaken = _player.StepsTakenThisTurn;
            _lastPreviewPosture = _player.PreviewMovementPosture;
            RebuildMask();
        }
    }

    private void EnsureCellsCache()
    {
        if (_grid == null)
            return;
        if (_cachedAllCells == null || _cachedAllCells.Length == 0)
        {
            // Используем кэш HexGrid вместо дорогого GetComponentsInChildren (O(n) поиск по иерархии).
            var cache = _grid.GetCellCache();
            if (cache != null)
            {
                int w = _grid.Width;
                int l = _grid.Length;
                _cachedAllCells = new HexCell[w * l];
                int idx = 0;
                for (int c = 0; c < w; c++)
                    for (int r = 0; r < l; r++)
                        _cachedAllCells[idx++] = cache[c, r];
            }
            else
            {
                _cachedAllCells = _grid.GetComponentsInChildren<HexCell>(true);
            }
        }
    }

    private void ClearMask()
    {
        EnsureCellsCache();
        if (_cachedAllCells == null)
            return;
        for (int i = 0; i < _cachedAllCells.Length; i++)
        {
            HexCell cell = _cachedAllCells[i];
            if (cell != null)
                cell.SetApMask(false, Color.clear);
        }
    }

    private void TryEnqueueApState(int col, int row, int apRem, int k)
    {
        var key = (col, row, k);
        if (_bestApByCellAndSteps.TryGetValue(key, out int prev) && apRem <= prev)
            return;
        _bestApByCellAndSteps[key] = apRem;
        _queueApBfs.Enqueue((col, row, apRem, k));
    }

    private void RebuildMask()
    {
        EnsureCellsCache();
        if (_cachedAllCells != null)
        {
            for (int i = 0; i < _cachedAllCells.Length; i++)
            {
                HexCell c = _cachedAllCells[i];
                if (c != null)
                    c.SetApMask(false, Color.clear);
            }
        }

        int startAp = _player.CurrentAp;
        if (startAp < 1)
            return;

        int sc = _player.CurrentCol;
        int sr = _player.CurrentRow;

        _bestApByCellAndSteps.Clear();
        _minStepsToReachCell.Clear();
        while (_queueApBfs.Count > 0)
            _queueApBfs.Dequeue();

        TryEnqueueApState(sc, sr, startAp, 0);

        while (_queueApBfs.Count > 0)
        {
            var (col, row, apRem, k) = _queueApBfs.Dequeue();
            var stateKey = (col, row, k);
            if (!_bestApByCellAndSteps.TryGetValue(stateKey, out int bestHere) || apRem < bestHere)
                continue;

            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(col, row, dir, out int nc, out int nr);
                if (GameSession.Active != null)
                {
                    if (!GameSession.Active.IsHexInActiveBattleZone(nc, nr)
                        && !GameSession.Active.IsEscapeBorderHex(nc, nr))
                        continue;
                }
                else if (!_grid.IsInBounds(nc, nr))
                    continue;

                if (_grid.IsInBounds(nc, nr))
                {
                    HexCell nextCell = _grid.GetCell(nc, nr);
                    if (nextCell == null || nextCell.IsObstacle)
                        continue;
                }

                int zeroBasedStepIndex = _player.StepsTakenThisTurn + k;
                int cost = _player.ComputePlanningEdgeApCost(col, row, nc, nr, zeroBasedStepIndex, apRem);
                if (cost == int.MaxValue || cost > apRem)
                    continue;
                int ap2 = apRem - cost;
                TryEnqueueApState(nc, nr, ap2, k + 1);
            }
        }

        foreach (var kv in _bestApByCellAndSteps)
        {
            var (col, row, k) = kv.Key;
            var cellKey = (col, row);
            if (!_minStepsToReachCell.TryGetValue(cellKey, out int mk) || k < mk)
                _minStepsToReachCell[cellKey] = k;
        }

        foreach (var kv in _minStepsToReachCell)
        {
            var (col, row) = kv.Key;
            int dist = kv.Value;
            HexCell cell = _grid.GetCell(col, row);
            if (cell == null || cell.IsObstacle)
                continue;

            bool isPenaltyRing = _player.IsPenaltyHexAtDistance(dist);
            Color c = isPenaltyRing ? _farColor : _nearColor;
            c.a = 1f;
            cell.SetApMask(true, c);
        }
    }
}

