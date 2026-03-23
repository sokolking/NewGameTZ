using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// При загрузке LoginScene и MainMenu добавляет полноэкранный зацикленный видеофон
/// (<c>Resources/Video/tz_background</c> или <c>StreamingAssets/tz_background.mp4</c>).
/// До первого кадра показывается чёрный плейсхолдер, чтобы меню не мигало «без фона».
/// </summary>
public static class UiLoopingVideoBackgroundSetup
{
    const string ResourceClipPath = "Video/tz_background";
    const string StreamingFileName = "tz_background.mp4";
    public const string RootObjectName = "VideoBackgroundRoot";
    const string PlaceholderObjectName = "VideoBackgroundPlaceholder";

    static ResourceRequest _clipWarmup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void WarmupClip()
    {
        _clipWarmup = Resources.LoadAsync<VideoClip>(ResourceClipPath);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "LoginScene" && scene.name != "MainMenu")
            return;
        EnsureVideoBackground(scene);
    }

    static void EnsureVideoBackground(Scene scene)
    {
        Canvas canvas = FindCanvasInScene(scene);
        if (canvas == null)
            return;

        if (canvas.transform.Find(RootObjectName) != null)
            return;

        GameObject placeholder = CreateBlackPlaceholder(canvas);

        var root = new GameObject(RootObjectName);
        root.transform.SetParent(canvas.transform, false);
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

        var vp = root.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = true;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.sendFrameReadyEvents = true;
        // Иначе при соотношении сторон экрана ≠ ролика (например 16:10 vs 16:9) Unity «заполняет» RT с обрезкой краёв.
        vp.aspectRatio = VideoAspectRatio.Stretch;

        int w = Mathf.Max(256, Screen.width);
        int h = Mathf.Max(256, Screen.height);
        var renderTex = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        renderTex.name = "VideoBackgroundRT";
        renderTex.Create();
        vp.targetTexture = renderTex;
        raw.texture = renderTex;

        var holder = root.AddComponent<VideoBackgroundHolder>();
        holder.Init(renderTex, vp, placeholder, canvasGroup);

        VideoClip clip = null;
        if (_clipWarmup != null && _clipWarmup.isDone)
            clip = _clipWarmup.asset as VideoClip;
        if (clip == null)
            clip = Resources.Load<VideoClip>(ResourceClipPath);

        if (clip != null)
        {
            vp.source = VideoSource.VideoClip;
            vp.clip = clip;
            holder.BeginPlaybackFromClip();
            return;
        }

        string streamingPath = Path.Combine(Application.streamingAssetsPath, StreamingFileName);
        if (!File.Exists(streamingPath))
        {
            Debug.LogWarning(
                $"UiLoopingVideoBackgroundSetup: не найдено видео. Ожидалось Resources/{ResourceClipPath} или StreamingAssets/{StreamingFileName}.");
            holder.ReleaseWithoutDestroy();
            UnityEngine.Object.Destroy(placeholder);
            UnityEngine.Object.Destroy(root);
            return;
        }

        vp.source = VideoSource.Url;
        vp.url = ToVideoUrl(streamingPath);
        holder.BeginPlaybackFromUrl();
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

    static string ToVideoUrl(string absolutePath)
    {
        absolutePath = absolutePath.Replace('\\', '/');
#if UNITY_ANDROID && !UNITY_EDITOR
        return absolutePath;
#else
        if (absolutePath.StartsWith("file://", System.StringComparison.OrdinalIgnoreCase))
            return absolutePath;
        return "file://" + absolutePath;
#endif
    }

    static Canvas FindCanvasInScene(Scene scene)
    {
        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (Canvas c in canvases)
        {
            if (c != null && c.gameObject.scene == scene)
                return c;
        }

        return null;
    }

    static void StretchFullScreen(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    sealed class VideoBackgroundHolder : MonoBehaviour
    {
        RenderTexture _rt;
        VideoPlayer _vp;
        GameObject _placeholder;
        CanvasGroup _canvasGroup;
        bool _revealed;
        Coroutine _fallbackReveal;

        public void Init(RenderTexture rt, VideoPlayer vp, GameObject placeholder, CanvasGroup canvasGroup)
        {
            _rt = rt;
            _vp = vp;
            _placeholder = placeholder;
            _canvasGroup = canvasGroup;
        }

        public void ReleaseWithoutDestroy()
        {
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }

            _vp = null;
        }

        public void BeginPlaybackFromClip()
        {
            if (_vp == null)
                return;
            RegisterFirstFrameReveal();
            _vp.Play();
        }

        public void BeginPlaybackFromUrl()
        {
            if (_vp == null)
                return;
            RegisterFirstFrameReveal();
            _vp.prepareCompleted += OnPrepareCompleted;
            _vp.Prepare();
        }

        void RegisterFirstFrameReveal()
        {
            _vp.frameReady += OnFrameReady;
            _fallbackReveal = StartCoroutine(CoFallbackRevealAfterDelay(4f));
        }

        IEnumerator CoFallbackRevealAfterDelay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (!_revealed)
                RevealVideo();
        }

        void OnPrepareCompleted(VideoPlayer source)
        {
            if (source != null)
            {
                source.prepareCompleted -= OnPrepareCompleted;
                source.Play();
            }
        }

        void OnFrameReady(VideoPlayer source, long frameIdx)
        {
            if (source != null)
                source.frameReady -= OnFrameReady;
            RevealVideo();
        }

        void RevealVideo()
        {
            if (_revealed)
                return;
            _revealed = true;

            if (_fallbackReveal != null)
            {
                StopCoroutine(_fallbackReveal);
                _fallbackReveal = null;
            }

            if (_canvasGroup != null)
                _canvasGroup.alpha = 1f;

            if (_placeholder != null)
            {
                UnityEngine.Object.Destroy(_placeholder);
                _placeholder = null;
            }
        }

        void OnDestroy()
        {
            if (_vp != null)
            {
                _vp.prepareCompleted -= OnPrepareCompleted;
                _vp.frameReady -= OnFrameReady;
                _vp.Stop();
                _vp.targetTexture = null;
            }

            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }
    }
}
