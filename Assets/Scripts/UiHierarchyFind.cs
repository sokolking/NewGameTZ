using UnityEngine;

/// <summary>
/// Поиск объектов UI по имени (в т.ч. неактивных), та же логика, что в <see cref="ActionPointsUI"/>.
/// </summary>
public static class UiHierarchyFind
{
    public static T FindChildComponent<T>(Transform root, string childName) where T : Component
    {
        Transform child = FindNamedTransform(childName, root);
        return child != null ? child.GetComponent<T>() : null;
    }

    public static Transform FindNamedTransform(string objectName)
    {
        return FindNamedTransform(objectName, null);
    }

    public static Transform FindNamedTransform(string objectName, Transform root)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        if (root != null)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child.name == objectName)
                    return child;
            }

            return null;
        }

        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || candidate.hideFlags != HideFlags.None)
                continue;
            if (!candidate.gameObject.scene.IsValid())
                continue;
            if (candidate.name == objectName)
                return candidate;
        }

        return null;
    }
}
