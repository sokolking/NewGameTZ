using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Логика главного меню: Новая игра, Настройки, Выход, разрешение экрана.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Сцены")]
    [SerializeField] private string _gameSceneName = "MainScene";

    [Header("Панель настроек")]
    [SerializeField] private GameObject _settingsPanel;

    [Header("Разрешение экрана")]
    [SerializeField] private Text _resolutionText;

    [Header("Матчмейкинг")]
    [SerializeField] private MainMenuMatchmaking _matchmaking;

    [Header("Авторизация")]
    [Tooltip("Назначь в сцене (имя AuthPanel/LoginInputField). Создаётся Tools → Hex Grid → Setup Main Menu UI / Add Main Menu Auth Panel.")]
    [SerializeField] private InputField _loginInputField;
    [Tooltip("Назначь в сцене (AuthPanel/PasswordInputField).")]
    [SerializeField] private InputField _passwordInputField;

    [Header("Режим игры")]
    [Tooltip("Включено — соло-бой на сервере (1 игрок + моб). Выключено — поиск живого соперника (PvP). Назначь Toggle в сцене (имя Toggle_SoloVsMonster или Toggle_SinglePlayer).")]
    [SerializeField] private Toggle _soloVsMonsterToggle;

    [Header("Сервер")]
    [Tooltip("Включено: http://localhost:5000. Выключено: " + BattleServerRuntime.ProductionBaseUrl + ". Только ссылка из сцены (имя Toggle_Debug).")]
    [SerializeField] private Toggle _debugLocalhostToggle;

    // Кнопки меню (находим по имени).
    private Button _btnFindGame;
    private Button _btnSettings;
    private Button _btnQuit;
    private Button _btnCloseSettings;
    private Button _btnResPrev;
    private Button _btnResNext;
    private Button _btnResApply;

    private Resolution[] _availableResolutions;
    private int _currentResolutionIndex;

    private void Start()
    {
        // Найти кнопки по именам в иерархии под этим объектом.
        CacheButtons();
        WireDebugServerToggle();
        WireButtonEvents();

        InitResolutions();
        UpdateResolutionLabel();
    }

    private void CacheButtons()
    {
        _btnFindGame = transform.Find("Button_FindGame")?.GetComponent<Button>();
        if (_btnFindGame == null) _btnFindGame = transform.Find("Button_NewGame")?.GetComponent<Button>();
        _btnSettings = transform.Find("Button_Settings")?.GetComponent<Button>();
        _btnQuit = transform.Find("Button_Quit")?.GetComponent<Button>();
        if (_loginInputField == null)
            _loginInputField = transform.Find("AuthPanel/LoginInputField")?.GetComponent<InputField>();
        if (_passwordInputField == null)
            _passwordInputField = transform.Find("AuthPanel/PasswordInputField")?.GetComponent<InputField>();

        // SettingsPanel предположительно лежит рядом с MainMenuUI под Canvas.
        if (_settingsPanel == null)
        {
            Transform sp = transform.parent != null ? transform.parent.Find("SettingsPanel") : null;
            if (sp != null) _settingsPanel = sp.gameObject;
        }

        if (_settingsPanel != null)
        {
            _btnCloseSettings = _settingsPanel.transform.Find("Button_CloseSettings")?.GetComponent<Button>();
            _btnResPrev = _settingsPanel.transform.Find("Button_ResolutionPrev")?.GetComponent<Button>();
            _btnResNext = _settingsPanel.transform.Find("Button_ResolutionNext")?.GetComponent<Button>();
            _btnResApply = _settingsPanel.transform.Find("Button_ResolutionApply")?.GetComponent<Button>();
            if (_resolutionText == null)
            {
                _resolutionText = _settingsPanel.transform.Find("ResolutionText")?.GetComponent<Text>();
            }
        }

        if (_soloVsMonsterToggle == null)
            _soloVsMonsterToggle = transform.Find("Toggle_SoloVsMonster")?.GetComponent<Toggle>();
        if (_soloVsMonsterToggle == null)
            _soloVsMonsterToggle = transform.Find("Toggle_SinglePlayer")?.GetComponent<Toggle>();

        if (_debugLocalhostToggle == null)
            _debugLocalhostToggle = transform.Find("Toggle_Debug")?.GetComponent<Toggle>();
    }

    private void WireDebugServerToggle()
    {
        if (_debugLocalhostToggle == null)
            return;

        _debugLocalhostToggle.SetIsOnWithoutNotify(BattleServerRuntime.UseDebugLocalhost);
        _debugLocalhostToggle.onValueChanged.RemoveListener(OnDebugLocalhostToggleChanged);
        _debugLocalhostToggle.onValueChanged.AddListener(OnDebugLocalhostToggleChanged);
    }

    private static void OnDebugLocalhostToggleChanged(bool useLocalhost)
    {
        BattleServerRuntime.UseDebugLocalhost = useLocalhost;
    }

    private void WireButtonEvents()
    {
        if (_btnFindGame != null)
        {
            _btnFindGame.onClick.RemoveAllListeners();
            _btnFindGame.onClick.AddListener(OnFindGameClicked);
        }
        if (_btnSettings != null)
        {
            _btnSettings.onClick.RemoveAllListeners();
            _btnSettings.onClick.AddListener(OnSettingsClicked);
        }
        if (_btnQuit != null)
        {
            _btnQuit.onClick.RemoveAllListeners();
            _btnQuit.onClick.AddListener(OnQuitClicked);
        }
        if (_btnCloseSettings != null)
        {
            _btnCloseSettings.onClick.RemoveAllListeners();
            _btnCloseSettings.onClick.AddListener(OnCloseSettingsClicked);
        }
        if (_btnResPrev != null)
        {
            _btnResPrev.onClick.RemoveAllListeners();
            _btnResPrev.onClick.AddListener(OnResolutionPrevious);
        }
        if (_btnResNext != null)
        {
            _btnResNext.onClick.RemoveAllListeners();
            _btnResNext.onClick.AddListener(OnResolutionNext);
        }
        if (_btnResApply != null)
        {
            _btnResApply.onClick.RemoveAllListeners();
            _btnResApply.onClick.AddListener(OnApplyResolution);
        }
    }

    private void InitResolutions()
    {
        _availableResolutions = Screen.resolutions;
        if (_availableResolutions == null || _availableResolutions.Length == 0)
        {
            // Fallback без задания частоты обновления, чтобы не трогать устаревший refreshRate.
            _availableResolutions = new[]
            {
                new Resolution { width = 1280, height = 720 },
                new Resolution { width = 1920, height = 1080 },
            };
        }

        // Найти ближайшее к текущему разрешение.
        Resolution current = Screen.currentResolution;
        _currentResolutionIndex = 0;
        float bestScore = float.MaxValue;
        for (int i = 0; i < _availableResolutions.Length; i++)
        {
            float dw = _availableResolutions[i].width - current.width;
            float dh = _availableResolutions[i].height - current.height;
            float score = dw * dw + dh * dh;
            if (score < bestScore)
            {
                bestScore = score;
                _currentResolutionIndex = i;
            }
        }
    }

    private void UpdateResolutionLabel()
    {
        if (_resolutionText == null || _availableResolutions == null || _availableResolutions.Length == 0)
            return;

        Resolution r = _availableResolutions[_currentResolutionIndex];
        _resolutionText.text = $"{r.width} x {r.height}";
    }

    public void OnResolutionNext()
    {
        if (_availableResolutions == null || _availableResolutions.Length == 0) return;
        _currentResolutionIndex = (_currentResolutionIndex + 1) % _availableResolutions.Length;
        UpdateResolutionLabel();
    }

    public void OnResolutionPrevious()
    {
        if (_availableResolutions == null || _availableResolutions.Length == 0) return;
        _currentResolutionIndex--;
        if (_currentResolutionIndex < 0) _currentResolutionIndex = _availableResolutions.Length - 1;
        UpdateResolutionLabel();
    }

    public void OnApplyResolution()
    {
        if (_availableResolutions == null || _availableResolutions.Length == 0) return;
        Resolution r = _availableResolutions[_currentResolutionIndex];
        // Используем overload без частоты кадров, чтобы не обращаться к устаревшему refreshRate.
        Screen.SetResolution(r.width, r.height, Screen.fullScreenMode);
    }

    /// <summary>Find Game: встать в очередь на сервере; при старте боя загружается игровая сцена.</summary>
    public void OnFindGameClicked()
    {
        bool singlePlayer = _soloVsMonsterToggle != null && _soloVsMonsterToggle.isOn;
        string username = GetLoginValue();
        string password = GetPasswordValue();
        
        GameModeState.SetSinglePlayer(singlePlayer);

        if (singlePlayer)
        {
            // Одиночный режим теперь тоже использует серверный бой (1 игрок + серверный моб),
            // чтобы логика боя была общей с онлайн-режимом.
            if (_matchmaking != null)
            {
                _matchmaking.StartSinglePlayerServerBattle(username, password);
                return;
            }
            // Fallback: если матчмейкинг не настроен, старое поведение — просто загрузить сцену.
            if (!string.IsNullOrEmpty(_gameSceneName))
                SceneManager.LoadScene(_gameSceneName);
            return;
        }

        if (_matchmaking != null)
        {
            _matchmaking.FindGame(username, password);
            return;
        }
        if (!string.IsNullOrEmpty(_gameSceneName))
            SceneManager.LoadScene(_gameSceneName);
    }

    public void OnSettingsClicked()
    {
        if (_settingsPanel != null)
            _settingsPanel.SetActive(true);
    }

    public void OnCloseSettingsClicked()
    {
        if (_settingsPanel != null)
            _settingsPanel.SetActive(false);
    }

    public void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private string GetLoginValue()
    {
        return _loginInputField != null && !string.IsNullOrWhiteSpace(_loginInputField.text)
            ? _loginInputField.text.Trim()
            : "test";
    }

    private string GetPasswordValue()
    {
        return _passwordInputField != null && !string.IsNullOrEmpty(_passwordInputField.text)
            ? _passwordInputField.text
            : "test";
    }
}


