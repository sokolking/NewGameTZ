using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HexGrid))]
public class HexGridEditor : Editor
{
    private const float ButtonSpacing = 5f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        HexGrid grid = (HexGrid)target;
        EditorGUILayout.Space(ButtonSpacing);

        if (GUILayout.Button("Сгенерировать сетку (25×40)"))
        {
            try
            {
                Undo.RecordObject(grid.gameObject, "HexGrid Generate");
                grid.GenerateGrid();
                MarkDirtyAndRefresh(grid);
                Debug.Log("Сетка гексов сгенерирована.");
            }
            catch (System.Exception e)
            {
                Debug.LogError("Ошибка генерации сетки: " + e.Message);
            }
        }

        if (GUILayout.Button("Очистить сетку"))
        {
            Undo.RecordObject(grid.gameObject, "HexGrid Clear");
            grid.ClearGrid();
            MarkDirtyAndRefresh(grid);
        }
    }

    private static void MarkDirtyAndRefresh(HexGrid grid)
    {
        EditorUtility.SetDirty(grid.gameObject);
        SceneView.RepaintAll();
        Selection.activeGameObject = grid.gameObject;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();
    }
}
