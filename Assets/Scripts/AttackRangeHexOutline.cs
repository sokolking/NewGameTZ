using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Контур радиуса атаки в гексах (только внешние рёбра «диска»), без заливки гексов.
/// Показывается при удержании ЛКМ по цели (см. <see cref="HexInputManager"/>).
/// </summary>
public sealed class AttackRangeHexOutline : MonoBehaviour
{
    private const int MaxEdgeSegments = 400;

    [SerializeField] private HexGrid _grid;
    [SerializeField] private float _yOffset = 0.01f;
    [SerializeField] private float _lineWidth = 0.25f;
    [SerializeField] private Color _lineColor = new Color(0.94f, 0.42f, 0.30f, 0.95f);
    [SerializeField] private int _defaultWeaponRangeHexes = 1;

    private readonly List<LineRenderer> _pool = new();
    private Transform _poolRoot;
    private bool _visible;

    private void Awake()
    {
        if (_grid == null)
            _grid = FindFirstObjectByType<HexGrid>();

        _poolRoot = new GameObject("AttackRangeLinePool").transform;
        _poolRoot.SetParent(transform, false);

        Material lineMat = new Material(Shader.Find("Sprites/Default"));
        for (int i = 0; i < MaxEdgeSegments; i++)
        {
            GameObject go = new GameObject($"Seg_{i}");
            go.transform.SetParent(_poolRoot, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.loop = false;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = _lineWidth;
            lr.endWidth = _lineWidth;
            lr.startColor = _lineColor;
            lr.endColor = _lineColor;
            lr.material = lineMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            _pool.Add(lr);
        }
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

        var inside = new HashSet<(int c, int r)>();
        for (int col = 0; col < _grid.Width; col++)
        {
            for (int row = 0; row < _grid.Length; row++)
            {
                if (HexGrid.GetDistance(centerCol, centerRow, col, row) <= r)
                    inside.Add((col, row));
            }
        }

        float visR = _grid.HexVisualRadius;
        var segments = new List<(Vector3 a, Vector3 b)>(256);

        foreach ((int col, int row) in inside)
        {
            for (int dir = 0; dir < 6; dir++)
            {
                HexGrid.GetNeighbor(col, row, dir, out int nc, out int nr);
                bool neighborIn = _grid.IsInBounds(nc, nr) && inside.Contains((nc, nr));
                if (neighborIn)
                    continue;

                if (!GetSharedEdgeWorld(col, row, nc, nr, visR, out Vector3 a, out Vector3 b))
                    continue;
                segments.Add((a, b));
            }
        }

        int n = Mathf.Min(segments.Count, _pool.Count);
        for (int i = 0; i < n; i++)
        {
            LineRenderer lr = _pool[i];
            lr.SetPosition(0, segments[i].a);
            lr.SetPosition(1, segments[i].b);
            lr.enabled = true;
        }
        for (int i = n; i < _pool.Count; i++)
            _pool[i].enabled = false;

        _visible = n > 0;
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
        for (int i = 0; i < _pool.Count; i++)
            _pool[i].enabled = false;
        _visible = false;
    }

    public bool IsVisible => _visible;

    public void SetDefaultWeaponRangeHexes(int hexes) => _defaultWeaponRangeHexes = Mathf.Max(0, hexes);

    /// <summary>Ребро между текущим гексом и соседом (nc,nr): общая граница — ближайшая к середине между центрами.</summary>
    private bool GetSharedEdgeWorld(int col, int row, int nc, int nr, float radius, out Vector3 a, out Vector3 b)
    {
        a = b = default;
        Vector3 c = _grid.GetCellWorldPosition(col, row);
        Vector3 n = _grid.GetCellWorldPosition(nc, nr);

        Vector3[] corners = GetHexCornerWorldPositions(col, row, radius);
        Vector3 midTarget = (c + n) * 0.5f;
        midTarget.y = c.y + _yOffset;

        int best = 0;
        float bestD = float.MaxValue;
        for (int e = 0; e < 6; e++)
        {
            Vector3 mid = (corners[e] + corners[(e + 1) % 6]) * 0.5f;
            mid.y = midTarget.y;
            float d = (mid - midTarget).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }

        a = corners[best];
        b = corners[(best + 1) % 6];
        a.y = c.y + _yOffset;
        b.y = c.y + _yOffset;
        return true;
    }

    private Vector3[] GetHexCornerWorldPositions(int col, int row, float radius)
    {
        float halfW = radius * 0.8660254f;
        Vector3[] local = new Vector3[6];
        local[0] = new Vector3(0f, 0f, radius);
        local[1] = new Vector3(halfW, 0f, radius * 0.5f);
        local[2] = new Vector3(halfW, 0f, -radius * 0.5f);
        local[3] = new Vector3(0f, 0f, -radius);
        local[4] = new Vector3(-halfW, 0f, -radius * 0.5f);
        local[5] = new Vector3(-halfW, 0f, radius * 0.5f);

        Vector3 center = _grid.GetCellWorldPosition(col, row);
        Transform t = _grid.transform;
        var world = new Vector3[6];
        for (int i = 0; i < 6; i++)
            world[i] = center + t.TransformVector(local[i]);
        return world;
    }
}
