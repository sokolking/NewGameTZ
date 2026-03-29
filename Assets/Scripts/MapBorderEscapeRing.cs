using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Orange markers on the escape frame: active battle bounds ±1 in col/row (may include virtual coords outside the grid).
/// </summary>
[DisallowMultipleComponent]
public class MapBorderEscapeRing : MonoBehaviour
{
    [Tooltip("Defaults to HexGrid on this object or in the scene.")]
    [SerializeField] private HexGrid _grid;

    [Tooltip("Optional: assign a visible URP Unlit material (orange). If empty, a runtime material is created.")]
    [SerializeField] private Material _markerMaterial;

    [Tooltip("Log once (per component) when there are zero markers in Play Mode.")]
    [SerializeField] private bool _logWhenRingIsEmpty = true;

    private readonly Dictionary<(int c, int r), BorderHexMarker> _markers = new();
    private static Material _sSharedRuntimeMaterial;
    private bool _loggedEmptyOnce;

    private void Awake()
    {
        if (_grid == null)
            _grid = GetComponent<HexGrid>() ?? FindFirstObjectByType<HexGrid>();
    }

    private void OnEnable()
    {
        GameSession.ActiveBattleZoneChanged += RefreshMarkers;
        RefreshMarkers();
    }

    private void OnDisable()
    {
        GameSession.ActiveBattleZoneChanged -= RefreshMarkers;
    }

    private void Start()
    {
        RefreshMarkers();
    }

    public void RefreshMarkers()
    {
        if (_grid == null)
            _grid = GetComponent<HexGrid>() ?? FindFirstObjectByType<HexGrid>();
        GameSession gs = GameSession.Active;
        if (_grid == null || gs == null)
            return;

        if (!gs.TryGetActiveBattleBounds(out int minC, out int maxC, out int minR, out int maxR))
            return;

        var keep = new HashSet<(int, int)>();
        Material mat = ResolveMaterial();
        for (int c = minC - 1; c <= maxC + 1; c++)
        {
            for (int r = minR - 1; r <= maxR + 1; r++)
            {
                if (!gs.IsEscapeBorderHex(c, r))
                    continue;
                keep.Add((c, r));
                if (_markers.TryGetValue((c, r), out var existing) && existing != null)
                    continue;

                var go = new GameObject($"BorderHex_{c}_{r}", typeof(BorderHexMarker));
                var marker = go.GetComponent<BorderHexMarker>();
                marker.Initialize(_grid, c, r, c, r, mat);
                _markers[(c, r)] = marker;
            }
        }

        if (keep.Count == 0 && _logWhenRingIsEmpty && Application.isPlaying && !_loggedEmptyOnce)
        {
            _loggedEmptyOnce = true;
            Debug.Log(
                "[MapBorderEscapeRing] No escape-border hexes to draw (check GameSession battle bounds).",
                this);
        }

        if (keep.Count > 0)
            _loggedEmptyOnce = false;

        var remove = new List<(int c, int r)>();
        foreach (var kv in _markers)
        {
            if (!keep.Contains(kv.Key) && kv.Value != null)
                remove.Add(kv.Key);
        }

        foreach (var key in remove)
        {
            if (_markers.TryGetValue(key, out var m) && m != null)
                Destroy(m.gameObject);
            _markers.Remove(key);
        }
    }

    private Material ResolveMaterial()
    {
        if (_markerMaterial != null)
            return _markerMaterial;
        if (_sSharedRuntimeMaterial != null)
            return _sSharedRuntimeMaterial;

        Color color = new Color(1f, 0.48f, 0.06f, 0.94f);
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
            sh = Shader.Find("Unlit/Universal Render Pipeline/Unlit");
        if (sh == null)
            sh = Shader.Find("Unlit/Color");
        if (sh == null)
            sh = Shader.Find("Sprites/Default");

        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        else
            mat.color = color;

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", (float)CullMode.Off);

        mat.renderQueue = (int)RenderQueue.Transparent;
        _sSharedRuntimeMaterial = mat;
        return mat;
    }
}
