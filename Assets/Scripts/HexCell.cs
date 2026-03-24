using UnityEngine;

/// <summary>
/// Одна ячейка гекс-сетки. Подсветка при наведении (MaterialPropertyBlock).
/// </summary>
public class HexCell : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly Color HoverColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
    private static readonly Color ObstacleColor = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color MovementFlagTint = new Color(0.95f, 0.75f, 0.1f, 1f);

    [SerializeField] private int _col;
    [SerializeField] private int _row;
    [SerializeField] private string _colLabel;
    [SerializeField] private string _rowLabel;

    private Color _defaultColor = new Color(0.4f, 0.6f, 0.9f, 0.15f);
    private MaterialPropertyBlock _block;
    private MeshRenderer _renderer;

    private bool _hovered;
    private bool _apMaskActive;
    private bool _movementFlag;
    private bool _isObstacle;
    private Color _apMaskColor;
    private TextMesh _costLabel;
    private int _lastCostLabelValue = int.MinValue;
    private GameObject _obstacleModelInstance;


    private static GameObject _cachedPrefabWall;
    private static GameObject _cachedPrefabDamagedWall;
    private static GameObject _cachedPrefabTree;
    private static GameObject _cachedPrefabRock;

    /// <summary>Гекс под курсором (OnMouseEnter) — для обновления цвета при смене орто/3-е лицо.</summary>
    private static HexCell _hoveredCellInstance;

    public int Col => _col;
    public int Row => _row;
    public HexCube Cube => HexCubeOffset.FromOffset(_col, _row);
    /// <summary>Метка колонки (A, B, C, ...).</summary>
    public string ColLabel => _colLabel;
    /// <summary>Метка строки (01–40).</summary>
    public string RowLabel => _rowLabel;

    private void Awake()
    {
        _block = new MaterialPropertyBlock();
        _renderer = GetComponent<MeshRenderer>();
    }

    public void SetCoordinates(int col, int row)
    {
        _col = col;
        _row = row;
        // Если метки ещё не заданы в инспекторе, проставим их по умолчанию:
        // колонка: A, B, C...; строка: 01–40.
        if (string.IsNullOrEmpty(_colLabel))
        {
            char colChar = (char)('A' + _col);
            _colLabel = colChar.ToString();
        }
        if (string.IsNullOrEmpty(_rowLabel))
        {
            _rowLabel = (_row + 1).ToString("D2");
        }
    }

    /// <summary>Установить текстовые метки колонки и строки (не отображаются, используются как теги).</summary>
    public void SetLabels(string colLabel, string rowLabel)
    {
        _colLabel = colLabel;
        _rowLabel = rowLabel;
    }

    public void SetDefaultColor(Color color)
    {
        _defaultColor = color;
        ApplyCurrentColor();
    }

    /// <summary>Включить/выключить подсветку при наведении.</summary>
    public void SetHighlight(bool hovered)
    {
        _hovered = hovered;
        if (hovered)
            _hoveredCellInstance = this;
        else if (_hoveredCellInstance == this)
            _hoveredCellInstance = null;
        ApplyCurrentColor();
    }

    /// <summary>Маска ОД: active=true – гекс достижим, цвет = maskColor (обычно с альфой 0.9).</summary>
    public void SetApMask(bool active, Color maskColor)
    {
        _apMaskActive = active;
        _apMaskColor = maskColor;
        ApplyCurrentColor();
    }

    /// <summary>Маркер цели «добежать на следующем ходе» (двойной клик за пределы ОД).</summary>
    public void SetMovementFlag(bool active)
    {
        _movementFlag = active;
        ApplyCurrentColor();
    }

    public void SetObstacle(bool active)
    {
        if (!active)
            ClearObstacleVisual();
        _isObstacle = active;
        ApplyCurrentColor();
    }

    public bool IsObstacle => _isObstacle;

    /// <summary>Модель препятствия с сервера (Resources/Obstacles/{wall|damaged_wall|tree|rock}_visual).</summary>
    public void SetObstacleVisual(string tag, float yawDegrees = 0f)
    {
        ClearObstacleVisual();
        if (string.IsNullOrWhiteSpace(tag))
            return;

        string t = tag.Trim().ToLowerInvariant();
        if (t != "wall" && t != "damaged_wall" && t != "tree" && t != "rock")
            return;

        GameObject prefab = GetCachedObstaclePrefab(t);
        if (prefab == null)
        {
            Debug.LogWarning($"[HexCell] Нет Resources/Obstacles/{t}_visual.");
            return;
        }

        GameObject go = Instantiate(prefab, transform);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;

        go.transform.localPosition = new Vector3(0f, 0.02f, 0f);

        if (t == "wall" || t == "damaged_wall")
        {
            Quaternion bakedRot = go.transform.localRotation;
            float gridYaw = transform.parent != null ? transform.parent.eulerAngles.y : 0f;
            float localYaw = yawDegrees - gridYaw;
            go.transform.localRotation = Quaternion.AngleAxis(localYaw, Vector3.up) * bakedRot;
        }

        _obstacleModelInstance = go;
    }

    private static GameObject GetCachedObstaclePrefab(string t)
    {
        switch (t)
        {
            case "wall":
                return _cachedPrefabWall ?? (_cachedPrefabWall = Resources.Load<GameObject>("Obstacles/wall_visual"));
            case "damaged_wall":
                return _cachedPrefabDamagedWall ?? (_cachedPrefabDamagedWall = Resources.Load<GameObject>("Obstacles/damaged_wall_visual"));
            case "tree":
                return _cachedPrefabTree ?? (_cachedPrefabTree = Resources.Load<GameObject>("Obstacles/tree_visual"));
            case "rock":
                return _cachedPrefabRock ?? (_cachedPrefabRock = Resources.Load<GameObject>("Obstacles/rock_visual"));
            default:
                return null;
        }
    }

    public void ClearObstacleVisual()
    {
        if (_obstacleModelInstance != null)
        {
            Destroy(_obstacleModelInstance);
            _obstacleModelInstance = null;
        }
    }

    private void ApplyCurrentColor()
    {
        float gridAlpha = _defaultColor.a;

        Color color;
        if (_isObstacle)
        {
            // Как у обычной клетки «вне движения»: та же альфа сетки (HexGrid), тёмно-серый оттенок препятствия.
            color = new Color(ObstacleColor.r, ObstacleColor.g, ObstacleColor.b, gridAlpha);
        }
        else
        {
            color = _defaultColor;
            if (_movementFlag)
                color = Color.Lerp(color, MovementFlagTint, 0.5f);
            if (_apMaskActive)
                color = _apMaskColor;
        }

        // В 3-м лице луч из фиксированной точки экрана «бьёт» в один гекс — ложный hover (серый).
        if (_hovered && !HexGridCamera.ThirdPersonFollowActive)
        {
            if (_isObstacle)
                color = new Color(HoverColor.r, HoverColor.g, HoverColor.b, gridAlpha);
            else
                color = HoverColor;
        }

        ApplyColor(color);
    }

    /// <summary>Вызывается из <see cref="HexGridCamera"/> после смены ThirdPersonFollowActive.</summary>
    public static void RefreshHoverAfterThirdPersonCamera()
    {
        if (_hoveredCellInstance != null)
            _hoveredCellInstance.ApplyCurrentColor();
    }

    private void ApplyColor(Color color)
    {
        if (_block == null) _block = new MaterialPropertyBlock();
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null) return;

        _block.SetColor(BaseColorId, color);
        _block.SetColor(ColorId, color);
        _renderer.SetPropertyBlock(_block);
    }

    private void OnMouseEnter()
    {
        SetHighlight(true);
    }

    private void OnMouseExit()
    {
        SetHighlight(false);
        SetCostLabelVisible(false);
    }

    public void SetCostLabel(int cost)
    {
        if (cost < 0)
        {
            _lastCostLabelValue = int.MinValue;
            SetCostLabelVisible(false);
            return;
        }

        if (_costLabel != null && cost == _lastCostLabelValue && _costLabel.gameObject.activeSelf)
            return;

        if (_costLabel == null)
        {
            GameObject go = new GameObject("CostLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            // Фиксированная ориентация текста относительно гекса (всегда одна и та же)
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 60f);

            _costLabel = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) _costLabel.font = font;
            _costLabel.anchor = TextAnchor.MiddleCenter;
            _costLabel.alignment = TextAlignment.Center;
            _costLabel.fontSize = 14;
            _costLabel.color = Color.white;
            ApplyCostLabelSize();
        }
        else
        {
            ApplyCostLabelSize();
        }

        _costLabel.text = cost.ToString();
        _lastCostLabelValue = cost;
        SetCostLabelVisible(true);
    }

    /// <summary>Подстраивает размер подписи под размер гекса (по bounds меша).</summary>
    private void ApplyCostLabelSize()
    {
        if (_costLabel == null) return;
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null) return;

        Bounds bounds = _renderer.bounds;
        float hexSize = Mathf.Max(bounds.size.x, bounds.size.z, 0.1f);
        float characterSize = hexSize * 0.45f;
        _costLabel.characterSize = Mathf.Max(0.01f, characterSize);
    }

    public void SetCostLabelVisible(bool visible)
    {
        if (_costLabel != null)
            _costLabel.gameObject.SetActive(visible);
    }
}
