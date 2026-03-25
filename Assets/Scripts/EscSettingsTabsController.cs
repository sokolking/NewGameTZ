using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Вкладки «Аудио» / «Видео» / «Язык» в SettingsPanel (EscScene). Язык: <see cref="EscLanguagePickerUI"/> на LanguagePage
/// (создаётся через Tools → Esc Scene → Setup Language Page + Esc UI localization).
/// </summary>
public sealed class EscSettingsTabsController : MonoBehaviour
{
    [SerializeField] private GameObject _videoPage;
    [SerializeField] private GameObject _audioPage;
    [SerializeField] private GameObject _languagePage;
    [SerializeField] private Button _tabVideoButton;
    [SerializeField] private Button _tabAudioButton;
    [SerializeField] private Button _tabLanguageButton;
    [SerializeField] private Slider _masterVolumeSlider;
    [SerializeField] private Toggle _muteToggle;
    [SerializeField] private EscLanguagePickerUI _languagePicker;

    private void Start()
    {
        CacheRefs();
        WireTabs();
        WireAudio();
        ShowVideoTab();
    }

    private void CacheRefs()
    {
        Transform root = transform;
        if (_videoPage == null)
        {
            var t = FindDeep(root, "VideoPage");
            if (t != null)
                _videoPage = t.gameObject;
        }

        if (_audioPage == null)
        {
            var t = FindDeep(root, "AudioPage");
            if (t != null)
                _audioPage = t.gameObject;
        }

        if (_languagePage == null)
        {
            var t = FindDeep(root, "LanguagePage");
            if (t != null)
                _languagePage = t.gameObject;
        }

        if (_tabVideoButton == null)
            _tabVideoButton = FindDeep<Button>(root, "Button_TabVideo");
        if (_tabAudioButton == null)
            _tabAudioButton = FindDeep<Button>(root, "Button_TabAudio");
        if (_tabLanguageButton == null)
            _tabLanguageButton = FindDeep<Button>(root, "Button_TabLanguage");
        if (_masterVolumeSlider == null)
            _masterVolumeSlider = FindDeep<Slider>(root, "Slider_MasterVolume");
        if (_muteToggle == null)
            _muteToggle = FindDeep<Toggle>(root, "Toggle_MuteAudio");
        if (_languagePicker == null && _languagePage != null)
            _languagePicker = _languagePage.GetComponent<EscLanguagePickerUI>()
                               ?? _languagePage.GetComponentInChildren<EscLanguagePickerUI>(true);
    }

    private void WireTabs()
    {
        if (_tabVideoButton != null)
        {
            _tabVideoButton.onClick.RemoveListener(ShowVideoTab);
            _tabVideoButton.onClick.AddListener(ShowVideoTab);
        }

        if (_tabAudioButton != null)
        {
            _tabAudioButton.onClick.RemoveListener(ShowAudioTab);
            _tabAudioButton.onClick.AddListener(ShowAudioTab);
        }

        if (_tabLanguageButton != null)
        {
            _tabLanguageButton.onClick.RemoveListener(ShowLanguageTab);
            _tabLanguageButton.onClick.AddListener(ShowLanguageTab);
        }
    }

    private void WireAudio()
    {
        if (_masterVolumeSlider != null)
        {
            _masterVolumeSlider.minValue = 0f;
            _masterVolumeSlider.maxValue = 1f;
            _masterVolumeSlider.wholeNumbers = false;
            bool mute = GameAudioSettings.MasterMute;
            float v = GameAudioSettings.MasterVolume01;
            _masterVolumeSlider.SetValueWithoutNotify(mute ? 0f : v);
            _masterVolumeSlider.onValueChanged.RemoveListener(OnVolumeSlider);
            _masterVolumeSlider.onValueChanged.AddListener(OnVolumeSlider);
        }

        if (_muteToggle != null)
        {
            _muteToggle.SetIsOnWithoutNotify(GameAudioSettings.MasterMute);
            _muteToggle.onValueChanged.RemoveListener(OnMuteToggle);
            _muteToggle.onValueChanged.AddListener(OnMuteToggle);
        }

        RefreshInteractable();
    }

    private void OnVolumeSlider(float v)
    {
        GameAudioSettings.SetMasterVolume(v);
        if (_muteToggle != null && _muteToggle.isOn && v > 0f)
        {
            _muteToggle.SetIsOnWithoutNotify(false);
            GameAudioSettings.SetMasterMute(false);
        }

        RefreshInteractable();
    }

    private void OnMuteToggle(bool mute)
    {
        GameAudioSettings.SetMasterMute(mute);
        if (_masterVolumeSlider != null)
        {
            if (mute)
                _masterVolumeSlider.SetValueWithoutNotify(0f);
            else
                _masterVolumeSlider.SetValueWithoutNotify(GameAudioSettings.MasterVolume01);
        }

        RefreshInteractable();
    }

    private void RefreshInteractable()
    {
        if (_masterVolumeSlider != null)
            _masterVolumeSlider.interactable = _muteToggle == null || !_muteToggle.isOn;
    }

    public void ShowVideoTab()
    {
        if (_videoPage != null)
            _videoPage.SetActive(true);
        if (_audioPage != null)
            _audioPage.SetActive(false);
        if (_languagePage != null)
            _languagePage.SetActive(false);
    }

    public void ShowAudioTab()
    {
        if (_videoPage != null)
            _videoPage.SetActive(false);
        if (_audioPage != null)
            _audioPage.SetActive(true);
        if (_languagePage != null)
            _languagePage.SetActive(false);
    }

    public void ShowLanguageTab()
    {
        if (_videoPage != null)
            _videoPage.SetActive(false);
        if (_audioPage != null)
            _audioPage.SetActive(false);
        if (_languagePage != null)
            _languagePage.SetActive(true);
        if (_languagePicker != null)
            _languagePicker.SyncFromLoc();
    }

    private static Transform FindDeep(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrEmpty(objectName))
            return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == objectName)
                return t;
        }

        return null;
    }

    private static T FindDeep<T>(Transform root, string objectName) where T : Component
    {
        var t = FindDeep(root, objectName);
        return t != null ? t.GetComponent<T>() : null;
    }
}
