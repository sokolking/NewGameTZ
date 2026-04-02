using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// References game vs menu URP assets for <see cref="MenuScenePipelineBootstrap"/>.
/// Instance lives under Resources/Rendering/MenuScenePipelineSwitchConfig.
/// </summary>
[CreateAssetMenu(fileName = "MenuScenePipelineSwitchConfig", menuName = "Hope/Rendering/Menu Scene Pipeline Switch Config")]
public sealed class MenuScenePipelineSwitchConfig : ScriptableObject
{
    [Tooltip("URP asset for battle and non-menu scenes (e.g. PC_RPAsset).")]
    public UniversalRenderPipelineAsset gamePipeline;

    [Tooltip("Lightweight URP asset for LoginScene / MainMenu (no SSAO, lower render scale).")]
    public UniversalRenderPipelineAsset menuPipeline;

    [Tooltip("Scenes that should use menuPipeline.")]
    public string[] menuSceneNames = { "LoginScene", "MainMenu" };
}
