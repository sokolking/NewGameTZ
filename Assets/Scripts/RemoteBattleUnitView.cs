using System.Collections;
using UnityEngine;
using System;

/// <summary>
/// Визуал удалённого игрока на сетке (Этап 4): позиция и анимация по actualPath с сервера.
/// Не обрабатывает ввод и локальные ОД.
/// </summary>
public class RemoteBattleUnitView : MonoBehaviour
{
    [SerializeField] private HexGrid _grid;
    [SerializeField] private float _moveDurationPerHex = 0.2f;

    private bool _isMoving;
    private int _maxHp = 10;
    private int _currentHp = 10;

    public string NetworkPlayerId { get; private set; }
    public bool IsMoving => _isMoving;
    public bool IsMob => !string.IsNullOrEmpty(NetworkPlayerId) && NetworkPlayerId.StartsWith("MOB_", StringComparison.OrdinalIgnoreCase);
    public int CurrentCol { get; private set; }
    public int CurrentRow { get; private set; }
    public int CurrentHp => _currentHp;
    public int MaxHp => _maxHp;

    public void Initialize(string playerId, HexGrid grid, int startCol, int startRow, float moveDurationPerHex = -1f)
    {
        NetworkPlayerId = playerId;
        _grid = grid;
        if (moveDurationPerHex > 0f) _moveDurationPerHex = moveDurationPerHex;
        if (_grid != null)
        {
            transform.position = _grid.GetCellWorldPosition(startCol, startRow);
            CurrentCol = startCol;
            CurrentRow = startRow;
        }
        EnsureVisual();
    }

    private void EnsureVisual()
    {
        if (GetComponentInChildren<MeshFilter>() != null) return;
        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.name = "Visual";
        cap.transform.SetParent(transform);
        cap.transform.localPosition = Vector3.zero;
        cap.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);
        var r = cap.GetComponent<Renderer>();
        if (r != null) r.material.color = new Color(0.85f, 0.35f, 0.25f, 1f);
        // Оставляем коллайдер для raycast (удержание ПКМ по юниту).
        Collider capCollider = cap.GetComponent<Collider>();
        if (capCollider != null)
            capCollider.isTrigger = true;
    }

    public void ApplyServerTurnResult(HexPosition finalPosition, HexPosition[] actualPath, int currentAp, float penaltyFraction, bool prepareForAnimation = true)
    {
        if (_grid == null || actualPath == null || actualPath.Length == 0)
        {
            if (_grid != null && finalPosition != null)
            {
                transform.position = _grid.GetCellWorldPosition(finalPosition.col, finalPosition.row);
                CurrentCol = finalPosition.col;
                CurrentRow = finalPosition.row;
            }
            return;
        }
        var pos = prepareForAnimation ? actualPath[0] : actualPath[actualPath.Length - 1];
        transform.position = _grid.GetCellWorldPosition(pos.col, pos.row);
        CurrentCol = finalPosition != null ? finalPosition.col : pos.col;
        CurrentRow = finalPosition != null ? finalPosition.row : pos.row;
    }

    public void SetHealth(int currentHp, int maxHp)
    {
        _maxHp = Mathf.Max(1, maxHp);
        _currentHp = Mathf.Clamp(currentHp, 0, _maxHp);
    }

    public IEnumerator PlayPathAnimation(HexPosition[] path)
    {
        if (_grid == null || path == null || path.Length < 2)
        {
            if (_grid != null && path != null && path.Length == 1)
                transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);
            yield break;
        }

        _isMoving = true;
        transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);

        for (int i = 1; i < path.Length; i++)
        {
            var step = path[i];
            Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
            Vector3 stepStart = transform.position;
            float elapsed = 0f;
            while (elapsed < _moveDurationPerHex)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _moveDurationPerHex);
                transform.position = Vector3.Lerp(stepStart, target, t);
                yield return null;
            }
            transform.position = target;
        }

        _isMoving = false;
    }

    public void ForceStopMovement()
    {
        _isMoving = false;
    }
}
