using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стек маленьких "кирпичиков" урона над юнитом.
/// Держится 3 секунды, максимум 5 элементов, новые добавляются снизу.
/// </summary>
public class DamagePopupStack : MonoBehaviour
{
    [SerializeField] private Vector3 _anchorLocalOffset = new Vector3(0f, 2.1f, 0f);
    [SerializeField] private float _verticalSpacing = 0.32f;
    [SerializeField] private float _entryLifetimeSeconds = 3f;
    [SerializeField] private int _maxEntries = 5;
    [SerializeField] private Vector2 _entrySize = new Vector2(0.72f, 0.24f);
    [SerializeField] private Color _backgroundColor = new Color(0.85f, 0f, 0f, 0.92f);
    [SerializeField] private Color _textColor = Color.white;

    private sealed class Entry
    {
        public GameObject Root;
        public float ExpireAt;
    }

    private static Sprite _whiteSprite;
    private static Material _overlayMaterial;

    private readonly List<Entry> _entries = new();
    private Transform _anchor;
    private Camera _cam;
    private Transform _camTransform;

    public void ShowDamage(int damage)
    {
        if (damage <= 0)
            return;

        EnsureAnchor();
        if (_anchor == null)
            return;

        if (_entries.Count >= Mathf.Max(1, _maxEntries))
            RemoveEntryAt(0);

        GameObject root = new GameObject("DamagePopupEntry");
        root.transform.SetParent(_anchor, false);

        GameObject bgGo = new GameObject("Background");
        bgGo.transform.SetParent(root.transform, false);
        SpriteRenderer bg = bgGo.AddComponent<SpriteRenderer>();
        bg.sprite = GetWhiteSprite();
        bg.color = _backgroundColor;
        ConfigureOverlayRenderer(bg, 5200);
        bgGo.transform.localScale = new Vector3(_entrySize.x, _entrySize.y, 1f);

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        TextMesh tm = textGo.AddComponent<TextMesh>();
        tm.text = damage.ToString();
        tm.fontSize = 48;
        tm.characterSize = 0.045f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = _textColor;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        MeshRenderer textRenderer = textGo.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            textRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            textRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            textRenderer.sortingOrder = 5201;
        }

        _entries.Add(new Entry
        {
            Root = root,
            ExpireAt = Time.unscaledTime + Mathf.Max(0.1f, _entryLifetimeSeconds)
        });

        RefreshLayout();
    }

    public void ClearAll()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
            RemoveEntryAt(i);
    }

    private void Update()
    {
        if (_entries.Count == 0)
            return;

        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam != null) _camTransform = _cam.transform;
        }

        if (_anchor != null && _camTransform != null)
            _anchor.rotation = Quaternion.LookRotation(-_camTransform.forward, _camTransform.up);

        float now = Time.unscaledTime;
        bool removedAny = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].ExpireAt > now)
                continue;
            RemoveEntryAt(i);
            removedAny = true;
        }

        if (removedAny && _entries.Count > 0)
            RefreshLayout();
    }

    private void EnsureAnchor()
    {
        if (_anchor != null)
            return;

        GameObject anchorGo = new GameObject("DamagePopupAnchor");
        anchorGo.transform.SetParent(transform, false);
        anchorGo.transform.localPosition = _anchorLocalOffset;
        _anchor = anchorGo.transform;
    }

    private void RefreshLayout()
    {
        if (_anchor == null)
            return;

        _anchor.localPosition = _anchorLocalOffset;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Root == null)
                continue;
            _entries[i].Root.transform.localPosition = new Vector3(0f, -i * _verticalSpacing, 0f);
        }
    }

    private void RemoveEntryAt(int index)
    {
        if (index < 0 || index >= _entries.Count)
            return;

        Entry entry = _entries[index];
        _entries.RemoveAt(index);
        if (entry.Root != null)
            Destroy(entry.Root);
    }

    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null)
            return _whiteSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return _whiteSprite;
    }

    private static void ConfigureOverlayRenderer(SpriteRenderer sr, int sortingOrder)
    {
        if (sr == null)
            return;

        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        sr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        sr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        sr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        sr.allowOcclusionWhenDynamic = false;
        sr.sortingOrder = sortingOrder;

        int uiLayerId = SortingLayer.NameToID("UI");
        if (uiLayerId != 0)
            sr.sortingLayerID = uiLayerId;

        Material overlayMat = GetOverlayMaterial();
        if (overlayMat != null)
            sr.sharedMaterial = overlayMat;
    }

    private static Material GetOverlayMaterial()
    {
        if (_overlayMaterial != null)
            return _overlayMaterial;

        Shader shader = Shader.Find("Custom/HoldIndicatorOverlay");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        _overlayMaterial = new Material(shader)
        {
            renderQueue = 5000
        };
        _overlayMaterial.enableInstancing = true;
        return _overlayMaterial;
    }
}
