#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Создаёт ClientUpdatePanel на Canvas текущей сцены (LoginScene, MainMenu) и вешает <see cref="ClientUpdateGate"/>.
/// </summary>
public static class ClientUpdatePanelSetupTool
{
    [MenuItem("Tools/Hex Grid/Setup Client Update Panel (LoginScene / MainMenu)")]
    public static void Setup()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Client Update Panel", "В сцене не найден Canvas.", "OK");
            return;
        }

        Transform root = canvas.transform;
        Transform existing = root.Find(UiHierarchyNames.ClientUpdatePanel);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Client Update Panel", "ClientUpdatePanel уже есть. Пересоздать?", "Да", "Нет"))
                return;
            Object.DestroyImmediate(existing.gameObject);
        }

        var panelGo = new GameObject(UiHierarchyNames.ClientUpdatePanel, typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(root, false);
        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        var panelImg = panelGo.GetComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.55f);
        panelImg.raycastTarget = true;

        var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        box.transform.SetParent(panelGo.transform, false);
        var boxRt = box.GetComponent<RectTransform>();
        boxRt.anchorMin = new Vector2(0.5f, 0.5f);
        boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(520f, 360f);
        box.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.98f);
        var vlg = box.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 24, 24);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = false;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        CreateText(box.transform, UiHierarchyNames.ClientUpdateMessageText,
            "Доступна новая версия игры.\nСкачайте обновление, чтобы продолжить.", 18, TextAnchor.UpperLeft);

        CreateText(box.transform, UiHierarchyNames.ClientUpdateStatusText, "", 14, TextAnchor.UpperLeft);

        var sliderGo = new GameObject(UiHierarchyNames.ClientUpdateProgressSlider, typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(box.transform, false);
        var slRt = sliderGo.GetComponent<RectTransform>();
        slRt.sizeDelta = new Vector2(0f, 24f);
        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;
        slider.gameObject.SetActive(false);

        var btnGo = new GameObject(UiHierarchyNames.ClientUpdateDownloadButton, typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(box.transform, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.sizeDelta = new Vector2(0f, 44f);
        var btn = btnGo.GetComponent<Button>();
        btn.targetGraphic = btnGo.GetComponent<Image>();
        btnGo.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);

        var btnLabel = new GameObject("Text", typeof(RectTransform), typeof(Text));
        btnLabel.transform.SetParent(btnGo.transform, false);
        var tl = btnLabel.GetComponent<RectTransform>();
        tl.anchorMin = Vector2.zero;
        tl.anchorMax = Vector2.one;
        tl.offsetMin = Vector2.zero;
        tl.offsetMax = Vector2.zero;
        var bt = btnLabel.GetComponent<Text>();
        bt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bt.fontSize = 18;
        bt.color = Color.white;
        bt.alignment = TextAnchor.MiddleCenter;
        bt.text = "Скачать";

        panelGo.SetActive(false);

        if (canvas.GetComponent<ClientUpdateGate>() == null)
            canvas.gameObject.AddComponent<ClientUpdateGate>();

        EditorUtility.SetDirty(canvas.gameObject);
        Debug.Log("[ClientUpdatePanel] Готово: Canvas → ClientUpdatePanel, компонент ClientUpdateGate. Повторите для сцены MainMenu при необходимости.");
    }

    private static void CreateText(Transform parent, string name, string msg, int fontSize, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 100f);
        var tx = go.GetComponent<Text>();
        tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tx.fontSize = fontSize;
        tx.color = Color.white;
        tx.alignment = align;
        tx.text = msg;
        tx.horizontalOverflow = HorizontalWrapMode.Wrap;
        tx.verticalOverflow = VerticalWrapMode.Overflow;
    }
}
#endif
