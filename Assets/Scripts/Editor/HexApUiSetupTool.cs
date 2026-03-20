using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools → Hex Grid → Setup AP UI — создаёт Canvas, Text и Button и вешает ActionPointsUI.
/// </summary>
public static class HexApUiSetupTool
{
    private const string MenuPath = "Tools/Hex Grid/Setup AP UI";

    [MenuItem(MenuPath)]
    public static void SetupApUi()
    {
#if UNITY_2023_1_OR_NEWER
        Player player = Object.FindFirstObjectByType<Player>();
#else
        Player player = Object.FindObjectOfType<Player>();
#endif
        if (player == null)
        {
            Debug.LogError("Hex Grid: не найден Player в сцене. Сначала настрой сцену (Setup Scene From Scratch).");
            return;
        }

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
            Undo.RegisterCreatedObjectUndo(canvasGo, "Hex AP UI");
        }

        // Text
        GameObject textGo = new GameObject("AP Text");
        textGo.transform.SetParent(canvasGo.transform, false);
        Text text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        RectTransform textRt = text.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0f, 1f);
        textRt.anchorMax = new Vector2(0f, 1f);
        textRt.pivot = new Vector2(0f, 1f);
        textRt.anchoredPosition = new Vector2(10f, -10f);
        textRt.sizeDelta = new Vector2(200f, 30f);

        // Button
        GameObject buttonGo = new GameObject(UiHierarchyNames.EndTurnButton);
        buttonGo.transform.SetParent(canvasGo.transform, false);
        Image buttonImg = buttonGo.AddComponent<Image>();
        buttonImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        Button button = buttonGo.AddComponent<Button>();
        RectTransform btnRt = button.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0f, 1f);
        btnRt.anchorMax = new Vector2(0f, 1f);
        btnRt.pivot = new Vector2(0f, 1f);
        btnRt.anchoredPosition = new Vector2(10f, -50f);
        btnRt.sizeDelta = new Vector2(160f, 30f);

        GameObject btnTextGo = new GameObject("Text");
        btnTextGo.transform.SetParent(buttonGo.transform, false);
        Text btnText = btnTextGo.AddComponent<Text>();
        btnText.text = "Закончить ход";
        btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.white;
        RectTransform btnTextRt = btnText.GetComponent<RectTransform>();
        btnTextRt.anchorMin = Vector2.zero;
        btnTextRt.anchorMax = Vector2.one;
        btnTextRt.offsetMin = Vector2.zero;
        btnTextRt.offsetMax = Vector2.zero;

        // ActionPointsUI
        ActionPointsUI apUi = canvasGo.GetComponent<ActionPointsUI>();
        if (apUi == null)
            apUi = canvasGo.AddComponent<ActionPointsUI>();

        // Log ScrollView под кнопкой
        GameObject scrollGo = new GameObject("LogScrollView", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(canvasGo.transform, false);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 0.4f);
        scrollRt.pivot = new Vector2(0.5f, 0f);
        scrollRt.anchoredPosition = new Vector2(0f, 10f);
        scrollRt.offsetMin = new Vector2(10f, 10f);
        scrollRt.offsetMax = new Vector2(-10f, 0f);
        Image bg = scrollGo.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.3f);

        GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = new Vector2(0f, 0f);
        viewportRt.anchorMax = new Vector2(1f, 1f);
        viewportRt.pivot = new Vector2(0.5f, 0.5f);
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        Image viewportImg = viewportGo.GetComponent<Image>();
        viewportImg.color = new Color(0f, 0f, 0f, 0.2f);
        Mask mask = viewportGo.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.offsetMin = new Vector2(10f, 0f);
        contentRt.offsetMax = new Vector2(-10f, 0f);
        ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject logTextGo = new GameObject("LogText", typeof(RectTransform), typeof(Text));
        logTextGo.transform.SetParent(contentGo.transform, false);
        RectTransform logTextRt = logTextGo.GetComponent<RectTransform>();
        logTextRt.anchorMin = new Vector2(0f, 1f);
        logTextRt.anchorMax = new Vector2(1f, 1f);
        logTextRt.pivot = new Vector2(0.5f, 1f);
        logTextRt.anchoredPosition = Vector2.zero;
        logTextRt.offsetMin = Vector2.zero;
        logTextRt.offsetMax = Vector2.zero;

        Text logText = logTextGo.GetComponent<Text>();
        logText.text = "";
        logText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        logText.fontSize = 14;
        logText.alignment = TextAnchor.UpperLeft;
        logText.horizontalOverflow = HorizontalWrapMode.Wrap;
        logText.verticalOverflow = VerticalWrapMode.Overflow;
        logText.color = Color.white;

        ScrollRect scrollRect = scrollGo.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRt;
        scrollRect.content = contentRt;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = false;

        SerializedObject so = new SerializedObject(apUi);
        so.FindProperty("_player").objectReferenceValue = player;
        so.FindProperty("_apText").objectReferenceValue = text;
        so.FindProperty("_endTurnButton").objectReferenceValue = button;
        so.FindProperty("_logText").objectReferenceValue = logText;
        so.FindProperty("_logScrollRect").objectReferenceValue = scrollRect;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = canvasGo;
        Debug.Log("Hex Grid: AP UI создан (Canvas + AP Text + End Turn Button + Log ScrollView).");
    }

    private const string RoundWaitMenu = "Tools/Hex Grid/Add Round Wait Overlay to AP UI";

    [MenuItem(RoundWaitMenu)]
    public static void AddRoundWaitOverlay()
    {
#if UNITY_2023_1_OR_NEWER
        var apUi = Object.FindFirstObjectByType<ActionPointsUI>();
#else
        var apUi = Object.FindObjectOfType<ActionPointsUI>();
#endif
        if (apUi == null)
        {
            Debug.LogError("Hex Grid: не найден ActionPointsUI. Сначала Setup AP UI.");
            return;
        }

        Transform canvas = apUi.transform;
        while (canvas != null && canvas.GetComponent<Canvas>() == null)
            canvas = canvas.parent;
        if (canvas == null)
        {
            Debug.LogError("Hex Grid: нет Canvas.");
            return;
        }

        Transform old = canvas.Find(UiHierarchyNames.RoundWaitPanel);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject panel = new GameObject(UiHierarchyNames.RoundWaitPanel, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas, false);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        Image pimg = panel.GetComponent<Image>();
        pimg.color = new Color(0f, 0f, 0f, 0.65f);
        pimg.raycastTarget = true;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(panel.transform, false);
        RectTransform lr = labelGo.GetComponent<RectTransform>();
        lr.anchorMin = new Vector2(0.5f, 0.55f);
        lr.anchorMax = new Vector2(0.5f, 0.55f);
        lr.sizeDelta = new Vector2(480f, 40f);
        Text lt = labelGo.GetComponent<Text>();
        lt.text = "Ожидание результата раунда…";
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.fontSize = 20;
        lt.alignment = TextAnchor.MiddleCenter;
        lt.color = Color.white;

        GameObject sliderGo = new GameObject("WaitSlider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(panel.transform, false);
        RectTransform sr = sliderGo.GetComponent<RectTransform>();
        sr.anchorMin = new Vector2(0.5f, 0.45f);
        sr.anchorMax = new Vector2(0.5f, 0.45f);
        sr.sizeDelta = new Vector2(400f, 24f);
        Slider slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0.5f;
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(sliderGo.transform, false);
        RectTransform bgr = bg.GetComponent<RectTransform>();
        bgr.anchorMin = Vector2.zero;
        bgr.anchorMax = Vector2.one;
        bgr.offsetMin = Vector2.zero;
        bgr.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        RectTransform far = fillArea.GetComponent<RectTransform>();
        far.anchorMin = Vector2.zero;
        far.anchorMax = Vector2.one;
        far.offsetMin = new Vector2(8f, 6f);
        far.offsetMax = new Vector2(-8f, -6f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fr = fill.GetComponent<RectTransform>();
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = new Vector2(0.5f, 1f);
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;
        Image fim = fill.GetComponent<Image>();
        fim.color = new Color(0.35f, 0.65f, 0.95f, 1f);
        slider.fillRect = fr;
        slider.targetGraphic = fim;

        panel.SetActive(false);

        SerializedObject so = new SerializedObject(apUi);
        so.FindProperty("_roundWaitPanel").objectReferenceValue = panel;
        so.FindProperty("_roundWaitSlider").objectReferenceValue = slider;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = panel;
        Debug.Log("Hex Grid: RoundWaitPanel добавлен (последний дочерний у Canvas — поверх UI). Перемести в конец иерархии Canvas при необходимости.");
    }

    private const string SkipDialogMenu = "Tools/Hex Grid/Add Skip Dialog (пропуск ОД)";

    /// <summary>
    /// Создаёт панель «Сколько ОД пропустить» в Canvas и проставляет ссылки в ActionPointsUI (без runtime-генерации).
    /// </summary>
    [MenuItem(SkipDialogMenu)]
    public static void AddSkipDialog()
    {
#if UNITY_2023_1_OR_NEWER
        ActionPointsUI apUi = Object.FindFirstObjectByType<ActionPointsUI>();
#else
        ActionPointsUI apUi = Object.FindObjectOfType<ActionPointsUI>();
#endif
        if (apUi == null)
        {
            Debug.LogError("Hex Grid: не найден ActionPointsUI.");
            return;
        }

        Transform canvas = apUi.transform;
        while (canvas != null && canvas.GetComponent<Canvas>() == null)
            canvas = canvas.parent;
        if (canvas == null)
        {
            Debug.LogError("Hex Grid: нет Canvas.");
            return;
        }

        Transform existing = canvas.Find(UiHierarchyNames.SkipDialogPanel);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        Font legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Как в сцене: SkipDialogPanel → прямые дети SkipDialogQuestionText, SkipDialogInput, кнопки (без фона/обёрток).
        GameObject panel = new GameObject(UiHierarchyNames.SkipDialogPanel, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(panel, "Skip Dialog");
        panel.transform.SetParent(canvas, false);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;

        Transform panelTf = panel.transform;

        GameObject qGo = new GameObject(UiHierarchyNames.SkipDialogQuestionText, typeof(RectTransform), typeof(Text));
        qGo.transform.SetParent(panelTf, false);
        RectTransform qrt = qGo.GetComponent<RectTransform>();
        qrt.anchorMin = new Vector2(0.5f, 0.5f);
        qrt.anchorMax = new Vector2(0.5f, 0.5f);
        qrt.pivot = new Vector2(0.5f, 0.5f);
        qrt.sizeDelta = new Vector2(420f, 44f);
        qrt.anchoredPosition = new Vector2(0f, 52f);
        Text qt = qGo.GetComponent<Text>();
        qt.text = "Сколько ОД пропустить?";
        qt.alignment = TextAnchor.MiddleCenter;
        qt.color = Color.white;
        qt.fontSize = 24;
        if (legacyFont != null)
            qt.font = legacyFont;

        GameObject inputGo = new GameObject(UiHierarchyNames.SkipDialogInput, typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(panelTf, false);
        RectTransform irt = inputGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.5f, 0.5f);
        irt.anchorMax = new Vector2(0.5f, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.sizeDelta = new Vector2(200f, 40f);
        irt.anchoredPosition = new Vector2(0f, -6f);
        Image iBg = inputGo.GetComponent<Image>();
        iBg.color = new Color(1f, 1f, 1f, 0.12f);

        GameObject inputTextGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        inputTextGo.transform.SetParent(inputGo.transform, false);
        RectTransform inputTextRt = inputTextGo.GetComponent<RectTransform>();
        inputTextRt.anchorMin = Vector2.zero;
        inputTextRt.anchorMax = Vector2.one;
        inputTextRt.offsetMin = new Vector2(10f, 6f);
        inputTextRt.offsetMax = new Vector2(-10f, -6f);
        Text inputText = inputTextGo.GetComponent<Text>();
        inputText.alignment = TextAnchor.MiddleCenter;
        inputText.color = Color.white;
        inputText.fontSize = 22;
        if (legacyFont != null)
            inputText.font = legacyFont;
        inputText.raycastTarget = false;

        InputField inputField = inputGo.GetComponent<InputField>();
        inputField.targetGraphic = iBg;
        inputField.textComponent = inputText;
        inputField.contentType = InputField.ContentType.IntegerNumber;
        inputField.lineType = InputField.LineType.SingleLine;
        inputField.text = "1";

        Button okBtn = CreateSkipDialogUiButton(panelTf, UiHierarchyNames.SkipDialogOkButton, "OK", new Vector2(-70f, -64f), legacyFont);
        Button cancelBtn = CreateSkipDialogUiButton(panelTf, UiHierarchyNames.SkipDialogCancelButton, "Отмена", new Vector2(70f, -64f), legacyFont);

        panel.SetActive(false);

        SerializedObject so = new SerializedObject(apUi);
        so.FindProperty("_skipDialogPanel").objectReferenceValue = panel;
        so.FindProperty("_skipDialogQuestionText").objectReferenceValue = qt;
        so.FindProperty("_skipDialogInput").objectReferenceValue = inputField;
        so.FindProperty("_skipDialogOkButton").objectReferenceValue = okBtn;
        so.FindProperty("_skipDialogCancelButton").objectReferenceValue = cancelBtn;
        SerializedProperty init = so.FindProperty("_skipDialogInitialInput");
        if (init != null)
            init.stringValue = "1";
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = panel;
        Debug.Log("Hex Grid: SkipDialogPanel добавлен на Canvas и привязан к ActionPointsUI. При необходимости перенеси в конец иерархии Canvas (поверх остального UI).");
    }

    private static Button CreateSkipDialogUiButton(Transform parent, string name, string caption, Vector2 anchoredPos, Font font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(120f, 40f);
        Image bg = go.GetComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        Button button = go.GetComponent<Button>();

        GameObject labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        Text label = labelGo.GetComponent<Text>();
        label.text = caption;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.fontSize = 20;
        if (font != null)
            label.font = font;
        return button;
    }

    private const string BlockOverlayMenuPath = "Tools/Hex Grid/Setup Block Overlay";

    /// <summary>
    /// Создаёт <see cref="UiHierarchyNames.BlockOverlay"/> под корневым Canvas и вешает <see cref="UiBlockOverlaySync"/>.
    /// </summary>
    [MenuItem(BlockOverlayMenuPath)]
    public static void SetupBlockOverlay()
    {
        // Не FindFirstObjectByType<Canvas> — в сцене много Canvas; нужен тот же, что и ActionPointsUI.
        ActionPointsUI apUi;
#if UNITY_2023_1_OR_NEWER
        apUi = Object.FindFirstObjectByType<ActionPointsUI>();
#else
        apUi = Object.FindObjectOfType<ActionPointsUI>();
#endif
        Canvas canvas = apUi != null ? apUi.GetComponent<Canvas>() : null;
        if (canvas == null)
        {
#if UNITY_2023_1_OR_NEWER
            canvas = Object.FindFirstObjectByType<Canvas>();
#else
            canvas = Object.FindObjectOfType<Canvas>();
#endif
        }
        if (canvas == null)
        {
            Debug.LogError("Hex Grid: Canvas с ActionPointsUI не найден в сцене.");
            return;
        }

        Transform root = canvas.transform;
        if (root.Find(UiHierarchyNames.BlockOverlay) != null)
        {
            Debug.LogWarning("Hex Grid: объект BlockOverlay уже есть на Canvas.");
            if (canvas.GetComponent<UiBlockOverlaySync>() == null)
                Undo.AddComponent<UiBlockOverlaySync>(canvas.gameObject);
            return;
        }

        GameObject go = new GameObject(UiHierarchyNames.BlockOverlay, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, "Block Overlay");
        go.transform.SetParent(root, false);

        Transform front = root.Find(UiHierarchyNames.FrontContentMaker);
        int insertIndex = front != null ? front.GetSiblingIndex() + 1 : 0;
        go.transform.SetSiblingIndex(insertIndex);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        // Как в UiBlockOverlaySync: лёгкое затемнение, чтобы оверлей был виден под неполноэкранными панелями.
        img.color = new Color(0f, 0f, 0f, 0.35f);
        img.raycastTarget = true;

        go.SetActive(false);

        if (canvas.GetComponent<UiBlockOverlaySync>() == null)
            Undo.AddComponent<UiBlockOverlaySync>(canvas.gameObject);

        Selection.activeGameObject = go;
        Debug.Log("Hex Grid: BlockOverlay добавлен (прозрачный raycast-слой над Front Content Maker). Запусти сцену — оверлей включается вместе с паузой / ожиданием раунда / диалогом пропуска.");
    }
}

