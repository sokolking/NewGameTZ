using UnityEditor;

/// <summary>
/// Mixamo-style transition clips often carry root Y (and small XZ) motion. On a Humanoid with
/// <see cref="UnityEngine.Animator.applyRootMotion"/> off, that still shifts the body up/down vs idle/sit loops,
/// so the figure sinks or floats over the hex. Baking root translation/rotation into the pose fixes it.
/// </summary>
internal sealed class PostureTransitionClipImportPostprocessor : AssetPostprocessor
{
    internal static readonly string[] TrackedPaths =
    {
        "Assets/Art/Player/user_stand_to_sit.fbx",
        "Assets/Art/Player/user_sit_to_stand.fbx"
    };

    private void OnPreprocessAnimation()
    {
        if (!IsTracked(assetPath))
            return;

        var modelImporter = assetImporter as ModelImporter;
        if (modelImporter == null)
            return;

        ModelImporterClipAnimation[] clips = modelImporter.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
            return;

        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation c = clips[i];
            c.lockRootHeightY = true;
            c.lockRootRotation = true;
            c.lockRootPositionXZ = true;
            clips[i] = c;
        }

        modelImporter.clipAnimations = clips;
    }

    private static bool IsTracked(string path)
    {
        for (int i = 0; i < TrackedPaths.Length; i++)
        {
            if (path == TrackedPaths[i])
                return true;
        }

        return false;
    }
}

internal static class PostureTransitionClipImportMenu
{
    [MenuItem("Hope/Animation/Reimport posture transition clips")]
    private static void Reimport()
    {
        foreach (string path in PostureTransitionClipImportPostprocessor.TrackedPaths)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                continue;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            UnityEngine.Debug.Log($"[Hope] Reimported {path} (root baked into pose for posture transitions).", null);
        }
    }
}
