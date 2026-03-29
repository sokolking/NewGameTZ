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
    private bool _wasBlocked;
    private bool _wasMoving;

    private HexCell[] _cachedAllCells;
    private readonly HashSet<(int col, int row)> _visitedBfs = new();
    private readonly Queue<(int col, int row, int dist)> _queueBfs = new();

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

        if (_player.CurrentCol != _lastCol ||
            _player.CurrentRow != _lastRow ||
            _player.CurrentAp != _lastAp)
        {
            _lastCol = _player.CurrentCol;
            _lastRow = _player.CurrentRow;
            _lastAp = _player.CurrentAp;
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

    private int GetMaxReachableSteps(int stepsAlready)
    {
        int maxSteps = 0;
        for (int L = 1; ; L++)
        {
            if (_player.GetMoveCost(stepsAlready, L) > _player.CurrentAp)
                break;
            maxSteps = L;
        }
        return maxSteps;
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

        int stepsAlready = _player.StepsTakenThisTurn;
        int maxSteps = GetMaxReachableSteps(stepsAlready);
        if (maxSteps <= 0) return;

        _visitedBfs.Clear();
        while (_queueBfs.Count > 0)
            _queueBfs.Dequeue();

        _visitedBfs.Add((_player.CurrentCol, _player.CurrentRow));
        _queueBfs.Enqueue((_player.CurrentCol, _player.CurrentRow, 0));

        while (_queueBfs.Count > 0)
        {
            var (col, row, dist) = _queueBfs.Dequeue();

            HexCell cell = _grid.GetCell(col, row);
            if (cell != null && !cell.IsObstacle)
            {
                bool reachableNow = dist <= maxSteps;
                bool isPenaltyRing = reachableNow && _player.IsPenaltyHexAtDistance(dist);

                Color c = isPenaltyRing ? _farColor : _nearColor;
                c.a = 1f;
                cell.SetApMask(true, c);
            }

            if (dist >= maxSteps) continue;

            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(col, row, dir, out int nc, out int nr);
                var key = (nc, nr);
                if (_visitedBfs.Contains(key))
                    continue;
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

                _visitedBfs.Add(key);
                _queueBfs.Enqueue((nc, nr, dist + 1));
            }
        }
    }
}

