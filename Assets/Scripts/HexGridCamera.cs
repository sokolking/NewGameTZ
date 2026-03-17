using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Камера над картой. Тянуть влево/вправо/вверх/вниз можно, пока видны гексы. Как только виден последний гекс в эту сторону и дальше гексов нет — тянуть в эту сторону нельзя (серое не показываем).
/// </summary>
public class HexGridCamera : MonoBehaviour
{
    [SerializeField] private HexGrid _grid;
    [SerializeField] private float _height = 30f;
    [SerializeField] private float _padding = 2f;

    [Header("Изначальная позиция и поворот")]
    [Tooltip("Если включено — в Start() задаются Position и Rotation ниже; иначе считается от центра сетки.")]
    [SerializeField] private bool _useFixedInitialTransform = true;
    [SerializeField] private Vector3 _initialPosition = new Vector3(32f, 10f, 20f);
    [SerializeField] private Vector3 _initialRotationEuler = new Vector3(90f, 100f, 160f);

    [Header("Вид сбоку (если не используется фиксированный трансформ)")]
    [Tooltip("0 = строго сверху, 90 = с горизонта. Например 35–50 — вид сбоку сверху.")]
    [SerializeField] [Range(0f, 89f)] private float _elevationDegrees = 45f;
    [Tooltip("Градусы вокруг поля: 0 = с одной стороны, 180 = с противоположной.")]
    [SerializeField] [Range(-180f, 180f)] private float _azimuthDegrees = 0f;

    [Header("Панорамирование (зажать и тянуть — сдвиг по карте по X и Z)")]
    [Tooltip("0 = левая, 1 = правая, 2 = средняя кнопка мыши.")]
    [SerializeField] private int _panMouseButton = 0;
    [Tooltip("Чувствительность сдвига (по оси абсцисс и ординат).")]
    [SerializeField] private float _panSensitivity = 0.5f;
    [Tooltip("Отступ от краёв карты (0 = вплотную к последнему гексу).")]
    [SerializeField] private float _boundsInset = 0f;

    private Camera _cam;

    private void Start()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        ApplyInitialPosition();
        ClampPositionToGrid();
    }

    private void Update()
    {
        if (_cam == null) return;
        UpdatePan();
    }

    private void ApplyInitialPosition()
    {
        if (_cam == null) return;

        if (_useFixedInitialTransform)
        {
            _cam.transform.position = _initialPosition;
            _cam.transform.rotation = Quaternion.Euler(_initialRotationEuler);
            return;
        }

        if (_grid == null) return;

        Vector3 center = _grid.GetGridCenterWorld();
        float size = _grid.GetGridSize();
        float distance = size * 0.5f + _padding;
        distance = Mathf.Max(distance, _height);

        float el = _elevationDegrees * Mathf.Deg2Rad;
        float az = _azimuthDegrees * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(
            Mathf.Sin(az) * Mathf.Cos(el),
            Mathf.Sin(el),
            Mathf.Cos(az) * Mathf.Cos(el)
        ) * distance;

        _cam.transform.position = center + offset;
        _cam.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
    }

    /// <summary>Тянуть можно, пока видны гексы; как только в эту сторону виден последний гекс — дальше тянуть в эту сторону нельзя.</summary>
    private void UpdatePan()
    {
        if (Mouse.current == null) return;

        bool pressed = _panMouseButton == 0 ? Mouse.current.leftButton.isPressed
            : _panMouseButton == 1 ? Mouse.current.rightButton.isPressed
            : Mouse.current.middleButton.isPressed;

        if (!pressed) return;

        Vector2 delta = Mouse.current.delta.ReadValue();
        if (delta.sqrMagnitude < 0.0001f) return;

        Transform t = _cam.transform;

        Vector3 rightXZ = t.right;
        rightXZ.y = 0f;
        if (rightXZ.sqrMagnitude < 0.0001f) rightXZ = Vector3.right;
        rightXZ.Normalize();

        Vector3 upXZ = t.up;
        upXZ.y = 0f;
        if (upXZ.sqrMagnitude < 0.0001f) upXZ = Vector3.forward;
        upXZ.Normalize();

        Vector3 move = (-rightXZ * delta.x - upXZ * delta.y) * _panSensitivity;
        move.y = 0f;

        if (_grid == null)
        {
            t.position += move;
            return;
        }

        _grid.GetGridBoundsWorld(out float mapMinX, out float mapMaxX, out float mapMinZ, out float mapMaxZ);

        float inset = Mathf.Max(0f, _boundsInset);
        mapMinX += inset;
        mapMaxX -= inset;
        mapMinZ += inset;
        mapMaxZ -= inset;

        // Применяем движение и сразу откатываем, если нарушается условие
        Vector3 originalPos = t.position;
        t.position += move;

        // Проверяем: смотрит ли хоть какая-то точка экрана на карту?
        if (!AnyScreenPointHitsMap(mapMinX, mapMaxX, mapMinZ, mapMaxZ))
        {
            t.position = originalPos + new Vector3(move.x, 0f, 0f);
            bool xOk = AnyScreenPointHitsMap(mapMinX, mapMaxX, mapMinZ, mapMaxZ);

            t.position = originalPos + new Vector3(0f, 0f, move.z);
            bool zOk = AnyScreenPointHitsMap(mapMinX, mapMaxX, mapMinZ, mapMaxZ);

            if (xOk && !zOk)
                t.position = originalPos + new Vector3(move.x, 0f, 0f);
            else if (!xOk && zOk)
                t.position = originalPos + new Vector3(0f, 0f, move.z);
            else if (!xOk && !zOk)
                t.position = originalPos;
        }
    }

    /// <summary>Семплирует сетку точек по экрану. True, если хотя бы одна точка попадает внутрь карты на Y=0.</summary>
    private bool AnyScreenPointHitsMap(float mapMinX, float mapMaxX, float mapMinZ, float mapMaxZ)
    {
        const int steps = 8;

        for (int i = 0; i <= steps; i++)
            for (int j = 0; j <= steps; j++)
            {
                float u = i / (float)steps;
                float v = j / (float)steps;
                Ray ray = _cam.ViewportPointToRay(new Vector3(u, v, 0f));
                if (GroundPlane.Raycast(ray, out float enter) && enter > 0f)
                {
                    Vector3 p = ray.GetPoint(enter);
                    if (p.x >= mapMinX && p.x <= mapMaxX && p.z >= mapMinZ && p.z <= mapMaxZ)
                        return true;
                }
            }
        return false;
    }

    /// <summary>Подтягивает камеру в границы карты (один раз в Start).</summary>
    private void ClampPositionToGrid()
    {
        if (_grid == null || _cam == null) return;
        _grid.GetGridBoundsWorld(out float mapMinX, out float mapMaxX, out float mapMinZ, out float mapMaxZ);
        GetVisibleAABBOnGround(mapMinX, mapMaxX, mapMinZ, mapMaxZ, out float visMinX, out float visMaxX, out float visMinZ, out float visMaxZ, out _, out _, out _, out _);
        float inset = Mathf.Max(0f, _boundsInset);
        mapMinX += inset;
        mapMaxX -= inset;
        mapMinZ += inset;
        mapMaxZ -= inset;
        float dx = 0f, dz = 0f;
        if (visMinX < mapMinX) dx = mapMinX - visMinX;
        else if (visMaxX > mapMaxX) dx = mapMaxX - visMaxX;
        if (visMinZ < mapMinZ) dz = mapMinZ - visMinZ;
        else if (visMaxZ > mapMaxZ) dz = mapMaxZ - visMaxZ;
        if (Mathf.Abs(dx) > 0.0001f || Mathf.Abs(dz) > 0.0001f)
            _cam.transform.position += new Vector3(dx, 0f, dz);
    }

    private static readonly Plane GroundPlane = new Plane(Vector3.up, 0f);

    /// <summary>Видимый AABB на Y=0 и флаги, попали ли лучи в углы на землю. Если не попали — подставляем границу карты для AABB (ограничение сдвига), но запрет «тянуть за край» включаем только при has*.</summary>
    private void GetVisibleAABBOnGround(float mapMinX, float mapMaxX, float mapMinZ, float mapMaxZ,
        out float minX, out float maxX, out float minZ, out float maxZ,
        out bool hasLeft, out bool hasRight, out bool hasBottom, out bool hasTop)
    {
        float mnX = float.MaxValue, mxX = float.MinValue, mnZ = float.MaxValue, mxZ = float.MinValue;
        hasLeft = false;
        hasRight = false;
        hasBottom = false;
        hasTop = false;
        for (int i = 0; i <= 1; i++)
            for (int j = 0; j <= 1; j++)
            {
                Ray ray = _cam.ViewportPointToRay(new Vector3(i, j, 0f));
                if (GroundPlane.Raycast(ray, out float enter) && enter > 0f)
                {
                    Vector3 p = ray.GetPoint(enter);
                    if (p.x < mnX) mnX = p.x;
                    if (p.x > mxX) mxX = p.x;
                    if (p.z < mnZ) mnZ = p.z;
                    if (p.z > mxZ) mxZ = p.z;
                    if (i == 0) hasLeft = true;
                    else hasRight = true;
                    if (j == 0) hasBottom = true;
                    else hasTop = true;
                }
            }
        float cx = _cam.transform.position.x, cz = _cam.transform.position.z;
        if (!hasLeft) mnX = mapMinX;
        if (!hasRight) mxX = mapMaxX;
        if (!hasBottom) mnZ = mapMinZ;
        if (!hasTop) mxZ = mapMaxZ;
        if (mnX > mxX) { mnX = cx - 0.01f; mxX = cx + 0.01f; }
        if (mnZ > mxZ) { mnZ = cz - 0.01f; mxZ = cz + 0.01f; }
        minX = mnX; maxX = mxX; minZ = mnZ; maxZ = mxZ;
    }

    private static Vector3 GetLookAtPointOnMap(Vector3 camPos, Vector3 camForward)
    {
        if (Mathf.Abs(camForward.y) < 0.0001f)
            return new Vector3(camPos.x, 0f, camPos.z);
        float t = -camPos.y / camForward.y;
        if (t <= 0f) return new Vector3(camPos.x, 0f, camPos.z);
        Vector3 hit = camPos + t * camForward;
        return new Vector3(hit.x, 0f, hit.z);
    }
}
