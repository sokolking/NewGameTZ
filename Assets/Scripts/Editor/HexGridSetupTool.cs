using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Сетка, камера, игрок, ввод. Вызывается из <b>Tools → Hope → Create MainScene</b>.
/// </summary>
public static class HexGridSetupTool
{
    public static void PerformFullHexGridLayout()
    {
        SetupSceneFromScratch();
    }

    public static void SetupSceneFromScratch()
    {
        if (!Application.isPlaying && !EditorSceneManager.GetActiveScene().isDirty)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        // 1. HexGrid — принудительно 25×40 и генерация
#if UNITY_2023_1_OR_NEWER
        HexGrid grid = Object.FindFirstObjectByType<HexGrid>();
#else
        HexGrid grid = Object.FindObjectOfType<HexGrid>();
#endif
        GameObject gridGo;
        if (grid != null)
        {
            gridGo = grid.gameObject;
            grid.ClearGrid();
        }
        else
        {
            gridGo = new GameObject("HexGrid");
            grid = gridGo.AddComponent<HexGrid>();
            Undo.RegisterCreatedObjectUndo(gridGo, "Hex Grid Setup");
        }

        SetSerializedInt(grid, "_width", 25);
        SetSerializedInt(grid, "_length", 40);

        EditorUtility.DisplayProgressBar("Hex Grid", "Генерация 25×40 гексов...", 0.5f);
        grid.GenerateGrid();
        EditorUtility.ClearProgressBar();

        EditorUtility.SetDirty(gridGo);

        // 2. Камера сверху
#if UNITY_2023_1_OR_NEWER
        Camera cam = Object.FindFirstObjectByType<Camera>();
#else
        Camera cam = Object.FindObjectOfType<Camera>();
#endif
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";
            Undo.RegisterCreatedObjectUndo(camGo, "Hex Grid Setup");
        }

        HexGridCamera camSetup = cam.GetComponent<HexGridCamera>();
        if (camSetup == null)
            camSetup = cam.gameObject.AddComponent<HexGridCamera>();
        SetSerializedField(camSetup, "_grid", grid);
        EditorUtility.SetDirty(cam.gameObject);

        // 3. Player
#if UNITY_2023_1_OR_NEWER
        Player player = Object.FindFirstObjectByType<Player>();
#else
        Player player = Object.FindObjectOfType<Player>();
#endif
        GameObject playerGo;
        if (player != null)
            playerGo = player.gameObject;
        else
        {
            playerGo = new GameObject("Player");
            player = playerGo.AddComponent<Player>();
            Undo.RegisterCreatedObjectUndo(playerGo, "Hex Grid Setup");
        }
        SetSerializedField(player, "_grid", grid);
        EditorUtility.SetDirty(playerGo);

        // 4. InputManager
#if UNITY_2023_1_OR_NEWER
        HexInputManager input = Object.FindFirstObjectByType<HexInputManager>();
#else
        HexInputManager input = Object.FindObjectOfType<HexInputManager>();
#endif
        GameObject inputGo;
        if (input != null)
            inputGo = input.gameObject;
        else
        {
            inputGo = new GameObject("InputManager");
            input = inputGo.AddComponent<HexInputManager>();
            Undo.RegisterCreatedObjectUndo(inputGo, "Hex Grid Setup");
        }
        SetSerializedField(input, "_camera", cam);
        SetSerializedField(input, "_grid", grid);
        SetSerializedField(input, "_player", player);
        var holdIndicator = AssetDatabase.LoadAssetAtPath<HoldTargetIndicator>("Assets/Resources/HoldTargetIndicator.prefab");
        if (holdIndicator != null)
            SetSerializedField(input, "_holdIndicatorPrefab", holdIndicator);
        EditorUtility.SetDirty(inputGo);

        Selection.activeGameObject = gridGo;
        SceneView.RepaintAll();
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log("Hex Grid: сцена настроена (HexGrid, камера, Player, InputManager). Сетка 25×40 сгенерирована.");
    }

    private static void SetSerializedField(Object obj, string fieldName, Object value)
    {
        if (obj == null) return;
        SerializedObject so = new SerializedObject(obj);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop != null)
        {
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedInt(Object obj, string fieldName, int value)
    {
        if (obj == null) return;
        SerializedObject so = new SerializedObject(obj);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop != null)
        {
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
