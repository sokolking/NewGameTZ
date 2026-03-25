using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Силуэт цели при удержании ЛКМ по удалённому юниту: голова, руки, корпус, ноги.
/// Экземпляр создаётся из префаба (см. Resources/HoldTargetIndicator или меню Tools).
/// </summary>
public class HoldTargetIndicator : MonoBehaviour
{
    public enum BodyPartKind
    {
        None = 0,
        Head = 1,
        Torso = 2,
        Legs = 3,
        LeftHand = 4,
        RightHand = 5
    }

    [Tooltip("Если не задан — Resources/hold_indicator (Sprite или Texture2D).")]
    [SerializeField] private Sprite _spriteOverride;
    [SerializeField] private Color _partBaseColor = new Color(1, 1, 1, 0.55f);
    [SerializeField] private Color _partHighlightColor = new Color(1f, 0f, 0f, 0.55f);
    [SerializeField] private Rect _headUvRect = new Rect(0.25f, 0.8f, 0.5f, 0.2f);
    [SerializeField] private Rect _torsoUvRect = new Rect(0.25f, 0.45f, 0.5f, 0.35f);
    [SerializeField] private Rect _legsUvRect = new Rect(0.25f, 0f, 0.5f, 0.45f);
    [Tooltip("UV зона левой руки (вид со стороны силуэта). Проверяется раньше корпуса.")]
    [SerializeField] private Rect _leftHandUvRect = new Rect(0.02f, 0.42f, 0.2f, 0.36f);
    [Tooltip("UV зона правой руки. Проверяется раньше корпуса.")]
    [SerializeField] private Rect _rightHandUvRect = new Rect(0.78f, 0.42f, 0.2f, 0.36f);

    private SpriteRenderer _baseRenderer;
    private static Sprite _holdBlockSprite;
    private static Material _holdOverlayMaterial;
    private readonly SpriteRenderer[] _partRenderers = new SpriteRenderer[6];
    private BodyPartKind _hoveredPart = BodyPartKind.None;
    private float _compositeHalfWidth = 0.5f;
    private float _compositeHalfHeight = 0.5f;
    private Vector2 _lastHighlightMousePos = new Vector2(float.NaN, float.NaN);
    private bool _built;

    private float _headMinU, _headMaxU, _headMinV, _headMaxV;
    private float _torsoMinU, _torsoMaxU, _torsoMinV, _torsoMaxV;
    private float _legsMinU, _legsMaxU, _legsMinV, _legsMaxV;
    private float _leftHandMinU, _leftHandMaxU, _leftHandMinV, _leftHandMaxV;
    private float _rightHandMinU, _rightHandMaxU, _rightHandMinV, _rightHandMaxV;

    public BodyPartKind HoveredPart => _hoveredPart;

    public bool HasValidVisuals => _baseRenderer != null && _baseRenderer.sprite != null;

    public void EnsureBuilt()
    {
        if (_built)
            return;

        if (!TryAttachExistingHierarchy())
            BuildFromScratch();

        if (_baseRenderer == null || _baseRenderer.sprite == null)
            return;

        PrecomputeUvBounds();
        RecalculateCompositeBounds();
        ApplyPartColors(BodyPartKind.None);
        _built = true;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
        if (!visible)
        {
            _lastHighlightMousePos = new Vector2(float.NaN, float.NaN);
            ApplyPartColors(BodyPartKind.None);
        }
    }

    public void SetWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        transform.SetPositionAndRotation(worldPosition, worldRotation);
    }

    /// <param name="forceImmediate">При первом кадре удержания — без пропуска по чётности кадра.</param>
    public void UpdateBodyPartHighlight(Camera camera, Vector2 mouseScreenPos, bool forceImmediate)
    {
        if (camera == null)
            return;

        if (!forceImmediate && (Time.frameCount & 1) != 0)
            return;
        if (!forceImmediate && !float.IsNaN(_lastHighlightMousePos.x) &&
            (mouseScreenPos - _lastHighlightMousePos).sqrMagnitude < 0.25f)
            return;
        _lastHighlightMousePos = mouseScreenPos;

        Ray ray = camera.ScreenPointToRay(new Vector3(mouseScreenPos.x, mouseScreenPos.y, 0f));
        Plane spritePlane = new Plane(transform.forward, transform.position);
        if (!spritePlane.Raycast(ray, out float enter) || enter <= 0f)
        {
            ApplyPartColors(BodyPartKind.None);
            return;
        }

        Vector3 worldHit = ray.GetPoint(enter);
        Vector3 localHit = transform.InverseTransformPoint(worldHit);
        float halfW = Mathf.Max(0.0001f, _compositeHalfWidth);
        float halfH = Mathf.Max(0.0001f, _compositeHalfHeight);
        float u = localHit.x / (halfW * 2f) + 0.5f;
        float v = localHit.y / (halfH * 2f) + 0.5f;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            ApplyPartColors(BodyPartKind.None);
            return;
        }

        ApplyPartColors(ClassifyBodyPartFromUv(u, v));
    }

    private bool TryAttachExistingHierarchy()
    {
        Transform baseT = transform.Find("HoldBase");
        if (baseT == null)
            return false;
        _baseRenderer = baseT.GetComponent<SpriteRenderer>();
        if (_baseRenderer == null || _baseRenderer.sprite == null)
            return false;

        for (int p = 1; p <= 5; p++)
        {
            var kind = (BodyPartKind)p;
            Transform t = transform.Find("HoldBlock_" + kind);
            if (t == null)
                return false;
            var sr = t.GetComponent<SpriteRenderer>();
            if (sr == null)
                return false;
            _partRenderers[p] = sr;
        }

        return true;
    }

    private void ClearVisualChildren()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);
            return;
        }
#endif
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }

    private void BuildFromScratch()
    {
        ClearVisualChildren();

        Sprite baseSprite = LoadSprite();
        if (baseSprite == null)
            return;

        var baseGo = new GameObject("HoldBase");
        baseGo.transform.SetParent(transform, false);
        _baseRenderer = baseGo.AddComponent<SpriteRenderer>();
        _baseRenderer.sprite = baseSprite;
        ConfigureOverlayRenderer(_baseRenderer, 5000);
        _baseRenderer.color = Color.white;

        RecalculateCompositeBounds();
        CreateBodyPartBlock(BodyPartKind.Head, _headUvRect);
        CreateBodyPartBlock(BodyPartKind.Torso, _torsoUvRect);
        CreateBodyPartBlock(BodyPartKind.Legs, _legsUvRect);
        CreateBodyPartBlock(BodyPartKind.LeftHand, _leftHandUvRect);
        CreateBodyPartBlock(BodyPartKind.RightHand, _rightHandUvRect);
        RecalculateCompositeBounds();
    }

#if UNITY_EDITOR
    /// <summary>Сборка иерархии в редакторе при создании префаба.</summary>
    public void EditorRebuildVisuals()
    {
        _built = false;
        _baseRenderer = null;
        for (int i = 0; i < _partRenderers.Length; i++)
            _partRenderers[i] = null;
        BuildFromScratch();
        PrecomputeUvBounds();
        RecalculateCompositeBounds();
        ApplyPartColors(BodyPartKind.None);
        _built = true;
    }
#endif

    private Sprite LoadSprite()
    {
        if (_spriteOverride != null)
            return _spriteOverride;

        Sprite fromSprite = Resources.Load<Sprite>("hold_indicator");
        if (fromSprite != null)
            return fromSprite;

        Texture2D tex = Resources.Load<Texture2D>("hold_indicator");
        if (tex == null)
            return null;

        return Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    private void PrecomputeUvBounds()
    {
        _headMinU = Mathf.Clamp01(_headUvRect.xMin);
        _headMaxU = Mathf.Clamp01(_headUvRect.xMax);
        _headMinV = Mathf.Clamp01(_headUvRect.yMin);
        _headMaxV = Mathf.Clamp01(_headUvRect.yMax);
        _torsoMinU = Mathf.Clamp01(_torsoUvRect.xMin);
        _torsoMaxU = Mathf.Clamp01(_torsoUvRect.xMax);
        _torsoMinV = Mathf.Clamp01(_torsoUvRect.yMin);
        _torsoMaxV = Mathf.Clamp01(_torsoUvRect.yMax);
        _legsMinU = Mathf.Clamp01(_legsUvRect.xMin);
        _legsMaxU = Mathf.Clamp01(_legsUvRect.xMax);
        _legsMinV = Mathf.Clamp01(_legsUvRect.yMin);
        _legsMaxV = Mathf.Clamp01(_legsUvRect.yMax);
        _leftHandMinU = Mathf.Clamp01(_leftHandUvRect.xMin);
        _leftHandMaxU = Mathf.Clamp01(_leftHandUvRect.xMax);
        _leftHandMinV = Mathf.Clamp01(_leftHandUvRect.yMin);
        _leftHandMaxV = Mathf.Clamp01(_leftHandUvRect.yMax);
        _rightHandMinU = Mathf.Clamp01(_rightHandUvRect.xMin);
        _rightHandMaxU = Mathf.Clamp01(_rightHandUvRect.xMax);
        _rightHandMinV = Mathf.Clamp01(_rightHandUvRect.yMin);
        _rightHandMaxV = Mathf.Clamp01(_rightHandUvRect.yMax);
    }

    private void CreateBodyPartBlock(BodyPartKind part, Rect uvRect)
    {
        Sprite s = GetHoldBlockSprite();
        if (s == null)
            return;

        float fullW = _compositeHalfWidth * 2f;
        float fullH = _compositeHalfHeight * 2f;
        float minU = Mathf.Clamp01(uvRect.xMin);
        float maxU = Mathf.Clamp01(uvRect.xMax);
        float minV = Mathf.Clamp01(uvRect.yMin);
        float maxV = Mathf.Clamp01(uvRect.yMax);
        float centerU = (minU + maxU) * 0.5f;
        float centerV = (minV + maxV) * 0.5f;
        float sizeU = Mathf.Max(0f, maxU - minU);
        float sizeV = Mathf.Max(0f, maxV - minV);

        var go = new GameObject("HoldBlock_" + part);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3((centerU - 0.5f) * fullW, (centerV - 0.5f) * fullH, 0f);
        go.transform.localScale = new Vector3(sizeU * fullW, sizeV * fullH, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        ConfigureOverlayRenderer(sr, 5001);
        sr.color = _partBaseColor;
        _partRenderers[(int)part] = sr;
    }

    private static void ConfigureOverlayRenderer(SpriteRenderer sr, int sortingOrder)
    {
        if (sr == null)
            return;
        sr.shadowCastingMode = ShadowCastingMode.Off;
        sr.receiveShadows = false;
        sr.lightProbeUsage = LightProbeUsage.Off;
        sr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        sr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        sr.allowOcclusionWhenDynamic = false;
        sr.sortingOrder = sortingOrder;

        int uiLayerId = SortingLayer.NameToID("UI");
        if (uiLayerId != 0)
            sr.sortingLayerID = uiLayerId;

        Material overlayMat = GetHoldOverlayMaterial();
        if (overlayMat != null)
            sr.sharedMaterial = overlayMat;
    }

    private static Material GetHoldOverlayMaterial()
    {
        if (_holdOverlayMaterial != null)
            return _holdOverlayMaterial;
        Shader shader = Shader.Find("Custom/HoldIndicatorOverlay");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return null;
        _holdOverlayMaterial = new Material(shader)
        {
            renderQueue = 5000
        };
        _holdOverlayMaterial.enableInstancing = true;
        return _holdOverlayMaterial;
    }

    private void RecalculateCompositeBounds()
    {
        float maxHalfW = 0.5f;
        float maxHalfH = 0.5f;
        if (_baseRenderer != null && _baseRenderer.sprite != null)
        {
            Bounds bb = _baseRenderer.sprite.bounds;
            maxHalfW = Mathf.Max(maxHalfW, Mathf.Abs(bb.extents.x));
            maxHalfH = Mathf.Max(maxHalfH, Mathf.Abs(bb.extents.y));
        }

        for (int i = 1; i < _partRenderers.Length; i++)
        {
            SpriteRenderer sr = _partRenderers[i];
            if (sr == null || sr.sprite == null)
                continue;
            Bounds b = sr.bounds;
            maxHalfW = Mathf.Max(maxHalfW, Mathf.Abs(b.extents.x));
            maxHalfH = Mathf.Max(maxHalfH, Mathf.Abs(b.extents.y));
        }

        _compositeHalfWidth = maxHalfW;
        _compositeHalfHeight = maxHalfH;
    }

    private static Sprite GetHoldBlockSprite()
    {
        if (_holdBlockSprite != null)
            return _holdBlockSprite;
        Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        _holdBlockSprite = Sprite.Create(t, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return _holdBlockSprite;
    }

    private BodyPartKind ClassifyBodyPartFromUv(float u, float v)
    {
        if (u >= _headMinU && u <= _headMaxU && v >= _headMinV && v <= _headMaxV)
            return BodyPartKind.Head;
        // Боковые зоны раньше корпуса — иначе центральная колонка перехватывает клики по рукам.
        if (u >= _leftHandMinU && u <= _leftHandMaxU && v >= _leftHandMinV && v <= _leftHandMaxV)
            return BodyPartKind.LeftHand;
        if (u >= _rightHandMinU && u <= _rightHandMaxU && v >= _rightHandMinV && v <= _rightHandMaxV)
            return BodyPartKind.RightHand;
        if (u >= _torsoMinU && u <= _torsoMaxU && v >= _torsoMinV && v <= _torsoMaxV)
            return BodyPartKind.Torso;
        if (u >= _legsMinU && u <= _legsMaxU && v >= _legsMinV && v <= _legsMaxV)
            return BodyPartKind.Legs;
        return BodyPartKind.None;
    }

    private void ApplyPartColors(BodyPartKind highlightedPart)
    {
        if (_hoveredPart == highlightedPart)
            return;
        _hoveredPart = highlightedPart;

        int highlighted = (int)highlightedPart;
        for (int i = 1; i < _partRenderers.Length; i++)
        {
            SpriteRenderer sr = _partRenderers[i];
            if (sr == null)
                continue;
            sr.color = i == highlighted ? _partHighlightColor : _partBaseColor;
        }
    }
}
