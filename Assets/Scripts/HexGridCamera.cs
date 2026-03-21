using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// Камера: при старте кадрирует весь HexGrid (ортографика, вид сверху). Колёсико — зум, перетаскивание — сдвиг (удобно после приближения).
/// </summary>
[RequireComponent(typeof(Camera))]
public class HexGridCamera : MonoBehaviour
{
    [SerializeField] private HexGrid _grid;
    [Tooltip("Отступ от краёв сетки (world units).")]
    [SerializeField] private float _padding = 2f;

    [Header("Зум")]
    [Tooltip("Чувствительность колёсика (чем больше — сильнее зум за один щелчок).")]
    [SerializeField] private float _zoomSensitivity = 3f;
    [Tooltip("Минимальный orthographic size относительно начального «вся сетка» (меньше — сильнее приближение). Отдалить дальше начального кадра нельзя.")]
    [SerializeField] [Range(0.05f, 1f)] private float _minZoomFactor = 0.2f;

    [Header("Перетаскивание камеры (drag)")]
    [Tooltip("0 = ЛКМ, 1 = ПКМ, 2 = СКМ. Рекомендуется СКМ или ПКМ, чтобы не мешать кликам по гексам.")]
    [SerializeField] private int _panMouseButton = 0;
    [Tooltip("Чувствительность сдвига при перетаскивании.")]
    [SerializeField] private float _panSensitivity = 0.35f;
    [Tooltip("Разрешать сдвиг только если зум ближе, чем «вся сетка» (orthographic size меньше максимума).")]
    [SerializeField] private bool _panOnlyWhenZoomedIn = true;

    private Camera _cam;
    private float _orthoMin;
    private float _orthoMax;
    private float _mapMinX, _mapMaxX, _mapMinZ, _mapMaxZ;
    private bool _hasMapBounds;
    private float _lastZoomInputTime = -999f;
    private int _zoomChangeCount;

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

        _cam.orthographic = true;
        float aspect = _cam.aspect;
        float sizeByHeight = height * 0.5f;
        float sizeByWidth = (width * 0.5f) / aspect;
        float fitAll = Mathf.Max(sizeByHeight, sizeByWidth, 1f);
        _cam.orthographicSize = fitAll;

        _orthoMin = Mathf.Max(0.5f, fitAll * _minZoomFactor);
        _orthoMax = fitAll; // отдаление не дальше начального состояния (вся сетка в кадре)
        if (_orthoMax <= _orthoMin)
            _orthoMin = Mathf.Max(0.5f, _orthoMax * 0.5f);

        float camY = Mathf.Max(10f, _cam.orthographicSize);
        transform.position = new Vector3(centerX, camY, centerZ);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void Update()
    {
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

    /// <summary>Пустой метод для совместимости с вызовами из GameSession и др.</summary>
    public void RefocusOnLocalPlayer() { }

    /// <summary>Центрирует камеру по XZ на заданной мировой позиции (Y сохраняется/пересчитывается от текущего зума).</summary>
    public void FocusOnWorldPosition(Vector3 worldPosition)
    {
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
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            // SmoothStep для более мягкого начала/конца
            float k = t * t * (3f - 2f * t);

            transform.position = Vector3.Lerp(startPos, targetPos, k);
            _cam.orthographicSize = Mathf.Lerp(startSize, targetSize, k);
            ClampPanToMap();
            yield return null;
        }

        transform.position = targetPos;
        _cam.orthographicSize = targetSize;
        ClampPanToMap();
    }
}
