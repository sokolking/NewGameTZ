using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Карта из гексов: каждый гекс — отдельный дочерний объект. Flat-top, odd-r (Red Blob).
/// https://www.redblobgames.com/grids/hexagons/
/// </summary>
public class HexGrid : MonoBehaviour
{
    private const float MinHexSize = 0.01f;
    private const float MinVisualRadius = 0.001f;
    private const float EdgeInsetToRadiusFactor = 2f / 1.732050808f; // 2/sqrt(3)

    // ─── Кэши для O(1) доступа вместо transform.Find O(n) ───
    private HexCell[,] _cellCache;
    private Vector3[,] _worldPosCache;
    private bool _worldPosCacheDirty = true;
    private bool _boundsCacheDirty = true;
    private float _cachedMinX, _cachedMaxX, _cachedMinZ, _cachedMaxZ;

    [Header("Field size (25×40 = 1000 hexes)")]
    [SerializeField] private int _width = 25;
    [SerializeField] private int _length = 40;
    [Tooltip("If off, generate the grid with the inspector button before play.")]
    [SerializeField] private bool _generateOnPlay = false;

    [Header("Hex")]
    [SerializeField] private float _hexSize = 1f;
    [Tooltip("Inset from each edge (world units). ~0.05 ≈ 5 px at 100 ppu.")]
    [SerializeField] private float _edgeInset = 0.05f;

    [Header("Appearance")]
    [SerializeField] private Material _hexMaterial;
    [Tooltip("Fill color (RGB). Opacity is controlled by the Transparency slider.")]
    [SerializeField] private Color _hexColor = new Color(0.4f, 0.6f, 0.9f, 1f);
    [SerializeField, Range(0f, 100f)]
    [Tooltip("Hex fill transparency %. 100 = most transparent (α→0), 0 = opaque. ~85% ≈ α 0.15 for fill.")]
    private float _hexTransparencyPercent = 85f;

    public int Width => _width;
    public int Length => _length;

    /// <summary>Размер шага гекса (как в <see cref="CubeToWorldFlatTop"/>).</summary>
    public float HexSize => Mathf.Max(MinHexSize, _hexSize);

    /// <summary>Радиус визуального шестиугольника (как у меша <see cref="Hexagon"/>), для контуров дальности.</summary>
    public float HexVisualRadius =>
        Mathf.Max(MinVisualRadius, HexSize - Mathf.Max(0f, _edgeInset) * EdgeInsetToRadiusFactor);

    private void Awake()
    {
        ApplyHexTransparencyToColor();
        PropagateHexFillColorToCells();
    }

    private void OnValidate()
    {
        ApplyHexTransparencyToColor();
        PropagateHexFillColorToCells();
    }

    /// <summary>Связывает ползунок «Прозрачность %» с альфой <see cref="_hexColor"/> (в т.ч. в билде, где OnValidate не вызывается).</summary>
    private void ApplyHexTransparencyToColor()
    {
        _hexTransparencyPercent = Mathf.Clamp(_hexTransparencyPercent, 0f, 100f);
        float opacity = Mathf.Clamp01(1f - _hexTransparencyPercent * 0.01f);
        _hexColor = new Color(_hexColor.r, _hexColor.g, _hexColor.b, opacity);
    }

    /// <summary>Обновляет базовый цвет у уже созданных гексов (иначе меняется только serialized <see cref="_hexColor"/>, а не PropertyBlock на ячейках).</summary>
    private void PropagateHexFillColorToCells()
    {
        HexCell[] cells = GetComponentsInChildren<HexCell>(true);
        if (cells == null || cells.Length == 0) return;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null)
                cells[i].SetDefaultColor(_hexColor);
        }
    }

    private void Start()
    {
        if (_generateOnPlay)
            GenerateGrid();
    }

    /// <summary>Создаёт ровно Width×Length дочерних объектов (гексов). Вызывать кнопкой в инспекторе или при _generateOnPlay.</summary>
    [ContextMenu("Generate grid")]
    public void GenerateGrid()
    {
        ClearGrid();
        ApplyHexTransparencyToColor();

        // Отдельный экземпляр материала: не затираем shared-ассет и не смешиваем альфу с PropertyBlock.
        Material mat = _hexMaterial != null ? new Material(_hexMaterial) : CreateDefaultMaterial();
        if (mat != null)
        {
            ConfigureHexMaterialTransparency(mat);
            ApplyNeutralHexMaterialBase(mat);
            mat.enableInstancing = true;
        }

        float size = Mathf.Max(MinHexSize, _hexSize);
        float inset = Mathf.Max(0f, _edgeInset);
        float visualRadius = Mathf.Max(MinVisualRadius, size - inset * EdgeInsetToRadiusFactor);

        _cellCache = new HexCell[_width, _length];
        _worldPosCache = new Vector3[_width, _length];

        for (int col = 0; col < _width; col++)
        {
            for (int row = 0; row < _length; row++)
            {
                CreateHexCell(col, row, size, visualRadius, mat);
            }
        }

        _worldPosCacheDirty = false;
        _boundsCacheDirty = true;
    }

    private void CreateHexCell(int col, int row, float size, float visualRadius, Material mat)
    {
        GameObject go = new GameObject($"Hex_{col}_{row}");
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.Euler(0f, 60f, 0f);
        go.transform.localScale = Vector3.one;
        // PropertyBlock + прозрачность: гексы не в static batch (иначе цвета/альфа ломаются).
        go.isStatic = false;

        HexCube cube = HexCubeOffset.FromOffset(col, row);
        HexCubeOffset.CubeToWorldFlatTop(cube, size, out float wx, out float wz);
        go.transform.localPosition = new Vector3(wx, 0f, wz);

        Hexagon hex = go.AddComponent<Hexagon>();
        hex.BuildMesh(visualRadius);

        HexCell cell = go.AddComponent<HexCell>();
        cell.SetCoordinates(col, row);
        // Пронумеровать гекс: колонки A–Z..., строки 01–40.
        char colChar = (char)('A' + col);
        string colLabel = colChar.ToString();
        string rowLabel = (row + 1).ToString("D2");
        cell.SetLabels(colLabel, rowLabel);
        cell.SetDefaultColor(_hexColor);

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = mat;
            // Тени гексов стоят ~8 ms; пол не отбрасывает значимых теней.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        MeshCollider collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;

        // Заполняем кэши.
        if (_cellCache != null)
            _cellCache[col, row] = cell;
        if (_worldPosCache != null)
            _worldPosCache[col, row] = transform.TransformPoint(new Vector3(wx, 0f, wz));
    }

    private Material CreateDefaultMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Legacy Shaders/Diffuse")
            ?? Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader != null ? shader : Shader.Find("VertexLit"));
        if (mat.shader == null)
            return mat;
        ConfigureHexMaterialTransparency(mat);
        ApplyNeutralHexMaterialBase(mat);
        return mat;
    }

    /// <summary>
    /// URP Lit в режиме Opaque не смешивает альфу с фоном — заливка с α &lt; 1 выглядит как непрозрачная или «ломается».
    /// Прозрачный surface + alpha blend нужны, пока сетка рисуется с полупрозрачным <see cref="_hexColor"/>.
    /// Если материал в ассете уже Transparent, вызов лишь закрепляет те же настройки.
    /// </summary>
    private static void ConfigureHexMaterialTransparency(Material mat)
    {
        if (mat == null) return;

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        }

        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend"))
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);

        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
    }

    /// <summary>Нейтральная база на материале; реальный цвет задаёт <see cref="HexCell"/> через PropertyBlock.</summary>
    private static void ApplyNeutralHexMaterialBase(Material mat)
    {
        if (mat == null) return;
        Color white = Color.white;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", white);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", white);
    }

    [ContextMenu("Clear grid")]
    public void ClearGrid()
    {
        _cellCache = null;
        _worldPosCache = null;
        _worldPosCacheDirty = true;
        _boundsCacheDirty = true;
        while (transform.childCount > 0)
        {
            Transform child = transform.GetChild(0);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    /// <summary>O(1) доступ к ячейке вместо transform.Find (O(n) при 1000 объектах).</summary>
    public HexCell GetCell(int col, int row)
    {
        if (!IsInBounds(col, row)) return null;
        // Быстрый путь: кэш заполнен при GenerateGrid.
        if (_cellCache != null)
        {
            HexCell c = _cellCache[col, row];
            if (c != null && c.IsExcludedFromPlay)
                return null;
            return c;
        }
        // Fallback для сеток, созданных в редакторе (без GenerateGrid в рантайме).
        BuildCellCacheIfNeeded();
        if (_cellCache != null)
            return _cellCache[col, row];
        // Последний fallback.
        Transform t = transform.Find($"Hex_{col}_{row}");
        HexCell found = t != null ? t.GetComponent<HexCell>() : null;
        if (found != null && found.IsExcludedFromPlay)
            return null;
        return found;
    }

    /// <summary>Reset hexes fallen off after zone shrink — call when a new battle starts on this grid.</summary>
    public void ClearAllZoneShrinkExclusions()
    {
        HexCell[] cells = GetComponentsInChildren<HexCell>(true);
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null)
                cells[i].ClearZoneShrinkExclusion(this);
        }
    }

    /// <summary>Clears blue/red PvP occupancy tint from all hexes (before recomputing).</summary>
    public void ClearAllPvpOccupancyHighlights()
    {
        HexCell[] cells = GetComponentsInChildren<HexCell>(true);
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null)
                cells[i].ClearPvpOccupancyHighlight();
        }
    }

    private void BuildCellCacheIfNeeded()
    {
        if (_cellCache != null || _width <= 0 || _length <= 0) return;
        _cellCache = new HexCell[_width, _length];
        _worldPosCache = new Vector3[_width, _length];
        float size = Mathf.Max(MinHexSize, _hexSize);
        for (int col = 0; col < _width; col++)
        {
            for (int row = 0; row < _length; row++)
            {
                Transform t = transform.Find($"Hex_{col}_{row}");
                if (t != null)
                    _cellCache[col, row] = t.GetComponent<HexCell>();
                HexCube cube = HexCubeOffset.FromOffset(col, row);
                HexCubeOffset.CubeToWorldFlatTop(cube, size, out float x, out float z);
                _worldPosCache[col, row] = transform.TransformPoint(new Vector3(x, 0f, z));
            }
        }
        _worldPosCacheDirty = false;
    }

    /// <summary>Мировая позиция центра ячейки (col, row). Использует кэш — без пересчёта каждый вызов.</summary>
    public Vector3 GetCellWorldPosition(int col, int row)
    {
        if (_worldPosCache != null && !_worldPosCacheDirty
            && col >= 0 && col < _width && row >= 0 && row < _length)
            return _worldPosCache[col, row];
        HexCube cube = HexCubeOffset.FromOffset(col, row);
        float size = Mathf.Max(MinHexSize, _hexSize);
        HexCubeOffset.CubeToWorldFlatTop(cube, size, out float x, out float z);
        return transform.TransformPoint(new Vector3(x, 0f, z));
    }

    /// <summary>Центр сетки в мире (для камеры).</summary>
    public Vector3 GetGridCenterWorld()
    {
        int c = Mathf.Clamp(_width / 2, 0, _width - 1);
        int r = Mathf.Clamp(_length / 2, 0, _length - 1);
        return GetCellWorldPosition(c, r);
    }

    /// <summary>Приблизительный размер сетки (для камеры).</summary>
    public float GetGridSize()
    {
        float size = Mathf.Max(MinHexSize, _hexSize);
        float w = size * 1.732050808f * (_width + 0.5f);
        float h = size * 1.5f * (_length + 0.5f);
        return Mathf.Max(w, h);
    }

    /// <summary>Границы карты (кэшируются после первого вычисления).</summary>
    public void GetGridBoundsWorld(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        if (_width == 0 || _length == 0)
        {
            minX = maxX = minZ = maxZ = 0f;
            return;
        }
        if (!_boundsCacheDirty)
        {
            minX = _cachedMinX;
            maxX = _cachedMaxX;
            minZ = _cachedMinZ;
            maxZ = _cachedMaxZ;
            return;
        }
        float size = Mathf.Max(MinHexSize, _hexSize);
        float inset = Mathf.Max(0f, _edgeInset);
        float visualRadius = Mathf.Max(MinVisualRadius, size - inset * EdgeInsetToRadiusFactor);
        float marginX = visualRadius * 0.8660254f;
        float marginZ = visualRadius;

        float mnX = float.MaxValue, mxX = float.MinValue, mnZ = float.MaxValue, mxZ = float.MinValue;
        for (int col = 0; col < _width; col++)
        {
            for (int row = 0; row < _length; row++)
            {
                Vector3 p = GetCellWorldPosition(col, row);
                if (p.x < mnX) mnX = p.x;
                if (p.x > mxX) mxX = p.x;
                if (p.z < mnZ) mnZ = p.z;
                if (p.z > mxZ) mxZ = p.z;
            }
        }
        _cachedMinX = minX = mnX - marginX;
        _cachedMaxX = maxX = mxX + marginX;
        _cachedMinZ = minZ = mnZ - marginZ;
        _cachedMaxZ = maxZ = mxZ + marginZ;
        _boundsCacheDirty = false;
    }

    /// <summary>Возвращает кэш ячеек (Width × Length). Не модифицировать!</summary>
    public HexCell[,] GetCellCache()
    {
        BuildCellCacheIfNeeded();
        return _cellCache;
    }

    #region Координаты и расстояние (cube: q+r+s=0, H = Max(|Δq|,|Δr|,|Δs|))

    public static HexCube GetCube(int col, int row) =>
        HexCubeOffset.FromOffset(col, row);

    public static int GetDistance(int col1, int row1, int col2, int row2) =>
        HexCubeOffset.DistanceBetweenOffsets(col1, row1, col2, row2);

    public static void GetNeighbor(int col, int row, int direction, out int outCol, out int outRow)
    {
        HexCube neighbor = GetCube(col, row).Neighbor(direction);
        HexCubeOffset.ToOffset(neighbor, out outCol, out outRow);
    }

    public bool IsInBounds(int col, int row) =>
        HexCubeOffset.IsInBounds(col, row, _width, _length);

    #endregion
}
