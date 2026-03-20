using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools → Hex Grid → Setup In-Game Menu — создаёт панель паузы в игровой сцене и вешает InGameMenuUI.
/// ESC открывает/закрывает панель, кнопки Resume/Main Menu работают сразу.
/// </summary>
public static class InGameMenuSetupTool
{
    private const string MenuPath = "Tools/Hex Grid/Setup In-Game Menu";

    [MenuItem(MenuPath)]
    public static void SetupInGameMenu()
    {
        // Найти/создать Canvas
        Canvas canvas;
#if UNITY_2023_1_OR_NEWER
        canvas = Object.FindFirstObjectByType<Canvas>();
#else
        canvas = Object.FindObjectOfType<Canvas>();
#endif

        GameObject canvasGo;
        if (canvas != null)
        {
            canvasGo = canvas.gameObject;
        }
        else
        {
            canvasGo = new GameObject("Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGo, "In-Game Menu UI");
        }

        // Удалить старую панель паузы, если была
        Transform oldPanel = canvasGo.transform.Find(UiHierarchyNames.PauseMenuPanel);
        if (oldPanel != null)
            Object.DestroyImmediate(oldPanel.gameObject);

        // Корень контроллера InGameMenuUI
        InGameMenuUI controller = canvasGo.GetComponent<InGameMenuUI>();
        if (controller == null)
            controller = canvasGo.AddComponent<InGameMenuUI>();

        // Панель паузы
        GameObject panelGo = new GameObject(UiHierarchyNames.PauseMenuPanel, typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(canvasGo.transform, false);
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(260f, 180f);

        Image panelImg = panelGo.GetComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.8f);

        // Заголовок "Paused"
        GameObject titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text));
        titleGo.transform.SetParent(panelGo.transform, false);
        RectTransform titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 1f);
        titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -20f);
        titleRt.sizeDelta = new Vector2(220f, 30f);

        Text titleText = titleGo.GetComponent<Text>();
        titleText.text = "Paused";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;

        // Кнопки Resume / Main Menu
        Button CreateButton(string name, string label, float anchoredY)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(panelGo.transform, false);

            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.sizeDelta = new Vector2(180f, 32f);

            Image img = btnGo.GetComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

            GameObject txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(btnGo.transform, false);
            RectTransform txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            Text txt = txtGo.GetComponent<Text>();
            txt.text = label;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;

            return btnGo.GetComponent<Button>();
        }

        Button resumeBtn = CreateButton(UiHierarchyNames.PauseButtonResume, "Resume", 20f);
        Button mainMenuBtn = CreateButton(UiHierarchyNames.PauseButtonMainMenu, "Main Menu", -20f);

        // Привязка панели к контроллеру
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("_menuPanel").objectReferenceValue = panelGo;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Очистить и повесить события
        resumeBtn.onClick.RemoveAllListeners();
        resumeBtn.onClick.AddListener(controller.OnResumeClicked);

        mainMenuBtn.onClick.RemoveAllListeners();
        mainMenuBtn.onClick.AddListener(controller.OnMainMenuClicked);

        panelGo.SetActive(false);

        Selection.activeGameObject = panelGo;
        Debug.Log("Hex Grid: In-Game Menu создан (ESC открывает/закрывает меню паузы).");
    }
}

