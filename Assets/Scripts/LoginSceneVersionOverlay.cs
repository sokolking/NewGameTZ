using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Белая подпись версии (<see cref="Application.version"/>) в правом нижнем углу LoginScene.
/// </summary>
public static class LoginSceneVersionOverlay
{
    const string SceneName = "LoginScene";
    const string RootName = "LoginSceneVersionLabel";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneName)
            return;
        Ensure(scene);
    }

    static void Ensure(Scene scene)
    {
        Canvas canvas = FindCanvasInScene(scene);
        if (canvas == null)
            return;
        if (canvas.transform.Find(RootName) != null)
            return;

        var go = new GameObject(RootName, typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-16f, 16f);
        rt.sizeDelta = new Vector2(320f, 36f);

        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 16;
        text.color = Color.white;
        text.alignment = TextAnchor.LowerRight;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = string.IsNullOrEmpty(Application.version) ? "—" : Application.version;

        go.AddComponent<LoginSceneVersionBringToFront>();
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

    sealed class LoginSceneVersionBringToFront : MonoBehaviour
    {
        void LateUpdate()
        {
            transform.SetAsLastSibling();
            Destroy(this);
        }
    }
}
