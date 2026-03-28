using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Гарантирует наличие <see cref="ClientUpdateGate"/> на LoginScene, если его не добавили в редакторе.
/// </summary>
static class LoginSceneClientUpdateBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "LoginScene")
            return;
        if (Object.FindFirstObjectByType<ClientUpdateGate>(FindObjectsInactive.Include) != null)
            return;
        Canvas canvas = FindCanvasInScene(scene);
        if (canvas == null)
            return;
        canvas.gameObject.AddComponent<ClientUpdateGate>();
    }

    static Canvas FindCanvasInScene(Scene scene)
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (Canvas c in canvases)
        {
            if (c != null && c.gameObject.scene == scene)
                return c;
        }

        return null;
    }
}
