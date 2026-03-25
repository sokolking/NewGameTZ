using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// Камера: при старте кадрирует весь HexGrid (ортографика, вид сверху). Колёсико — зум, перетаскивание — сдвиг (удобно после приближения).
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-100)]
public class HexGridCamera : MonoBehaviour
{
    /// <summary>Синхронно с режимом слежения 3-го лица (для HexCell: не красить hover серым при движении перспективной камеры).</summary>
    public static bool ThirdPersonFollowActive { get; private set; }

    /// <summary>True после завершения входа в 3-е лицо (не только во время плавного перехода).</summary>
    public bool IsFollowThirdPersonFullyActive => _followThirdPersonActive;

    [SerializeField] private HexGrid _grid;
    [Tooltip("Padding from grid edges (world units).")]
    [SerializeField] private float _padding = 2f;

    [Header("Zoom")]
    [Tooltip("Mouse wheel sensitivity (higher = stronger zoom per notch).")]
    [SerializeField] private float _zoomSensitivity = 3f;
    [Tooltip("Minimum orthographic size vs initial \"fit whole grid\" (lower = zoom in more). Cannot zoom out past initial frame.")]
    [SerializeField] [Range(0.05f, 1f)] private float _minZoomFactor = 0.2f;

    [Header("Camera pan (drag)")]
    [Tooltip("0 = LMB, 1 = RMB, 2 = MMB. Prefer MMB or RMB so hex clicks are not blocked.")]
    [SerializeField] private int _panMouseButton = 0;
    [Tooltip("Pan sensitivity while dragging.")]
    [SerializeField] private float _panSensitivity = 0.35f;
    [Tooltip("Allow pan only when zoomed in closer than \"fit whole grid\" (ortho size below max).")]
    [SerializeField] private bool _panOnlyWhenZoomedIn = true;

    private Camera _cam;
    private float _orthoMin;
    private float _orthoMax;
    private float _mapMinX, _mapMaxX, _mapMinZ, _mapMaxZ;
    private bool _hasMapBounds;
    private float _lastZoomInputTime = -999f;
    private int _zoomChangeCount;

    [Header("Third-person follow during move animation")]
    [Tooltip("Camera height above ground relative to unit.")]
    [SerializeField] private float _followHeight = 5f;
    [Tooltip("Horizontal distance behind target (relative to where the target faces).")]
    [SerializeField] private float _followBackDistance = 6f;
    [Tooltip("Camera rotation smoothing toward target (higher = less jitter on pitch/yaw).")]
    [SerializeField] private float _followRotationLerp = 18f;
    [Tooltip("Camera position smoothing (0 = rigid follow; >0 may jitter with movement lerp).")]
    [SerializeField] private float _followPositionSmoothTime = 0f;
    [Tooltip("Duration of smooth zoom from top-down into third person before move animation.")]
    [SerializeField] private float _thirdPersonEnterDuration = 0.45f;
    [Tooltip("Duration of smooth return to top-down after move animation.")]
    [SerializeField] private float _thirdPersonExitDuration = 0.5f;

    [Header("Third person: orbit (manual rotate)")]
    [Tooltip("0 = LMB, 1 = RMB, 2 = MMB. Hold and drag to orbit camera around target.")]
    [SerializeField] private int _thirdPersonOrbitMouseButton = 0;
    [Tooltip("Yaw sensitivity (deg/pixel).")]
    [SerializeField] private float _thirdPersonOrbitYawSensitivity = 0.5f;
    [Tooltip("Pitch sensitivity (deg/pixel).")]
    [SerializeField] private float _thirdPersonOrbitPitchSensitivity = 0.12f;
    [Tooltip("Minimum pitch (slightly from above).")]
    [SerializeField] private float _thirdPersonOrbitPitchMin = -35f;
    [Tooltip("Maximum pitch (can look from below).")]
    [SerializeField] private float _thirdPersonOrbitPitchMax = 55f;

    private Transform _followTarget;
    private Vector3 _followCamPosVelocity;
    private Vector3 _savedPosition;
    private Quaternion _savedRotation;
    private bool _savedOrthographic;
    private float _savedOrthoSize;
    private float _savedFieldOfView;
    private bool _followThirdPersonActive;
    private float _thirdPersonOrbitYawDeg;
    private float _thirdPersonOrbitPitchDeg;
    /// <summary>За текущее удержание ЛКМ орбиты уже было движение мыши — не показывать прицел атаки по юниту.</summary>
    private bool _thirdPersonOrbitLmbDragThisPress;

    /// <summary>Режим 3-го лица: после движения мыши с зажатой ЛКМ орбиты — скрыть индикатор/контур атаки (см. <see cref="HexInputManager"/>).</summary>
    public bool ThirdPersonOrbitSuppressAttackHold =>
        ThirdPersonFollowActive
        && _thirdPersonOrbitMouseButton == 0
        && _thirdPersonOrbitLmbDragThisPress;

    /// <summary>Сбрасывает «орбиту ЛКМ в этом нажатии», когда начато удержание цели по силуэту (камера обновляется раньше ввода).</summary>
    public void ClearThirdPersonOrbitLmbDragThisPress()
    {
        _thirdPersonOrbitLmbDragThisPress = false;
    }

    public float LastZoomInputTime => _lastZoomInputTime;
    public int ZoomChangeCount => _zoomChangeCount;
    public bool IsZoomApplied => _cam != null && _cam.orthographic && _cam.orthographicSize < _orthoMax - 0.001f;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    private void Start()
    {
        if (_cam == null) return;
        if (_grid == null) return;

        _grid.GetGridBoundsWorld(out float minX, out float maxX, out float minZ, out float maxZ);
        _mapMinX = minX;
        _mapMaxX = maxX;
        _mapMinZ = minZ;
        _mapMaxZ = maxZ;
        _hasMapBounds = true;

        float width = maxX - minX + 2f * _padding;
        float height = maxZ - minZ + 2f * _padding;
        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;

        float aspect = _cam.aspect;
        float sizeByHeight = height * 0.5f;
        float sizeByWidth = (width * 0.5f) / aspect;
        float fitAll = Mathf.Max(sizeByHeight, sizeByWidth, 1f);

        _orthoMin = Mathf.Max(0.5f, fitAll * _minZoomFactor);
        _orthoMax = fitAll; // отдаление не дальше начального состояния (вся сетка в кадре)
        if (_orthoMax <= _orthoMin)
            _orthoMin = Mathf.Max(0.5f, _orthoMax * 0.5f);

        // Если Start выполнился позже анимации хода, не сбрасываем камеру в орто «сверху» поверх 3-го лица.
        if (!_followThirdPersonActive)
        {
            _cam.orthographic = true;
            _cam.orthographicSize = fitAll;

            float camY = Mathf.Max(10f, _cam.orthographicSize);
            transform.position = new Vector3(centerX, camY, centerZ);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            _cam.orthographicSize = fitAll;
        }
    }

    private void Update()
    {
        if (ThirdPersonFollowActive && _followTarget != null)
            UpdateThirdPersonOrbitInput();

        if (_followThirdPersonActive && _followTarget != null)
            return;
        if (_cam == null || !_cam.orthographic) return;
        if (Mouse.current == null) return;
        if (GameplayMapInputBlock.IsBlocked)
            return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        bool canZoom = Mathf.Abs(scroll) >= 0.0001f;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        bool panPressed = _panMouseButton == 0 ? Mouse.current.leftButton.isPressed
            : _panMouseButton == 1 ? Mouse.current.rightButton.isPressed
            : Mouse.current.middleButton.isPressed;
        bool panNeedsWork = panPressed && mouseDelta.sqrMagnitude >= 0.0001f;

        if (!canZoom && !panNeedsWork)
            return;

        if (canZoom)
            UpdateZoom(scroll);

        if (panNeedsWork)
            UpdatePan();
    }

    private void UpdateZoom(float scroll)
    {
        if (Mathf.Abs(scroll) < 0.0001f) return;

        // Вверх = приблизить (уменьшить orthographic size), вниз = отдалить
        float delta = -scroll * _zoomSensitivity * 0.01f;
        _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize + delta, _orthoMin, _orthoMax);
        _lastZoomInputTime = Time.unscaledTime;
        _zoomChangeCount++;

        float camY = Mathf.Max(10f, _cam.orthographicSize);
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, camY, p.z);
        ClampPanToMap();
    }

    private void UpdatePan()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Если ЛКМ удерживает индикатор цели на другом юните, не панорамируем карту.
        if (_panMouseButton == 0 && HexInputManager.IsHoldingRemoteTargetWithLeftMouse)
            return;

        if (_panOnlyWhenZoomedIn && _cam.orthographicSize >= _orthoMax - 0.001f)
            return;

        bool pressed = _panMouseButton == 0 ? Mouse.current.leftButton.isPressed
            : _panMouseButton == 1 ? Mouse.current.rightButton.isPressed
            : Mouse.current.middleButton.isPressed;

        if (!pressed) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        if (mouseDelta.sqrMagnitude < 0.0001f) return;

        Transform t = transform;
        Vector3 rightXZ = t.right;
        rightXZ.y = 0f;
        if (rightXZ.sqrMagnitude < 0.0001f) rightXZ = Vector3.right;
        rightXZ.Normalize();

        Vector3 upXZ = t.up;
        upXZ.y = 0f;
        if (upXZ.sqrMagnitude < 0.0001f) upXZ = Vector3.forward;
        upXZ.Normalize();

        // Тянем «карту»: двигаем камеру против движения мыши (как в стратегиях)
        Vector3 move = (-rightXZ * mouseDelta.x - upXZ * mouseDelta.y) * _panSensitivity;
        move.y = 0f;
        t.position += move;

        float camY = Mathf.Max(10f, _cam.orthographicSize);
        Vector3 p = t.position;
        t.position = new Vector3(p.x, camY, p.z);

        ClampPanToMap();
    }

    private void UpdateThirdPersonOrbitInput()
    {
        if (Mouse.current == null) return;
        if (GameplayMapInputBlock.IsBlocked) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        bool orbitPressed = _thirdPersonOrbitMouseButton == 0
            ? Mouse.current.leftButton.isPressed
            : _thirdPersonOrbitMouseButton == 1
                ? Mouse.current.rightButton.isPressed
                : Mouse.current.middleButton.isPressed;

        if (!orbitPressed)
        {
            _thirdPersonOrbitLmbDragThisPress = false;
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        if (mouseDelta.sqrMagnitude < 0.0001f)
            return;

        // Движение мыши по силуэту цели (ЛКМ) не считаем орбитой — иначе флаг выставлялся до этой проверки и индикатор исчезал.
        if (_thirdPersonOrbitMouseButton == 0 && HexInputManager.IsHoldingRemoteTargetWithLeftMouse)
            return;

        if (_thirdPersonOrbitMouseButton == 0)
            _thirdPersonOrbitLmbDragThisPress = true;

        _thirdPersonOrbitYawDeg += mouseDelta.x * _thirdPersonOrbitYawSensitivity;
        _thirdPersonOrbitPitchDeg -= mouseDelta.y * _thirdPersonOrbitPitchSensitivity;
        _thirdPersonOrbitPitchDeg = Mathf.Clamp(_thirdPersonOrbitPitchDeg, _thirdPersonOrbitPitchMin, _thirdPersonOrbitPitchMax);
    }

    /// <summary>
    /// Смещение камеры от позиции цели с учётом дистанции, высоты и ручной орбиты.
    /// </summary>
    private Vector3 ComputeThirdPersonCameraOffsetFromPlayer(Vector3 flatHorizontalForward)
    {
        Vector3 flat = flatHorizontalForward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-8f)
            flat = Vector3.forward;
        else
            flat.Normalize();

        Vector3 hz = Quaternion.AngleAxis(_thirdPersonOrbitYawDeg, Vector3.up) * (-flat * _followBackDistance);
        Vector3 right = Vector3.Cross(Vector3.up, hz);
        if (right.sqrMagnitude < 1e-8f)
            right = Vector3.right;
        else
            right.Normalize();

        return Quaternion.AngleAxis(_thirdPersonOrbitPitchDeg, right) * hz + Vector3.up * _followHeight;
    }

    /// <summary>
    /// Вид сверху (90,0,0): половина видимой ширины по X = orthoSize * aspect, по Z = orthoSize.
    /// </summary>
    private void ClampPanToMap()
    {
        if (!_hasMapBounds || _cam == null) return;

        float halfVisX = _cam.orthographicSize * _cam.aspect;
        float halfVisZ = _cam.orthographicSize;

        float mapW = _mapMaxX - _mapMinX;
        float mapH = _mapMaxZ - _mapMinZ;
        float cx = (_mapMinX + _mapMaxX) * 0.5f;
        float cz = (_mapMinZ + _mapMaxZ) * 0.5f;

        Vector3 p = transform.position;
        float px = p.x;
        float pz = p.z;

        if (mapW <= 2f * halfVisX)
            px = cx;
        else
        {
            float minPx = _mapMinX + halfVisX;
            float maxPx = _mapMaxX - halfVisX;
            px = Mathf.Clamp(px, minPx, maxPx);
        }

        if (mapH <= 2f * halfVisZ)
            pz = cz;
        else
        {
            float minPz = _mapMinZ + halfVisZ;
            float maxPz = _mapMaxZ - halfVisZ;
            pz = Mathf.Clamp(pz, minPz, maxPz);
        }

        transform.position = new Vector3(px, Mathf.Max(10f, _cam.orthographicSize), pz);
    }

    private void LateUpdate()
    {
        if (!_followThirdPersonActive || _followTarget == null || _cam == null)
            return;

        // Иначе другой код / поздний Start() может снова включить ortho — картинка как «вид сверху».
        _cam.orthographic = false;

        ApplyThirdPersonFollowPose(instantRotation: false);
    }

    /// <summary>
    /// Поза 3-го лица: базовое «сзади» по <see cref="GetFollowBehindHorizontalForward"/> плюс ручная орбита (<see cref="_thirdPersonOrbitYawDeg"/>, <see cref="_thirdPersonOrbitPitchDeg"/>).
    /// </summary>
    private void ApplyThirdPersonFollowPose(bool instantRotation = false)
    {
        // При паузе (Time.timeScale = 0) Time.deltaTime == 0 — SmoothDamp/Slerp дают сбой или рывок камеры.
        float dt = CameraEffectiveDeltaTime();

        Vector3 p = _followTarget.position;
        Vector3 flat = GetFollowBehindHorizontalForward();

        Vector3 desiredCamPos = p + ComputeThirdPersonCameraOffsetFromPlayer(flat);
        Vector3 lookAt = p + Vector3.up * 0.5f;
        if (_followPositionSmoothTime > 1e-5f)
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredCamPos,
                ref _followCamPosVelocity,
                _followPositionSmoothTime,
                Mathf.Infinity,
                dt);
        }
        else
        {
            transform.position = desiredCamPos;
            _followCamPosVelocity = Vector3.zero;
        }

        Vector3 lookDir = lookAt - transform.position;
        if (lookDir.sqrMagnitude > 1e-8f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            if (instantRotation)
                transform.rotation = targetRot;
            else
            {
                float k = 1f - Mathf.Exp(-_followRotationLerp * dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);
            }
        }
    }

    /// <summary>При timeScale = 0 scaled delta = 0; для камеры используем unscaled, чтобы пауза не ломала сглаживание.</summary>
    private static float CameraEffectiveDeltaTime()
    {
        return Time.deltaTime > 1e-7f ? Time.deltaTime : Time.unscaledDeltaTime;
    }

    /// <summary>
    /// Горизонтальный «вперёд» для расчёта точки «сзади» от цели. Не используем <see cref="Transform.forward"/> цели:
    /// иначе поворот персонажа (модель/корень) крутит вектор камеры вместе с орбитой мышью.
    /// Орбита задаётся пользователем через <see cref="_thirdPersonOrbitYawDeg"/>.
    /// </summary>
    private Vector3 GetFollowBehindHorizontalForward()
    {
        return Vector3.forward;
    }

    private static float ApproxVerticalFovFromOrthoTopDown(float orthoSize, float cameraY)
    {
        float h = Mathf.Max(0.01f, cameraY);
        return 2f * Mathf.Atan(orthoSize / h) * Mathf.Rad2Deg;
    }

    /// <summary>Конечная поза 3-го лица по ориентации цели и позиции.</summary>
    private void ComputeThirdPersonEndPose(out Vector3 endPos, out Quaternion endRot, out float endFov)
    {
        Vector3 p = _followTarget.position;
        Vector3 flat = GetFollowBehindHorizontalForward();

        Vector3 desiredCamPos = p + ComputeThirdPersonCameraOffsetFromPlayer(flat);
        Vector3 lookAt = p + Vector3.up * 0.5f;
        Vector3 lookDir = lookAt - desiredCamPos;
        if (lookDir.sqrMagnitude > 1e-8f)
            endRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        else
            endRot = transform.rotation;
        endPos = desiredCamPos;
        endFov = _savedFieldOfView < 25f ? 55f : _savedFieldOfView;
        if (endFov < 30f)
            endFov = 55f;
    }

    /// <summary>Плавный переход из орто «сверху» в 3-е лицо. Дождаться в корутине перед анимацией хода.</summary>
    /// <param name="initialHorizontalDir">Не используется; база «сзади» — мировой +Z, орбита — мышью.</param>
    public IEnumerator EnterThirdPersonFollowRoutine(Transform target, Vector3? initialHorizontalDir = null)
    {
        if (target == null) yield break;
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) yield break;
        if (_followThirdPersonActive)
            EndThirdPersonFollowImmediate();

        StopAllCoroutines();

        _savedPosition = transform.position;
        _savedRotation = transform.rotation;
        _savedOrthographic = _cam.orthographic;
        _savedOrthoSize = _cam.orthographicSize;
        _savedFieldOfView = _cam.fieldOfView;

        _followTarget = target;
        _thirdPersonOrbitYawDeg = 0f;
        _thirdPersonOrbitPitchDeg = 0f;
        _thirdPersonOrbitLmbDragThisPress = false;

        ComputeThirdPersonEndPose(out Vector3 endPos, out Quaternion endRot, out float endFov);

        Vector3 startPos = _savedPosition;
        Quaternion startRot = _savedRotation;
        float startFovPersp = _savedOrthographic
            ? ApproxVerticalFovFromOrthoTopDown(_savedOrthoSize, _savedPosition.y)
            : Mathf.Clamp(_savedFieldOfView, 5f, 170f);

        ThirdPersonFollowActive = true;
        HexCell.RefreshHoverAfterThirdPersonCamera();

        _cam.orthographic = false;
        _cam.fieldOfView = startFovPersp;

        float duration = Mathf.Max(0.01f, _thirdPersonEnterDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float k = t * t * (3f - 2f * t);

            transform.position = Vector3.Lerp(startPos, endPos, k);
            transform.rotation = Quaternion.Slerp(startRot, endRot, k);
            _cam.fieldOfView = Mathf.Lerp(startFovPersp, endFov, k);
            yield return null;
        }

        transform.SetPositionAndRotation(endPos, endRot);
        _cam.fieldOfView = endFov;

        _followThirdPersonActive = true;
        HexCell.RefreshHoverAfterThirdPersonCamera();
        _followCamPosVelocity = Vector3.zero;
        ApplyThirdPersonFollowPose(instantRotation: true);
    }

    /// <summary>Мгновенный переход в 3-е лицо (без анимации). Для кнопки режима планирование/просмотр.</summary>
    public void EnterThirdPersonFollowImmediate(Transform target, Vector3? initialHorizontalDir = null)
    {
        if (target == null) return;
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;
        if (_followThirdPersonActive)
            EndThirdPersonFollowImmediate();

        StopAllCoroutines();

        _savedPosition = transform.position;
        _savedRotation = transform.rotation;
        _savedOrthographic = _cam.orthographic;
        _savedOrthoSize = _cam.orthographicSize;
        _savedFieldOfView = _cam.fieldOfView;

        _followTarget = target;
        _thirdPersonOrbitYawDeg = 0f;
        _thirdPersonOrbitPitchDeg = 0f;
        _thirdPersonOrbitLmbDragThisPress = false;

        ComputeThirdPersonEndPose(out Vector3 endPos, out Quaternion endRot, out float endFov);

        ThirdPersonFollowActive = true;
        HexCell.RefreshHoverAfterThirdPersonCamera();

        _cam.orthographic = false;
        _cam.fieldOfView = endFov;

        transform.SetPositionAndRotation(endPos, endRot);

        _followThirdPersonActive = true;
        HexCell.RefreshHoverAfterThirdPersonCamera();
        _followCamPosVelocity = Vector3.zero;
        ApplyThirdPersonFollowPose(instantRotation: true);
    }

    /// <summary>Плавный возврат к сохранённому орто-виду после анимации.</summary>
    public IEnumerator ExitThirdPersonFollowRoutine()
    {
        if (!_followThirdPersonActive)
            yield break;
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) yield break;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        float startFov = _cam.fieldOfView;

        _followTarget = null;
        _followThirdPersonActive = false;
        _thirdPersonOrbitYawDeg = 0f;
        _thirdPersonOrbitPitchDeg = 0f;
        _thirdPersonOrbitLmbDragThisPress = false;

        Vector3 endPos = _savedPosition;
        Quaternion endRot = _savedRotation;
        float endFovPersp = _savedOrthographic
            ? ApproxVerticalFovFromOrthoTopDown(_savedOrthoSize, _savedPosition.y)
            : Mathf.Clamp(_savedFieldOfView, 5f, 170f);

        float duration = Mathf.Max(0.01f, _thirdPersonExitDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float k = t * t * (3f - 2f * t);

            transform.position = Vector3.Lerp(startPos, endPos, k);
            transform.rotation = Quaternion.Slerp(startRot, endRot, k);
            _cam.fieldOfView = Mathf.Lerp(startFov, endFovPersp, k);
            _cam.orthographic = false;
            yield return null;
        }

        ThirdPersonFollowActive = false;
        HexCell.RefreshHoverAfterThirdPersonCamera();

        transform.SetPositionAndRotation(_savedPosition, _savedRotation);
        _cam.orthographic = _savedOrthographic;
        _cam.orthographicSize = _savedOrthoSize;
        _cam.fieldOfView = _savedFieldOfView;
    }

    /// <summary>Мгновенный сброс (прерывание, ForceStopMovement).</summary>
    public void EndThirdPersonFollowImmediate()
    {
        if (!_followThirdPersonActive && !ThirdPersonFollowActive)
            return;

        StopAllCoroutines();

        _followTarget = null;
        _followThirdPersonActive = false;
        ThirdPersonFollowActive = false;
        _thirdPersonOrbitYawDeg = 0f;
        _thirdPersonOrbitPitchDeg = 0f;
        _thirdPersonOrbitLmbDragThisPress = false;
        HexCell.RefreshHoverAfterThirdPersonCamera();

        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;

        transform.SetPositionAndRotation(_savedPosition, _savedRotation);
        _cam.orthographic = _savedOrthographic;
        _cam.orthographicSize = _savedOrthoSize;
        _cam.fieldOfView = _savedFieldOfView;
    }

    /// <summary>Устар.: используйте <see cref="EnterThirdPersonFollowRoutine"/>.</summary>
    public void BeginThirdPersonFollow(Transform target, Vector3? initialHorizontalDir = null)
    {
        if (target == null) return;
        StartCoroutine(EnterThirdPersonFollowRoutine(target, initialHorizontalDir));
    }

    /// <summary>Устар.: используйте <see cref="ExitThirdPersonFollowRoutine"/> или <see cref="EndThirdPersonFollowImmediate"/>.</summary>
    public void EndThirdPersonFollow()
    {
        EndThirdPersonFollowImmediate();
    }

    /// <summary>Пустой метод для совместимости с вызовами из GameSession и др.</summary>
    public void RefocusOnLocalPlayer() { }

    /// <summary>Центрирует камеру по XZ на заданной мировой позиции (Y сохраняется/пересчитывается от текущего зума).</summary>
    public void FocusOnWorldPosition(Vector3 worldPosition)
    {
        if (_followThirdPersonActive || ThirdPersonFollowActive) return;
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;
        float camY = Mathf.Max(10f, _cam.orthographic ? _cam.orthographicSize : transform.position.y);
        transform.position = new Vector3(worldPosition.x, camY, worldPosition.z);
        ClampPanToMap();
    }

    /// <summary>
    /// Приблизить камеру в factor раз (например 10 = зум x10).
    /// Для ортографической камеры это уменьшает orthographicSize с учётом внутренних границ.
    /// </summary>
    public void ZoomInByFactor(float factor)
    {
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null || !_cam.orthographic) return;
        float safeFactor = Mathf.Max(1f, factor);
        float newSize = _cam.orthographicSize / safeFactor;
        _cam.orthographicSize = Mathf.Clamp(newSize, _orthoMin, _orthoMax);
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, Mathf.Max(10f, _cam.orthographicSize), p.z);
        ClampPanToMap();
    }

    /// <summary>
    /// Плавно центрирует камеру на worldPosition и плавно приближает в factor раз за duration секунд.
    /// </summary>
    public IEnumerator FocusAndZoomSmooth(Vector3 worldPosition, float factor, float duration)
    {
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null || !_cam.orthographic)
            yield break;

        float safeFactor = Mathf.Max(1f, factor);
        float safeDuration = Mathf.Max(0.01f, duration);

        Vector3 startPos = transform.position;
        float startSize = _cam.orthographicSize;
        float targetSize = Mathf.Clamp(startSize / safeFactor, _orthoMin, _orthoMax);
        Vector3 targetPos = new Vector3(worldPosition.x, Mathf.Max(10f, targetSize), worldPosition.z);

        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            if (_followThirdPersonActive || _cam == null || !_cam.orthographic)
                yield break;

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            // SmoothStep для более мягкого начала/конца
            float k = t * t * (3f - 2f * t);

            transform.position = Vector3.Lerp(startPos, targetPos, k);
            _cam.orthographicSize = Mathf.Lerp(startSize, targetSize, k);
            ClampPanToMap();
            yield return null;
        }

        if (_followThirdPersonActive || _cam == null || !_cam.orthographic)
            yield break;

        transform.position = targetPos;
        _cam.orthographicSize = targetSize;
        ClampPanToMap();
    }
}
