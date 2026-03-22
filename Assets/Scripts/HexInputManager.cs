using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Наведение на гекс (подсветка) и двойной клик — путь и движение игрока.
/// Использует Input System package.
/// </summary>
[DefaultExecutionOrder(50)]
public class HexInputManager : MonoBehaviour
{
    private enum BodyPart
    {
        None = 0,
        Head = 1,
        Torso = 2,
        Legs = 3
    }

    private const float DoubleClickTime = 0.3f;
    private const float DoubleClickMaxDist = 10f;
    private const float DoubleClickMaxDistSqr = DoubleClickMaxDist * DoubleClickMaxDist;

    [SerializeField] private Camera _camera;
    [SerializeField] private HexGrid _grid;
    [SerializeField] private Player _player;
    [SerializeField] private LayerMask _hexLayer = -1;
    [Header("ЛКМ по другому юниту/мобу")]
    [Tooltip("Sprite для индикатора (если не задан, загружается Resources/hold_indicator).")]
    [SerializeField] private Sprite _holdIndicatorSprite;
    [SerializeField] private Vector3 _holdIndicatorWorldOffset = new Vector3(0.8f, 1.5f, 0f);
    [SerializeField] private Vector3 _holdIndicatorScale = new Vector3(0.8f, 0.8f, 1f);
    [Header("Подсветка частей силуэта")]
    [SerializeField] private Color _holdPartBaseColor = new Color(1, 1, 1, 0.55f);
    [SerializeField] private Color _holdPartHighlightColor = new Color(1f, 0f, 0f, 0.55f);
    [Header("Границы блоков (UV 0..1)")]
    [SerializeField] private Rect _headUvRect = new Rect(0.25f, 0.8f, 0.5f, 0.2f);
    [SerializeField] private Rect _torsoUvRect = new Rect(0.25f, 0.45f, 0.5f, 0.35f);
    [SerializeField] private Rect _legsUvRect = new Rect(0.25f, 0f, 0.5f, 0.45f);

    private float _lastClickTime;
    private Vector2 _lastClickPosition;
    private HexCell _lastHoveredCell;
    private int _lastHoverAp = int.MinValue;
    private MovementPosture _lastHoverPosture = MovementPosture.Walk;
    private GameObject _holdIndicatorGo;
    private SpriteRenderer _holdIndicatorBaseRenderer;
    private static Sprite _holdBlockSprite;
    private static Material _holdOverlayMaterial;
    // Индекс = (int)BodyPart (0=None,1=Head,2=Torso,3=Legs). Массив вместо Dictionary — нет foreach-аллокации.
    private readonly SpriteRenderer[] _holdPartRenderers = new SpriteRenderer[4];
    private BodyPart _hoveredBodyPart = BodyPart.None;
    private RemoteBattleUnitView _heldRemoteTarget;
    private Vector3 _holdIndicatorAnchorWorld;
    private bool _hasHoldIndicatorAnchor;

    private static readonly RaycastHit[] _hexRaycastHits = new RaycastHit[1];
    /// <summary>Двойной клик: порядок попаданий луча (гекс vs моб) без аллокации.</summary>
    private static readonly RaycastHit[] _doubleClickRaycastHits = new RaycastHit[48];
    /// <summary>Удержание ЛКМ по юниту — без <see cref="Physics.RaycastAll"/> (GC).</summary>
    private static readonly RaycastHit[] _remoteHoldRaycastHits = new RaycastHit[64];
    private readonly List<(int col, int row)> _doubleClickPathBuffer = new(64);
    private float _holdCompositeHalfWidth = 0.5f;
    private float _holdCompositeHalfHeight = 0.5f;
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

    private Vector2 _lastBodyPartHighlightMousePos = new Vector2(float.NaN, float.NaN);

    // Кэш camera.transform — чтобы не дёргать native bridge каждый кадр.
    private Transform _cameraTransform;

    // Предвычисленные границы UV-зон для ClassifyBodyPartFromUv — без Mathf.Clamp01 в хот-пасе.
    private float _headMinU, _headMaxU, _headMinV, _headMaxV;
    private float _torsoMinU, _torsoMaxU, _torsoMinV, _torsoMaxV;
    private float _legsMinU, _legsMaxU, _legsMinV, _legsMaxV;

    private void Awake()
    {
        _hexGridCamera = FindFirstObjectByType<HexGridCamera>();
        if (_camera != null) _cameraTransform = _camera.transform;
        PrecomputeUvBounds();
    }

    private void PrecomputeUvBounds()
    {
        _headMinU  = Mathf.Clamp01(_headUvRect.xMin);  _headMaxU  = Mathf.Clamp01(_headUvRect.xMax);
        _headMinV  = Mathf.Clamp01(_headUvRect.yMin);  _headMaxV  = Mathf.Clamp01(_headUvRect.yMax);
        _torsoMinU = Mathf.Clamp01(_torsoUvRect.xMin); _torsoMaxU = Mathf.Clamp01(_torsoUvRect.xMax);
        _torsoMinV = Mathf.Clamp01(_torsoUvRect.yMin); _torsoMaxV = Mathf.Clamp01(_torsoUvRect.yMax);
        _legsMinU  = Mathf.Clamp01(_legsUvRect.xMin);  _legsMaxU  = Mathf.Clamp01(_legsUvRect.xMax);
        _legsMinV  = Mathf.Clamp01(_legsUvRect.yMin);  _legsMaxV  = Mathf.Clamp01(_legsUvRect.yMax);
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
            GameSession.OnNetworkMessage?.Invoke("Недостаточно ОД для выхода из укрытия");
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
            _hasHoldIndicatorAnchor = false;
            _hoveredBodyPart = BodyPart.None;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        // ПКМ никогда не должна выполнять ЛКМ-логику.
        if (mouse.rightButton.isPressed)
        {
            _heldRemoteTarget = null;
            _hasHoldIndicatorAnchor = false;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        // При отпускании ЛКМ — сразу скрываем.
        if (!mouse.leftButton.isPressed)
        {
            if (_heldRemoteTarget != null && _hoveredBodyPart != BodyPart.None)
                ApplyAttackOnRelease(_heldRemoteTarget, _hoveredBodyPart, kb);
            _heldRemoteTarget = null;
            _hasHoldIndicatorAnchor = false;
            _hoveredBodyPart = BodyPart.None;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            _heldRemoteTarget = null;
            _hasHoldIndicatorAnchor = false;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        bool shouldCaptureAnchor = mouse.leftButton.wasPressedThisFrame || !_hasHoldIndicatorAnchor;
        Vector3 remoteHitPoint = Vector3.zero;
        RemoteBattleUnitView remote = shouldCaptureAnchor ? GetRemoteUnitUnderCursor(mouse, out remoteHitPoint) : null;
        if (remote != null)
        {
            _heldRemoteTarget = remote;
            _holdIndicatorAnchorWorld = remoteHitPoint;
            _hasHoldIndicatorAnchor = true;
            if (mouse.leftButton.wasPressedThisFrame)
                GameSession.Active?.ApplyLocalPlayerRangedFacingTowardTargetHex(remote.CurrentCol, remote.CurrentRow);
        }

        if (_heldRemoteTarget == null)
        {
            IsHoldingRemoteTargetWithLeftMouse = false;
            SetHoldIndicatorVisible(false);
            return;
        }

        EnsureAttackRangeOutline();
        if (_attackRangeOutline != null && _player != null)
            _attackRangeOutline.ShowFromPlayer(_player);

        EnsureHoldIndicator();
        if (_holdIndicatorGo == null) return;

        _holdIndicatorGo.transform.position = _hasHoldIndicatorAnchor
            ? _holdIndicatorAnchorWorld
            : _heldRemoteTarget.transform.position + _holdIndicatorWorldOffset;
        if (_cameraTransform != null)
            _holdIndicatorGo.transform.rotation = Quaternion.LookRotation(-_cameraTransform.forward, _cameraTransform.up);
        UpdateHoldIndicatorBodyPartHighlight(mouse, mouse.leftButton.wasPressedThisFrame);
        SetHoldIndicatorVisible(true);
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

    private void EnsureHoldIndicator()
    {
        if (_holdIndicatorGo != null) return;

        _holdIndicatorGo = new GameObject("HoldTargetIndicator");
        _holdIndicatorGo.transform.localScale = _holdIndicatorScale;

        Sprite baseSprite = LoadHoldIndicatorSprite();
        if (baseSprite == null)
        {
            Destroy(_holdIndicatorGo);
            _holdIndicatorGo = null;
            return;
        }

        GameObject baseGo = new GameObject("HoldBase");
        baseGo.transform.SetParent(_holdIndicatorGo.transform, false);
        _holdIndicatorBaseRenderer = baseGo.AddComponent<SpriteRenderer>();
        _holdIndicatorBaseRenderer.sprite = baseSprite;
        ConfigureOverlayRenderer(_holdIndicatorBaseRenderer, 5000);
        _holdIndicatorBaseRenderer.color = Color.white;

        RecalculateCompositeBounds();
        CreateBodyPartBlock(BodyPart.Head, _headUvRect);
        CreateBodyPartBlock(BodyPart.Torso, _torsoUvRect);
        CreateBodyPartBlock(BodyPart.Legs, _legsUvRect);

        RecalculateCompositeBounds();
        ApplyHoldPartColors(BodyPart.None);
        SetHoldIndicatorVisible(false);
    }

    private Sprite LoadHoldIndicatorSprite()
    {
        if (_holdIndicatorSprite != null)
            return _holdIndicatorSprite;

        Sprite fromSprite = Resources.Load<Sprite>("hold_indicator");
        if (fromSprite != null)
            return fromSprite;

        Texture2D tex = Resources.Load<Texture2D>("hold_indicator");
        if (tex == null)
            return null;

        return Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    private void SetHoldIndicatorVisible(bool visible)
    {
        if (_holdIndicatorGo != null && _holdIndicatorGo.activeSelf != visible)
            _holdIndicatorGo.SetActive(visible);
        if (!visible)
            HideAttackRangeOutline();
    }

    private void CreateBodyPartBlock(BodyPart part, Rect uvRect)
    {
        Sprite s = GetHoldBlockSprite();
        if (s == null) return;

        float fullW = _holdCompositeHalfWidth * 2f;
        float fullH = _holdCompositeHalfHeight * 2f;
        float minU = Mathf.Clamp01(uvRect.xMin);
        float maxU = Mathf.Clamp01(uvRect.xMax);
        float minV = Mathf.Clamp01(uvRect.yMin);
        float maxV = Mathf.Clamp01(uvRect.yMax);
        float centerU = (minU + maxU) * 0.5f;
        float centerV = (minV + maxV) * 0.5f;
        float sizeU = Mathf.Max(0f, maxU - minU);
        float sizeV = Mathf.Max(0f, maxV - minV);

        GameObject go = new GameObject("HoldBlock_" + part);
        go.transform.SetParent(_holdIndicatorGo.transform, false);
        go.transform.localPosition = new Vector3((centerU - 0.5f) * fullW, (centerV - 0.5f) * fullH, 0f);
        go.transform.localScale = new Vector3(sizeU * fullW, sizeV * fullH, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        ConfigureOverlayRenderer(sr, 5001);
        sr.color = _holdPartBaseColor;
        _holdPartRenderers[(int)part] = sr;
    }

    private static void ConfigureOverlayRenderer(SpriteRenderer sr, int sortingOrder)
    {
        if (sr == null) return;
        sr.shadowCastingMode = ShadowCastingMode.Off;
        sr.receiveShadows = false;
        sr.lightProbeUsage = LightProbeUsage.Off;
        sr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        sr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        sr.allowOcclusionWhenDynamic = false;
        sr.sortingOrder = sortingOrder;

        int uiLayerId = SortingLayer.NameToID("UI");
        if (uiLayerId != 0)
            sr.sortingLayerID = uiLayerId;

        Material overlayMat = GetHoldOverlayMaterial();
        if (overlayMat != null)
            sr.sharedMaterial = overlayMat;
    }

    private static Material GetHoldOverlayMaterial()
    {
        if (_holdOverlayMaterial != null) return _holdOverlayMaterial;
        Shader shader = Shader.Find("Custom/HoldIndicatorOverlay");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;
        _holdOverlayMaterial = new Material(shader)
        {
            renderQueue = 5000
        };
        _holdOverlayMaterial.enableInstancing = true;
        return _holdOverlayMaterial;
    }

    private void RecalculateCompositeBounds()
    {
        float maxHalfW = 0.5f;
        float maxHalfH = 0.5f;
        if (_holdIndicatorBaseRenderer != null && _holdIndicatorBaseRenderer.sprite != null)
        {
            Bounds bb = _holdIndicatorBaseRenderer.sprite.bounds;
            maxHalfW = Mathf.Max(maxHalfW, Mathf.Abs(bb.extents.x));
            maxHalfH = Mathf.Max(maxHalfH, Mathf.Abs(bb.extents.y));
        }

        for (int i = 1; i < _holdPartRenderers.Length; i++) // 0=None, пропускаем
        {
            SpriteRenderer sr = _holdPartRenderers[i];
            if (sr == null || sr.sprite == null) continue;
            Bounds b = sr.bounds;
            maxHalfW = Mathf.Max(maxHalfW, Mathf.Abs(b.extents.x));
            maxHalfH = Mathf.Max(maxHalfH, Mathf.Abs(b.extents.y));
        }
        _holdCompositeHalfWidth = maxHalfW;
        _holdCompositeHalfHeight = maxHalfH;
    }

    private static Sprite GetHoldBlockSprite()
    {
        if (_holdBlockSprite != null) return _holdBlockSprite;
        Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        _holdBlockSprite = Sprite.Create(t, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return _holdBlockSprite;
    }

    private BodyPart ClassifyBodyPartFromUv(float u, float v)
    {
        if (u >= _headMinU  && u <= _headMaxU  && v >= _headMinV  && v <= _headMaxV)  return BodyPart.Head;
        if (u >= _torsoMinU && u <= _torsoMaxU && v >= _torsoMinV && v <= _torsoMaxV) return BodyPart.Torso;
        if (u >= _legsMinU  && u <= _legsMaxU  && v >= _legsMinV  && v <= _legsMaxV)  return BodyPart.Legs;
        return BodyPart.None;
    }

    private void UpdateHoldIndicatorBodyPartHighlight(Mouse mouse, bool forceImmediate = false)
    {
        if (_holdIndicatorGo == null || _camera == null)
            return;

        Vector2 mousePos = mouse.position.ReadValue();
        // Каждый второй кадр + пропуск при почти неподвижной мыши — меньше Plane.Raycast / InverseTransform.
        // forceImmediate: при первом нажатии обязательно определяем body part, иначе быстрый клик-отпуск не зарегистрирует атаку.
        if (!forceImmediate && (Time.frameCount & 1) != 0)
            return;
        if (!forceImmediate && !float.IsNaN(_lastBodyPartHighlightMousePos.x) &&
            (mousePos - _lastBodyPartHighlightMousePos).sqrMagnitude < 0.25f)
            return;
        _lastBodyPartHighlightMousePos = mousePos;

        Ray ray = _camera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));
        Plane spritePlane = new Plane(_holdIndicatorGo.transform.forward, _holdIndicatorGo.transform.position);
        if (!spritePlane.Raycast(ray, out float enter) || enter <= 0f)
        {
            ApplyHoldPartColors(BodyPart.None);
            return;
        }

        Vector3 worldHit = ray.GetPoint(enter);
        Vector3 localHit = _holdIndicatorGo.transform.InverseTransformPoint(worldHit);
        float halfW = Mathf.Max(0.0001f, _holdCompositeHalfWidth);
        float halfH = Mathf.Max(0.0001f, _holdCompositeHalfHeight);
        float u = localHit.x / (halfW * 2f) + 0.5f;
        float v = localHit.y / (halfH * 2f) + 0.5f;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            ApplyHoldPartColors(BodyPart.None);
            return;
        }

        BodyPart part = ClassifyBodyPartFromUv(u, v);
        ApplyHoldPartColors(part);
    }

    private void ApplyAttackOnRelease(RemoteBattleUnitView target, BodyPart part, Keyboard kb)
    {
        if (target == null || part == BodyPart.None)
            return;

        string label = "корпус";
        switch (part)
        {
            case BodyPart.Head:
                label = "голова";
                break;
            case BodyPart.Torso:
                label = "корпус";
                break;
            case BodyPart.Legs:
                label = "ноги";
                break;
        }

        bool shiftRepeat = kb != null &&
            (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        bool applied = GameSession.Active != null &&
            GameSession.Active.TryPerformSilhouetteAttack(target, label, shiftRepeat);
        if (!applied)
            GameSession.OnNetworkMessage?.Invoke("Атака не применена");
    }

    private void ApplyHoldPartColors(BodyPart highlightedPart)
    {
        if (_hoveredBodyPart == highlightedPart) return;
        _hoveredBodyPart = highlightedPart;

        int highlighted = (int)highlightedPart;
        for (int i = 1; i < _holdPartRenderers.Length; i++) // 0=None, пропускаем
        {
            SpriteRenderer sr = _holdPartRenderers[i];
            if (sr == null) continue;
            sr.color = i == highlighted ? _holdPartHighlightColor : _holdPartBaseColor;
        }
    }
}
