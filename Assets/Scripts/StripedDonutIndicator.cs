using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI donut indicator with configurable stripe count.
/// Draws a ring (donut) split into stripes; each stripe can be filled/empty by Value01.
/// </summary>
[AddComponentMenu("UI/Striped Donut Indicator")]
public sealed class StripedDonutIndicator : MaskableGraphic
{
    [SerializeField, Min(1)] private int _stripeCount = 12;
    [SerializeField, Range(0f, 1f)] private float _value01 = 0.5f;
    [SerializeField, Range(0.05f, 0.95f)] private float _innerRadius01 = 0.62f;
    [SerializeField, Range(0f, 0.7f)] private float _gap01 = 0.18f;
    [SerializeField] private float _startAngleDeg = 90f;
    [SerializeField] private bool _clockwise = true;
    [SerializeField] private Color _filledColor = new Color(1f, 0.75f, 0.2f, 1f);
    [SerializeField] private Color _emptyColor = new Color(1f, 1f, 1f, 0.2f);
    [SerializeField, Min(1)] private int _arcStepsPerStripe = 6;

    public int StripeCount
    {
        get => _stripeCount;
        set
        {
            int v = Mathf.Max(1, value);
            if (_stripeCount == v) return;
            _stripeCount = v;
            SetVerticesDirty();
        }
    }

    public float Value01
    {
        get => _value01;
        set
        {
            float v = Mathf.Clamp01(value);
            if (Mathf.Approximately(_value01, v)) return;
            _value01 = v;
            SetVerticesDirty();
        }
    }

    public void SetValue01(float value01) => Value01 = value01;
    public void SetStripeCount(int stripeCount) => StripeCount = stripeCount;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        float radius = Mathf.Min(r.width, r.height) * 0.5f;
        if (radius <= 0.0001f)
            return;

        Vector2 center = r.center;
        float innerR = radius * Mathf.Clamp(_innerRadius01, 0.05f, 0.95f);
        float outerR = radius;

        int stripes = Mathf.Max(1, _stripeCount);
        float stepDeg = 360f / stripes;
        float stripeDeg = stepDeg * (1f - Mathf.Clamp01(_gap01));
        float dir = _clockwise ? -1f : 1f;

        float rawFilled = Mathf.Clamp01(_value01) * stripes;
        int full = Mathf.FloorToInt(rawFilled);
        float partial = rawFilled - full;

        float start = _startAngleDeg;
        for (int i = 0; i < stripes; i++)
        {
            float stripeStart = start + dir * (i * stepDeg);
            float stripeEnd = stripeStart + dir * stripeDeg;

            Color c;
            if (i < full)
                c = _filledColor;
            else if (i == full && partial > 0.001f)
            {
                // Partial stripe alpha blend for smoother value changes.
                c = Color.Lerp(_emptyColor, _filledColor, partial);
            }
            else
                c = _emptyColor;

            AddRingSector(vh, center, innerR, outerR, stripeStart, stripeEnd, c, Mathf.Max(1, _arcStepsPerStripe));
        }
    }

    private static void AddRingSector(
        VertexHelper vh,
        Vector2 center,
        float innerR,
        float outerR,
        float startDeg,
        float endDeg,
        Color color,
        int steps)
    {
        float s = startDeg * Mathf.Deg2Rad;
        float e = endDeg * Mathf.Deg2Rad;
        if (Mathf.Approximately(s, e))
            return;

        int baseIndex = vh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float a = Mathf.Lerp(s, e, t);
            float cs = Mathf.Cos(a);
            float sn = Mathf.Sin(a);

            Vector2 outer = center + new Vector2(cs, sn) * outerR;
            Vector2 inner = center + new Vector2(cs, sn) * innerR;

            vh.AddVert(outer, color, Vector2.zero);
            vh.AddVert(inner, color, Vector2.zero);
        }

        for (int i = 0; i < steps; i++)
        {
            int o0 = baseIndex + i * 2;
            int i0 = o0 + 1;
            int o1 = o0 + 2;
            int i1 = o0 + 3;

            vh.AddTriangle(o0, o1, i1);
            vh.AddTriangle(o0, i1, i0);
        }
    }
#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        _stripeCount = Mathf.Max(1, _stripeCount);
        _arcStepsPerStripe = Mathf.Max(1, _arcStepsPerStripe);
        _value01 = Mathf.Clamp01(_value01);
        _innerRadius01 = Mathf.Clamp(_innerRadius01, 0.05f, 0.95f);
        _gap01 = Mathf.Clamp(_gap01, 0f, 0.7f);
        SetVerticesDirty();
    }
#endif
}

