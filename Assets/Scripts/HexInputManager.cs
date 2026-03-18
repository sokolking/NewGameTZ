using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Наведение на гекс (подсветка) и двойной клик — путь и движение игрока.
/// Использует Input System package.
/// </summary>
public class HexInputManager : MonoBehaviour
{
    private const float DoubleClickTime = 0.3f;
    private const float DoubleClickMaxDist = 10f;

    [SerializeField] private Camera _camera;
    [SerializeField] private HexGrid _grid;
    [SerializeField] private Player _player;
    [SerializeField] private LayerMask _hexLayer = -1;

    private float _lastClickTime;
    private Vector2 _lastClickPosition;
    private HexCell _lastHoveredCell;

    private void Update()
    {
        if (_grid == null) return;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;
        if (Mouse.current == null) return;
        if (GameSession.Active != null && GameSession.Active.BlockPlayerInput)
        {
            if (_lastHoveredCell != null)
            {
                _lastHoveredCell.SetHighlight(false);
                _lastHoveredCell.SetCostLabel(-1);
                _lastHoveredCell = null;
            }
            return;
        }

        UpdateHover();
        UpdateDoubleClick();
    }

    private void UpdateHover()
    {
        HexCell cell = GetHexUnderCursor();
        if (cell == _lastHoveredCell) return;

        if (_lastHoveredCell != null)
        {
            _lastHoveredCell.SetHighlight(false);
            _lastHoveredCell.SetCostLabel(-1);
        }

        _lastHoveredCell = cell;
        if (cell != null)
        {
            cell.SetHighlight(true);

            if (_player != null)
            {
                int steps = HexGrid.GetDistance(_player.CurrentCol, _player.CurrentRow, cell.Col, cell.Row);
                int stepCost = _player.GetMoveCost(_player.StepsTakenThisTurn, steps);
                cell.SetCostLabel(stepCost);
            }
        }
    }

    private void UpdateDoubleClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        float time = Time.time;

        if (time - _lastClickTime <= DoubleClickTime &&
            Vector2.Distance(mousePos, _lastClickPosition) <= DoubleClickMaxDist)
        {
            _lastClickTime = 0f;
            OnDoubleClick();
            return;
        }

        _lastClickTime = time;
        _lastClickPosition = mousePos;
    }

    private void OnDoubleClick()
    {
        HexCell cell = GetHexUnderCursor();
        if (cell == null || _player == null || _player.IsMoving) return;

        List<(int col, int row)> path = HexPathfinding.FindPath(
            _grid,
            _player.CurrentCol, _player.CurrentRow,
            cell.Col, cell.Row);

        if (path != null && path.Count > 0)
            _player.MoveAlongPath(path, animate: true);
    }

    private HexCell GetHexUnderCursor()
    {
        if (Mouse.current == null) return null;
        Vector2 pos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, _hexLayer))
            return null;

        return hit.collider.GetComponent<HexCell>();
    }
}
