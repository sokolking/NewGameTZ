using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// LoginScene: проверка /api/client/version, блокировка входа при устаревшей сборке, панель обновления и загрузка dmg.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ClientUpdateGate : MonoBehaviour
{
    [Header("Ссылки (пусто — поиск по имени под Canvas)")]
    [SerializeField] private Button _loginButton;
    [SerializeField] private GameObject _updatePanelRoot;
    [SerializeField] private Text _messageText;
    [SerializeField] private Text _statusText;
    [SerializeField] private Button _downloadButton;
    [SerializeField] private Slider _progressSlider;
    [SerializeField] private Toggle _debugLocalhostToggle;

    [Tooltip("Если true — при ошибке сети вход остаётся доступен (иначе блокируется до успешной проверки).")]
    [SerializeField] private bool _allowLoginIfVersionCheckFails = true;

    private string _versionApiBaseUrl;
    private string _downloadUrl;
    private string _dmgPrefix = "Hope";
    private string _pendingDownloadPath;
    private HttpSimple.DownloadProgressHolder _downloadProgress;
    private bool _busy;
    private readonly List<Button> _blockedButtons = new List<Button>();

    private void Start()
    {
        CacheRefs();
        foreach (var b in _blockedButtons)
        {
            if (b != null)
                b.interactable = false;
        }

        HidePanel();

        bool debug = _debugLocalhostToggle != null && _debugLocalhostToggle.isOn;
        _versionApiBaseUrl = (debug ? BattleServerRuntime.DebugLocalBaseUrl : BattleServerRuntime.ProductionBaseUrl).TrimEnd('/');

        StartCoroutine(CoCheckVersion());
    }

    private void Update()
    {
        if (_progressSlider != null && _downloadProgress != null)
            _progressSlider.value = _downloadProgress.Value;
    }

    private void CacheRefs()
    {
        Transform root = transform;
        _blockedButtons.Clear();
        if (_loginButton != null)
            _blockedButtons.Add(_loginButton);
        else if (SceneManager.GetActiveScene().name == "MainMenu")
        {
            var find = FindComponent<Button>(root, "Button_FindGame");
            var neu = FindComponent<Button>(root, "Button_NewGame");
            if (find != null)
                _blockedButtons.Add(find);
            if (neu != null)
                _blockedButtons.Add(neu);
        }
        else
        {
            var enter = FindComponent<Button>(root, "Button_Enter")
                ?? FindComponent<Button>(root, "Button (Legacy)");
            if (enter != null)
                _blockedButtons.Add(enter);
        }
        if (_updatePanelRoot == null)
        {
            var t = FindTransform(root, UiHierarchyNames.ClientUpdatePanel);
            if (t != null)
                _updatePanelRoot = t.gameObject;
        }
        if (_messageText == null)
            _messageText = FindComponent<Text>(root, UiHierarchyNames.ClientUpdateMessageText);
        if (_statusText == null)
            _statusText = FindComponent<Text>(root, UiHierarchyNames.ClientUpdateStatusText);
        if (_downloadButton == null)
            _downloadButton = FindComponent<Button>(root, UiHierarchyNames.ClientUpdateDownloadButton);
        if (_progressSlider == null)
            _progressSlider = FindComponent<Slider>(root, UiHierarchyNames.ClientUpdateProgressSlider);
        if (_debugLocalhostToggle == null)
            _debugLocalhostToggle = FindComponent<Toggle>(root, "Toggle_Debug");

        if (_downloadButton != null)
        {
            _downloadButton.onClick.RemoveListener(OnDownloadClicked);
            _downloadButton.onClick.AddListener(OnDownloadClicked);
        }
    }

    private static Transform FindTransform(Transform root, string objectName)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == objectName)
                return t;
        }
        return null;
    }

    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        var t = FindTransform(root, objectName);
        return t != null ? t.GetComponent<T>() : null;
    }

    private IEnumerator CoCheckVersion()
    {
        string url = _versionApiBaseUrl + "/api/client/version";
        string body = null;
        string err = null;

        yield return HttpSimple.GetString(url, b => body = b, e => err = e);

        if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(body))
        {
            if (_allowLoginIfVersionCheckFails)
            {
                SetLoginInteractable(true);
                if (_statusText != null)
                    _statusText.text = "";
            }
            else
            {
                ShowPanel();
                if (_messageText != null)
                    _messageText.text = "Не удалось проверить версию клиента. Проверьте соединение.";
            }
            yield break;
        }

        ClientVersionJson v;
        try
        {
            v = JsonUtility.FromJson<ClientVersionJson>(body);
        }
        catch
        {
            SetLoginInteractable(true);
            yield break;
        }

        if (v == null)
        {
            SetLoginInteractable(true);
            yield break;
        }

        string clientVer = Application.version ?? "0.0.0";
        if (!ClientVersionComparer.TryCompare(clientVer, v.latestVersion, out int cmpToLatest))
            cmpToLatest = 0;

        _dmgPrefix = string.IsNullOrEmpty(v.dmgFileNamePrefix) ? "Hope" : v.dmgFileNamePrefix.Trim();

        if (cmpToLatest < 0)
        {
            _downloadUrl = BuildAbsoluteDownloadUrl(v.downloadDmgRelativePath);
            string dmgName = "Hope.dmg";
            if (Uri.TryCreate(_downloadUrl, UriKind.Absolute, out var uri))
            {
                string leaf = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(leaf))
                    dmgName = leaf;
            }
            _pendingDownloadPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                dmgName);

            ShowPanel();
            if (_messageText != null)
            {
                _messageText.text =
                    $"Доступна новая версия игры.\nУ вас: {clientVer}\nАктуальная: {v.latestVersion}";
            }
            SetLoginInteractable(false);
        }
        else
        {
            HidePanel();
            SetLoginInteractable(true);
        }
    }

    private string BuildAbsoluteDownloadUrl(string relativeOrAbsolute)
    {
        if (string.IsNullOrEmpty(relativeOrAbsolute))
            return _versionApiBaseUrl + "/downloads/Hope.dmg";
        string t = relativeOrAbsolute.Trim();
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return t;
        if (!t.StartsWith("/"))
            t = "/" + t;
        return _versionApiBaseUrl + t;
    }

    private void OnDownloadClicked()
    {
        if (_busy || string.IsNullOrEmpty(_downloadUrl) || string.IsNullOrEmpty(_pendingDownloadPath))
            return;
        StartCoroutine(CoDownload());
    }

    private IEnumerator CoDownload()
    {
        _busy = true;
        if (_downloadButton != null)
            _downloadButton.interactable = false;
        if (_progressSlider != null)
        {
            _progressSlider.gameObject.SetActive(true);
            _progressSlider.value = 0f;
        }
        if (_statusText != null)
            _statusText.text = "Загрузка…";

        _downloadProgress = new HttpSimple.DownloadProgressHolder();
        string err = null;

        yield return HttpSimple.DownloadFile(
            _downloadUrl,
            _pendingDownloadPath,
            _downloadProgress,
            () => { },
            e => err = e);

        _busy = false;

        if (!string.IsNullOrEmpty(err))
        {
            if (_downloadButton != null)
                _downloadButton.interactable = true;
            if (_statusText != null)
                _statusText.text = "Ошибка: " + err;
            yield break;
        }

        if (_statusText != null)
            _statusText.text = "Готово. Установка…";

#if UNITY_EDITOR
        if (_statusText != null)
            _statusText.text = "В редакторе установка не запускается.";
        if (_downloadButton != null)
            _downloadButton.interactable = true;
        yield break;
#else
        MacOsUpdateInstaller.ScheduleInstallAndQuit(_pendingDownloadPath, _dmgPrefix);
#endif
    }

    private void ShowPanel()
    {
        if (_updatePanelRoot != null)
            _updatePanelRoot.SetActive(true);
    }

    private void HidePanel()
    {
        if (_updatePanelRoot != null)
            _updatePanelRoot.SetActive(false);
        if (_progressSlider != null)
            _progressSlider.gameObject.SetActive(false);
    }

    private void SetLoginInteractable(bool on)
    {
        foreach (var b in _blockedButtons)
        {
            if (b != null)
                b.interactable = on;
        }
    }

    [Serializable]
    private class ClientVersionJson
    {
        public string latestVersion;
        public string minimumVersion;
        public string downloadDmgRelativePath;
        public string dmgFileNamePrefix;
    }
}

public static class ClientVersionComparer
{
    public static bool TryCompare(string a, string b, out int result)
    {
        result = 0;
        if (!TryParse(a, out System.Version va) || !TryParse(b, out System.Version vb))
            return false;
        result = va.CompareTo(vb);
        return true;
    }

    private static bool TryParse(string s, out System.Version v)
    {
        v = null;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        s = s.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(1);
        try
        {
            v = new System.Version(s);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
