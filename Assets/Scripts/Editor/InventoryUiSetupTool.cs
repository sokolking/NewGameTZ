using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools → Hex Grid → Setup Inventory UI — создаёт Inventory (12 ячеек), Canvas ActiveItemPanel
/// с ActiveItem (Image), ActiveItemAmmoDonut (radial Image), ActiveItemAmmoText (Text),
/// вешает InventoryUI и сохраняет сцену.
/// Родитель: «Front Content Maker» (как у остального боя UI).
/// </summary>
public static class InventoryUiSetupTool
{
    private const string MenuPath = "Tools/Hex Grid/Setup Inventory UI";

    [MenuItem(MenuPath)]
    public static void SetupInventoryUi()
    {
        var front = GameObject.Find(UiHierarchyNames.FrontContentMaker);
        if (front == null)
        {
            EditorUtility.DisplayDialog(
                "Inventory UI",
                "Не найден объект «Front Content Maker». Открой сцену MainScene и повтори.",
                "OK");
            return;
        }

        foreach (var n in new[] { UiHierarchyNames.Inventory })
        {
            var old = GameObject.Find(n);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
        }

        var oldPanel = GameObject.Find(UiHierarchyNames.ActiveItemPanel);
        if (oldPanel != null)
            Undo.DestroyObjectImmediate(oldPanel);
        else
        {
            var legacy = GameObject.Find(UiHierarchyNames.ActiveItem);
            if (legacy != null)
                Undo.DestroyObjectImmediate(legacy);
        }

        var invRoot = CreateInventoryGrid(front.transform);
        var activeGo = CreateActiveItemUnderPanel(front.transform);

        Undo.RegisterCreatedObjectUndo(invRoot, "Inventory UI");

        var invUi = invRoot.GetComponent<InventoryUI>();
        if (invUi == null)
            invUi = Undo.AddComponent<InventoryUI>(invRoot);

        var so = new SerializedObject(invUi);
        so.FindProperty("_inventoryRoot").objectReferenceValue = invRoot.transform;

        var cellImagesProp = so.FindProperty("_cellImages");
        var cellButtonsProp = so.FindProperty("_cellButtons");
        cellImagesProp.arraySize = 12;
        cellButtonsProp.arraySize = 12;

        for (int i = 0; i < 12; i++)
        {
            var cell = invRoot.transform.Find(UiHierarchyNames.InventoryCellName(i + 1));
            if (cell == null)
                continue;

            var imgTr = cell.Find(UiHierarchyNames.InventoryCellImage);
            if (imgTr != null)
                cellImagesProp.GetArrayElementAtIndex(i).objectReferenceValue = imgTr.GetComponent<Image>();

            cellButtonsProp.GetArrayElementAtIndex(i).objectReferenceValue = cell.GetComponent<Button>();
        }

#if UNITY_2023_1_OR_NEWER
        var player = Object.FindFirstObjectByType<Player>();
#else
        var player = Object.FindObjectOfType<Player>();
#endif
        so.FindProperty("_player").objectReferenceValue = player;

        so.FindProperty("_activeWeaponImage").objectReferenceValue = activeGo.GetComponent<Image>();

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(invUi);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Inventory UI] Готово: Inventory + ActiveItemPanel → ActiveItem под «Front Content Maker». Сохрани сцену (Ctrl+S).");
    }

    /// <summary>
    /// Canvas ActiveItemPanel с дочерними ActiveItem (Image), ActiveItemAmmoDonut (radial Image),
    /// ActiveItemAmmoText (Text). Возвращает ActiveItem для ссылки в InventoryUI.
    /// </summary>
    private static GameObject CreateActiveItemUnderPanel(Transform parent)
    {
        var panelGo = new GameObject(UiHierarchyNames.ActiveItemPanel);
        panelGo.transform.SetParent(parent, false);

        var canvas = panelGo.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;
        panelGo.AddComponent<CanvasScaler>();
        panelGo.AddComponent<GraphicRaycaster>();

        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0f, 0f);
        panelRt.pivot = new Vector2(0f, 0f);
        panelRt.anchoredPosition = new Vector2(20f, 140f);
        panelRt.sizeDelta = new Vector2(64f, 64f);

        var child = new GameObject(UiHierarchyNames.ActiveItem);
        child.transform.SetParent(panelGo.transform, false);
        var childRt = child.AddComponent<RectTransform>();
        childRt.anchorMin = new Vector2(0f, 0f);
        childRt.anchorMax = new Vector2(0f, 0f);
        childRt.pivot = new Vector2(0f, 0f);
        childRt.anchoredPosition = Vector2.zero;
        childRt.sizeDelta = new Vector2(64f, 64f);

        var img = child.AddComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = false;

        var donut = new GameObject(UiHierarchyNames.ActiveItemAmmoDonut);
        donut.transform.SetParent(panelGo.transform, false);
        var donutRt = donut.AddComponent<RectTransform>();
        donutRt.anchorMin = new Vector2(0f, 0f);
        donutRt.anchorMax = new Vector2(0f, 0f);
        donutRt.pivot = new Vector2(0f, 0f);
        donutRt.anchoredPosition = new Vector2(-2f, -2f);
        donutRt.sizeDelta = new Vector2(68f, 68f);
        var donutImg = donut.AddComponent<Image>();
        donutImg.color = new Color(1f, 0.74f, 0.24f, 0.9f);
        donutImg.raycastTarget = true;
        donutImg.type = Image.Type.Filled;
        donutImg.fillMethod = Image.FillMethod.Radial360;
        donutImg.fillOrigin = (int)Image.Origin360.Top;
        donutImg.fillClockwise = true;
        donutImg.fillAmount = 0f;

        var ammoTxt = new GameObject(UiHierarchyNames.ActiveItemAmmoText);
        ammoTxt.transform.SetParent(panelGo.transform, false);
        var ammoTxtRt = ammoTxt.AddComponent<RectTransform>();
        ammoTxtRt.anchorMin = new Vector2(0f, 0f);
        ammoTxtRt.anchorMax = new Vector2(0f, 0f);
        ammoTxtRt.pivot = new Vector2(0f, 0f);
        ammoTxtRt.anchoredPosition = new Vector2(0f, -20f);
        ammoTxtRt.sizeDelta = new Vector2(150f, 18f);
        var ammoTxtComp = ammoTxt.AddComponent<Text>();
        ammoTxtComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ammoTxtComp.fontSize = 13;
        ammoTxtComp.alignment = TextAnchor.MiddleLeft;
        ammoTxtComp.color = new Color(0.92f, 0.92f, 0.92f, 0.95f);
        ammoTxtComp.raycastTarget = false;
        ammoTxtComp.text = "";

        Undo.RegisterCreatedObjectUndo(panelGo, "Inventory UI");
        Undo.RegisterCreatedObjectUndo(child, "Inventory UI");
        Undo.RegisterCreatedObjectUndo(donut, "Inventory UI");
        Undo.RegisterCreatedObjectUndo(ammoTxt, "Inventory UI");
        return child;
    }

    private static GameObject CreateInventoryGrid(Transform parent)
    {
        var root = new GameObject(UiHierarchyNames.Inventory);
        root.transform.SetParent(parent, false);
        var rootRt = root.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0f, 0f);
        rootRt.anchorMax = new Vector2(0f, 0f);
        rootRt.pivot = new Vector2(0f, 0f);
        rootRt.anchoredPosition = new Vector2(20f, 20f);
        rootRt.sizeDelta = new Vector2(200f, 148f);

        var grid = root.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(40f, 40f);
        grid.spacing = new Vector2(6f, 6f);
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;

        for (int i = 1; i <= 12; i++)
            CreateCell(root.transform, i);

        return root;
    }

    private static void CreateCell(Transform parent, int index1Based)
    {
        var go = new GameObject(UiHierarchyNames.InventoryCellName(index1Based));
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.14f, 0.18f, 0.92f);
        bg.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.35f, 0.45f, 0.6f, 1f);
        colors.pressedColor = new Color(0.25f, 0.35f, 0.5f, 1f);
        btn.colors = colors;

        var child = new GameObject(UiHierarchyNames.InventoryCellImage);
        child.transform.SetParent(go.transform, false);
        var imgRt = child.AddComponent<RectTransform>();
        imgRt.anchorMin = new Vector2(0.5f, 0.5f);
        imgRt.anchorMax = new Vector2(0.5f, 0.5f);
        imgRt.sizeDelta = new Vector2(32f, 32f);
        imgRt.anchoredPosition = Vector2.zero;

        var icon = child.AddComponent<Image>();
        icon.color = Color.white;
        icon.raycastTarget = false;
    }
}
