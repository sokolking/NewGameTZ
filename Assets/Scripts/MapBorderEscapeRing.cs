using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Markers on the escape frame: active battle bounds ±1 in col/row (may include virtual coords outside the grid).
/// </summary>
[DisallowMultipleComponent]
public class MapBorderEscapeRing : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("Used when Marker Material is empty: tint for the generated Unlit material.")]
    [SerializeField] private Color _escapeBorderColor = new Color(1f, 0.48f, 0.06f, 0.94f);

    [Tooltip("Optional: assign your own material. For alpha from Escape Border Color, use a Transparent URP Unlit (or leave empty for auto setup).")]
    [SerializeField] private Material _markerMaterial;

    [Header("References")]
    [Tooltip("Defaults to HexGrid on this object or in the scene.")]
    [SerializeField] private HexGrid _grid;

    [Tooltip("Log once (per component) when there are zero markers in Play Mode.")]
    [SerializeField] private bool _logWhenRingIsEmpty = true;

    private readonly Dictionary<(int c, int r), BorderHexMarker> _markers = new();
    private Material _runtimeMaterialInstance;
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

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_markerMaterial != null)
            return;
        if (_runtimeMaterialInstance != null)
            ApplyColorToRuntimeMaterial(_escapeBorderColor);
    }
#endif

    /// <summary>Runtime or editor: tint when using the auto-generated material.</summary>
    public Color EscapeBorderColor
    {
        get => _escapeBorderColor;
        set
        {
            _escapeBorderColor = value;
            if (_markerMaterial == null && _runtimeMaterialInstance != null)
                ApplyColorToRuntimeMaterial(_escapeBorderColor);
            RefreshMarkers();
        }
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
                {
                    var rend = existing.GetComponent<MeshRenderer>();
                    if (rend != null)
                        rend.sharedMaterial = mat;
                    continue;
                }

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
        if (_runtimeMaterialInstance != null)
        {
            ApplyColorToRuntimeMaterial(_escapeBorderColor);
            return _runtimeMaterialInstance;
        }

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
            sh = Shader.Find("Unlit/Universal Render Pipeline/Unlit");
        if (sh == null)
            sh = Shader.Find("Unlit/Color");
        if (sh == null)
            sh = Shader.Find("Sprites/Default");

        var mat = new Material(sh);
        ApplyColorToRuntimeMaterial(_escapeBorderColor, mat);
        ConfigureMaterialTransparencyForEscape(mat, sh.name);

        _runtimeMaterialInstance = mat;
        return mat;
    }

    /// <summary>
    /// URP Unlit only applies alpha when the material is in the transparent surface mode and the matching shader keywords are set;
    /// setting _Surface floats alone is not enough at runtime.
    /// </summary>
    private static void ConfigureMaterialTransparencyForEscape(Material mat, string shaderName)
    {
        if (mat == null || string.IsNullOrEmpty(shaderName))
            return;

        bool urpUnlit = shaderName.IndexOf("Universal Render Pipeline", StringComparison.OrdinalIgnoreCase) >= 0
            || shaderName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;

        if (urpUnlit)
        {
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");

            if (mat.HasProperty("_SrcBlend"))
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha"))
                mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha"))
                mat.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", (float)CullMode.Off);

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return;
        }

        if (shaderName.IndexOf("Sprites", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            mat.renderQueue = (int)RenderQueue.Transparent;
            return;
        }

        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    private void ApplyColorToRuntimeMaterial(Color color)
    {
        if (_runtimeMaterialInstance != null)
            ApplyColorToRuntimeMaterial(color, _runtimeMaterialInstance);
    }

    private static void ApplyColorToRuntimeMaterial(Color color, Material mat)
    {
        if (mat == null)
            return;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        else
            mat.color = color;
    }
}
