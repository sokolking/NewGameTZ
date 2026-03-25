using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Силуэт цели при удержании ЛКМ по удалённому юниту: голова, руки, корпус, ноги.
/// Рендерится как Screen Space Overlay (Canvas/Image).
///
/// Настройка префаба: правой кнопкой по компоненту в Inspector → «Setup Canvas Hierarchy».
/// После этого Canvas и все Image-дочерние объекты запекаются в префаб и видны в Editor.
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

    [Tooltip("If unset: Resources/hold_indicator (Sprite or Texture2D).")]
    [SerializeField] private Sprite _spriteOverride;
    [SerializeField] private Color _partBaseColor      = new Color(1, 1, 1, 0.55f);
    [SerializeField] private Color _partHighlightColor = new Color(1f, 0f, 0f, 0.55f);
    [SerializeField] private Rect _headUvRect      = new Rect(0.25f, 0.8f,  0.5f, 0.2f);
    [SerializeField] private Rect _torsoUvRect     = new Rect(0.25f, 0.45f, 0.5f, 0.35f);
    [SerializeField] private Rect _legsUvRect      = new Rect(0.25f, 0f,    0.5f, 0.45f);
    [Tooltip("UV zone left arm (silhouette-facing). Tested before torso.")]
    [SerializeField] private Rect _leftHandUvRect  = new Rect(0.02f, 0.42f, 0.2f, 0.36f);
    [Tooltip("UV zone right arm. Tested before torso.")]
    [SerializeField] private Rect _rightHandUvRect = new Rect(0.78f, 0.42f, 0.2f, 0.36f);
    [Tooltip("Hold indicator height in screen pixels.")]
    [SerializeField] private float _screenHeightPx = 280f;

    // ── Canvas / UI ──────────────────────────────────────────────────────────
    private Canvas        _canvas;
    private RectTransform _rootRect;   // "IndicatorRoot" внутри Canvas
    private Image         _baseImage;
    private readonly Image[] _partImages = new Image[6];

    // ── Состояние ────────────────────────────────────────────────────────────
    private BodyPartKind _hoveredPart            = BodyPartKind.None;
    private Vector2      _screenSizePx;
    private Vector2      _lastHighlightMousePos  = new Vector2(float.NaN, float.NaN);
    private bool         _built;
    private bool         _ownedCanvas;   // true = мы создали Canvas сами, нужно удалить в OnDestroy

    // ── Precomputed UV bounds ────────────────────────────────────────────────
    private float _headMinU,      _headMaxU,      _headMinV,      _headMaxV;
    private float _torsoMinU,     _torsoMaxU,     _torsoMinV,     _torsoMaxV;
    private float _legsMinU,      _legsMaxU,      _legsMinV,      _legsMaxV;
    private float _leftHandMinU,  _leftHandMaxU,  _leftHandMinV,  _leftHandMaxV;
    private float _rightHandMinU, _rightHandMaxU, _rightHandMinV, _rightHandMaxV;

    // ── Public API ───────────────────────────────────────────────────────────

    public BodyPartKind HoveredPart    => _hoveredPart;
    public bool         HasValidVisuals => _baseImage != null && _baseImage.sprite != null;

    public void EnsureBuilt()
    {
        if (_built)
            return;

        // Если префаб уже содержит Canvas-иерархию — подключаемся к ней.
        // Иначе строим всё в рантайме (fallback).
        if (!TryAttachExistingHierarchy())
            BuildFromScratch();

        if (_baseImage == null || _baseImage.sprite == null)
            return;

        PrecomputeUvBounds();
        ApplyPartColors(BodyPartKind.None);
        _built = true;
    }

    /// <summary>Показать / скрыть индикатор.</summary>
    public void SetVisible(bool visible)
    {
        if (_rootRect == null)
            return;
        if (_rootRect.gameObject.activeSelf != visible)
            _rootRect.gameObject.SetActive(visible);
        if (!visible)
        {
            _lastHighlightMousePos = new Vector2(float.NaN, float.NaN);
            ApplyPartColors(BodyPartKind.None);
        }
    }

    /// <summary>
    /// Центрирует индикатор на <paramref name="screenCenter"/>
    /// (пиксели экрана, (0,0) = нижний левый угол).
    /// </summary>
    public void SetScreenCenter(Vector2 cursorScreenPos)
    {
        if (_rootRect == null)
            return;
        // Курсор на 12.5% выше центра канваса → сдвигаем канвас на 12.5% вниз.
        _rootRect.anchoredPosition = new Vector2(
            cursorScreenPos.x,
            cursorScreenPos.y - _screenSizePx.y * 0.125f);
    }

    /// <param name="forceImmediate">При первом кадре удержания — без пропуска по чётности.</param>
    public void UpdateBodyPartHighlight(Vector2 mouseScreenPos, bool forceImmediate)
    {
        if (!forceImmediate && (Time.frameCount & 1) != 0)
            return;
        if (!forceImmediate && !float.IsNaN(_lastHighlightMousePos.x) &&
            (mouseScreenPos - _lastHighlightMousePos).sqrMagnitude < 0.25f)
            return;
        _lastHighlightMousePos = mouseScreenPos;

        if (_rootRect == null) { ApplyPartColors(BodyPartKind.None); return; }

        Vector2 center = _rootRect.anchoredPosition;
        Vector2 local  = mouseScreenPos - center;
        float   hw     = _screenSizePx.x * 0.5f;
        float   hh     = _screenSizePx.y * 0.5f;

        if (hw < 1f || hh < 1f) { ApplyPartColors(BodyPartKind.None); return; }

        float u = local.x / (hw * 2f) + 0.5f;
        float v = local.y / (hh * 2f) + 0.5f;

        if (u < 0f || u > 1f || v < 0f || v > 1f)
        { ApplyPartColors(BodyPartKind.None); return; }

        ApplyPartColors(ClassifyBodyPartFromUv(u, v));
    }

    // ── Подключение к существующей иерархии (из префаба) ────────────────────

    /// <returns>true, если нашли готовую Canvas-иерархию в дочернем объекте префаба.</returns>
    private bool TryAttachExistingHierarchy()
    {
        // Canvas — дочерний объект с именем "HoldTargetCanvas"
        Transform canvasT = transform.Find("HoldTargetCanvas");
        if (canvasT == null)
            return false;

        _canvas = canvasT.GetComponent<Canvas>();
        if (_canvas == null)
            return false;

        Transform rootT = canvasT.Find("IndicatorRoot");
        if (rootT == null)
            return false;

        _rootRect = rootT.GetComponent<RectTransform>();
        if (_rootRect == null)
            return false;

        Transform baseT = rootT.Find("HoldBase");
        if (baseT == null)
            return false;

        _baseImage = baseT.GetComponent<Image>();
        if (_baseImage == null)
            return false;
        // Спрайт мог не сохраниться как asset-ссылка в префабе (Sprite.Create).
        // Перезагружаем из Resources вместо того чтобы возвращать false и дублировать иерархию.
        if (_baseImage.sprite == null)
            _baseImage.sprite = LoadSprite();
        if (_baseImage.sprite == null)
            return false;

        for (int p = 1; p <= 5; p++)
        {
            Transform partT = rootT.Find("HoldBlock_" + (BodyPartKind)p);
            if (partT == null) return false;
            Image img = partT.GetComponent<Image>();
            if (img == null) return false;
            _partImages[p] = img;
        }

        _screenSizePx = _rootRect.sizeDelta;
        _ownedCanvas  = false;
        return true;
    }

    // ── Построение иерархии в рантайме (если префаб не настроен) ────────────

    private void BuildFromScratch()
    {
        // Удаляем существующий HoldTargetCanvas (мог остаться из префаба),
        // чтобы не плодить дублирующие Canvas-оверлеи.
        var existing = transform.Find("HoldTargetCanvas");
        if (existing != null)
            Destroy(existing.gameObject);

        Sprite baseSprite = LoadSprite();
        if (baseSprite == null)
            return;

        float aspect  = baseSprite.rect.width / Mathf.Max(1f, baseSprite.rect.height);
        _screenSizePx = new Vector2(_screenHeightPx * aspect, _screenHeightPx);

        var canvasGo         = new GameObject("HoldTargetCanvas");
        canvasGo.transform.SetParent(transform, false);   // дочерний — уничтожается вместе с родителем
        _canvas              = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        var scaler           = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode   = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor   = 1f;
        _ownedCanvas         = false;   // дочерний — не удаляем вручную

        var rootGo           = new GameObject("IndicatorRoot");
        rootGo.transform.SetParent(canvasGo.transform, false);
        _rootRect            = rootGo.AddComponent<RectTransform>();
        _rootRect.anchorMin  = Vector2.zero;
        _rootRect.anchorMax  = Vector2.zero;
        _rootRect.pivot      = new Vector2(0.5f, 0.5f);
        _rootRect.sizeDelta  = _screenSizePx;
        _rootRect.anchoredPosition = new Vector2(-9999f, -9999f);

        // Base image
        var baseGo            = new GameObject("HoldBase");
        baseGo.transform.SetParent(_rootRect, false);
        var baseRT            = baseGo.AddComponent<RectTransform>();
        baseRT.anchorMin      = Vector2.zero;
        baseRT.anchorMax      = Vector2.one;
        baseRT.offsetMin      = baseRT.offsetMax = Vector2.zero;
        _baseImage            = baseGo.AddComponent<Image>();
        _baseImage.sprite     = baseSprite;
        _baseImage.color      = Color.white;
        _baseImage.preserveAspect = false;
        _baseImage.raycastTarget  = false;

        CreateBodyPartBlock(BodyPartKind.Head,      _headUvRect);
        CreateBodyPartBlock(BodyPartKind.Torso,     _torsoUvRect);
        CreateBodyPartBlock(BodyPartKind.Legs,      _legsUvRect);
        CreateBodyPartBlock(BodyPartKind.LeftHand,  _leftHandUvRect);
        CreateBodyPartBlock(BodyPartKind.RightHand, _rightHandUvRect);
    }

    private void CreateBodyPartBlock(BodyPartKind part, Rect uvRect)
    {
        float minU = Mathf.Clamp01(uvRect.xMin), maxU = Mathf.Clamp01(uvRect.xMax);
        float minV = Mathf.Clamp01(uvRect.yMin), maxV = Mathf.Clamp01(uvRect.yMax);

        var go          = new GameObject("HoldBlock_" + part);
        go.transform.SetParent(_rootRect, false);
        var rt          = go.AddComponent<RectTransform>();
        rt.anchorMin    = new Vector2(minU, minV);
        rt.anchorMax    = new Vector2(maxU, maxV);
        rt.offsetMin    = rt.offsetMax = Vector2.zero;
        var img         = go.AddComponent<Image>();
        img.color       = _partBaseColor;
        img.raycastTarget = false;
        _partImages[(int)part] = img;
    }

    private void OnDestroy()
    {
        if (_ownedCanvas && _canvas != null)
            Destroy(_canvas.gameObject);
    }

    // ── Editor: запекаем Canvas в префаб ────────────────────────────────────

#if UNITY_EDITOR
    /// <summary>
    /// Правой кнопкой по компоненту → «Setup Canvas Hierarchy».
    /// Добавляет Canvas и Image-дочерние объекты прямо на этот GameObject,
    /// чтобы структура была видна в Edit Mode и сохранялась в префабе.
    /// </summary>
    [ContextMenu("Setup Canvas Hierarchy")]
    void EditorSetupCanvasHierarchy()
    {
        Sprite baseSprite = LoadSprite();
        if (baseSprite == null)
        {
            Debug.LogWarning("HoldTargetIndicator: hold_indicator sprite not found in Resources.");
            return;
        }

        // Находим путь к .prefab файлу (работает и в Prefab Mode, и из сцены)
        string prefabPath = null;
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
            prefabPath = stage.assetPath;
        if (string.IsNullOrEmpty(prefabPath))
            prefabPath = AssetDatabase.GetAssetPath(gameObject);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError("HoldTargetIndicator: GameObject is not a prefab. " +
                           "Save it as a .prefab first.");
            return;
        }

        float aspect = baseSprite.rect.width / Mathf.Max(1f, baseSprite.rect.height);
        var   sizePx = new Vector2(_screenHeightPx * aspect, _screenHeightPx);

        // LoadPrefabContents загружает изолированную редактируемую копию — здесь
        // AddComponent работает без ограничений Prefab Mode.
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var ind = prefabRoot.GetComponent<HoldTargetIndicator>();
            if (ind == null)
                throw new System.Exception("HoldTargetIndicator component is missing on prefab root.");

            // Удаляем старую иерархию если была
            var old = prefabRoot.transform.Find("HoldTargetCanvas");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // ── HoldTargetCanvas ────────────────────────────────────────────
            var canvasGo = new GameObject("HoldTargetCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(prefabRoot.transform, false);

            var cv = canvasGo.AddComponent<Canvas>();
            cv.renderMode   = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 200;

            var sc = canvasGo.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            sc.scaleFactor = 1f;

            // ── IndicatorRoot ────────────────────────────────────────────────
            var rootGo = new GameObject("IndicatorRoot", typeof(RectTransform));
            rootGo.transform.SetParent(canvasGo.transform, false);
            var rr             = rootGo.GetComponent<RectTransform>();
            rr.anchorMin       = Vector2.zero;
            rr.anchorMax       = Vector2.zero;
            rr.pivot           = new Vector2(0.5f, 0.5f);
            rr.sizeDelta       = sizePx;
            rr.anchoredPosition = new Vector2(-9999f, -9999f);

            // ── HoldBase ─────────────────────────────────────────────────────
            PrefabEnsureImage(rootGo.transform, "HoldBase",
                rt  => { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                         rt.offsetMin = rt.offsetMax = Vector2.zero; },
                img => { img.sprite = baseSprite; img.color = Color.white;
                         img.preserveAspect = false; img.raycastTarget = false; });

            // ── Зоны частей тела ─────────────────────────────────────────────
            PrefabEnsurePartBlock(rootGo.transform, BodyPartKind.Head,      ind._headUvRect,      ind._partBaseColor);
            PrefabEnsurePartBlock(rootGo.transform, BodyPartKind.Torso,     ind._torsoUvRect,     ind._partBaseColor);
            PrefabEnsurePartBlock(rootGo.transform, BodyPartKind.Legs,      ind._legsUvRect,      ind._partBaseColor);
            PrefabEnsurePartBlock(rootGo.transform, BodyPartKind.LeftHand,  ind._leftHandUvRect,  ind._partBaseColor);
            PrefabEnsurePartBlock(rootGo.transform, BodyPartKind.RightHand, ind._rightHandUvRect, ind._partBaseColor);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            Debug.Log($"HoldTargetIndicator: Canvas hierarchy saved -> {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.Refresh();
    }

    private static void PrefabEnsurePartBlock(
        Transform root, BodyPartKind part, Rect uvRect, Color baseColor)
    {
        float minU = Mathf.Clamp01(uvRect.xMin), maxU = Mathf.Clamp01(uvRect.xMax);
        float minV = Mathf.Clamp01(uvRect.yMin), maxV = Mathf.Clamp01(uvRect.yMax);
        PrefabEnsureImage(root, "HoldBlock_" + part,
            rt  => { rt.anchorMin = new Vector2(minU, minV);
                     rt.anchorMax = new Vector2(maxU, maxV);
                     rt.offsetMin = rt.offsetMax = Vector2.zero; },
            img => { img.color = baseColor; img.raycastTarget = false; });
    }

    private static void PrefabEnsureImage(
        Transform parent, string childName,
        System.Action<RectTransform> cfgRect,
        System.Action<Image> cfgImage)
    {
        var existing = parent.Find(childName);
        GameObject go = existing != null
            ? existing.gameObject
            : new GameObject(childName, typeof(RectTransform));
        if (existing == null)
            go.transform.SetParent(parent, false);
        cfgRect(go.GetComponent<RectTransform>());
        cfgImage(go.GetComponent<Image>() ?? go.AddComponent<Image>());
    }

    /// <summary>Для предпросмотра в редакторе.</summary>
    public void EditorRebuildVisuals() => EditorSetupCanvasHierarchy();
#endif

    // ── Загрузка спрайта ─────────────────────────────────────────────────────

    private Sprite LoadSprite()
    {
        if (_spriteOverride != null)
            return _spriteOverride;
        Sprite s = Resources.Load<Sprite>("hold_indicator");
        if (s != null)
            return s;
        Texture2D tex = Resources.Load<Texture2D>("hold_indicator");
        if (tex == null)
            return null;
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), 100f);
    }

    // ── UV bounds ────────────────────────────────────────────────────────────

    private void PrecomputeUvBounds()
    {
        _headMinU  = Mathf.Clamp01(_headUvRect.xMin);  _headMaxU  = Mathf.Clamp01(_headUvRect.xMax);
        _headMinV  = Mathf.Clamp01(_headUvRect.yMin);  _headMaxV  = Mathf.Clamp01(_headUvRect.yMax);
        _torsoMinU = Mathf.Clamp01(_torsoUvRect.xMin); _torsoMaxU = Mathf.Clamp01(_torsoUvRect.xMax);
        _torsoMinV = Mathf.Clamp01(_torsoUvRect.yMin); _torsoMaxV = Mathf.Clamp01(_torsoUvRect.yMax);
        _legsMinU  = Mathf.Clamp01(_legsUvRect.xMin);  _legsMaxU  = Mathf.Clamp01(_legsUvRect.xMax);
        _legsMinV  = Mathf.Clamp01(_legsUvRect.yMin);  _legsMaxV  = Mathf.Clamp01(_legsUvRect.yMax);
        _leftHandMinU  = Mathf.Clamp01(_leftHandUvRect.xMin);  _leftHandMaxU  = Mathf.Clamp01(_leftHandUvRect.xMax);
        _leftHandMinV  = Mathf.Clamp01(_leftHandUvRect.yMin);  _leftHandMaxV  = Mathf.Clamp01(_leftHandUvRect.yMax);
        _rightHandMinU = Mathf.Clamp01(_rightHandUvRect.xMin); _rightHandMaxU = Mathf.Clamp01(_rightHandUvRect.xMax);
        _rightHandMinV = Mathf.Clamp01(_rightHandUvRect.yMin); _rightHandMaxV = Mathf.Clamp01(_rightHandUvRect.yMax);
    }

    // ── Классификация части тела ─────────────────────────────────────────────

    private BodyPartKind ClassifyBodyPartFromUv(float u, float v)
    {
        if (u >= _headMinU && u <= _headMaxU && v >= _headMinV && v <= _headMaxV)
            return BodyPartKind.Head;
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
        for (int i = 1; i < _partImages.Length; i++)
        {
            Image img = _partImages[i];
            if (img == null) continue;
            img.color = i == highlighted ? _partHighlightColor : _partBaseColor;
        }
    }
}
