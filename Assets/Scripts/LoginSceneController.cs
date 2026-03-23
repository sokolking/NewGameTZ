using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// LoginScene: кнопка входа, проверка логина/пароля на сервере, переход в MainMenu.
/// Сохраняет состояние Toggle_SoloVsMonster и Toggle_Debug после успешного входа.
/// </summary>
public class LoginSceneController : MonoBehaviour
{
    [Header("Сцена после входа")]
    [SerializeField] private string _mainMenuSceneName = "MainMenu";

    [Header("Ссылки (пусто — поиск по имени под Canvas)")]
    [SerializeField] private InputField _loginInputField;
    [SerializeField] private InputField _passwordInputField;
    [SerializeField] private Toggle _soloVsMonsterToggle;
    [SerializeField] private Toggle _debugLocalhostToggle;
    [SerializeField] private Button _enterButton;
    [SerializeField] private Text _errorText;

    private bool _busy;

    private void Start()
    {
        CacheRefs();
        if (_enterButton != null)
        {
            _enterButton.onClick.RemoveListener(OnEnterClicked);
            _enterButton.onClick.AddListener(OnEnterClicked);
        }

        ClearError();
    }

    private void CacheRefs()
    {
        Transform root = transform;
        if (_loginInputField == null)
            _loginInputField = FindComponent<InputField>(root, "LoginInputField");
        if (_passwordInputField == null)
            _passwordInputField = FindComponent<InputField>(root, "PasswordInputField");
        if (_soloVsMonsterToggle == null)
            _soloVsMonsterToggle = FindComponent<Toggle>(root, "Toggle_SoloVsMonster");
        if (_debugLocalhostToggle == null)
            _debugLocalhostToggle = FindComponent<Toggle>(root, "Toggle_Debug");
        if (_enterButton == null)
            _enterButton = FindComponent<Button>(root, "Button_Enter");
        if (_errorText == null)
            _errorText = FindComponent<Text>(root, "LoginErrorText");
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

    private void OnEnterClicked()
    {
        if (_busy) return;
        ClearError();

        string username = _loginInputField != null ? (_loginInputField.text ?? "").Trim() : "";
        string password = _passwordInputField != null ? (_passwordInputField.text ?? "") : "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Введите логин и пароль.");
            return;
        }

        bool solo = _soloVsMonsterToggle != null && _soloVsMonsterToggle.isOn;
        bool debug = _debugLocalhostToggle != null && _debugLocalhostToggle.isOn;
        string baseUrl = (debug ? BattleServerRuntime.DebugLocalBaseUrl : BattleServerRuntime.ProductionBaseUrl).TrimEnd('/');

        StartCoroutine(CoTryLogin(username, password, solo, debug, baseUrl));
    }

    private IEnumerator CoTryLogin(string username, string password, bool solo, bool debug, string baseUrl)
    {
        _busy = true;
        string url = baseUrl + "/api/db/user/inventory";
        string json = JsonUtility.ToJson(new InventoryAuthJson { username = username, password = password });

        bool success = false;
        string errorBody = null;

        yield return HttpSimple.PostJson(
            url,
            json,
            _ => { success = true; },
            err => { errorBody = err; });

        _busy = false;

        if (!success)
        {
            ShowError(string.IsNullOrEmpty(errorBody) ? "Неверный логин или пароль." : TruncateError(errorBody));
            yield break;
        }

        BattleServerRuntime.UseDebugLocalhost = debug;
        GameModeState.SetSinglePlayer(solo);
        BattleSessionState.SetAuthCredentials(username, password);

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
        else
            Debug.LogWarning("[LoginScene] " + message);
    }

    private void ClearError()
    {
        if (_errorText != null)
            _errorText.text = "";
    }

    [System.Serializable]
    private class InventoryAuthJson
    {
        public string username;
        public string password;
    }
}
