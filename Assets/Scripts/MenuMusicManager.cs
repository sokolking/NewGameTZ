using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Фоновая музыка для LoginScene и MainMenu: один <see cref="AudioSource"/> на DontDestroyOnLoad,
/// не перезапускается при переходе Login → MainMenu и при additive Esc. Останавливается при входе в бой (MainScene).
/// </summary>
public sealed class MenuMusicManager : MonoBehaviour
{
    public static MenuMusicManager Instance { get; private set; }

    private const string LoginSceneName = "LoginScene";
    private const string MainMenuSceneName = "MainMenu";
    private const string MainBattleSceneName = "MainScene";

    [Tooltip("Resources path without extension, e.g. Audio/MenuMusic for Assets/Resources/Audio/MenuMusic.mp3")]
    [SerializeField] private string _clipResourcesPath = "Audio/MenuMusic";

    [SerializeField] [Range(0f, 1f)] private float _volume = 0.45f;

    private AudioSource _audioSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;
        var go = new GameObject(nameof(MenuMusicManager));
        go.AddComponent<MenuMusicManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.volume = _volume;

        var clip = Resources.Load<AudioClip>(_clipResourcesPath);
        if (clip != null)
            _audioSource.clip = clip;
        else
            Debug.LogWarning($"[MenuMusicManager] Не найден AudioClip в Resources: {_clipResourcesPath}");

        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyForScene(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == MainBattleSceneName)
        {
            StopMenuMusic();
            return;
        }

        if (mode == LoadSceneMode.Additive)
            return;

        if (scene.name == LoginSceneName || scene.name == MainMenuSceneName)
            EnsureMenuMusicPlaying();
    }

    private void ApplyForScene(Scene scene)
    {
        if (!scene.IsValid())
            return;

        if (scene.name == MainBattleSceneName)
            StopMenuMusic();
        else if (scene.name == LoginSceneName || scene.name == MainMenuSceneName)
            EnsureMenuMusicPlaying();
    }

    private void EnsureMenuMusicPlaying()
    {
        if (_audioSource == null || _audioSource.clip == null)
            return;

        _audioSource.volume = _volume;
        if (!_audioSource.isPlaying)
            _audioSource.Play();
    }

    private void StopMenuMusic()
    {
        if (_audioSource == null)
            return;
        _audioSource.Stop();
    }
}
