using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Swaps the active URP asset between menu (lightweight) and game (full quality) on scene load.
/// </summary>
static class MenuScenePipelineBootstrap
{
    const string ConfigResourcePath = "Rendering/MenuScenePipelineSwitchConfig";

    static MenuScenePipelineSwitchConfig _config;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ApplyStartupScene()
    {
        EnsureConfigLoaded();
        ApplyForScene(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive)
            return;
        ApplyForScene(scene);
    }

    static void EnsureConfigLoaded()
    {
        if (_config != null)
            return;
        _config = Resources.Load<MenuScenePipelineSwitchConfig>(ConfigResourcePath);
    }

    static void ApplyForScene(Scene scene)
    {
        EnsureConfigLoaded();
        if (_config == null || _config.gamePipeline == null || _config.menuPipeline == null)
            return;

        bool menu = IsMenuScene(scene.name);
        var next = menu ? _config.menuPipeline : _config.gamePipeline;
        if (GraphicsSettings.defaultRenderPipeline != next)
            GraphicsSettings.defaultRenderPipeline = next;
    }

    static bool IsMenuScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || _config.menuSceneNames == null)
            return false;
        for (int i = 0; i < _config.menuSceneNames.Length; i++)
        {
            if (string.Equals(sceneName, _config.menuSceneNames[i], System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
