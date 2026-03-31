using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main menu: lists unfinished battles from session WebSocket and starts spectator <see cref="MainScene"/>.
/// </summary>
[AddComponentMenu("Hex Grid/Main Menu Spectator Battles")]
public class MainMenuSpectatorBattlesController : MonoBehaviour
{
    [SerializeField] private string _gameSceneName = "MainScene";
    [Tooltip("Parent for row instances (e.g. Battle_List under Active_Battles_Panel).")]
    [SerializeField] private RectTransform _battleListParent;
    [SerializeField] private GameObject _battleRowPrefab;
    [Tooltip("How often to refresh the list while this menu is open (seconds). 0 = only on enable.")]
    [SerializeField] private float _refreshIntervalSeconds = 8f;

    private readonly List<GameObject> _spawnedRows = new();
    private Coroutine _refreshCo;

    private void OnEnable()
    {
        Loc.LanguageChanged += OnLocLanguageChanged;
        SessionWebSocketConnection.OnSpectatorListReceived += OnSpectatorListReceived;
        SessionWebSocketConnection.OnSpectatorWatchReceived += OnSpectatorWatchReceived;
        SessionWebSocketConnection.EnsureStarted();
        RequestListSoon();
        if (_refreshIntervalSeconds > 0.5f)
            _refreshCo = StartCoroutine(RefreshLoop());
    }

    private void OnDisable()
    {
        SessionWebSocketConnection.OnSpectatorListReceived -= OnSpectatorListReceived;
        SessionWebSocketConnection.OnSpectatorWatchReceived -= OnSpectatorWatchReceived;
        if (_refreshCo != null)
        {
            StopCoroutine(_refreshCo);
            _refreshCo = null;
        }
    }

    void OnLocLanguageChanged(GameLanguage _)
    {
        foreach (var row in _spawnedRows)
        {
            if (row == null)
                continue;
            Transform watchTr = row.transform.Find("Watch_Battle_Button");
            Text txt = watchTr != null ? watchTr.GetComponentInChildren<Text>(true) : null;
            if (txt != null)
                txt.text = Loc.T("menu.spectator_watch");
        }
    }

    private void RequestListSoon()
    {
        StartCoroutine(RequestListAfterDelay(0.35f));
    }

    private IEnumerator RequestListAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SessionWebSocketConnection.SendSpectatorListRequest();
    }

    private IEnumerator RefreshLoop()
    {
        var wait = new WaitForSeconds(_refreshIntervalSeconds);
        while (enabled)
        {
            yield return wait;
            if (string.IsNullOrEmpty(BattleSessionState.AccessToken))
                continue;
            SessionWebSocketConnection.SendSpectatorListRequest();
        }
    }

    private void OnSpectatorListReceived(SpectatorBattleListItem[] battles)
    {
        if (_battleListParent == null || _battleRowPrefab == null)
            return;

        foreach (var go in _spawnedRows)
        {
            if (go != null)
                Destroy(go);
        }

        _spawnedRows.Clear();

        if (battles == null || battles.Length == 0)
            return;

        foreach (var b in battles)
        {
            if (b == null || string.IsNullOrEmpty(b.battleId))
                continue;
            var row = Instantiate(_battleRowPrefab, _battleListParent, false);
            _spawnedRows.Add(row);

            var title = row.transform.Find("Battle_Title")?.GetComponent<Text>();
            if (title != null)
                title.text = string.IsNullOrEmpty(b.mode) ? b.battleId : b.mode.ToUpperInvariant();

            var btnTr = row.transform.Find("Watch_Battle_Button");
            var btn = btnTr != null ? btnTr.GetComponent<Button>() : null;
            if (btn != null)
            {
                string bid = b.battleId;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SessionWebSocketConnection.SendSpectatorWatchRequest(bid));
                var watchTxt = btnTr.GetComponentInChildren<Text>(true);
                if (watchTxt != null)
                    watchTxt.text = Loc.T("menu.spectator_watch");
            }
        }
    }

    private void OnSpectatorWatchReceived(bool ok, string code, BattleStartedPayload payload)
    {
        if (!ok)
        {
            Debug.LogWarning("[Spectator] watch failed: " + code);
            return;
        }

        if (payload == null || string.IsNullOrEmpty(payload.battleId))
        {
            Debug.LogWarning("[Spectator] watch: empty payload");
            return;
        }

        string url = !string.IsNullOrEmpty(BattleSessionState.SessionBaseUrl)
            ? BattleSessionState.SessionBaseUrl
            : BattleServerRuntime.CurrentBaseUrl;
        BattleSessionState.SetSpectatorPending(payload.battleId, url, payload);
        SceneManager.LoadScene(_gameSceneName);
    }
}
