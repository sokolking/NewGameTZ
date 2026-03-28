#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Создаёт редактируемый префаб планки над головой в Resources/CharacterNameplate.prefab.</summary>
public static class CharacterNameplatePrefabCreator
{
    private const string PrefabPath = "Assets/Resources/CharacterNameplate.prefab";

    [MenuItem("Tools/UI/Create Character Nameplate Prefab (Resources)")]
    public static void CreatePrefab()
    {
        var root = new GameObject("CharacterNameplate");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        root.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("NameplatePanel");
        panel.transform.SetParent(root.transform, false);
        var panelRt = panel.AddComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(220f, 48f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.45f);
        panelImg.raycastTarget = false;

        var nameGo = new GameObject("NameLevelText");
        nameGo.transform.SetParent(panel.transform, false);
        var nameRt = nameGo.AddComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0f, 0.45f);
        nameRt.anchorMax = new Vector2(1f, 1f);
        nameRt.offsetMin = new Vector2(6f, 2f);
        nameRt.offsetMax = new Vector2(-6f, -2f);
        var tmp = nameGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Player [1]";
        tmp.fontSize = 16f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        var hpBgGo = new GameObject("HpBarBackground");
        hpBgGo.transform.SetParent(panel.transform, false);
        var hpBgRt = hpBgGo.AddComponent<RectTransform>();
        hpBgRt.anchorMin = new Vector2(0f, 0.08f);
        hpBgRt.anchorMax = new Vector2(1f, 0.38f);
        hpBgRt.offsetMin = new Vector2(6f, 2f);
        hpBgRt.offsetMax = new Vector2(-6f, -2f);
        var hpBgImg = hpBgGo.AddComponent<Image>();
        hpBgImg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        hpBgImg.raycastTarget = false;

        var hpFillGo = new GameObject("HpFill");
        hpFillGo.transform.SetParent(hpBgGo.transform, false);
        var hpFillRt = hpFillGo.AddComponent<RectTransform>();
        hpFillRt.anchorMin = Vector2.zero;
        hpFillRt.anchorMax = new Vector2(1f, 1f);
        hpFillRt.offsetMin = Vector2.zero;
        hpFillRt.offsetMax = Vector2.zero;
        var hpFillImg = hpFillGo.AddComponent<Image>();
        hpFillImg.color = new Color(0.85f, 0.12f, 0.1f, 1f);
        hpFillImg.raycastTarget = false;

        var view = root.AddComponent<CharacterNameplateView>();
        var so = new SerializedObject(view);
        so.FindProperty("_nameLevelText").objectReferenceValue = tmp;
        so.FindProperty("_hpFillRect").objectReferenceValue = hpFillRt;
        so.FindProperty("_worldOffset").vector3Value = new Vector3(0f, 2.1f, 0f);
        so.FindProperty("_faceCamera").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EnsureFolder("Assets/Resources");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log($"[CharacterNameplatePrefabCreator] Saved {PrefabPath}. Назначьте префаб на Player / RemoteBattleUnitView или оставьте пустым для Resources.");
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
