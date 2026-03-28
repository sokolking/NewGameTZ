using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Наведение на гекс (подсветка) и двойной клик — путь и движение игрока.
/// Использует Input System package.
/// </summary>
[DefaultExecutionOrder(50)]
public class HexInputManager : MonoBehaviour
{
    private const float DoubleClickTime = 0.3f;
    private const float DoubleClickMaxDist = 10f;
    private const float DoubleClickMaxDistSqr = DoubleClickMaxDist * DoubleClickMaxDist;

    [SerializeField] private Camera _camera;
    [SerializeField] private HexGrid _grid;
    [SerializeField] private Player _player;
    [SerializeField] private LayerMask _hexLayer = -1;
    [Header("Click other unit / mob")]
    [Tooltip("Prefab with HoldTargetIndicator (Tools/UI/Create Hold Target Indicator Prefab).")]
    [SerializeField] private HoldTargetIndicator _holdIndicatorPrefab;

    private float _lastClickTime;
    private Vector2 _lastClickPosition;
    private HexCell _lastHoveredCell;
    private int _lastHoverAp = int.MinValue;
    private MovementPosture _lastHoverPosture = MovementPosture.Walk;
    private HoldTargetIndicator _holdIndicator;
    private RemoteBattleUnitView _heldRemoteTarget;
    private bool _heldSelfTarget;
    private Vector2 _holdIndicatorAnchorScreen;
    private bool _hasHoldIndicatorAnchor;

    private static readonly RaycastHit[] _hexRaycastHits = new RaycastHit[1];
    /// <summary>Двойной клик: порядок попаданий луча (гекс vs моб) без аллокации.</summary>
    private static readonly RaycastHit[] _doubleClickRaycastHits = new RaycastHit[48];
    /// <summary>Удержание ЛКМ по юниту — без <see cref="Physics.RaycastAll"/> (GC).</summary>
    private static readonly RaycastHit[] _remoteHoldRaycastHits = new RaycastHit[64];
    private readonly List<(int col, int row)> _doubleClickPathBuffer = new(64);
    [SerializeField] private AttackRangeHexOutline _attackRangeOutline;
    public static bool IsHoldingRemoteTargetWithLeftMouse { get; private set; }

    private HexGridCamera _hexGridCamera;

    /// <summary>Можно не бить лучом в <see cref="GetHexUnderCursor"/>, если мышь/камера не сдвинулись; сбрасывается при блоке ввода.</summary>
    private bool _hoverRaycastCacheValid;
    private Vector2 _lastHoverMousePos;
    private Vector3 _lastHoverCamPos;
    private Quaternion _lastHoverCamRot;
    private const float HoverMouseMoveEpsilonSq = 0.25f;

    /// <summary>Обрабатываем только ближайшие попадания — дальше по лучу обычно не нужны; меньше сортировка/итерации.</summary>
    private const int MaxRaycastHitsToProcess = 32;

    // Кэш camera.transform — чтобы не дёргать native bridge каждый кадр.
    private Transform _cameraTransform;

    private void Awake()
    {
        _hexGridCamera = FindFirstObjectByType<HexGridCamera>();
        if (_camera != null) _cameraTransform = _camera.transform;
        TryAssignHoldIndicatorPrefabFromResources();
    }

    private void TryAssignHoldIndicatorPrefabFromResources()
    {
        if (_holdIndicatorPrefab != null)
            return;
        var fromResources = Resources.Load<HoldTargetIndicator>("HoldTargetIndicator");
        if (fromResources != null)
            _holdIndicatorPrefab = fromResources;
    }

    /// <summary>Сортировка по distance (insertion sort; при n ≤ 32 дёшево). Не используем System.Array.Sort — в Unity конфликтует с UnityEngine.Array.</summary>
    private static void SortRaycastHitsByDistance(RaycastHit[] hits, int n)
    {
        if (n <= 1) return;
        for (int i = 1; i < n; i++)
        {
            RaycastHit key = hits[i];
            float keyD = key.distance;
            int j = i - 1;
            while (j >= 0 && hits[j].distance > keyD)
            {
                hits[j + 1] = hits[j];
                j--;
            }
            hits[j + 1] = key;
        }
    }

    private void Update()
    {
        if (_grid == null) return;
        if (_camera == null)
        {
            _camera = Camera.main; // FindObjectWithTag — дорого, только при null
            if (_camera == null) return;
            _cameraTransform = _camera.transform;
        }
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        if (GameplayMapInputBlock.IsBlocked)
        {
            if (_lastHoveredCell != null)
            {
                _lastHoveredCell.SetHighlight(false);
                _lastHoveredCell.SetCostLabel(-1);
                _lastHoveredCell = null;
            }
            _hoverRaycastCacheValid = false;
            IsHoldingRemoteTargetWithLeftMouse = false;
            HideAttackRangeOutline();
            return;
        }

        if (_player != null && (_player.IsDead || _player.IsHidden))
        {
            if (_lastHoveredCell != null)
            {
                _lastHoveredCell.SetHighlight(false);
                _lastHoveredCell.SetCostLabel(-1);
                _lastHoveredCell = null;
            }
            _hoverRaycastCacheValid = false;
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        GameSession activeSession = GameSession.Active;
        if (activeSession != null && activeSession.BlockPlayerInput)
        {
            if (_lastHoveredCell != null)
            {
                _lastHoveredCell.SetHighlight(false);
                _lastHoveredCell.SetCostLabel(-1);
                _lastHoveredCell = null;
            }
            _hoverRaycastCacheValid = false;
            IsHoldingRemoteTargetWithLeftMouse = false;
            HideAttackRangeOutline();
            return;
        }

        Keyboard kb = Keyboard.current;
        UpdateHover(mouse);
        UpdateCtrlHexAttack(mouse, kb);
        UpdateDoubleClick(mouse);
        UpdateLeftHoldIndicator(mouse, kb);
        UpdateCtrlAttackRangeOutline(kb);
    }

    /// <summary>
    /// Контур дальности атаки при зажатом Ctrl (прицел по гексу). После удержания ЛКМ по цели — не гасим, если ещё держим цель.
    /// </summary>
    private void UpdateCtrlAttackRangeOutline(Keyboard kb)
    {
        if (_player == null || _player.IsMoving || _player.IsDead || _player.IsHidden)
        {
            HideAttackRangeOutline();
            return;
        }
        if (kb == null)
            return;
        if (_hexGridCamera == null)
            _hexGridCamera = FindFirstObjectByType<HexGridCamera>();
        if (_hexGridCamera != null && _hexGridCamera.ThirdPersonOrbitSuppressAttackHold)
        {
            HideAttackRangeOutline();
            return;
        }

        bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
        if (ctrl)
        {
            EnsureAttackRangeOutline();
            if (_attackRangeOutline != null)
                _attackRangeOutline.ShowFromPlayer(_player);
        }
        else if (!IsHoldingRemoteTargetWithLeftMouse)
        {
            HideAttackRangeOutline();
        }
    }

    /// <summary>Ctrl+ЛКМ по гексу — выстрел по прицелу (стена на ЛС / враг на клетке), дальность &gt; 1.</summary>
    private void UpdateCtrlHexAttack(Mouse mouse, Keyboard kb)
    {
        if (_grid == null || _player == null || _player.IsMoving || _player.IsDead || _player.IsHidden)
            return;
        if (kb == null)
            return;
        if (!mouse.leftButton.wasPressedThisFrame)
            return;
        if (!kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed)
            return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (!TryGetHexCellForCtrlAim(mouse, out HexCell cell) || cell == null)
            return;

        bool shiftRepeat = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        GameSession session = GameSession.Active != null ? GameSession.Active : FindFirstObjectByType<GameSession>();
        if (session != null && session.TryPerformHexAimAttack(cell.Col, cell.Row, shiftRepeat))
        {
            // Не даём этому клику стать первым в паре «двойной клик — движение».
            _lastClickTime = 0f;
        }
    }

    /// <summary>
    /// Гекс под прицелом для Ctrl+выстрела: луч по всем слоям; если раньше попали в удалённого/локального юнита — берём его клетку на сетке.
    /// </summary>
    private bool TryGetHexCellForCtrlAim(Mouse mouse, out HexCell cell)
    {
        cell = null;
        if (_camera == null || _grid == null)
            return false;

        Vector2 pos = mouse.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        int n = Physics.RaycastNonAlloc(ray, _doubleClickRaycastHits, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (n <= 0)
            return false;
        if (n > MaxRaycastHitsToProcess)
            n = MaxRaycastHitsToProcess;
        SortRaycastHitsByDistance(_doubleClickRaycastHits, n);

        for (int i = 0; i < n; i++)
        {
            Collider c = _doubleClickRaycastHits[i].collider;
            if (c == null)
                continue;

            RemoteBattleUnitView remote = c.GetComponentInParent<RemoteBattleUnitView>();
            if (remote != null)
            {
                cell = _grid.GetCell(remote.CurrentCol, remote.CurrentRow);
                return cell != null;
            }

            if (_player != null)
            {
                Player pl = c.GetComponentInParent<Player>();
                if (pl != null && pl == _player)
                {
                    cell = _grid.GetCell(pl.CurrentCol, pl.CurrentRow);
                    return cell != null;
                }
            }

            HexCell hex = c.GetComponent<HexCell>();
            if (hex != null)
            {
                cell = hex;
                return true;
            }
        }

        return false;
    }

    private void UpdateHover(Mouse mouse)
    {
        Vector2 mousePos = mouse.position.ReadValue();
        bool mouseMoved = !_hoverRaycastCacheValid ||
            (mousePos - _lastHoverMousePos).sqrMagnitude > HoverMouseMoveEpsilonSq;
        bool cameraMoved = !_hoverRaycastCacheValid;
        if (!cameraMoved && _cameraTransform != null)
        {
            Vector3 camPos = _cameraTransform.position;
            float dx = camPos.x - _lastHoverCamPos.x, dy = camPos.y - _lastHoverCamPos.y, dz = camPos.z - _lastHoverCamPos.z;
            cameraMoved = dx * dx + dy * dy + dz * dz > 1e-10f;
            if (!cameraMoved)
            {
                // Dot product вместо Quaternion.Angle (избегаем Acos).
                // dot^2 < 1 означает ненулевой угол; порог ~0.01 deg.
                Quaternion cr = _cameraTransform.rotation;
                float dot = cr.x * _lastHoverCamRot.x + cr.y * _lastHoverCamRot.y
                           + cr.z * _lastHoverCamRot.z + cr.w * _lastHoverCamRot.w;
                cameraMoved = dot * dot < 1f - 1e-10f;
            }
        }

        HexCell cell;
        if (!mouseMoved && !cameraMoved && _hoverRaycastCacheValid)
            cell = _lastHoveredCell;
        else
        {
            cell = GetHexUnderCursor(mouse);
            _lastHoverMousePos = mousePos;
            if (_cameraTransform != null)
            {
                _lastHoverCamPos = _cameraTransform.position;
                _lastHoverCamRot = _cameraTransform.rotation;
            }
            _hoverRaycastCacheValid = true;
        }

        MovementPosture posture = _player != null ? _player.CurrentMovementPosture : MovementPosture.Walk;
        int currentAp = _player != null ? _player.CurrentAp : int.MinValue;
        if (cell == _lastHoveredCell && currentAp == _lastHoverAp && posture == _lastHoverPosture)
            return;

        if (_lastHoveredCell != null)
        {
            _lastHoveredCell.SetHighlight(false);
            _lastHoveredCell.SetCostLabel(-1);
        }

        _lastHoveredCell = cell;
        _lastHoverAp = currentAp;
        _lastHoverPosture = posture;
        if (cell != null)
        {
            cell.SetHighlight(true);

            if (_player != null)
            {
                if (HexPathfinding.TryGetShortestPathStepCount(_grid, _player.CurrentCol, _player.CurrentRow, cell.Col, cell.Row, out int steps)
                    && steps > 0)
                {
                    int stepCost = _player.GetMoveCost(_player.StepsTakenThisTurn, steps);
                    cell.SetCostLabel(stepCost);
                }
                else
                    cell.SetCostLabel(-1);
            }
        }
    }

    private void UpdateDoubleClick(Mouse mouse)
    {
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (mouse.rightButton.isPressed) return; // Только ЛКМ
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 mousePos = mouse.position.ReadValue();
        float time = Time.time;

        if (time - _lastClickTime <= DoubleClickTime &&
            (mousePos - _lastClickPosition).sqrMagnitude <= DoubleClickMaxDistSqr)
        {
            _lastClickTime = 0f;
            OnDoubleClick(mouse);
            return;
        }

        _lastClickTime = time;
        _lastClickPosition = mousePos;
    }

    private void OnDoubleClick(Mouse mouse)
    {
        // Двойной клик по коллайдеру юнита — только атака (удержание/клики), не ход.
        // Двойной клик по гексу (в т.ч. клетка под мобом, если луч попал в пол) — движение.
        if (!TryGetHexCellForDoubleClickMove(mouse, out HexCell cell) || _player == null || _player.IsMoving || _player.IsDead || _player.IsHidden)
            return;

        if (!_player.EnsureMovablePostureForMovement())
        {
            GameSession.OnNetworkMessage?.Invoke(Loc.T("ui.not_enough_ap_unhide"));
            return;
        }

        if (!HexPathfinding.TryBuildPath(
                _grid,
                _player.CurrentCol, _player.CurrentRow,
                cell.Col, cell.Row,
                _doubleClickPathBuffer))
            return;

        int stepsToMove = _doubleClickPathBuffer.Count - 1;
        if (stepsToMove <= 0)
            return;

        int allowed = _player.GetAllowedMoveStepsForPath(stepsToMove);
        if (allowed >= stepsToMove)
            _player.ClearMovementFlag();
        else
            _player.SetMovementFlag(cell.Col, cell.Row);

        if (allowed > 0)
            _player.MoveAlongPath(_doubleClickPathBuffer, MovementPlanningVisualSettings.ShowMovementAnimation);
    }

    private HexCell GetHexUnderCursor(Mouse mouse)
    {
        Vector2 pos = mouse.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        if (Physics.RaycastNonAlloc(ray, _hexRaycastHits, 1000f, _hexLayer) <= 0)
            return null;

        Collider col = _hexRaycastHits[0].collider;
        if (col == null)
            return null;
        return col.GetComponent<HexCell>();
    }

    /// <summary>
    /// Клетка для движения по двойному клику: по лучу по расстоянию ищем первое попадание в юнита или в гекс.
    /// Если раньше гекса идёт <see cref="RemoteBattleUnitView"/> — клик по силуэту, движение не выполняем.
    /// </summary>
    private bool TryGetHexCellForDoubleClickMove(Mouse mouse, out HexCell cell)
    {
        cell = null;
        if (_camera == null) return false;
        Vector2 pos = mouse.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        int n = Physics.RaycastNonAlloc(ray, _doubleClickRaycastHits, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (n <= 0) return false;
        if (n > MaxRaycastHitsToProcess)
            n = MaxRaycastHitsToProcess;
        SortRaycastHitsByDistance(_doubleClickRaycastHits, n);

        for (int i = 0; i < n; i++)
        {
            Collider c = _doubleClickRaycastHits[i].collider;
            if (c == null) continue;
            if (c.GetComponentInParent<RemoteBattleUnitView>() != null)
                return false;
            HexCell hex = c.GetComponent<HexCell>();
            if (hex == null)
                continue;
            cell = hex;
            return true;
        }

        return false;
    }

    private void UpdateLeftHoldIndicator(Mouse mouse, Keyboard kb)
    {
        if (_hexGridCamera == null)
            _hexGridCamera = FindFirstObjectByType<HexGridCamera>();

        // 3-е лицо: после движения мыши с зажатой ЛКМ орбиты — не показывать силуэт/контур атаки (поворот камеры).
        if (_hexGridCamera != null && _hexGridCamera.ThirdPersonOrbitSuppressAttackHold)
        {
            _heldRemoteTarget = null;
            _heldSelfTarget = false;
            _hasHoldIndicatorAnchor = false;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        // ПКМ никогда не должна выполнять ЛКМ-логику.
        if (mouse.rightButton.isPressed)
        {
            _heldRemoteTarget = null;
            _heldSelfTarget = false;
            _hasHoldIndicatorAnchor = false;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        // При отпускании ЛКМ — сразу скрываем.
        if (!mouse.leftButton.isPressed)
        {
            if (_heldRemoteTarget != null && _holdIndicator != null &&
                _holdIndicator.HoveredPart != HoldTargetIndicator.BodyPartKind.None)
                ApplyAttackOnRelease(_heldRemoteTarget, _holdIndicator.HoveredPart, kb);
            else if (_heldSelfTarget && _holdIndicator != null &&
                     _holdIndicator.HoveredPart != HoldTargetIndicator.BodyPartKind.None)
                GameSession.Active?.TryUseSelfItem();
            _heldRemoteTarget = null;
            _heldSelfTarget = false;
            _hasHoldIndicatorAnchor = false;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            _heldRemoteTarget = null;
            _heldSelfTarget = false;
            _hasHoldIndicatorAnchor = false;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        bool shouldCaptureAnchor = mouse.leftButton.wasPressedThisFrame || !_hasHoldIndicatorAnchor;
        bool selfItemActive = IsSelfItemActive();
        Vector3 remoteHitPoint = Vector3.zero;
        RemoteBattleUnitView remote = (shouldCaptureAnchor && !selfItemActive)
            ? GetRemoteUnitUnderCursor(mouse, out remoteHitPoint)
            : null;
        if (remote != null)
        {
            _heldRemoteTarget = remote;
            _heldSelfTarget = false;
            // Вариант A: anchor в пикселях экрана — ровно там, куда кликнул курсор.
            // Не зависит от угла камеры, глубины, угла обзора.
            _holdIndicatorAnchorScreen = mouse.position.ReadValue();
            _hasHoldIndicatorAnchor = true;
            if (mouse.leftButton.wasPressedThisFrame)
                GameSession.Active?.ApplyLocalPlayerRangedFacingTowardTargetHex(remote.CurrentCol, remote.CurrentRow);
        }
        else if (shouldCaptureAnchor && mouse.leftButton.wasPressedThisFrame)
        {
            if (selfItemActive && (IsLocalPlayerUnderCursor(mouse) || IsLocalPlayerHexUnderCursor(mouse)))
            {
                _heldRemoteTarget = null;
                _heldSelfTarget = true;
                _holdIndicatorAnchorScreen = mouse.position.ReadValue();
                _hasHoldIndicatorAnchor = true;
            }
            else if (selfItemActive)
            {
                // Self item: disallow target indicator for any non-self unit.
                _heldRemoteTarget = null;
                _heldSelfTarget = false;
                _hasHoldIndicatorAnchor = false;
                SetHoldIndicatorVisible(false);
                IsHoldingRemoteTargetWithLeftMouse = false;
                return;
            }
        }

        if (_heldRemoteTarget == null && !_heldSelfTarget)
        {
            IsHoldingRemoteTargetWithLeftMouse = false;
            SetHoldIndicatorVisible(false);
            return;
        }

        EnsureAttackRangeOutline();
        if (_attackRangeOutline != null && _player != null)
            _attackRangeOutline.ShowFromPlayer(_player);

        EnsureHoldIndicator();
        if (_holdIndicator == null || !_holdIndicator.HasValidVisuals)
            return;

        Vector2 screenCenter = _hasHoldIndicatorAnchor
            ? _holdIndicatorAnchorScreen
            : mouse.position.ReadValue();
        _holdIndicator.SetScreenCenter(screenCenter);
        _holdIndicator.SetWholeBodyHighlightMode(_heldSelfTarget);
        _holdIndicator.UpdateBodyPartHighlight(mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame);
        _holdIndicator.SetVisible(true);
        if (_hexGridCamera != null)
            _hexGridCamera.ClearThirdPersonOrbitLmbDragThisPress();
        IsHoldingRemoteTargetWithLeftMouse = true;
    }

    private void EnsureAttackRangeOutline()
    {
        if (_attackRangeOutline != null)
            return;
#if UNITY_2023_1_OR_NEWER
        _attackRangeOutline = UnityEngine.Object.FindFirstObjectByType<AttackRangeHexOutline>();
#else
        _attackRangeOutline = UnityEngine.Object.FindObjectOfType<AttackRangeHexOutline>();
#endif
        if (_attackRangeOutline == null && _grid != null)
            _attackRangeOutline = _grid.gameObject.AddComponent<AttackRangeHexOutline>();
    }

    /// <summary>Скрывает контур дальности; при первом вызове находит компонент в сцене (без создания нового).</summary>
    private void HideAttackRangeOutline()
    {
        if (_attackRangeOutline == null)
        {
#if UNITY_2023_1_OR_NEWER
            _attackRangeOutline = UnityEngine.Object.FindFirstObjectByType<AttackRangeHexOutline>();
#else
            _attackRangeOutline = UnityEngine.Object.FindObjectOfType<AttackRangeHexOutline>();
#endif
        }
        if (_attackRangeOutline != null)
            _attackRangeOutline.Hide();
    }

    private RemoteBattleUnitView GetRemoteUnitUnderCursor(Mouse mouse, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (_camera == null) return null;
        Vector2 pos = mouse.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        int n = Physics.RaycastNonAlloc(ray, _remoteHoldRaycastHits, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (n <= 0)
            return null;
        if (n > MaxRaycastHitsToProcess)
            n = MaxRaycastHitsToProcess;

        float bestD = float.MaxValue;
        RemoteBattleUnitView best = null;
        Vector3 bestPt = default;
        for (int i = 0; i < n; i++)
        {
            Collider col = _remoteHoldRaycastHits[i].collider;
            if (col == null) continue;
            RemoteBattleUnitView remote = col.GetComponentInParent<RemoteBattleUnitView>();
            if (remote == null)
                continue;
            float d = _remoteHoldRaycastHits[i].distance;
            if (d < bestD)
            {
                bestD = d;
                best = remote;
                bestPt = _remoteHoldRaycastHits[i].point;
            }
        }

        if (best == null)
            return null;
        hitPoint = bestPt;
        return best;
    }

    private bool IsLocalPlayerUnderCursor(Mouse mouse)
    {
        if (_camera == null || _player == null)
            return false;
        Vector2 pos = mouse.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        int n = Physics.RaycastNonAlloc(ray, _remoteHoldRaycastHits, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (n <= 0)
            return false;
        if (n > MaxRaycastHitsToProcess)
            n = MaxRaycastHitsToProcess;

        for (int i = 0; i < n; i++)
        {
            Collider col = _remoteHoldRaycastHits[i].collider;
            if (col == null)
                continue;
            Player hitPlayer = col.GetComponentInParent<Player>();
            if (hitPlayer != null && hitPlayer == _player)
                return true;
        }

        return false;
    }

    private bool IsLocalPlayerHexUnderCursor(Mouse mouse)
    {
        if (_player == null)
            return false;
        HexCell cell = GetHexUnderCursor(mouse);
        if (cell == null)
            return false;
        return cell.Col == _player.CurrentCol && cell.Row == _player.CurrentRow;
    }

    private bool IsSelfItemActive()
    {
#if UNITY_2023_1_OR_NEWER
        InventoryUI inv = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
#else
        InventoryUI inv = UnityEngine.Object.FindObjectOfType<InventoryUI>();
#endif
        return inv != null && inv.IsActiveItemMedicine();
    }

    private void EnsureHoldIndicator()
    {
        if (_holdIndicator != null)
            return;
        if (_holdIndicatorPrefab == null)
            return;

        GameObject go = Instantiate(_holdIndicatorPrefab.gameObject);
        go.name = "HoldTargetIndicator";
        _holdIndicator = go.GetComponent<HoldTargetIndicator>();
        if (_holdIndicator == null)
        {
            Destroy(go);
            return;
        }

        _holdIndicator.EnsureBuilt();
        if (!_holdIndicator.HasValidVisuals)
        {
            Destroy(go);
            _holdIndicator = null;
            return;
        }

        _holdIndicator.SetVisible(false);
    }

    private void SetHoldIndicatorVisible(bool visible)
    {
        if (_holdIndicator != null)
            _holdIndicator.SetVisible(visible);
        if (!visible)
            HideAttackRangeOutline();
    }

    private void ApplyAttackOnRelease(RemoteBattleUnitView target, HoldTargetIndicator.BodyPartKind part, Keyboard kb)
    {
        if (target == null || part == HoldTargetIndicator.BodyPartKind.None)
            return;

        int bodyPartId = BodyPartIds.FromHoldTargetPart(part);

        bool shiftRepeat = kb != null &&
            (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        bool applied = GameSession.Active != null &&
            GameSession.Active.TryPerformSilhouetteAttack(target, bodyPartId, shiftRepeat);
        if (!applied)
            GameSession.OnNetworkMessage?.Invoke(Loc.T("ui.attack_not_applied"));
    }
}
