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
    private readonly Dictionary<BodyPart, SpriteRenderer> _holdPartRenderers = new();
    private BodyPart _hoveredBodyPart = BodyPart.None;
    private RemoteBattleUnitView _heldRemoteTarget;
    private Vector3 _holdIndicatorAnchorWorld;
    private bool _hasHoldIndicatorAnchor;

    private static readonly RaycastHit[] _hexRaycastHits = new RaycastHit[1];
    /// <summary>Двойной клик: порядок попаданий луча (гекс vs моб) без аллокации.</summary>
    private static readonly RaycastHit[] _doubleClickRaycastHits = new RaycastHit[48];
    private readonly List<(int col, int row)> _doubleClickPathBuffer = new(64);
    private float _holdCompositeHalfWidth = 0.5f;
    private float _holdCompositeHalfHeight = 0.5f;
    [SerializeField] private AttackRangeHexOutline _attackRangeOutline;
    public static bool IsHoldingRemoteTargetWithLeftMouse { get; private set; }

    private void Update()
    {
        if (_grid == null) return;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;
        if (Mouse.current == null) return;
        if (GameplayMapInputBlock.IsBlocked)
        {
            if (_lastHoveredCell != null)
            {
                _lastHoveredCell.SetHighlight(false);
                _lastHoveredCell.SetCostLabel(-1);
                _lastHoveredCell = null;
            }
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
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }
        if (GameSession.Active != null && GameSession.Active.BlockPlayerInput)
        {
            if (_lastHoveredCell != null)
            {
                _lastHoveredCell.SetHighlight(false);
                _lastHoveredCell.SetCostLabel(-1);
                _lastHoveredCell = null;
            }
            IsHoldingRemoteTargetWithLeftMouse = false;
            HideAttackRangeOutline();
            return;
        }

        UpdateHover();
        UpdateDoubleClick();
        UpdateLeftHoldIndicator();
    }

    private void UpdateHover()
    {
        HexCell cell = GetHexUnderCursor();
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

    private void UpdateDoubleClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (Mouse.current.rightButton.isPressed) return; // Только ЛКМ
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
        // Двойной клик по коллайдеру юнита — только атака (удержание/клики), не ход.
        // Двойной клик по гексу (в т.ч. клетка под мобом, если луч попал в пол) — движение.
        if (!TryGetHexCellForDoubleClickMove(out HexCell cell) || _player == null || _player.IsMoving || _player.IsDead || _player.IsHidden)
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

    private HexCell GetHexUnderCursor()
    {
        if (Mouse.current == null) return null;
        Vector2 pos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        if (Physics.RaycastNonAlloc(ray, _hexRaycastHits, 1000f, _hexLayer) <= 0)
            return null;

        return _hexRaycastHits[0].collider != null
            ? _hexRaycastHits[0].collider.GetComponent<HexCell>()
            : null;
    }

    /// <summary>
    /// Клетка для движения по двойному клику: по лучу по расстоянию ищем первое попадание в юнита или в гекс.
    /// Если раньше гекса идёт <see cref="RemoteBattleUnitView"/> — клик по силуэту, движение не выполняем.
    /// </summary>
    private bool TryGetHexCellForDoubleClickMove(out HexCell cell)
    {
        cell = null;
        if (_camera == null || Mouse.current == null) return false;
        Vector2 pos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        int n = Physics.RaycastNonAlloc(ray, _doubleClickRaycastHits, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (n <= 0) return false;

        for (int a = 0; a < n - 1; a++)
        {
            for (int b = a + 1; b < n; b++)
            {
                if (_doubleClickRaycastHits[a].distance > _doubleClickRaycastHits[b].distance)
                {
                    RaycastHit tmp = _doubleClickRaycastHits[a];
                    _doubleClickRaycastHits[a] = _doubleClickRaycastHits[b];
                    _doubleClickRaycastHits[b] = tmp;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            Collider c = _doubleClickRaycastHits[i].collider;
            if (c == null) continue;
            if (c.GetComponentInParent<RemoteBattleUnitView>() != null)
                return false;
            HexCell hex = c.GetComponentInParent<HexCell>();
            if (hex != null)
            {
                cell = hex;
                return true;
            }
        }

        return false;
    }

    private void UpdateLeftHoldIndicator()
    {
        if (Mouse.current == null) return;

        // ПКМ никогда не должна выполнять ЛКМ-логику.
        if (Mouse.current.rightButton.isPressed)
        {
            _heldRemoteTarget = null;
            _hasHoldIndicatorAnchor = false;
            SetHoldIndicatorVisible(false);
            IsHoldingRemoteTargetWithLeftMouse = false;
            return;
        }

        // При отпускании ЛКМ — сразу скрываем.
        if (!Mouse.current.leftButton.isPressed)
        {
            if (_heldRemoteTarget != null && _hoveredBodyPart != BodyPart.None)
                ApplyAttackOnRelease(_heldRemoteTarget, _hoveredBodyPart);
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

        bool shouldCaptureAnchor = Mouse.current.leftButton.wasPressedThisFrame || !_hasHoldIndicatorAnchor;
        Vector3 remoteHitPoint = Vector3.zero;
        RemoteBattleUnitView remote = shouldCaptureAnchor ? GetRemoteUnitUnderCursor(out remoteHitPoint) : null;
        if (remote != null)
        {
            _heldRemoteTarget = remote;
            _holdIndicatorAnchorWorld = remoteHitPoint;
            _hasHoldIndicatorAnchor = true;
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
        if (_camera != null)
            _holdIndicatorGo.transform.rotation = Quaternion.LookRotation(-_camera.transform.forward, _camera.transform.up);
        UpdateHoldIndicatorBodyPartHighlight();
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

    private RemoteBattleUnitView GetRemoteUnitUnderCursor(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (_camera == null || Mouse.current == null) return null;
        Vector2 pos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(new Vector3(pos.x, pos.y, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
        if (hits == null || hits.Length == 0)
            return null;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            RemoteBattleUnitView remote = hits[i].collider.GetComponentInParent<RemoteBattleUnitView>();
            if (remote != null)
            {
                hitPoint = hits[i].point;
                return remote;
            }
        }
        return null;
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
        _holdPartRenderers[part] = sr;
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

        foreach (var kv in _holdPartRenderers)
        {
            SpriteRenderer sr = kv.Value;
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
        if (ContainsUv(_headUvRect, u, v)) return BodyPart.Head;
        if (ContainsUv(_torsoUvRect, u, v)) return BodyPart.Torso;
        if (ContainsUv(_legsUvRect, u, v)) return BodyPart.Legs;
        return BodyPart.None;
    }

    private static bool ContainsUv(Rect rect, float u, float v)
    {
        float minU = Mathf.Clamp01(rect.xMin);
        float maxU = Mathf.Clamp01(rect.xMax);
        float minV = Mathf.Clamp01(rect.yMin);
        float maxV = Mathf.Clamp01(rect.yMax);
        return u >= minU && u <= maxU && v >= minV && v <= maxV;
    }

    private void UpdateHoldIndicatorBodyPartHighlight()
    {
        if (_holdIndicatorGo == null || _camera == null || Mouse.current == null)
            return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
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

    private void ApplyAttackOnRelease(RemoteBattleUnitView target, BodyPart part)
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

        bool shiftRepeat = Keyboard.current != null &&
            (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
        bool applied = GameSession.Active != null &&
            GameSession.Active.TryPerformSilhouetteAttack(target, label, shiftRepeat);
        if (!applied)
            GameSession.OnNetworkMessage?.Invoke("Атака не применена");
    }

    private void ApplyHoldPartColors(BodyPart highlightedPart)
    {
        if (_hoveredBodyPart == highlightedPart) return;
        _hoveredBodyPart = highlightedPart;

        foreach (var kv in _holdPartRenderers)
        {
            if (kv.Value == null) continue;
            kv.Value.color = kv.Key == highlightedPart ? _holdPartHighlightColor : _holdPartBaseColor;
        }
    }
}
