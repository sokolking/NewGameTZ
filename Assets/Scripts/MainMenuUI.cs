using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Логика главного меню: Новая игра, Настройки, Выход, разрешение экрана.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string _gameSceneName = "MainScene";
    [SerializeField] private string _loginSceneName = "LoginScene";

    [Header("Settings panel")]
    [SerializeField] private GameObject _settingsPanel;

    [Header("Screen resolution")]
    [SerializeField] private Text _resolutionText;

    [Header("Matchmaking")]
    [SerializeField] private MainMenuMatchmaking _matchmaking;
    [Tooltip("Optional legacy: dropdown when MatchTypes toggles are not used.")]
    [SerializeField] private Dropdown _pvpMatchmakingModeDropdown;
    [Tooltip("Exclusive PvP modes under MatchTypes (Toggle_1v1, Toggle_3v3, Toggle_5v5). On = join socket queue; off others; off all = leave queue.")]
    [SerializeField] private Toggle _matchTypeToggle1v1;
    [SerializeField] private Toggle _matchTypeToggle3v3;
    [SerializeField] private Toggle _matchTypeToggle5v5;

    [Header("Auth")]
    [Tooltip("Assign in scene (AuthPanel/LoginInputField). Created via Tools → Hex Grid → Setup Main Menu UI / Add Main Menu Auth Panel.")]
    [SerializeField] private InputField _loginInputField;
    [Tooltip("Assign in scene (AuthPanel/PasswordInputField).")]
    [SerializeField] private InputField _passwordInputField;

    [Header("Game mode")]
    [Tooltip("On: solo battle on server (1 player + mob). Off: PvP matchmaking. Assign Toggle in scene (Toggle_SoloVsMonster or Toggle_SinglePlayer).")]
    [SerializeField] private Toggle _soloVsMonsterToggle;

    [Header("Server")]
    [Tooltip("On: http://localhost:5000. Off: " + BattleServerRuntime.ProductionBaseUrl + ". Wire from scene (Toggle_Debug).")]
    [SerializeField] private Toggle _debugLocalhostToggle;

    // Кнопки меню (находим по имени).
    private Button _btnFindGame;
    private Button _btnSettings;
    private Button _btnQuit;
    private Button _btnLogout;
    private Button _btnCloseSettings;
    private Button _btnResPrev;
    private Button _btnResNext;
    private Button _btnResApply;

    private Resolution[] _availableResolutions;
    private int _currentResolutionIndex;
    private bool _suppressMatchTypeToggleEvents;

    private void Start()
    {
        // Найти кнопки по именам в иерархии под этим объектом.
        CacheButtons();
        EnsureLogoutButtonLabel();
        if (!string.IsNullOrEmpty(BattleSessionState.LastUsername) && _loginInputField != null)
            _loginInputField.text = BattleSessionState.LastUsername;
        WireDebugServerToggle();
        ApplySoloToggleFromSavedState();
        if (_matchmaking == null)
            _matchmaking = GetComponent<MainMenuMatchmaking>();
        if (_matchmaking != null)
        {
            _matchmaking.SetMatchTypeTogglesResetHandler(UncheckAllMatchTypeTogglesNoCallbacks);
            _matchmaking.SetSocketJoinOnlyAfterMatchTypeToggle(HasMatchTypeToggles());
        }

        if (HasMatchTypeToggles())
        {
            UncheckAllMatchTypeTogglesNoCallbacks();
            WireMatchTypeToggles();
        }
        else
            WirePvpMatchmakingDropdown();
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
        _btnLogout = transform.Find("Button_LogOut")?.GetComponent<Button>();
        if (_btnLogout == null)
            _btnLogout = FindDeepChild(transform, "Button_LogOut")?.GetComponent<Button>();
        if (_btnLogout == null)
            _btnLogout = FindDeepChild(transform.root, "Button_LogOut")?.GetComponent<Button>();
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
            Transform sp = _settingsPanel.transform;
            _btnCloseSettings = FindDeepChild(sp, "Button_CloseSettings")?.GetComponent<Button>();
            _btnResPrev = FindDeepChild(sp, "Button_ResolutionPrev")?.GetComponent<Button>();
            _btnResNext = FindDeepChild(sp, "Button_ResolutionNext")?.GetComponent<Button>();
            _btnResApply = FindDeepChild(sp, "Button_ResolutionApply")?.GetComponent<Button>();
            if (_resolutionText == null)
                _resolutionText = FindDeepChild(sp, "ResolutionText")?.GetComponent<Text>();
        }

        if (_soloVsMonsterToggle == null)
            _soloVsMonsterToggle = transform.Find("Toggle_SoloVsMonster")?.GetComponent<Toggle>();
        if (_soloVsMonsterToggle == null)
            _soloVsMonsterToggle = transform.Find("Toggle_SinglePlayer")?.GetComponent<Toggle>();

        if (_debugLocalhostToggle == null)
            _debugLocalhostToggle = transform.Find("Toggle_Debug")?.GetComponent<Toggle>();
        if (_pvpMatchmakingModeDropdown == null)
            _pvpMatchmakingModeDropdown = transform.Find("Dropdown_PvpMatchmakingMode")?.GetComponent<Dropdown>();

        Transform matchTypes = transform.Find("MatchTypes");
        if (matchTypes == null && transform.parent != null)
            matchTypes = FindDeepChild(transform.parent, "MatchTypes");
        if (matchTypes != null)
        {
            if (_matchTypeToggle1v1 == null)
                _matchTypeToggle1v1 = FindDeepChild(matchTypes, "Toggle_1v1")?.GetComponent<Toggle>();
            if (_matchTypeToggle3v3 == null)
                _matchTypeToggle3v3 = FindDeepChild(matchTypes, "Toggle_3v3")?.GetComponent<Toggle>();
            if (_matchTypeToggle5v5 == null)
                _matchTypeToggle5v5 = FindDeepChild(matchTypes, "Toggle_5v5")?.GetComponent<Toggle>();
        }
    }

    private bool HasMatchTypeToggles() =>
        _matchTypeToggle1v1 != null && _matchTypeToggle3v3 != null && _matchTypeToggle5v5 != null;

    private bool AnyMatchTypeToggleOn() =>
        (_matchTypeToggle1v1 != null && _matchTypeToggle1v1.isOn)
        || (_matchTypeToggle3v3 != null && _matchTypeToggle3v3.isOn)
        || (_matchTypeToggle5v5 != null && _matchTypeToggle5v5.isOn);

    private void UncheckAllMatchTypeTogglesNoCallbacks()
    {
        _suppressMatchTypeToggleEvents = true;
        _matchTypeToggle1v1?.SetIsOnWithoutNotify(false);
        _matchTypeToggle3v3?.SetIsOnWithoutNotify(false);
        _matchTypeToggle5v5?.SetIsOnWithoutNotify(false);
        _suppressMatchTypeToggleEvents = false;
    }

    private void SetExclusiveMatchTypeOn(PvpMatchmakingMode mode)
    {
        _suppressMatchTypeToggleEvents = true;
        if (_matchTypeToggle1v1 != null)
            _matchTypeToggle1v1.SetIsOnWithoutNotify(mode == PvpMatchmakingMode.Pvp1v1);
        if (_matchTypeToggle3v3 != null)
            _matchTypeToggle3v3.SetIsOnWithoutNotify(mode == PvpMatchmakingMode.Pvp3v3);
        if (_matchTypeToggle5v5 != null)
            _matchTypeToggle5v5.SetIsOnWithoutNotify(mode == PvpMatchmakingMode.Pvp5v5);
        _suppressMatchTypeToggleEvents = false;
    }

    private Toggle ToggleForMode(PvpMatchmakingMode mode) => mode switch
    {
        PvpMatchmakingMode.Pvp1v1 => _matchTypeToggle1v1,
        PvpMatchmakingMode.Pvp3v3 => _matchTypeToggle3v3,
        PvpMatchmakingMode.Pvp5v5 => _matchTypeToggle5v5,
        _ => _matchTypeToggle1v1
    };

    private void WireMatchTypeToggles()
    {
        if (_matchmaking == null)
            _matchmaking = GetComponent<MainMenuMatchmaking>();
        if (!HasMatchTypeToggles() || _matchmaking == null)
            return;

        void WireOne(Toggle t, PvpMatchmakingMode mode)
        {
            if (t == null)
                return;
            t.onValueChanged.RemoveAllListeners();
            t.onValueChanged.AddListener(isOn => OnMatchTypeToggleChanged(mode, isOn));
        }

        WireOne(_matchTypeToggle1v1, PvpMatchmakingMode.Pvp1v1);
        WireOne(_matchTypeToggle3v3, PvpMatchmakingMode.Pvp3v3);
        WireOne(_matchTypeToggle5v5, PvpMatchmakingMode.Pvp5v5);
    }

    private void OnMatchTypeToggleChanged(PvpMatchmakingMode mode, bool isOn)
    {
        if (_suppressMatchTypeToggleEvents)
            return;
        if (_matchmaking == null)
            _matchmaking = GetComponent<MainMenuMatchmaking>();
        if (_matchmaking == null)
            return;

        if (isOn)
        {
            if (_soloVsMonsterToggle != null && _soloVsMonsterToggle.isOn)
            {
                _suppressMatchTypeToggleEvents = true;
                ToggleForMode(mode)?.SetIsOnWithoutNotify(false);
                _suppressMatchTypeToggleEvents = false;
                _matchmaking.ShowMenuStatus(Loc.T("menu.matchmaking_disabled_in_solo"));
                return;
            }

            SetExclusiveMatchTypeOn(mode);
            PersistGameplayTogglesForFindGame();
            _matchmaking.RequestSocketMatchmakingForMode(mode);
            return;
        }

        if (!AnyMatchTypeToggleOn())
            _matchmaking.StopSocketMatchmakingFromUi(true);
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

    /// <summary>После входа из LoginScene состояние соло хранится в <see cref="GameModeState"/>.</summary>
    private void ApplySoloToggleFromSavedState()
    {
        if (_soloVsMonsterToggle != null)
            _soloVsMonsterToggle.SetIsOnWithoutNotify(GameModeState.IsSinglePlayer);
    }

    /// <summary>
    /// Сохраняет Toggle_SoloVsMonster и Toggle_Debug (PlayerPrefs) и применяет перед поиском матча.
    /// Вызывается при нажатии Find Game. Если галка не задана в сцене — ранее сохранённое значение не затираем.
    /// </summary>
    private void PersistGameplayTogglesForFindGame()
    {
        if (_soloVsMonsterToggle != null)
            GameModeState.SetSinglePlayer(_soloVsMonsterToggle.isOn);
        if (_debugLocalhostToggle != null)
            BattleServerRuntime.UseDebugLocalhost = _debugLocalhostToggle.isOn;
    }

    private void ApplyPvpMatchmakingModeFromDropdown()
    {
        if (_matchmaking == null || _pvpMatchmakingModeDropdown == null)
            return;
        _matchmaking.SetPvpMatchmakingMode((PvpMatchmakingMode)Mathf.Clamp(_pvpMatchmakingModeDropdown.value, 0, 2));
    }

    private void WirePvpMatchmakingDropdown()
    {
        if (_matchmaking == null)
            _matchmaking = GetComponent<MainMenuMatchmaking>();
        if (_matchmaking == null || _pvpMatchmakingModeDropdown == null)
            return;

        var dd = _pvpMatchmakingModeDropdown;
        dd.ClearOptions();
        dd.AddOptions(new List<Dropdown.OptionData>
        {
            new(Loc.T("menu.matchmaking_mode_option_1v1")),
            new(Loc.T("menu.matchmaking_mode_option_3v3")),
            new(Loc.T("menu.matchmaking_mode_option_5v5"))
        });

        dd.onValueChanged.RemoveListener(OnPvpMatchmakingDropdownChanged);
        dd.onValueChanged.AddListener(OnPvpMatchmakingDropdownChanged);

        int current = Mathf.Clamp((int)_matchmaking.GetPvpMatchmakingMode(), 0, 2);
        dd.SetValueWithoutNotify(current);
        _matchmaking.SetPvpMatchmakingMode((PvpMatchmakingMode)current);
    }

    private void OnPvpMatchmakingDropdownChanged(int index)
    {
        if (_matchmaking == null)
            return;
        _matchmaking.SetPvpMatchmakingMode((PvpMatchmakingMode)Mathf.Clamp(index, 0, 2));
    }

    /// <summary>Sync mode + optional MatchTypes / dropdown (does not start socket search).</summary>
    private void ApplyPvpModeIndexFromUi(int index)
    {
        if (_matchmaking == null)
            return;
        index = Mathf.Clamp(index, 0, 2);
        _matchmaking.SetPvpMatchmakingMode((PvpMatchmakingMode)index);
        if (HasMatchTypeToggles())
            SetExclusiveMatchTypeOn((PvpMatchmakingMode)index);
        else if (_pvpMatchmakingModeDropdown != null)
            _pvpMatchmakingModeDropdown.SetValueWithoutNotify(index);
    }

    /// <summary>Wire to UI Button for 1v1 queue (updates mode label and dropdown if present).</summary>
    public void UiSelectPvpMode1v1() => ApplyPvpModeIndexFromUi(0);

    /// <summary>Wire to UI Button for 3v3 queue.</summary>
    public void UiSelectPvpMode3v3() => ApplyPvpModeIndexFromUi(1);

    /// <summary>Wire to UI Button for 5v5 queue.</summary>
    public void UiSelectPvpMode5v5() => ApplyPvpModeIndexFromUi(2);

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
        if (_btnLogout != null)
        {
            _btnLogout.onClick.RemoveAllListeners();
            _btnLogout.onClick.AddListener(OnLogoutClicked);
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
        PersistGameplayTogglesForFindGame();
        bool singlePlayer = GameModeState.IsSinglePlayer;

        if (singlePlayer)
        {
            // Одиночный режим теперь тоже использует серверный бой (1 игрок + серверный моб),
            // чтобы логика боя была общей с онлайн-режимом.
            if (_matchmaking != null)
            {
                _matchmaking.StartSinglePlayerServerBattle();
                return;
            }
            // Fallback: если матчмейкинг не настроен, старое поведение — просто загрузить сцену.
            if (!string.IsNullOrEmpty(_gameSceneName))
                SceneManager.LoadScene(_gameSceneName);
            return;
        }

        if (!HasMatchTypeToggles())
            ApplyPvpMatchmakingModeFromDropdown();

        if (_matchmaking != null)
        {
            _matchmaking.FindGame();
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

    public void OnLogoutClicked()
    {
        SessionWebSocketConnection.StopReconnectLoop();
        BattleSessionState.ClearSession();
        BattleSessionState.ClearPending();
        if (!string.IsNullOrEmpty(_loginSceneName))
            SceneManager.LoadScene(_loginSceneName, LoadSceneMode.Single);
    }

    private void EnsureLogoutButtonLabel()
    {
        if (_btnLogout == null)
            return;
        Text txt = FindDeepChild(_btnLogout.transform, "Text")?.GetComponent<Text>();
        if (txt == null)
            return;
        txt.text = Loc.T("menu.logout");
    }

    private static Transform FindDeepChild(Transform root, string objectName)
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
}


