using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Interactive radial slider for ammo donut (0..1).</summary>
public sealed class ActiveItemAmmoDonutSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    [SerializeField] private Image _image;
    [SerializeField] private StripedDonutIndicator _striped;
    [SerializeField] private Graphic _graphic;
    [SerializeField] private RectTransform _rect;

    private bool _suppress;
    private float _value01;
    public Action<float> OnValueChanged;

    private void Awake()
    {
        if (_image == null) _image = GetComponent<Image>();
        if (_striped == null) _striped = GetComponent<StripedDonutIndicator>();
        if (_graphic == null) _graphic = GetComponent<Graphic>();
        if (_rect == null)
            _rect = transform as RectTransform;
        float v = Mathf.Clamp01(_value01);
        if (_image != null) _image.fillAmount = v;
        if (_striped != null) _striped.SetValue01(v);
    }

    public void SetValue01(float value01, bool notify)
    {
        _value01 = Mathf.Clamp01(value01);
        if (_image != null) _image.fillAmount = _value01;
        if (_striped != null) _striped.SetValue01(_value01);
        if (notify)
            OnValueChanged?.Invoke(_value01);
    }

    public void OnPointerDown(PointerEventData eventData) => ApplyFromPointer(eventData);
    public void OnDrag(PointerEventData eventData) => ApplyFromPointer(eventData);

    private void ApplyFromPointer(PointerEventData eventData)
    {
        if (_rect == null || _suppress)
            return;
        if (_image == null && _striped == null && _graphic == null)
            return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out var local))
            return;
        float angle = Mathf.Atan2(local.y, local.x);
        float normalized = (angle + Mathf.PI) / (2f * Mathf.PI);
        SetValue01(normalized, notify: true);
    }
}

