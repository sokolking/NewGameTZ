using UnityEngine;

/// <summary>
/// Фоновая музыка боя на <c>MainScene</c>: загрузка из <see cref="Resources"/> и воспроизведение в цикле.
/// </summary>
public sealed class BattleSceneMusic : MonoBehaviour
{
    [Tooltip("Resources path without extension, e.g. Audio/battle for Assets/Resources/Audio/battle.mp3")]
    [SerializeField] private string _clipResourcesPath = "Audio/battle";

    [SerializeField] [Range(0f, 1f)] private float _volume = 0.55f;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
        _audioSource.volume = _volume;

        AudioClip clip = Resources.Load<AudioClip>(_clipResourcesPath);
        if (clip == null)
        {
            Debug.LogWarning($"[BattleSceneMusic] Не найден AudioClip в Resources: {_clipResourcesPath}");
            return;
        }

        _audioSource.clip = clip;
        _audioSource.Play();
    }
}
