#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Создаёт редактируемый префаб плашки урона в Resources/DamagePopup.prefab.</summary>
public static class DamagePopupPrefabCreator
{
    private const string PrefabPath = "Assets/Resources/DamagePopup.prefab";

    /// <summary>Вызывайте из кода при необходимости; в меню Tools только Hope → сцены.</summary>
    public static void CreatePrefab()
    {
        var root = new GameObject("DamagePopup");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        root.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("DamagePopupPanel");
        panel.transform.SetParent(root.transform, false);
        var panelRt = panel.AddComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(220f, 48f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.85f, 0f, 0f, 0.92f);
        panelImg.raycastTarget = false;

        var textGo = new GameObject("DamageText");
        textGo.transform.SetParent(panel.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(6f, 2f);
        textRt.offsetMax = new Vector2(-6f, -2f);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "0";
        tmp.fontSize = 56f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        var view = root.AddComponent<DamagePopupView>();
        var so = new SerializedObject(view);
        so.FindProperty("_damageText").objectReferenceValue = tmp;
        so.FindProperty("_panelRect").objectReferenceValue = panelRt;
        so.FindProperty("_worldOffset").vector3Value = new Vector3(0.9f, 2.1f, 0f);
        so.FindProperty("_leftOffsetFromNameplate").floatValue = 1.85f;
        so.FindProperty("_faceCamera").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EnsureFolder("Assets/Resources");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log($"[DamagePopupPrefabCreator] Saved {PrefabPath}.");
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
