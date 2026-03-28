using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Escape: открыть EscScene поверх текущей сцены (additive); повторный Escape в Esc — выгрузить только Esc,
/// вернуть активную сцену к той, что была до Esc (без перезагрузки и без Single, пока подходящая сцена ещё в памяти).
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class EscOpensEscScene : MonoBehaviour
{
    public static string EscSceneName { get; set; } = "EscScene";

    public static string FallbackSceneWhenNoReturn { get; set; } = "MainMenu";

    /// <summary>Имя игровой сцены боя (для кнопки «сдаться» в Esc).</summary>
    public static string MainBattleSceneName { get; set; } = "MainScene";

    private static string _returnSceneName;
    private static int _returnSceneBuildIndex = -1;

    /// <summary>Снимок сцены до открытия Esc — надёжнее имени после additive load/unload.</summary>
    private static Scene _returnSceneSnapshot;

    private static string _returnScenePath;

    private static bool _closing;
    private static bool _opening;
    private static bool _openedFromMainBattleScene;
    private static EscOpensEscScene _instance;

    /// <summary>Esc был открыт поверх <see cref="MainBattleSceneName"/> (для EscMenuPanel).</summary>
    public static bool WasOpenedFromMainScene => _openedFromMainBattleScene;

    /// <summary>Закрыть Esc как повторное нажатие Escape (кнопка Resume).</summary>
    public static void RequestClose()
    {
        if (_instance == null)
            return;
        _instance.RequestCloseFromUi();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null)
            return;
        var go = new GameObject(nameof(EscOpensEscScene));
        _instance = go.AddComponent<EscOpensEscScene>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        if (!WasEscapePressedThisFrame())
            return;

        if (_closing || _opening)
            return;

        if (IsEscSceneLoaded())
        {
            StartCoroutine(CoCloseEscOverlay());
            return;
        }

        OpenEscOverlay();
    }

    private void RequestCloseFromUi()
    {
        if (_closing || _opening)
            return;
        if (!IsEscSceneLoaded())
            return;
        StartCoroutine(CoCloseEscOverlay());
    }

    private static bool WasEscapePressedThisFrame()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;

        foreach (InputDevice d in InputSystem.devices)
        {
            if (d is Keyboard kb && kb.escapeKey.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    private static bool IsEscSceneName(string sceneName) =>
        !string.IsNullOrEmpty(sceneName) &&
        string.Equals(sceneName, EscSceneName, StringComparison.Ordinal);

    private void OpenEscOverlay()
    {
        if (_opening || IsEscSceneLoaded())
            return;

        Scene current = SceneManager.GetActiveScene();
        if (!current.IsValid())
            return;

        if (IsEscSceneName(current.name))
            return;

        StartCoroutine(CoOpenEscOverlay());
    }

    private IEnumerator CoOpenEscOverlay()
    {
        if (_opening || IsEscSceneLoaded())
            yield break;

        Scene current = SceneManager.GetActiveScene();
        if (!current.IsValid())
            yield break;

        if (IsEscSceneName(current.name))
            yield break;

        _opening = true;
        try
        {
            _returnSceneName = current.name;
            _returnSceneBuildIndex = current.buildIndex;
            _returnScenePath = current.path;
            _returnSceneSnapshot = current;
            _openedFromMainBattleScene = string.Equals(
                current.name,
                MainBattleSceneName,
                StringComparison.OrdinalIgnoreCase);

            AsyncOperation op = SceneManager.LoadSceneAsync(EscSceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                ClearReturnTarget();
                yield break;
            }

            yield return op;

            Scene esc = SceneManager.GetSceneByName(EscSceneName);
            if (!esc.IsValid() || !esc.isLoaded)
            {
                ClearReturnTarget();
                yield break;
            }

            // Не вызывать SetActiveScene(esc): иначе Camera.main часто становится камерой EscScene
            // (позиция по умолчанию — вид на «0,0»), а не боевой HexGridCamera.
            // UI в Esc — Screen Space Overlay, отдельная камера для меню не нужна.
            DisableWorldCamerasInEscOverlay(esc);
            EnsureEscMenuPanelWired(esc);
        }
        finally
        {
            _opening = false;
        }
    }

    /// <summary>
    /// Сцена Esc грузится additively: отключаем все <see cref="Camera"/> в ней,
    /// чтобы не было второго MainCamera и подмены <see cref="Camera.main"/> боевой камерой.
    /// </summary>
    private static void DisableWorldCamerasInEscOverlay(Scene escScene)
    {
        if (!escScene.IsValid() || !escScene.isLoaded)
            return;

        foreach (GameObject root in escScene.GetRootGameObjects())
        {
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
            {
                if (cam != null)
                    cam.enabled = false;
            }
        }
    }

    static void EnsureEscMenuPanelWired(Scene escScene)
    {
        if (!escScene.IsValid() || !escScene.isLoaded)
            return;

        foreach (GameObject root in escScene.GetRootGameObjects())
        {
            var trs = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trs.Length; i++)
            {
                Transform t = trs[i];
                if (t.name != "EscMenuPanel")
                    continue;
                if (t.GetComponent<EscMenuPanelController>() == null)
                    t.gameObject.AddComponent<EscMenuPanelController>();
                return;
            }
        }
    }

    private static bool IsEscSceneLoaded()
    {
        Scene s = SceneManager.GetSceneByName(EscSceneName);
        return s.IsValid() && s.isLoaded;
    }

    private static bool TryResolveSceneToReturnTo(out Scene scene)
    {
        scene = default;

        if (_returnSceneSnapshot.IsValid() && _returnSceneSnapshot.isLoaded && !IsEscSceneName(_returnSceneSnapshot.name))
        {
            scene = _returnSceneSnapshot;
            return true;
        }

        if (!string.IsNullOrEmpty(_returnScenePath))
        {
            Scene byPath = SceneManager.GetSceneByPath(_returnScenePath);
            if (byPath.IsValid() && byPath.isLoaded && !IsEscSceneName(byPath.name))
            {
                scene = byPath;
                return true;
            }
        }

        if (!string.IsNullOrEmpty(_returnSceneName))
        {
            Scene byName = SceneManager.GetSceneByName(_returnSceneName);
            if (byName.IsValid() && byName.isLoaded && !IsEscSceneName(byName.name))
            {
                scene = byName;
                return true;
            }
        }

        if (!string.IsNullOrEmpty(_returnSceneName))
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || IsEscSceneName(s.name) || s.name == "DontDestroyOnLoad")
                    continue;
                if (string.Equals(s.name, _returnSceneName, StringComparison.OrdinalIgnoreCase))
                {
                    scene = s;
                    return true;
                }
            }
        }

        if (_returnSceneBuildIndex >= 0)
        {
            Scene byIndex = SceneManager.GetSceneByBuildIndex(_returnSceneBuildIndex);
            if (byIndex.IsValid() && byIndex.isLoaded && !IsEscSceneName(byIndex.name))
            {
                scene = byIndex;
                return true;
            }
        }

        return TryPickFirstLoadedGameScene(out scene);
    }

    private static bool TryPickFirstLoadedGameScene(out Scene scene)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded || IsEscSceneName(s.name))
                continue;
            if (s.name == "DontDestroyOnLoad")
                continue;
            scene = s;
            return true;
        }

        scene = default;
        return false;
    }

    private static bool HasAnyLoadedGameScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded || IsEscSceneName(s.name))
                continue;
            if (s.name == "DontDestroyOnLoad")
                continue;
            return true;
        }

        return false;
    }

    private static void ClearReturnTarget()
    {
        _returnSceneName = null;
        _returnSceneBuildIndex = -1;
        _returnScenePath = null;
        _returnSceneSnapshot = default;
        _openedFromMainBattleScene = false;
    }

    private IEnumerator CoCloseEscOverlay()
    {
        if (_closing)
            yield break;
        _closing = true;

        try
        {
            if (!IsEscSceneLoaded())
                yield break;

            AsyncOperation op = SceneManager.UnloadSceneAsync(EscSceneName);
            if (op == null)
            {
                Scene esc = SceneManager.GetSceneByName(EscSceneName);
                if (esc.IsValid())
                    op = SceneManager.UnloadSceneAsync(esc);
            }

            if (op != null)
                yield return op;

            if (TryResolveSceneToReturnTo(out Scene back) && back.IsValid() && back.isLoaded)
            {
                SceneManager.SetActiveScene(back);
                ClearReturnTarget();
                yield break;
            }

            ClearReturnTarget();

            // Single только если не осталось ни одной игровой сцены — иначе полная перезагрузка уничтожает состояние X.
            if (HasAnyLoadedGameScene())
                yield break;

            if (!string.IsNullOrEmpty(FallbackSceneWhenNoReturn))
                yield return SceneManager.LoadSceneAsync(FallbackSceneWhenNoReturn, LoadSceneMode.Single);
            else if (SceneManager.sceneCountInBuildSettings > 0)
                yield return SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
        }
        finally
        {
            _closing = false;
        }
    }
}
