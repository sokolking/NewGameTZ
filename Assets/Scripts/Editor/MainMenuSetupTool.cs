using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools → Hex Grid → Setup Main Menu UI — создаёт простое главное меню на текущей сцене:
/// фон, кнопки New Game / Settings / Quit и панель настроек.
/// </summary>
public static class MainMenuSetupTool
{
    private const string MenuPath = "Tools/Hex Grid/Setup Main Menu UI";

    [MenuItem(MenuPath)]
    public static void SetupMainMenuUi()
    {
        // Канвас
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
            Undo.RegisterCreatedObjectUndo(canvasGo, "Main Menu UI");
        }

        // Удаляем старое меню (если было)
        Transform oldMainMenu = canvasGo.transform.Find("MainMenuUI");
        if (oldMainMenu != null)
            Object.DestroyImmediate(oldMainMenu.gameObject);
        Transform oldBg = canvasGo.transform.Find("MainMenu Background");
        if (oldBg != null)
            Object.DestroyImmediate(oldBg.gameObject);
        Transform oldSettings = canvasGo.transform.Find("SettingsPanel");
        if (oldSettings != null)
            Object.DestroyImmediate(oldSettings.gameObject);

        // Фон
        GameObject bgGo = new GameObject("MainMenu Background", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(canvasGo.transform, false);
        RectTransform bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        Image bgImg = bgGo.GetComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

        // Корень меню
        GameObject menuRoot = new GameObject("MainMenuUI", typeof(RectTransform), typeof(MainMenuUI));
        menuRoot.transform.SetParent(canvasGo.transform, false);
        RectTransform menuRt = menuRoot.GetComponent<RectTransform>();
        menuRt.anchorMin = new Vector2(0.5f, 0.5f);
        menuRt.anchorMax = new Vector2(0.5f, 0.5f);
        menuRt.pivot = new Vector2(0.5f, 0.5f);
        menuRt.anchoredPosition = Vector2.zero;
        menuRt.sizeDelta = new Vector2(400f, 300f);

        MainMenuUI mm = menuRoot.GetComponent<MainMenuUI>();

        // Утилита для создания кнопки с абсолютным позиционированием
        Button CreateButton(string name, string label, float anchoredY)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(menuRoot.transform, false);
            Image img = btnGo.GetComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.sizeDelta = new Vector2(220f, 40f);

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

        Button newGameBtn = CreateButton("Button_NewGame", "New Game", 40f);
        Button settingsBtn = CreateButton("Button_Settings", "Settings", 0f);
        Button quitBtn = CreateButton("Button_Quit", "Quit", -40f);

        // Панель настроек
        GameObject settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
        settingsPanel.transform.SetParent(canvasGo.transform, false);
        RectTransform spRt = settingsPanel.GetComponent<RectTransform>();
        spRt.anchorMin = new Vector2(0.5f, 0.5f);
        spRt.anchorMax = new Vector2(0.5f, 0.5f);
        spRt.pivot = new Vector2(0.5f, 0.5f);
        spRt.anchoredPosition = Vector2.zero;
        spRt.sizeDelta = new Vector2(300f, 200f);
        Image spImg = settingsPanel.GetComponent<Image>();
        spImg.color = new Color(0f, 0f, 0f, 0.8f);

        // Текст заголовка настроек
        GameObject spTextGo = new GameObject("Title", typeof(RectTransform), typeof(Text));
        spTextGo.transform.SetParent(settingsPanel.transform, false);
        RectTransform spTextRt = spTextGo.GetComponent<RectTransform>();
        spTextRt.anchorMin = new Vector2(0.5f, 1f);
        spTextRt.anchorMax = new Vector2(0.5f, 1f);
        spTextRt.pivot = new Vector2(0.5f, 1f);
        spTextRt.anchoredPosition = new Vector2(0f, -20f);
        spTextRt.sizeDelta = new Vector2(260f, 30f);
        Text spText = spTextGo.GetComponent<Text>();
        spText.text = "Settings";
        spText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        spText.alignment = TextAnchor.MiddleCenter;
        spText.color = Color.white;

        // Текст с текущим разрешением
        GameObject resLabelGo = new GameObject("ResolutionLabel", typeof(RectTransform), typeof(Text));
        resLabelGo.transform.SetParent(settingsPanel.transform, false);
        RectTransform resLabelRt = resLabelGo.GetComponent<RectTransform>();
        resLabelRt.anchorMin = new Vector2(0.5f, 0.5f);
        resLabelRt.anchorMax = new Vector2(0.5f, 0.5f);
        resLabelRt.pivot = new Vector2(0.5f, 0.5f);
        resLabelRt.anchoredPosition = new Vector2(0f, 20f);
        resLabelRt.sizeDelta = new Vector2(200f, 30f);
        Text resLabel = resLabelGo.GetComponent<Text>();
        resLabel.text = "Resolution";
        resLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        resLabel.alignment = TextAnchor.MiddleCenter;
        resLabel.color = Color.white;

        GameObject resTextGo = new GameObject("ResolutionText", typeof(RectTransform), typeof(Text));
        resTextGo.transform.SetParent(settingsPanel.transform, false);
        RectTransform resTextRt = resTextGo.GetComponent<RectTransform>();
        resTextRt.anchorMin = new Vector2(0.5f, 0.5f);
        resTextRt.anchorMax = new Vector2(0.5f, 0.5f);
        resTextRt.pivot = new Vector2(0.5f, 0.5f);
        resTextRt.anchoredPosition = new Vector2(0f, -10f);
        resTextRt.sizeDelta = new Vector2(200f, 30f);
        Text resText = resTextGo.GetComponent<Text>();
        resText.text = "";
        resText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        resText.alignment = TextAnchor.MiddleCenter;
        resText.color = Color.white;

        // Кнопки переключения разрешения и применения
        Button CreateSettingsButton(string name, string label, Vector2 anchoredPos, Vector2 size)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(settingsPanel.transform, false);
            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

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

        Button resPrevBtn = CreateSettingsButton("Button_ResolutionPrev", "<", new Vector2(-70f, -50f), new Vector2(40f, 30f));
        Button resNextBtn = CreateSettingsButton("Button_ResolutionNext", ">", new Vector2(70f, -50f), new Vector2(40f, 30f));
        Button resApplyBtn = CreateSettingsButton("Button_ResolutionApply", "Apply", new Vector2(0f, -50f), new Vector2(80f, 30f));

        // Кнопка Close на панели настроек
        GameObject closeBtnGo = new GameObject("Button_CloseSettings", typeof(RectTransform), typeof(Image), typeof(Button));
        closeBtnGo.transform.SetParent(settingsPanel.transform, false);
        RectTransform closeRt = closeBtnGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 20f);
        closeRt.sizeDelta = new Vector2(120f, 30f);
        Image closeImg = closeBtnGo.GetComponent<Image>();
        closeImg.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
        Button closeBtn = closeBtnGo.GetComponent<Button>();
        GameObject closeTextGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        closeTextGo.transform.SetParent(closeBtnGo.transform, false);
        RectTransform closeTextRt = closeTextGo.GetComponent<RectTransform>();
        closeTextRt.anchorMin = Vector2.zero;
        closeTextRt.anchorMax = Vector2.one;
        closeTextRt.offsetMin = Vector2.zero;
        closeTextRt.offsetMax = Vector2.zero;
        Text closeTxt = closeTextGo.GetComponent<Text>();
        closeTxt.text = "Close";
        closeTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        closeTxt.alignment = TextAnchor.MiddleCenter;
        closeTxt.color = Color.white;

        // Настройки MainMenuUI через SerializedObject
        SerializedObject so = new SerializedObject(mm);
        so.FindProperty("_settingsPanel").objectReferenceValue = settingsPanel;
        so.FindProperty("_resolutionText").objectReferenceValue = resText;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Привязка кнопок к методам MainMenuUI
        newGameBtn.onClick.AddListener(mm.OnNewGameClicked);
        settingsBtn.onClick.AddListener(mm.OnSettingsClicked);
        quitBtn.onClick.AddListener(mm.OnQuitClicked);
        closeBtn.onClick.AddListener(mm.OnCloseSettingsClicked);

        resPrevBtn.onClick.AddListener(mm.OnResolutionPrevious);
        resNextBtn.onClick.AddListener(mm.OnResolutionNext);
        resApplyBtn.onClick.AddListener(mm.OnApplyResolution);

        settingsPanel.SetActive(false);

        Selection.activeGameObject = menuRoot;
        Debug.Log("Hex Grid: Main Menu UI пересоздан (фон, New Game / Settings / Quit, панель настроек).");
    }
}

