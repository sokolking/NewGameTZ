using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Full-screen static background for LoginScene and MainMenu (<c>Resources/UI/menu_background_hero</c>).
/// Hosted in <c>DontDestroyOnLoad</c> with its own Canvas (low sorting order) so Login → MainMenu keeps the same image.
/// Destroyed when loading <c>MainScene</c>.
/// </summary>
public static class UiMenuStaticBackgroundSetup
{
    const string ResourceTexturePath = "UI/menu_background_hero";
    public const string RootObjectName = "MenuStaticBackgroundRoot";
    const string PlaceholderObjectName = "MenuBackgroundPlaceholder";
    const string MainBattleSceneName = "MainScene";

    const int MenuCanvasSortOrder = -1000;

    static GameObject _persistentHost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ApplyStartupScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name == MainBattleSceneName)
            return;
        if (scene.name == "LoginScene" || scene.name == "MainMenu")
            EnsureMenuBackground();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive)
            return;

        if (scene.name == MainBattleSceneName)
        {
            DestroyPersistentHost();
            return;
        }

        if (scene.name != "LoginScene" && scene.name != "MainMenu")
            return;

        EnsureMenuBackground();
    }

    static void DestroyPersistentHost()
    {
        if (_persistentHost == null)
            return;
        Object.Destroy(_persistentHost);
        _persistentHost = null;
    }

    static void EnsureMenuBackground()
    {
        if (_persistentHost != null)
        {
            _persistentHost.SetActive(true);
            return;
        }

        var host = new GameObject("MenuStaticBackgroundHost");
        Object.DontDestroyOnLoad(host);
        _persistentHost = host;

        var canvasGo = new GameObject("MenuStaticBackgroundCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(host.transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = MenuCanvasSortOrder;
        canvas.overrideSorting = true;

        StretchFullScreen(canvasGo.GetComponent<RectTransform>());

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject placeholder = CreateBlackPlaceholder(canvas);

        var root = new GameObject(RootObjectName);
        root.transform.SetParent(canvasGo.transform, false);
        var rect = root.AddComponent<RectTransform>();
        StretchFullScreen(rect);

        var canvasGroup = root.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var raw = root.AddComponent<RawImage>();
        raw.color = Color.white;
        raw.raycastTarget = false;

        placeholder.transform.SetAsFirstSibling();
        root.transform.SetSiblingIndex(1);

        var loader = host.AddComponent<MenuBackgroundLoadBehaviour>();
        loader.BeginLoad(ResourceTexturePath, raw, canvasGroup, placeholder, host);
    }

    static GameObject CreateBlackPlaceholder(Canvas canvas)
    {
        var ph = new GameObject(PlaceholderObjectName);
        ph.transform.SetParent(canvas.transform, false);
        var phRect = ph.AddComponent<RectTransform>();
        StretchFullScreen(phRect);
        var img = ph.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        ph.transform.SetAsFirstSibling();
        return ph;
    }

    static void StretchFullScreen(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    sealed class MenuBackgroundLoadBehaviour : MonoBehaviour
    {
        string _path;
        RawImage _raw;
        CanvasGroup _group;
        GameObject _placeholder;
        GameObject _host;

        public void BeginLoad(
            string resourcePath,
            RawImage raw,
            CanvasGroup group,
            GameObject placeholder,
            GameObject host)
        {
            _path = resourcePath;
            _raw = raw;
            _group = group;
            _placeholder = placeholder;
            _host = host;
            StartCoroutine(CoLoad());
        }

        IEnumerator CoLoad()
        {
            ResourceRequest req = Resources.LoadAsync<Texture2D>(_path);
            while (req != null && !req.isDone)
                yield return null;

            var tex = req != null ? req.asset as Texture2D : null;
            if (tex == null)
            {
                Debug.LogWarning(
                    $"UiMenuStaticBackgroundSetup: texture not found. Add PNG at Assets/Resources/{_path}.png");
                if (ReferenceEquals(_persistentHost, _host))
                    _persistentHost = null;
                Destroy(_host);
                yield break;
            }

            _raw.texture = tex;
            if (_group != null)
                _group.alpha = 1f;

            if (_placeholder != null)
            {
                Destroy(_placeholder);
                _placeholder = null;
            }
        }

        void OnDestroy()
        {
            if (_raw != null)
                _raw.texture = null;
        }
    }
}
