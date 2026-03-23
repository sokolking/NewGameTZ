using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Кнопки EscMenuPanel в EscScene: Resume = закрыть Esc; Settings = как у <see cref="MainMenuUI"/>;
/// Surrend — только если Esc открыт с MainScene; Exit — выход из боя и закрытие игры.
/// </summary>
[DefaultExecutionOrder(50)]
public sealed class EscMenuPanelController : MonoBehaviour
{
    const string ResumeButtonName = "Button_Resume";
    const string SurrenderButtonName = "Button_Surrend_Battle";
    const string ExitGameButtonName = "Button_Exit_Game";

    void Start()
    {
        WireResume();
        WireSurrenderAndExit();
        RefreshSurrenderVisibility();
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

    Button FindButtonDeep(string objectName)
    {
        Transform t = FindChildDeep(transform, objectName);
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
