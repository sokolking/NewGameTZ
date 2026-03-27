using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DamagePopupQueue : MonoBehaviour
{
    [SerializeField] private int _maxEntries = 10;
    [SerializeField] private float _entryLifetimeSeconds = 1.5f;
    [SerializeField] private float _extraRisePixels = 4f;

    private sealed class Entry
    {
        public DamagePopupView View;
        public float ExpireAt;
    }

    private readonly List<Entry> _entries = new();
    private GameObject _prefab;
    private float _riseStepWorld = -1f;

    public void ShowDamage(int damage)
    {
        if (damage <= 0)
            return;

        CleanupExpired();
        ShiftExistingUp();

        if (_entries.Count >= Mathf.Max(1, _maxEntries))
            RemoveOldest();

        DamagePopupView view = CreateView();
        if (view == null)
            return;

        view.SetStackRiseWorld(0f);
        view.ShowDamage(damage);
        _entries.Insert(0, new Entry
        {
            View = view,
            ExpireAt = Time.unscaledTime + Mathf.Max(0.1f, _entryLifetimeSeconds)
        });
    }

    public void ShowHeal(int healed)
    {
        if (healed <= 0)
            return;

        CleanupExpired();
        ShiftExistingUp();

        if (_entries.Count >= Mathf.Max(1, _maxEntries))
            RemoveOldest();

        DamagePopupView view = CreateView();
        if (view == null)
            return;

        view.SetStackRiseWorld(0f);
        view.ShowHeal(healed);
        _entries.Insert(0, new Entry
        {
            View = view,
            ExpireAt = Time.unscaledTime + Mathf.Max(0.1f, _entryLifetimeSeconds)
        });
    }

    private void Update()
    {
        if (_entries.Count == 0)
            return;
        CleanupExpired();
    }

    private void ShiftExistingUp()
    {
        float step = GetRiseStepWorld();
        if (step <= 0f)
            return;

        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e?.View == null)
                continue;
            e.View.SetStackRiseWorld((i + 1) * step);
        }
    }

    private float GetRiseStepWorld()
    {
        if (_riseStepWorld > 0f)
            return _riseStepWorld;

        float pxHeight = 48f;
        if (_entries.Count > 0 && _entries[0]?.View != null)
            pxHeight = _entries[0].View.GetPanelHeightPixels(48f);

        // Canvas в prefab имеет scale 0.01 -> 1 px = 0.01 world units.
        _riseStepWorld = (pxHeight + Mathf.Max(0f, _extraRisePixels)) * 0.01f;
        return _riseStepWorld;
    }

    private DamagePopupView CreateView()
    {
        if (_prefab == null)
            _prefab = Resources.Load<GameObject>("DamagePopup");
        if (_prefab == null)
            return null;

        GameObject go = Instantiate(_prefab, transform);
        DamagePopupView view = go.GetComponent<DamagePopupView>();
        if (view == null)
            view = go.AddComponent<DamagePopupView>();
        view.Bind(transform);

        if (_riseStepWorld <= 0f)
            _riseStepWorld = (view.GetPanelHeightPixels(48f) + Mathf.Max(0f, _extraRisePixels)) * 0.01f;
        return view;
    }

    private void CleanupExpired()
    {
        float now = Time.unscaledTime;
        bool removedAny = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry e = _entries[i];
            if (e == null || e.View == null || e.ExpireAt <= now)
            {
                if (e?.View != null)
                    Destroy(e.View.gameObject);
                _entries.RemoveAt(i);
                removedAny = true;
            }
        }

        if (removedAny)
        {
            float step = GetRiseStepWorld();
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].View.SetStackRiseWorld(i * step);
        }
    }

    private void RemoveOldest()
    {
        int last = _entries.Count - 1;
        if (last < 0)
            return;
        Entry e = _entries[last];
        _entries.RemoveAt(last);
        if (e?.View != null)
            Destroy(e.View.gameObject);
    }
}
