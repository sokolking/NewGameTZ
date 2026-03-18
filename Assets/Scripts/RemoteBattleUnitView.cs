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

    public string NetworkPlayerId { get; private set; }
    public bool IsMoving => _isMoving;
    public bool IsMob => !string.IsNullOrEmpty(NetworkPlayerId) && NetworkPlayerId.StartsWith("MOB_", StringComparison.OrdinalIgnoreCase);

    public void Initialize(string playerId, HexGrid grid, int startCol, int startRow, float moveDurationPerHex = -1f)
    {
        NetworkPlayerId = playerId;
        _grid = grid;
        if (moveDurationPerHex > 0f) _moveDurationPerHex = moveDurationPerHex;
        if (_grid != null)
            transform.position = _grid.GetCellWorldPosition(startCol, startRow);
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
        UnityEngine.Object.Destroy(cap.GetComponent<Collider>());
    }

    public void ApplyServerTurnResult(HexPosition finalPosition, HexPosition[] actualPath, int currentAp, float penaltyFraction, bool prepareForAnimation = true)
    {
        if (_grid == null || actualPath == null || actualPath.Length == 0)
        {
            if (_grid != null && finalPosition != null)
                transform.position = _grid.GetCellWorldPosition(finalPosition.col, finalPosition.row);
            return;
        }
        var pos = prepareForAnimation ? actualPath[0] : actualPath[actualPath.Length - 1];
        transform.position = _grid.GetCellWorldPosition(pos.col, pos.row);
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
        _isMoving = false;
    }
}
