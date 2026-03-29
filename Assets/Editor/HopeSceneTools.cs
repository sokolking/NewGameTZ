#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Единственные пункты меню <b>Tools → Hope</b>: открывают сцену и применяют актуальную вёрстку/доп. UI.
/// </summary>
public static class HopeSceneTools
{
    const string EscScenePath = "Assets/Scenes/EscScene.unity";
    const string LoginScenePath = "Assets/Scenes/LoginScene.unity";
    const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";

    [MenuItem("Tools/Hope/Create EscScene")]
    public static void CreateEscScene()
    {
        EditorSceneManager.OpenScene(EscScenePath, OpenSceneMode.Single);
        EscSettingsPanelSetupTool.RunSettingsPanelTabsIfNeeded();
        EscSettingsPanelSetupTool.RunLanguagePageAndLocalizationIfNeeded();
        SaveActiveScene();
    }

    [MenuItem("Tools/Hope/Create LoginScene")]
    public static void CreateLoginScene()
    {
        EditorSceneManager.OpenScene(LoginScenePath, OpenSceneMode.Single);
        LoginSceneSetupTool.PerformFullLoginSceneLayout();
        SaveActiveScene();
    }

    [MenuItem("Tools/Hope/Create MainMenu")]
    public static void CreateMainMenu()
    {
        EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
        MainMenuSetupTool.PerformFullMainMenuLayout();
#if UNITY_2023_1_OR_NEWER
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
#else
        Canvas canvas = Object.FindObjectOfType<Canvas>();
#endif
        if (canvas != null)
            ClientUpdatePanelSetupTool.TryEnsureClientUpdatePanel(canvas);
        SaveActiveScene();
    }

    [MenuItem("Tools/Hope/Create MainScene")]
    public static void CreateMainScene()
    {
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        HexGridSetupTool.PerformFullHexGridLayout();
        HexApUiSetupTool.PerformFullBattleHudLayout();
        InventoryUiSetupTool.PerformFullInventoryLayout();
        SaveActiveScene();
    }

    static void SaveActiveScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
#endif
