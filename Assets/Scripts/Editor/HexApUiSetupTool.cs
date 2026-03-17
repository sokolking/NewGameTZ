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
        GameObject buttonGo = new GameObject("EndTurn Button");
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
}

