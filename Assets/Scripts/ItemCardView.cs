using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ItemCardView : MonoBehaviour
{
    [SerializeField] Transform _statsRoot;
    [SerializeField] GameObject _itemStatShortPrefab;
    [SerializeField] GameObject _itemStatPrefab;

    readonly List<GameObject> _spawnedRows = new();

    void Awake()
    {
        EnsureRoot();
    }

    void EnsureRoot()
    {
        if (_statsRoot == null)
            _statsRoot = transform.Find("ItemStats");
        if (_statsRoot == null)
            _statsRoot = FindChildRecursive(transform, "ItemStats");
        if (_statsRoot == null)
        {
            var go = new GameObject("ItemStats", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(transform, false);
            _statsRoot = go.transform;
        }

    }

    public void SetVisible(bool visible) => gameObject.SetActive(visible);

    public void Clear()
    {
        for (int i = 0; i < _spawnedRows.Count; i++)
        {
            if (_spawnedRows[i] != null)
                Destroy(_spawnedRows[i]);
        }

        _spawnedRows.Clear();

        if (_statsRoot == null)
            return;
    }

    public void AddItemStatShort(string key)
    {
        EnsureRoot();
        if (_statsRoot == null)
            return;

        var row = InstantiateRow(_itemStatShortPrefab, "ItemStatShort");
        var keyText = FindDeepText(row.transform, "ItemValue");
        if (keyText == null)
            keyText = FindDeepText(row.transform, "ItemStatKey");
        if (keyText != null)
            keyText.text = key ?? "";
    }

    public void AddItemStat(string key, string value)
    {
        EnsureRoot();
        if (_statsRoot == null)
            return;

        var row = InstantiateRow(_itemStatPrefab, "ItemStat");
        var keyText = FindDeepText(row.transform, "ItemStatKey");
        var valueText = FindDeepText(row.transform, "ItemStatValue");
        if (keyText != null)
            keyText.text = key ?? "";
        if (valueText != null)
            valueText.text = value ?? "";
    }

    GameObject InstantiateRow(GameObject prefab, string fallbackName)
    {
        GameObject row = null;
        if (prefab != null)
            row = Instantiate(prefab, _statsRoot, false);
        else
            row = CreateFallbackRow(fallbackName);

        row.name = fallbackName;
        row.SetActive(true);
        DisableRaycasts(row);
        _spawnedRows.Add(row);
        return row;
    }

    GameObject CreateFallbackRow(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(_statsRoot, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 20f;
        le.minHeight = 20f;

        if (name == "ItemStatShort")
        {
            CreateTextChild(go.transform, "ItemValue");
            return go;
        }

        CreateTextChild(go.transform, "ItemStatKey");
        CreateTextChild(go.transform, "ItemStatValue");
        return go;
    }

    static void CreateTextChild(Transform parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(Text));
        child.transform.SetParent(parent, false);
        var text = child.GetComponent<Text>();
        text.text = "";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    static Text FindDeepText(Transform root, string childName)
    {
        if (root == null)
            return null;
        var t = root.Find(childName);
        if (t != null)
        {
            var tx = t.GetComponent<Text>();
            if (tx != null)
                return tx;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeepText(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    static void DisableRaycasts(GameObject row)
    {
        if (row == null)
            return;
        var graphics = row.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = false;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
            return null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == name)
                return c;
            var found = FindChildRecursive(c, name);
            if (found != null)
                return found;
        }

        return null;
    }
}
