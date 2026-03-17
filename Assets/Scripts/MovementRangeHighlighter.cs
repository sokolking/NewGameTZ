using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Подсвечивает достижимые за текущие ОД гексы.
/// Серые — все клетки в радиусе оставшихся ОД.
/// Красные — круги на 90% и 100% от МАКСИМАЛЬНЫХ ОД (если ещё достижимы по текущим ОД). Альфа 10%.
/// </summary>
public class MovementRangeHighlighter : MonoBehaviour
{
    [SerializeField] private HexGrid _grid;
    [SerializeField] private Player _player;
    [Tooltip("Близкие клетки — светло-серый. Альфа 10%.")]
    [SerializeField] private Color _nearColor = new Color(0.92f, 0.92f, 0.92f, 0.1f);
    [Tooltip("Дальние клетки — красный. Альфа 10%.")]
    [SerializeField] private Color _farColor = new Color(1f, 0f, 0f, 0.1f);

    private int _lastCol = int.MinValue;
    private int _lastRow = int.MinValue;
    private int _lastAp = int.MinValue;

    private void Update()
    {
        if (_grid == null || _player == null) return;

        if (_player.IsMoving)
        {
            ClearMask();
            return;
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

    private void ClearMask()
    {
        foreach (HexCell cell in _grid.GetComponentsInChildren<HexCell>())
            cell.SetApMask(false, Color.clear);
    }

    private void RebuildMask()
    {
        // Сброс маски на всех
        foreach (HexCell cell in _grid.GetComponentsInChildren<HexCell>())
            cell.SetApMask(false, Color.clear);

        // Радиус достижимых гексов считаем по ОСТАВШИМСЯ ОД.
        int maxSteps = _player.StepCostPerHex > 0
            ? _player.CurrentAp / _player.StepCostPerHex
            : 0;
        if (maxSteps <= 0) return;

        var visited = new HashSet<(int col, int row)>();
        var queue = new Queue<(int col, int row, int dist)>();

        visited.Add((_player.CurrentCol, _player.CurrentRow));
        queue.Enqueue((_player.CurrentCol, _player.CurrentRow, 0));

        while (queue.Count > 0)
        {
            var (col, row, dist) = queue.Dequeue();

            HexCell cell = _grid.GetCell(col, row);
            if (cell != null)
            {
                int costPerHex = _player.StepCostPerHex;
                int maxAp = _player.MaxAp;
                int ringAp = costPerHex > 0 ? costPerHex * dist : 0;

                int threshold90 = Mathf.RoundToInt(maxAp * 0.9f);
                int threshold100 = maxAp; // 100%

                // Сколько всего ОД будет потрачено за ход, если добежать до этого кольца.
                int totalApIfGoHere = _player.ApSpentThisTurn + ringAp;

                // Кольцо считается штрафным, если суммарная трата за ход
                // попадает в диапазон [90%; 100%] от максимума ОД.
                bool reachableNow = dist <= maxSteps;
                bool isPenaltyRing = reachableNow &&
                                     totalApIfGoHere >= threshold90 &&
                                     totalApIfGoHere <= threshold100;

                Color c = isPenaltyRing ? _farColor : _nearColor;
                c.a = 0.1f;
                cell.SetApMask(true, c);
            }

            if (dist >= maxSteps) continue;

            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(col, row, dir, out int nc, out int nr);
                var key = (nc, nr);
                if (!visited.Contains(key) && _grid.IsInBounds(nc, nr))
                {
                    visited.Add(key);
                    queue.Enqueue((nc, nr, dist + 1));
                }
            }
        }
    }
}

