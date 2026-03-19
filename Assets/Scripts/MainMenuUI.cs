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
    [SerializeField] private InputField _loginInputField;
    [SerializeField] private InputField _passwordInputField;

    [Header("Режим игры")]
    [Tooltip("Если включено — одиночная игра против ИИ, без сервера.")]
    [SerializeField] private Toggle _singlePlayerToggle;

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
        EnsureAuthInputs();
        // Найти кнопки по именам в иерархии под этим объектом.
        CacheButtons();
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

        // Попробовать найти чекбокс одиночной игры по имени (опционально, можно проставить в инспекторе).
        if (_singlePlayerToggle == null)
        {
            _singlePlayerToggle = transform.Find("Toggle_SinglePlayer")?.GetComponent<Toggle>();
        }
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
        bool singlePlayer = _singlePlayerToggle != null && _singlePlayerToggle.isOn;
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

    private void EnsureAuthInputs()
    {
        if (_loginInputField != null && _passwordInputField != null)
        {
            ApplyDefaultAuthValues();
            return;
        }

        RectTransform parentRect = transform as RectTransform;
        if (parentRect == null)
            return;

        var authPanelGo = new GameObject("AuthPanel", typeof(RectTransform));
        authPanelGo.transform.SetParent(transform, false);
        var authPanelRect = authPanelGo.GetComponent<RectTransform>();
        authPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        authPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        authPanelRect.pivot = new Vector2(0.5f, 0.5f);
        authPanelRect.anchoredPosition = new Vector2(0f, 140f);
        authPanelRect.sizeDelta = new Vector2(320f, 90f);

        _loginInputField = CreateAuthInputRow(authPanelRect, "Login", "LoginInputField", "Логин", "test", new Vector2(0f, 22f), isPassword: false);
        _passwordInputField = CreateAuthInputRow(authPanelRect, "Password", "PasswordInputField", "Пароль", "test", new Vector2(0f, -22f), isPassword: true);
        ApplyDefaultAuthValues();
    }

    private void ApplyDefaultAuthValues()
    {
        if (_loginInputField != null && string.IsNullOrEmpty(_loginInputField.text))
            _loginInputField.text = "test";
        if (_passwordInputField != null && string.IsNullOrEmpty(_passwordInputField.text))
            _passwordInputField.text = "test";
    }

    private static InputField CreateAuthInputRow(
        RectTransform parent,
        string labelObjectName,
        string inputObjectName,
        string labelText,
        string defaultValue,
        Vector2 anchoredPosition,
        bool isPassword)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var labelGo = new GameObject(labelObjectName, typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(parent, false);
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = anchoredPosition + new Vector2(-105f, 0f);
        labelRect.sizeDelta = new Vector2(80f, 28f);
        var label = labelGo.GetComponent<Text>();
        label.font = font;
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = Color.white;
        label.text = labelText;

        var inputGo = new GameObject(inputObjectName, typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(parent, false);
        var inputRect = inputGo.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.anchoredPosition = anchoredPosition + new Vector2(45f, 0f);
        inputRect.sizeDelta = new Vector2(190f, 32f);
        var inputImage = inputGo.GetComponent<Image>();
        inputImage.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(inputGo.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 6f);
        textRect.offsetMax = new Vector2(-10f, -6f);
        var text = textGo.GetComponent<Text>();
        text.font = font;
        text.fontSize = 15;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.text = defaultValue;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        placeholderGo.transform.SetParent(inputGo.transform, false);
        var placeholderRect = placeholderGo.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(10f, 6f);
        placeholderRect.offsetMax = new Vector2(-10f, -6f);
        var placeholder = placeholderGo.GetComponent<Text>();
        placeholder.font = font;
        placeholder.fontSize = 15;
        placeholder.alignment = TextAnchor.MiddleLeft;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.text = labelText;

        var inputField = inputGo.GetComponent<InputField>();
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.targetGraphic = inputImage;
        inputField.text = defaultValue;
        if (isPassword)
            inputField.contentType = InputField.ContentType.Password;

        return inputField;
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


