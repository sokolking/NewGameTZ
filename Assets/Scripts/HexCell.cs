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

    private Color _defaultColor = new Color(0.4f, 0.6f, 0.9f);
    private MaterialPropertyBlock _block;
    private MeshRenderer _renderer;

    private bool _hovered;
    private bool _apMaskActive;
    private bool _movementFlag;
    private bool _isObstacle;
    private Color _apMaskColor;
    private TextMesh _costLabel;
    private GameObject _obstacleModelInstance;

    /// <summary>Масштаб FBX стены (как раньше) — визуально ок.</summary>
    private const float WallObstacleScale = 0.9f;

    /// <summary>Капсула игрока по умолчанию: height 2 × localScale.y 0.5 → ~1.0 по вертикали.</summary>
    private const float PlayerCapsuleReferenceMaxExtent = 1f;

    /// <summary>Камень: характерный размер ≈ половина «игрока» (см. <see cref="PlayerCapsuleReferenceMaxExtent"/>).</summary>
    private const float RockFractionOfPlayerSize = 0.5f;

    /// <summary>Кэш max(size.x,size.y,size.z) для стены при <see cref="WallObstacleScale"/> (без родителя-гекса).</summary>
    private static float? _cachedWallPrefabMaxExtent;

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

    /// <summary>Модель препятствия с сервера (Resources/Obstacles/{wall|tree|rock}).</summary>
    public void SetObstacleVisual(string tag, float yawDegrees)
    {
        ClearObstacleVisual();
        if (string.IsNullOrWhiteSpace(tag))
            return;

        string t = tag.Trim().ToLowerInvariant();
        if (t != "wall" && t != "tree" && t != "rock")
            return;

        GameObject prefab = Resources.Load<GameObject>($"Obstacles/{t}_visual");
        if (prefab == null)
        {
            Debug.LogWarning($"[HexCell] Нет Resources/Obstacles/{t} (fbx в папке Obstacles).");
            return;
        }

        GameObject go = Instantiate(prefab, transform);
        go.transform.localPosition = new Vector3(0f, 0.02f, 0f);

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
        }
        switch (t)
        {
            case "wall":
                go.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
                go.transform.localScale = Vector3.one * WallObstacleScale;
                break;
        }

        _obstacleModelInstance = go;
    }

    private static float GetCachedWallPrefabMaxExtent()
    {
        if (_cachedWallPrefabMaxExtent.HasValue)
            return _cachedWallPrefabMaxExtent.Value;

        GameObject prefab = Resources.Load<GameObject>("Obstacles/wall");
        if (prefab == null)
        {
            _cachedWallPrefabMaxExtent = WallObstacleScale;
            return _cachedWallPrefabMaxExtent.Value;
        }

        GameObject go = Instantiate(prefab);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one * WallObstacleScale;
        float ext = MaxExtent(ComputeWorldBoundsSize(go));
        UnityEngine.Object.Destroy(go);
        _cachedWallPrefabMaxExtent = Mathf.Max(ext, 0.0001f);
        return _cachedWallPrefabMaxExtent.Value;
    }

    private static Vector3 ComputeWorldBoundsSize(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return Vector3.one * 0.01f;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return b.size;
    }

    private static float MaxExtent(Vector3 size) =>
        Mathf.Max(size.x, size.y, size.z, 0.0001f);

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
        Color color = _isObstacle ? ObstacleColor : _defaultColor;
        if (_movementFlag && !_isObstacle)
            color = Color.Lerp(color, MovementFlagTint, 0.5f);
        if (_apMaskActive) color = _apMaskColor;
        // В 3-м лице луч из фиксированной точки экрана «бьёт» в один гекс — ложный hover (серый).
        if (_hovered && !HexGridCamera.ThirdPersonFollowActive)
            color = HoverColor;
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
            SetCostLabelVisible(false);
            return;
        }

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
