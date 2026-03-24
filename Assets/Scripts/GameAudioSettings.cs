using UnityEngine;

/// <summary>
/// Глобальная громкость (множитель <see cref="AudioListener.volume"/>) и отключение звука. PlayerPrefs.
/// </summary>
public static class GameAudioSettings
{
    const string PrefVolume = "GameAudio_MasterVolume";
    const string PrefMute = "GameAudio_MasterMute";

    public static float MasterVolume01 => PlayerPrefs.GetFloat(PrefVolume, 1f);

    public static bool MasterMute => PlayerPrefs.GetInt(PrefMute, 0) == 1;

    public static void SetMasterVolume(float volume01)
    {
        PlayerPrefs.SetFloat(PrefVolume, Mathf.Clamp01(volume01));
        PlayerPrefs.Save();
        Apply();
    }

    public static void SetMasterMute(bool mute)
    {
        PlayerPrefs.SetInt(PrefMute, mute ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    public static void Apply()
    {
        float v = MasterVolume01;
        bool m = MasterMute;
        AudioListener.volume = m ? 0f : v;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Apply();
    }
}
