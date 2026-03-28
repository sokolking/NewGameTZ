#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Создаёт префаб индикатора удержания цели в Resources/HoldTargetIndicator.prefab.</summary>
public static class HoldIndicatorPrefabCreator
{
    private const string PrefabPath = "Assets/Resources/HoldTargetIndicator.prefab";

    [MenuItem("Tools/UI/Create Hold Target Indicator Prefab (Resources)")]
    public static void CreatePrefab()
    {
        var root = new GameObject("HoldTargetIndicator");
        root.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
        var view = root.AddComponent<HoldTargetIndicator>();

        view.EditorRebuildVisuals();

        if (!view.HasValidVisuals)
            Debug.LogWarning("[HoldIndicatorPrefabCreator] Нет спрайта hold_indicator в Resources — префаб будет без графики до добавления ассета.");

        EnsureFolder("Assets/Resources");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log($"[HoldIndicatorPrefabCreator] Saved {PrefabPath}.");
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
