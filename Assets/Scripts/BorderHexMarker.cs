using UnityEngine;

/// <summary>
/// Collider hit target for escape UI: drawn on the ring <b>outside</b> the map (or on the escape hex after zone shrink).
/// <see cref="Col"/>/<see cref="Row"/> are the escape hex (may be outside the physical grid); <see cref="HexGrid.GetCell"/> is often null.
/// Mesh matches <see cref="HexGrid"/> cells: <see cref="Hexagon"/> + same Y rotation as <see cref="HexGrid"/> generation.
/// Uses a <see cref="BoxCollider"/> sized from <see cref="Renderer.localBounds"/> (avoids MeshCollider / PhysX issues on some Unity versions).
/// </summary>
public class BorderHexMarker : MonoBehaviour
{
    private TextMesh _overlayLabel;
    private string _lastOverlayText;

    /// <summary>In-bounds escape-border hex (server / movement).</summary>
    public int Col { get; private set; }
    public int Row { get; private set; }

    /// <summary>World placement; may be outside the grid when the escape ring sits past the map edge.</summary>
    public void Initialize(HexGrid grid, int gameplayCol, int gameplayRow, int visualCol, int visualRow, Material material)
    {
        Col = gameplayCol;
        Row = gameplayRow;
        if (grid == null)
            return;

        transform.SetParent(grid.transform, worldPositionStays: true);
        // Lift above hex tops to reduce z-fighting with floor/cells.
        transform.position = grid.GetCellWorldPosition(visualCol, visualRow);
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.Euler(0f, 60f, 0f);

        foreach (Collider existing in GetComponents<Collider>())
        {
            if (existing != null)
                Destroy(existing);
        }

        // Same order as HexGrid.CreateHexCell: Hexagon pulls in MeshFilter/MeshRenderer via RequireComponent.
        Hexagon hex = GetComponent<Hexagon>() ?? gameObject.AddComponent<Hexagon>();
        hex.BuildMesh(grid.HexVisualRadius);

        MeshFilter mf = GetComponent<MeshFilter>();
        MeshRenderer rend = GetComponent<MeshRenderer>();
        if (mf == null || rend == null)
        {
            Debug.LogError("[BorderHexMarker] MeshFilter/MeshRenderer missing after Hexagon.BuildMesh.", this);
            return;
        }

        if (material != null)
            rend.sharedMaterial = material;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        Bounds lb = rend.localBounds;
        BoxCollider boxCol = gameObject.AddComponent<BoxCollider>();
        boxCol.center = lb.center;
        boxCol.size = new Vector3(lb.size.x, Mathf.Max(0.08f, lb.size.y), lb.size.z);
        boxCol.isTrigger = false;
    }

    /// <summary>World-space text above the marker (OOB escape hexes have no <see cref="HexCell"/>).</summary>
    public void SetOverlayLabel(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _lastOverlayText = null;
            if (_overlayLabel != null)
                _overlayLabel.gameObject.SetActive(false);
            return;
        }

        if (_overlayLabel != null && text == _lastOverlayText && _overlayLabel.gameObject.activeSelf)
            return;

        if (_overlayLabel == null)
        {
            GameObject go = new GameObject("HoverLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 120f);

            _overlayLabel = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) _overlayLabel.font = font;
            _overlayLabel.anchor = TextAnchor.MiddleCenter;
            _overlayLabel.alignment = TextAlignment.Center;
            _overlayLabel.fontSize = 10;
            _overlayLabel.color = Color.white;
        }

        MeshRenderer rend = GetComponent<MeshRenderer>();
        if (rend != null)
        {
            Bounds b = rend.bounds;
            float hexSize = Mathf.Max(b.size.x, b.size.z, 0.1f);
            _overlayLabel.characterSize = Mathf.Max(0.01f, hexSize * 0.45f);
        }

        _overlayLabel.text = text;
        _lastOverlayText = text;
        _overlayLabel.gameObject.SetActive(true);
    }
}
