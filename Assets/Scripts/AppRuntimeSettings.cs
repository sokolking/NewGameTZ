using UnityEngine;

/// <summary>
/// Global runtime flags applied for the whole app lifetime.
/// </summary>
public static class AppRuntimeSettings
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyGlobalRuntimeFlags()
    {
        // Keep game loop and networking alive when app window is unfocused.
        Application.runInBackground = true;
    }
}
