using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Контур радиуса атаки в гексах (внешние рёбра «диска»).
/// Один Mesh (Lines) вместо сотен LineRenderer — меньше draw calls в RenderLoop.Draw.
/// </summary>
public sealed class AttackRangeHexOutline : MonoBehaviour
{
    private const int MaxEdgeSegments = 400;

    [SerializeField] private HexGrid _grid;
    [SerializeField] private float _yOffset = 0.01f;
    [SerializeField] private Color _lineColor = new Color(0.94f, 0.42f, 0.30f, 0.95f);
    [SerializeField] private int _defaultWeaponRangeHexes = 1;

    private readonly HashSet<(int c, int r)> _insideScratch = new();
    private readonly List<(int col, int row)> _insideListScratch = new(1024);
    private readonly List<(Vector3 a, Vector3 b)> _segmentsScratch = new(256);

    private readonly Vector3[] _hexCornersWorld = new Vector3[6];

    private Mesh _lineMesh;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private readonly List<Vector3> _meshVertices = new List<Vector3>(MaxEdgeSegments * 2);
    private int[] _lineIndices;

    private bool _visible;
    private int _lastCenterCol = int.MinValue;
    private int _lastCenterRow = int.MinValue;
    private int _lastRangeHexes = int.MinValue;

    private void Awake()
    {
        if (_grid == null)
            _grid = FindFirstObjectByType<HexGrid>();

        var go = new GameObject("AttackRangeLineMesh");
        go.transform.SetParent(transform, false);
        _meshFilter = go.AddComponent<MeshFilter>();
        _meshRenderer = go.AddComponent<MeshRenderer>();
        _lineMesh = new Mesh { name = "AttackRangeLines" };
        _meshFilter.sharedMesh = _lineMesh;

        Shader sh = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        Material lineMat = new Material(sh);
        lineMat.color = _lineColor;
        _meshRenderer.sharedMaterial = lineMat;
        _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _meshRenderer.enabled = false;

        int maxIdx = MaxEdgeSegments * 2;
        _lineIndices = new int[maxIdx];
        for (int i = 0; i < maxIdx; i++)
            _lineIndices[i] = i;
    }

    /// <summary>Радиус в шагах гекса (max hex distance от центра включительно).</summary>
    public void ShowFromCell(int centerCol, int centerRow, int weaponRangeHexes)
    {
        if (_grid == null)
            return;

        int r = Mathf.Max(0, weaponRangeHexes);
        if (r <= 0)
        {
            Hide();
            return;
        }

        if (_visible
            && centerCol == _lastCenterCol
            && centerRow == _lastCenterRow
            && r == _lastRangeHexes)
        {
            return;
        }

        _lastCenterCol = centerCol;
        _lastCenterRow = centerRow;
        _lastRangeHexes = r;

        _insideScratch.Clear();
        _insideListScratch.Clear();
        for (int col = 0; col < _grid.Width; col++)
        {
            for (int row = 0; row < _grid.Length; row++)
            {
                if (HexGrid.GetDistance(centerCol, centerRow, col, row) <= r)
                {
                    var key = (col, row);
                    _insideScratch.Add(key);
                    _insideListScratch.Add(key);
                }
            }
        }

        float visR = _grid.HexVisualRadius;
        _segmentsScratch.Clear();

        int insideCount = _insideListScratch.Count;
        for (int idx = 0; idx < insideCount; idx++)
        {
            (int col, int row) = _insideListScratch[idx];
            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(col, row, dir, out int nc, out int nr);
                bool neighborIn = _grid.IsInBounds(nc, nr) && _insideScratch.Contains((nc, nr));
                if (neighborIn)
                    continue;

                if (!GetSharedEdgeWorld(col, row, nc, nr, visR, out Vector3 a, out Vector3 b))
                    continue;
                _segmentsScratch.Add((a, b));
            }
        }

        int segCount = Mathf.Min(_segmentsScratch.Count, MaxEdgeSegments);
        if (segCount <= 0)
        {
            _lineMesh.Clear();
            _meshRenderer.enabled = false;
            _visible = false;
            return;
        }

        _meshVertices.Clear();
        for (int i = 0; i < segCount; i++)
        {
            var s = _segmentsScratch[i];
            _meshVertices.Add(s.a);
            _meshVertices.Add(s.b);
        }

        int vc = _meshVertices.Count;
        _lineMesh.Clear();
        _lineMesh.SetVertices(_meshVertices);
        _lineMesh.SetIndices(_lineIndices, 0, vc, MeshTopology.Lines, 0, false, 0);
        _lineMesh.RecalculateBounds();
        _meshRenderer.enabled = true;
        _visible = true;
    }

    /// <summary>Радиус в гексах берётся из текущего оружия игрока (сервер).</summary>
    public void ShowFromPlayer(Player player)
    {
        if (player == null)
            return;
        int r = player.WeaponRangeHexes;
        if (r <= 0)
            r = _defaultWeaponRangeHexes;
        ShowFromCell(player.CurrentCol, player.CurrentRow, r);
    }

    public void ShowFromPlayerDefaultRange(Player player)
    {
        ShowFromPlayer(player);
    }

    public void Hide()
    {
        if (_lineMesh != null)
            _lineMesh.Clear();
        if (_meshRenderer != null)
            _meshRenderer.enabled = false;
        _visible = false;
        _lastCenterCol = int.MinValue;
        _lastCenterRow = int.MinValue;
        _lastRangeHexes = int.MinValue;
    }

    public bool IsVisible => _visible;

    public void SetDefaultWeaponRangeHexes(int hexes) => _defaultWeaponRangeHexes = Mathf.Max(0, hexes);

    private bool GetSharedEdgeWorld(int col, int row, int nc, int nr, float radius, out Vector3 a, out Vector3 b)
    {
        a = b = default;
        Vector3 c = _grid.GetCellWorldPosition(col, row);

        FillHexCornerWorldPositions(col, row, radius, _hexCornersWorld);
        Vector3 midTarget = (_grid.GetCellWorldPosition(col, row) + _grid.GetCellWorldPosition(nc, nr)) * 0.5f;
        midTarget.y = c.y + _yOffset;

        int best = 0;
        float bestD = float.MaxValue;
        for (int e = 0; e < 6; e++)
        {
            Vector3 mid = (_hexCornersWorld[e] + _hexCornersWorld[(e + 1) % 6]) * 0.5f;
            mid.y = midTarget.y;
            float d = (mid - midTarget).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }

        a = _hexCornersWorld[best];
        b = _hexCornersWorld[(best + 1) % 6];
        a.y = c.y + _yOffset;
        b.y = c.y + _yOffset;
        return true;
    }

    private void FillHexCornerWorldPositions(int col, int row, float radius, Vector3[] worldOut)
    {
        float halfW = radius * 0.8660254f;
        Vector3 center = _grid.GetCellWorldPosition(col, row);
        Transform t = _grid.transform;
        worldOut[0] = center + t.TransformVector(new Vector3(0f, 0f, radius));
        worldOut[1] = center + t.TransformVector(new Vector3(halfW, 0f, radius * 0.5f));
        worldOut[2] = center + t.TransformVector(new Vector3(halfW, 0f, -radius * 0.5f));
        worldOut[3] = center + t.TransformVector(new Vector3(0f, 0f, -radius));
        worldOut[4] = center + t.TransformVector(new Vector3(-halfW, 0f, -radius * 0.5f));
        worldOut[5] = center + t.TransformVector(new Vector3(-halfW, 0f, radius * 0.5f));
    }
}
