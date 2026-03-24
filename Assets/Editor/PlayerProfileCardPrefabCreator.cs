#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Создаёт префаб карточки профиля игрока в Resources/PlayerProfileCard.prefab.</summary>
public static class PlayerProfileCardPrefabCreator
{
    private const string PrefabPath = "Assets/Resources/PlayerProfileCard.prefab";

    [MenuItem("Tools/UI/Create Player Profile Card Prefab (Resources)")]
    public static void CreatePrefab()
    {
        var root = new GameObject("PlayerProfileCard", typeof(RectTransform));
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(480f, 280f);

        var bg = root.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);

        var modelAnchorGo = new GameObject("ModelAnchor", typeof(RectTransform));
        modelAnchorGo.transform.SetParent(root.transform, false);
        var modelRt = modelAnchorGo.GetComponent<RectTransform>();
        modelRt.anchorMin = new Vector2(0f, 0f);
        modelRt.anchorMax = new Vector2(0f, 1f);
        modelRt.pivot = new Vector2(0.5f, 0.5f);
        modelRt.sizeDelta = new Vector2(180f, -20f);
        modelRt.anchoredPosition = new Vector2(100f, 0f);

        var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        model.name = "PreviewModel";
        model.transform.SetParent(modelAnchorGo.transform, false);
        model.transform.localScale = new Vector3(60f, 90f, 60f);
        model.transform.localPosition = new Vector3(0f, -20f, 0f);

        var textBlock = new GameObject("TextBlock", typeof(RectTransform));
        textBlock.transform.SetParent(root.transform, false);
        var textBlockRt = textBlock.GetComponent<RectTransform>();
        textBlockRt.anchorMin = new Vector2(0f, 0f);
        textBlockRt.anchorMax = new Vector2(1f, 1f);
        textBlockRt.offsetMin = new Vector2(200f, 16f);
        textBlockRt.offsetMax = new Vector2(-16f, -16f);

        TextMeshProUGUI nick = CreateText("NicknameText", textBlock.transform, "Player", 26f, FontStyles.Bold);
        TextMeshProUGUI level = CreateText("LevelText", textBlock.transform, "Level 1", 20f, FontStyles.Normal);
        TextMeshProUGUI str = CreateText("StrengthText", textBlock.transform, "Сила: 10", 18f, FontStyles.Normal);
        TextMeshProUGUI end = CreateText("EnduranceText", textBlock.transform, "Выносливость: 10", 18f, FontStyles.Normal);
        TextMeshProUGUI acc = CreateText("AccuracyText", textBlock.transform, "Меткость: 10", 18f, FontStyles.Normal);

        LayoutVertical(textBlock.transform, nick.rectTransform, 0f);
        LayoutVertical(textBlock.transform, level.rectTransform, 40f);
        LayoutVertical(textBlock.transform, str.rectTransform, 88f);
        LayoutVertical(textBlock.transform, end.rectTransform, 124f);
        LayoutVertical(textBlock.transform, acc.rectTransform, 160f);

        var view = root.AddComponent<PlayerProfileCardView>();
        var so = new SerializedObject(view);
        so.FindProperty("_nicknameText").objectReferenceValue = nick;
        so.FindProperty("_levelText").objectReferenceValue = level;
        so.FindProperty("_strengthText").objectReferenceValue = str;
        so.FindProperty("_enduranceText").objectReferenceValue = end;
        so.FindProperty("_accuracyText").objectReferenceValue = acc;
        so.FindProperty("_modelAnchor").objectReferenceValue = modelAnchorGo.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        root.AddComponent<PlayerProfileCardController>();

        EnsureFolder("Assets/Resources");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log($"[PlayerProfileCardPrefabCreator] Saved {PrefabPath}");
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void LayoutVertical(Transform parent, RectTransform rt, float top)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, -top - 32f);
        rt.offsetMax = new Vector2(0f, -top);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = "Assets";
        foreach (string part in path.Substring("Assets/".Length).Split('/'))
        {
            string next = parent + "/" + part;
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(parent, part);
            parent = next;
        }
    }
}
#endif
