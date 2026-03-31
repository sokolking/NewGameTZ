using System.Collections;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// LoginScene: кнопка входа, проверка логина/пароля на сервере, переход в MainMenu.
/// Сохраняет состояние Toggle_Debug после успешного входа.
/// </summary>
public class LoginSceneController : MonoBehaviour
{
    [Header("Scene after login")]
    [SerializeField] private string _mainMenuSceneName = "MainMenu";

    [Tooltip("Opened when login response includes activeBattle (resume after re-login).")]
    [SerializeField] private string _gameSceneName = "MainScene";

    [Header("References (empty = find by name under Canvas)")]
    [SerializeField] private InputField _loginInputField;
    [SerializeField] private InputField _passwordInputField;
    [SerializeField] private Toggle _debugLocalhostToggle;
    [SerializeField] private Button _enterButton;
    [SerializeField] private TMP_Text _errorText;
    [SerializeField] private Text _errorTextLegacy;

    private bool _busy;

    private void Start()
    {
        CacheRefs();
        EnsureToggleLabels();
        if (_enterButton != null)
        {
            _enterButton.onClick.RemoveListener(OnEnterClicked);
            _enterButton.onClick.AddListener(OnEnterClicked);
        }

        ClearError();
        if (!string.IsNullOrEmpty(BattleSessionState.PendingLoginNoticeLocKey))
        {
            ShowError(Loc.T(BattleSessionState.PendingLoginNoticeLocKey));
            BattleSessionState.PendingLoginNoticeLocKey = "";
            BattleSessionState.PendingLoginNotice = "";
        }
        else if (!string.IsNullOrEmpty(BattleSessionState.PendingLoginNotice))
        {
            ShowError(BattleSessionState.PendingLoginNotice);
            BattleSessionState.PendingLoginNotice = "";
        }
    }

    private void CacheRefs()
    {
        Transform root = transform;
        if (_loginInputField == null)
            _loginInputField = FindComponent<InputField>(root, "LoginInputField");
        if (_passwordInputField == null)
            _passwordInputField = FindComponent<InputField>(root, "PasswordInputField");
        if (_debugLocalhostToggle == null)
            _debugLocalhostToggle = FindComponent<Toggle>(root, "Toggle_Debug");
        if (_enterButton == null)
            _enterButton = FindComponent<Button>(root, "Button_Enter");
        if (_errorText == null)
            _errorText = FindComponent<TMP_Text>(root, "LoginErrorText");
        if (_errorText == null)
            _errorText = FindComponent<TMP_Text>(root, "ErrorText");
        if (_errorTextLegacy == null)
            _errorTextLegacy = FindComponent<Text>(root, "LoginErrorText");
        if (_errorTextLegacy == null)
            _errorTextLegacy = FindComponent<Text>(root, "ErrorText");
    }

    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == objectName)
                return t.GetComponent<T>();
        }
        return null;
    }

    private void EnsureToggleLabels()
    {
        ApplyToggleLabel(_debugLocalhostToggle, Loc.T("login.debug_localhost_label"));
    }

    private static void ApplyToggleLabel(Toggle toggle, string fallbackText)
    {
        if (toggle == null)
            return;
        Text label = FindComponent<Text>(toggle.transform, "Label");
        if (label == null)
            return;
        if (string.IsNullOrWhiteSpace(label.text))
            label.text = string.IsNullOrEmpty(fallbackText) ? "?" : fallbackText;
    }

    private void OnEnterClicked()
    {
        if (_busy) return;
        ClearError();

        string username = _loginInputField != null ? (_loginInputField.text ?? "").Trim() : "";
        string password = _passwordInputField != null ? (_passwordInputField.text ?? "") : "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError(Loc.T("login.enter_credentials"));
            return;
        }

        bool debug = _debugLocalhostToggle != null && _debugLocalhostToggle.isOn;
        string baseUrl = (debug ? BattleServerRuntime.DebugLocalBaseUrl : BattleServerRuntime.ProductionBaseUrl).TrimEnd('/');

        StartCoroutine(CoTryLogin(username, password, debug, baseUrl));
    }

    private IEnumerator CoTryLogin(string username, string password, bool debug, string baseUrl)
    {
        _busy = true;
        string url = baseUrl + "/api/auth/login";
        string json = JsonUtility.ToJson(new LoginAuthJson { username = username, password = password });

        string responseBody = null;
        string errorBody = null;

        yield return HttpSimple.PostJson(
            url,
            json,
            b => { responseBody = b; },
            err => { errorBody = err; });

        _busy = false;

        if (errorBody != null)
        {
            ShowError(string.IsNullOrEmpty(errorBody) ? Loc.T("login.invalid_credentials") : TruncateError(errorBody));
            yield break;
        }

        LoginAuthResponseFull parsed = null;
        if (!string.IsNullOrEmpty(responseBody))
        {
            try
            {
                parsed = JsonConvert.DeserializeObject<LoginAuthResponseFull>(responseBody, HopeBattleJson.DeserializeSettings);
            }
            catch
            {
                try
                {
                    parsed = JsonConvert.DeserializeObject<LoginAuthResponseFull>(responseBody);
                }
                catch
                {
                    // fall through
                }
            }
        }

        if (parsed == null || string.IsNullOrEmpty(parsed.accessToken))
        {
            ShowError(Loc.T("login.invalid_credentials"));
            yield break;
        }

        string token = parsed.accessToken;
        string displayName = !string.IsNullOrEmpty(parsed.username) ? parsed.username : username;

        BattleServerRuntime.UseDebugLocalhost = debug;
        GameModeState.SetSinglePlayer(false);
        BattleSessionState.SetSessionToken(token, displayName, baseUrl);
        SessionWebSocketConnection.EnsureStarted();

        if (parsed.activeBattle != null
            && parsed.activeBattle.battleStarted != null
            && !string.IsNullOrEmpty(parsed.activeBattle.battleId)
            && !string.IsNullOrEmpty(parsed.activeBattle.playerId))
        {
            BattleSessionState.SetPending(
                parsed.activeBattle.battleId,
                parsed.activeBattle.playerId,
                baseUrl,
                parsed.activeBattle.battleStarted);
            if (!string.IsNullOrEmpty(_gameSceneName))
                SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
            yield break;
        }

        if (!string.IsNullOrEmpty(_mainMenuSceneName))
            SceneManager.LoadScene(_mainMenuSceneName, LoadSceneMode.Single);
    }

    private static string TruncateError(string s, int max = 120)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    private void ShowError(string message)
    {
        if (_errorText != null)
            _errorText.text = message;
        else if (_errorTextLegacy != null)
            _errorTextLegacy.text = message;
        else
            Debug.LogWarning("[LoginScene] " + message);
    }

    private void ClearError()
    {
        if (_errorText != null)
            _errorText.text = "";
        if (_errorTextLegacy != null)
            _errorTextLegacy.text = "";
    }

    [System.Serializable]
    private class LoginAuthResponseFull
    {
        public string accessToken;
        public string username;
        public LoginActiveBattlePayload activeBattle;
    }

    [System.Serializable]
    private class LoginActiveBattlePayload
    {
        public string battleId;
        public string playerId;
        public BattleStartedPayload battleStarted;
    }

    [System.Serializable]
    private class LoginAuthJson
    {
        public string username;
        public string password;
    }
}
