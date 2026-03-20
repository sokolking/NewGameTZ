using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor.SceneManagement;

/// <summary>
/// Tools → Hex Grid → Setup Main Menu UI — главное меню: фон, AuthPanel (логин/пароль), галки,
/// кнопки Find Game / Settings / Quit и панель настроек.
/// </summary>
public static class MainMenuSetupTool
{
    private const string MenuPath = "Tools/Hex Grid/Setup Main Menu UI";
    private const string AddTogglesMenuPath = "Tools/Hex Grid/Add Main Menu Toggles";
    private const string AddAuthMenuPath = "Tools/Hex Grid/Add Main Menu Auth Panel";

    [MenuItem(AddAuthMenuPath)]
    public static void AddAuthPanelToExistingMainMenu()
    {
#if UNITY_2023_1_OR_NEWER
        MainMenuUI mm = Object.FindFirstObjectByType<MainMenuUI>();
#else
        MainMenuUI mm = Object.FindObjectOfType<MainMenuUI>();
#endif
        if (mm == null)
        {
            Debug.LogError("Hex Grid: на сцене нет MainMenuUI.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(mm.gameObject, "Add Main Menu Auth Panel");
        Transform root = mm.transform;

        Transform existing = root.Find("AuthPanel");
        if (existing != null)
        {
            InputField login = existing.Find("LoginInputField")?.GetComponent<InputField>();
            InputField pass = existing.Find("PasswordInputField")?.GetComponent<InputField>();
            if (login != null && pass != null)
            {
                SerializedObject soMm = new SerializedObject(mm);
                soMm.FindProperty("_loginInputField").objectReferenceValue = login;
                soMm.FindProperty("_passwordInputField").objectReferenceValue = pass;
                soMm.ApplyModifiedProperties();
                EditorUtility.SetDirty(mm);
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(mm.gameObject.scene);
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("Hex Grid: AuthPanel уже есть — ссылки на поля обновлены в MainMenuUI.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        (InputField loginField, InputField passwordField) = CreateAuthPanel(root);

        SerializedObject so = new SerializedObject(mm);
        so.FindProperty("_loginInputField").objectReferenceValue = loginField;
        so.FindProperty("_passwordInputField").objectReferenceValue = passwordField;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(mm);
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(mm.gameObject.scene);

        Selection.activeGameObject = loginField.transform.parent.gameObject;
        Debug.Log("Hex Grid: AuthPanel добавлен и привязан к MainMenuUI.");
    }

    [MenuItem(AddTogglesMenuPath)]
    public static void AddTogglesToExistingMainMenu()
    {
#if UNITY_2023_1_OR_NEWER
        MainMenuUI mm = Object.FindFirstObjectByType<MainMenuUI>();
#else
        MainMenuUI mm = Object.FindObjectOfType<MainMenuUI>();
#endif
        if (mm == null)
        {
            Debug.LogError("Hex Grid: на сцене нет MainMenuUI.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(mm.gameObject, "Add Main Menu Toggles");
        Transform root = mm.transform;

        Toggle solo = root.Find("Toggle_SoloVsMonster")?.GetComponent<Toggle>();
        if (solo == null)
            solo = CreateMenuToggle(root, "Toggle_SoloVsMonster", "Бой с монстром (соло)", 110f);

        Toggle dbg = root.Find("Toggle_Debug")?.GetComponent<Toggle>();
        if (dbg == null)
            dbg = CreateMenuToggle(root, "Toggle_Debug", "Debug (localhost)", 75f);

        solo.isOn = false;
        dbg.isOn = BattleServerRuntime.UseDebugLocalhost;

        SerializedObject so = new SerializedObject(mm);
        so.FindProperty("_soloVsMonsterToggle").objectReferenceValue = solo;
        so.FindProperty("_debugLocalhostToggle").objectReferenceValue = dbg;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(mm);
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(mm.gameObject.scene);

        Selection.activeGameObject = mm.gameObject;
        Debug.Log("Hex Grid: галки «Бой с монстром» и «Debug (localhost)» добавлены/обновлены и привязаны к MainMenuUI.");
    }

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
        menuRt.sizeDelta = new Vector2(400f, 420f);

        MainMenuUI mm = menuRoot.GetComponent<MainMenuUI>();

        (InputField loginField, InputField passwordField) = CreateAuthPanel(menuRoot.transform);

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

        Button findGameBtn = CreateButton("Button_FindGame", "Find Game", 40f);
        Button settingsBtn = CreateButton("Button_Settings", "Settings", 0f);
        Button quitBtn = CreateButton("Button_Quit", "Quit", -40f);

        Toggle soloToggle = CreateMenuToggle(menuRoot.transform, "Toggle_SoloVsMonster", "Бой с монстром (соло)", 110f);
        soloToggle.isOn = false;
        Toggle debugToggle = CreateMenuToggle(menuRoot.transform, "Toggle_Debug", "Debug (localhost)", 75f);
        debugToggle.isOn = BattleServerRuntime.UseDebugLocalhost;

        // Текст статуса матчмейкинга («Searching for opponent...»)
        GameObject statusGo = new GameObject("StatusText", typeof(RectTransform), typeof(Text));
        statusGo.transform.SetParent(menuRoot.transform, false);
        RectTransform statusRt = statusGo.GetComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0.5f, 0.5f);
        statusRt.anchorMax = new Vector2(0.5f, 0.5f);
        statusRt.pivot = new Vector2(0.5f, 0.5f);
        statusRt.anchoredPosition = new Vector2(0f, -90f);
        statusRt.sizeDelta = new Vector2(320f, 28f);
        Text statusTxt = statusGo.GetComponent<Text>();
        statusTxt.text = "";
        statusTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusTxt.alignment = TextAnchor.MiddleCenter;
        statusTxt.color = new Color(0.8f, 0.8f, 0.8f, 1f);

        var matchmaking = menuRoot.AddComponent<MainMenuMatchmaking>();
        var soMatchmaking = new SerializedObject(matchmaking);
        soMatchmaking.FindProperty("_statusText").objectReferenceValue = statusTxt;
        soMatchmaking.FindProperty("_gameSceneName").stringValue = "MainScene";
        soMatchmaking.ApplyModifiedPropertiesWithoutUndo();

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
        so.FindProperty("_matchmaking").objectReferenceValue = matchmaking;
        so.FindProperty("_soloVsMonsterToggle").objectReferenceValue = soloToggle;
        so.FindProperty("_debugLocalhostToggle").objectReferenceValue = debugToggle;
        so.FindProperty("_loginInputField").objectReferenceValue = loginField;
        so.FindProperty("_passwordInputField").objectReferenceValue = passwordField;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Привязка кнопок к методам MainMenuUI
        findGameBtn.onClick.AddListener(mm.OnFindGameClicked);
        settingsBtn.onClick.AddListener(mm.OnSettingsClicked);
        quitBtn.onClick.AddListener(mm.OnQuitClicked);
        closeBtn.onClick.AddListener(mm.OnCloseSettingsClicked);

        resPrevBtn.onClick.AddListener(mm.OnResolutionPrevious);
        resNextBtn.onClick.AddListener(mm.OnResolutionNext);
        resApplyBtn.onClick.AddListener(mm.OnApplyResolution);

        settingsPanel.SetActive(false);

        Selection.activeGameObject = menuRoot;
        Debug.Log("Hex Grid: Main Menu UI пересоздан (фон, AuthPanel, галки соло/дебаг, Find Game / Settings / Quit, панель настроек).");
    }

    /// <summary>Панель логина/пароля: AuthPanel/LoginInputField, AuthPanel/PasswordInputField (как ожидает MainMenuUI).</summary>
    private static (InputField login, InputField password) CreateAuthPanel(Transform menuRoot)
    {
        var authPanelGo = new GameObject("AuthPanel", typeof(RectTransform));
        authPanelGo.transform.SetParent(menuRoot, false);
        var authPanelRect = authPanelGo.GetComponent<RectTransform>();
        authPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        authPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        authPanelRect.pivot = new Vector2(0.5f, 0.5f);
        authPanelRect.anchoredPosition = new Vector2(0f, 140f);
        authPanelRect.sizeDelta = new Vector2(320f, 90f);

        InputField loginField = CreateAuthInputRow(
            authPanelRect,
            "Login",
            "LoginInputField",
            "Логин",
            "test",
            new Vector2(0f, 22f),
            isPassword: false);

        InputField passwordField = CreateAuthInputRow(
            authPanelRect,
            "Password",
            "PasswordInputField",
            "Пароль",
            "test",
            new Vector2(0f, -22f),
            isPassword: true);

        Undo.RegisterCreatedObjectUndo(authPanelGo, "Auth Panel");
        return (loginField, passwordField);
    }

    private static InputField CreateAuthInputRow(
        RectTransform parent,
        string labelObjectName,
        string inputObjectName,
        string labelText,
        string defaultValue,
        Vector2 anchoredPosition,
        bool isPassword)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var labelGo = new GameObject(labelObjectName, typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(parent, false);
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = anchoredPosition + new Vector2(-105f, 0f);
        labelRect.sizeDelta = new Vector2(80f, 28f);
        var label = labelGo.GetComponent<Text>();
        label.font = font;
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = Color.white;
        label.text = labelText;

        var inputGo = new GameObject(inputObjectName, typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(parent, false);
        var inputRect = inputGo.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.anchoredPosition = anchoredPosition + new Vector2(45f, 0f);
        inputRect.sizeDelta = new Vector2(190f, 32f);
        var inputImage = inputGo.GetComponent<Image>();
        inputImage.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(inputGo.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 6f);
        textRect.offsetMax = new Vector2(-10f, -6f);
        var text = textGo.GetComponent<Text>();
        text.font = font;
        text.fontSize = 15;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.text = defaultValue;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        placeholderGo.transform.SetParent(inputGo.transform, false);
        var placeholderRect = placeholderGo.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(10f, 6f);
        placeholderRect.offsetMax = new Vector2(-10f, -6f);
        var placeholder = placeholderGo.GetComponent<Text>();
        placeholder.font = font;
        placeholder.fontSize = 15;
        placeholder.alignment = TextAnchor.MiddleLeft;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.text = labelText;

        var inputField = inputGo.GetComponent<InputField>();
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.targetGraphic = inputImage;
        inputField.text = defaultValue;
        if (isPassword)
            inputField.contentType = InputField.ContentType.Password;

        return inputField;
    }

    /// <summary>Создаёт стандартный UI Toggle (чекбокс + подпись) под родителем меню.</summary>
    private static Toggle CreateMenuToggle(Transform parent, string objectName, string label, float anchoredY)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var row = new GameObject(objectName, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = new Vector2(0f, anchoredY);
        rowRt.sizeDelta = new Vector2(300f, 28f);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(row.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(0f, 0.5f);
        bgRt.pivot = new Vector2(0f, 0.5f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(22f, 22f);
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0.35f, 0.35f, 0.4f, 1f);
        bgImg.raycastTarget = true;

        var mark = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        mark.transform.SetParent(bg.transform, false);
        var markRt = mark.GetComponent<RectTransform>();
        markRt.anchorMin = Vector2.zero;
        markRt.anchorMax = Vector2.one;
        markRt.offsetMin = new Vector2(4f, 4f);
        markRt.offsetMax = new Vector2(-4f, -4f);
        var markImg = mark.GetComponent<Image>();
        markImg.color = new Color(0.2f, 0.85f, 0.35f, 1f);
        markImg.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(row.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 1f);
        labelRt.offsetMin = new Vector2(30f, 0f);
        labelRt.offsetMax = Vector2.zero;
        var labelTxt = labelGo.GetComponent<Text>();
        labelTxt.font = font;
        labelTxt.fontSize = 15;
        labelTxt.alignment = TextAnchor.MiddleLeft;
        labelTxt.color = Color.white;
        labelTxt.text = label;
        labelTxt.raycastTarget = false;

        var toggle = row.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = markImg;
        toggle.isOn = false;

        Undo.RegisterCreatedObjectUndo(row, "Main Menu Toggle");
        return toggle;
    }
}

