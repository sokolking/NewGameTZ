using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover / press эффект через смену спрайта + звук клика.
/// Никаких coroutine/Update — только event-driven, нулевая нагрузка на FPS.
/// Требует Image на том же GameObject.
/// Спрайты и звук загружаются один раз из Resources.
/// </summary>
[RequireComponent(typeof(Image))]
public class ButtonEffect : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler,  IPointerUpHandler
{
    // ── Спрайты (назначить в инспекторе или оставить пустыми — тогда загрузятся из Resources) ──
    [Header("Спрайты")]
    [SerializeField] private Sprite _normalSprite;
    [SerializeField] private Sprite _hoverSprite;
    [SerializeField] private Sprite _pressedSprite;

    [Header("Звук")]
    [SerializeField] private AudioClip _clickSound;
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;

    // ── runtime ──────────────────────────────────────────────────────────────
    private Image        _image;
    private AudioSource  _audio;
    private bool         _isHovered;

    // ── статический кэш: Resources грузим один раз на весь тип ──────────────
    private static Sprite     _cachedNormal;
    private static Sprite     _cachedHover;
    private static Sprite     _cachedPressed;
    private static AudioClip  _cachedClick;

    private void Awake()
    {
        _image = GetComponent<Image>();

        // AudioSource — ищем на объекте или создаём
        _audio = GetComponent<AudioSource>();
        if (_audio == null)
            _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f; // 2D звук

        // Загружаем ресурсы один раз (static cache)
        if (_cachedNormal  == null) _cachedNormal  = Resources.Load<Sprite>("panel_bg");
        if (_cachedHover   == null) _cachedHover   = Resources.Load<Sprite>("panel_bg_hover");
        if (_cachedPressed == null) _cachedPressed = Resources.Load<Sprite>("panel_bg_pressed");
        if (_cachedClick   == null) _cachedClick   = Resources.Load<AudioClip>("button_click");

        // Inspector-значения имеют приоритет над Resources
        if (_normalSprite  == null) _normalSprite  = _cachedNormal;
        if (_hoverSprite   == null) _hoverSprite   = _cachedHover;
        if (_pressedSprite == null) _pressedSprite = _cachedPressed;
        if (_clickSound    == null) _clickSound    = _cachedClick;

        // Применяем стартовый спрайт
        if (_normalSprite != null) _image.sprite = _normalSprite;
    }

    // ── pointer events — мгновенная смена спрайта, никаких аллокаций ────────

    public void OnPointerEnter(PointerEventData _)
    {
        _isHovered = true;
        Apply(_hoverSprite);
    }

    public void OnPointerExit(PointerEventData _)
    {
        _isHovered = false;
        Apply(_normalSprite);
    }

    public void OnPointerDown(PointerEventData _)
    {
        Apply(_pressedSprite);
        PlayClick();
    }

    public void OnPointerUp(PointerEventData _)
    {
        Apply(_isHovered ? _hoverSprite : _normalSprite);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void Apply(Sprite sprite)
    {
        if (sprite != null && _image.sprite != sprite)
            _image.sprite = sprite;
    }

    private void PlayClick()
    {
        if (_clickSound != null)
            _audio.PlayOneShot(_clickSound, _volume);
    }

    private void OnDisable()
    {
        _isHovered = false;
        Apply(_normalSprite);
    }
}
