using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Кнопки EscMenuPanel в EscScene: Resume = закрыть Esc; Settings = как у <see cref="MainMenuUI"/>;
/// Surrend — только если Esc открыт с MainScene; Exit — выход из боя и закрытие игры.
/// При открытии Settings — EscMenuPanel скрывается; при закрытии Settings — возвращается.
/// </summary>
[DefaultExecutionOrder(50)]
public sealed class EscMenuPanelController : MonoBehaviour
{
    const string ResumeButtonName        = "Button_Resume";
    const string SettingsButtonName      = "Button_Settings";
    const string CloseSettingsButtonName = "Button_CloseSettings";
    const string SurrenderButtonName     = "Button_Surrend_Battle";
    const string ExitGameButtonName      = "Button_Exit_Game";

    // SettingsPanel — sibling-объект в иерархии EscScene
    GameObject _settingsPanel;

    void Start()
    {
        ResolveSettingsPanel();
        WireResume();
        WireSettings();
        WireSurrenderAndExit();
        RefreshSurrenderVisibility();
    }

    // ── Settings panel ────────────────────────────────────────────────────────

    void ResolveSettingsPanel()
    {
        // Ищем сначала как sibling EscMenuPanel, затем по всей сцене
        Transform parent = transform.parent;
        if (parent != null)
        {
            Transform sp = parent.Find("SettingsPanel");
            if (sp != null) { _settingsPanel = sp.gameObject; return; }
        }

        // Fallback: поиск по всей иерархии root-объектов сцены
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager
                     .GetSceneByName(EscOpensEscScene.EscSceneName).GetRootGameObjects())
        {
            Transform found = FindChildDeep(root.transform, "SettingsPanel");
            if (found != null) { _settingsPanel = found.gameObject; return; }
        }
    }

    void WireSettings()
    {
        Button btnOpen = FindButtonDeep(SettingsButtonName);
        if (btnOpen != null)
        {
            btnOpen.onClick.RemoveListener(OnSettingsClicked);
            btnOpen.onClick.AddListener(OnSettingsClicked);
        }

        if (_settingsPanel != null)
        {
            Button btnClose = FindButtonDeep(_settingsPanel.transform, CloseSettingsButtonName);
            if (btnClose != null)
            {
                btnClose.onClick.RemoveListener(OnCloseSettingsClicked);
                btnClose.onClick.AddListener(OnCloseSettingsClicked);
            }
        }
    }

    void OnSettingsClicked()
    {
        gameObject.SetActive(false);
        if (_settingsPanel != null)
            _settingsPanel.SetActive(true);
    }

    void OnCloseSettingsClicked()
    {
        if (_settingsPanel != null)
            _settingsPanel.SetActive(false);
        gameObject.SetActive(true);
    }

    void WireResume()
    {
        Button btn = FindButtonDeep(ResumeButtonName);
        if (btn == null)
            return;
        btn.onClick.RemoveListener(OnResumeClicked);
        btn.onClick.AddListener(OnResumeClicked);
    }

    void OnResumeClicked()
    {
        EscOpensEscScene.RequestClose();
    }

    void WireSurrenderAndExit()
    {
        Button surrender = FindButtonDeep(SurrenderButtonName);
        if (surrender != null)
        {
            surrender.onClick.RemoveListener(OnSurrenderBattleClicked);
            surrender.onClick.AddListener(OnSurrenderBattleClicked);
        }

        Button exit = FindButtonDeep(ExitGameButtonName);
        if (exit != null)
        {
            exit.onClick.RemoveListener(OnExitGameClicked);
            exit.onClick.AddListener(OnExitGameClicked);
        }
    }

    void RefreshSurrenderVisibility()
    {
        Transform t = FindChildDeep(transform, SurrenderButtonName);
        if (t != null)
            t.gameObject.SetActive(EscOpensEscScene.WasOpenedFromMainScene);
    }

    void OnSurrenderBattleClicked()
    {
        BattleEscActions.NotifyLeaveCurrentBattleIfAny();
        string menu = string.IsNullOrEmpty(EscOpensEscScene.FallbackSceneWhenNoReturn)
            ? "MainMenu"
            : EscOpensEscScene.FallbackSceneWhenNoReturn;
        SceneManager.LoadScene(menu, LoadSceneMode.Single);
    }

    void OnExitGameClicked()
    {
        BattleEscActions.NotifyLeaveCurrentBattleIfAny();
        BattleEscActions.QuitApplication();
    }

    Button FindButtonDeep(string objectName) =>
        FindButtonDeep(transform, objectName);

    static Button FindButtonDeep(Transform root, string objectName)
    {
        Transform t = FindChildDeep(root, objectName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    static Transform FindChildDeep(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrEmpty(objectName))
            return null;
        if (root.name == objectName)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildDeep(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }
}

/// <summary>Общий выход из боя по HTTP leave + завершение приложения.</summary>
public static class BattleEscActions
{
    public static void NotifyLeaveCurrentBattleIfAny()
    {
        var bsc = Object.FindFirstObjectByType<BattleServerConnection>();
        if (bsc == null || !bsc.IsInBattle)
            return;
        BattleServerConnection.NotifyLeaveBlocking(bsc.ServerUrl, bsc.BattleId, bsc.PlayerId);
    }

    public static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
