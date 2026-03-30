using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dynamic fill for <c>UnitCard</c>: <see cref="UnitStatShort"/>, <see cref="UnitStat"/>, <see cref="UnitResource"/> rows.
/// </summary>
public sealed class UnitCardPayload
{
    public string DisplayName = "";
    public int Level = 1;
    public int Strength;
    public int Agility;
    public int Intuition;
    public int Endurance;
    public int Accuracy;
    public int Intellect;
    public int CurrentHp;
    public int MaxHp = 1;
    /// <summary>Server movement fatigue 0..1; remaining AP% for bar = <c>(1 - penalty) * 100</c>.</summary>
    public float PenaltyFraction;
}

[DisallowMultipleComponent]
public sealed class UnitCardView : MonoBehaviour
{
    private static readonly Color HpBarColor = new Color(0.92f, 0.18f, 0.15f, 1f);
    private static readonly Color FatigueBarColor = new Color(0.95f, 0.85f, 0.2f, 1f);

    [SerializeField] private Transform _statsRoot;
    [SerializeField] private Transform _resourcesRoot;
    [SerializeField] private GameObject _unitStatShortPrefab;
    [SerializeField] private GameObject _unitStatPrefab;
    [SerializeField] private GameObject _unitResourcePrefab;

    private readonly List<GameObject> _spawnedRows = new();

    private void Awake() => EnsureRoots();

    private void EnsureRoots()
    {
        if (_statsRoot == null)
        {
            var t = transform.Find("UnitStats");
            if (t != null) _statsRoot = t;
        }

        if (_resourcesRoot == null)
        {
            var t = transform.Find("UnitResources");
            if (t != null) _resourcesRoot = t;
        }
    }

    public void SetVisible(bool visible) => gameObject.SetActive(visible);

    public void Render(UnitCardPayload data)
    {
        if (data == null)
            return;
        EnsureRoots();
        ClearSpawned();
        if (_statsRoot == null || _resourcesRoot == null)
            return;

        string name = string.IsNullOrWhiteSpace(data.DisplayName) ? "Player" : data.DisplayName.Trim();
        int lv = Mathf.Max(1, data.Level);

        if (_unitStatShortPrefab != null)
        {
            var row = Instantiate(_unitStatShortPrefab, _statsRoot);
            _spawnedRows.Add(row);
            var val = FindDeepText(row.transform, "UserStatValue");
            if (val != null)
                val.text = $"{name} [{lv}]";
        }

        void AddStat(string locKey, int value)
        {
            if (_unitStatPrefab == null) return;
            var row = Instantiate(_unitStatPrefab, _statsRoot);
            _spawnedRows.Add(row);
            var keyT = FindDeepText(row.transform, "UserStatKey");
            var valT = FindDeepText(row.transform, "UserStatValue");
            if (keyT != null) keyT.text = Loc.T(locKey);
            if (valT != null) valT.text = Mathf.Max(0, value).ToString();
        }

        AddStat("profile.stat_strength", data.Strength);
        AddStat("profile.stat_agility", data.Agility);
        AddStat("profile.stat_intuition", data.Intuition);
        AddStat("profile.stat_endurance", data.Endurance);
        AddStat("profile.stat_accuracy", data.Accuracy);
        AddStat("profile.stat_intellect", data.Intellect);

        int maxHp = Mathf.Max(1, data.MaxHp);
        int curHp = Mathf.Clamp(data.CurrentHp, 0, maxHp);
        float hpFill = curHp / (float)maxHp;
        AddResourceRow(hpFill, curHp, HpBarColor);

        float pen = Mathf.Clamp01(data.PenaltyFraction);
        float fatigueFill = Mathf.Clamp01(1f - pen);
        int apRemainPct = Mathf.Clamp(Mathf.RoundToInt(fatigueFill * 100f), 0, 100);
        AddResourceRow(fatigueFill, apRemainPct, FatigueBarColor);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_statsRoot as RectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_resourcesRoot as RectTransform);
    }

    private void AddResourceRow(float normalizedFill, int displayNumber, Color fillColor)
    {
        if (_unitResourcePrefab == null) return;
        var row = Instantiate(_unitResourcePrefab, _resourcesRoot);
        _spawnedRows.Add(row);
        var slider = FindDeepSlider(row.transform, "UnitResourceBar");
        var val = FindDeepText(row.transform, "UnitResourceValue");
        var keyT = FindDeepText(row.transform, "UnitResourceKey");
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = normalizedFill;
            var fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            if (fill != null)
                fill.color = fillColor;
        }

        if (val != null)
            val.text = displayNumber.ToString();
    }

    private void ClearSpawned()
    {
        for (int i = 0; i < _spawnedRows.Count; i++)
        {
            if (_spawnedRows[i] != null)
                Destroy(_spawnedRows[i]);
        }

        _spawnedRows.Clear();
    }

    private static Text FindDeepText(Transform root, string childName)
    {
        if (root == null) return null;
        var t = root.Find(childName);
        if (t != null)
        {
            var tx = t.GetComponent<Text>();
            if (tx != null) return tx;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeepText(root.GetChild(i), childName);
            if (found != null) return found;
        }

        return null;
    }

    private static Slider FindDeepSlider(Transform root, string childName)
    {
        if (root == null) return null;
        var t = root.Find(childName);
        if (t != null)
        {
            var s = t.GetComponent<Slider>();
            if (s != null) return s;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeepSlider(root.GetChild(i), childName);
            if (found != null) return found;
        }

        return null;
    }
}
