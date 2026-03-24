#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Одноразовая сборка SettingsPanel в EscScene: слева вкладки «Аудио» / «Видео», справа страницы с теми же именами.
/// Запуск: <b>Tools → Esc Scene → Setup Settings Panel Tabs</b> (откройте EscScene заранее).
/// </summary>
public static class EscSettingsPanelSetupTool
{
    const string EscScenePath = "Assets/Scenes/EscScene.unity";

    [MenuItem("Tools/Esc Scene/Setup Settings Panel Tabs")]
    public static void SetupSettingsPanelTabs()
    {
        var scene = EditorSceneManager.OpenScene(EscScenePath, OpenSceneMode.Single);

        GameObject panelGo = FindSettingsPanelInLoadedScene();
        if (panelGo == null)
        {
            Debug.LogError("EscSettingsPanelSetupTool: не найден GameObject «SettingsPanel» в открытой сцене.");
            return;
        }

        if (panelGo.transform.Find("BodyRow") != null)
        {
            Debug.LogWarning("EscSettingsPanelSetupTool: уже есть «BodyRow» — повторная настройка отменена.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(panelGo, "Setup Esc Settings Tabs");

        var bodyRow = CreateChild(panelGo.transform, "BodyRow");
        StretchFull(bodyRow.GetComponent<RectTransform>(), 8f, 8f, 8f, 40f);
        var hlg = bodyRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12f;
        hlg.childAlignment = TextAnchor.UpperLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        var leftNav = CreateChild(bodyRow.transform, "LeftNav");
        var leftLe = leftNav.AddComponent<LayoutElement>();
        leftLe.minWidth = 140f;
        leftLe.preferredWidth = 150f;
        leftLe.flexibleWidth = 0f;
        var vlg = leftNav.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(4, 4, 4, 4);

        CreateTabButton(leftNav.transform, "Button_TabAudio", "Аудио");
        CreateTabButton(leftNav.transform, "Button_TabVideo", "Видео");

        var rightStack = CreateChild(bodyRow.transform, "RightStack");
        var stackLe = rightStack.AddComponent<LayoutElement>();
        stackLe.flexibleWidth = 1f;
        stackLe.minWidth = 200f;

        var videoPage = CreateChild(rightStack.transform, "VideoPage");
        StretchFull(videoPage.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

        var audioPage = CreateChild(rightStack.transform, "AudioPage");
        audioPage.SetActive(false);
        StretchFull(audioPage.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

        ReparentVideoWidgets(panelGo.transform, videoPage.transform);

        BuildAudioPage(audioPage.transform);

        var tabs = panelGo.GetComponent<EscSettingsTabsController>();
        if (tabs == null)
            tabs = Undo.AddComponent<EscSettingsTabsController>(panelGo);

        SerializedObject so = new SerializedObject(tabs);
        so.FindProperty("_videoPage").objectReferenceValue = videoPage;
        so.FindProperty("_audioPage").objectReferenceValue = audioPage;
        so.FindProperty("_tabVideoButton").objectReferenceValue =
            leftNav.transform.Find("Button_TabVideo")?.GetComponent<Button>();
        so.FindProperty("_tabAudioButton").objectReferenceValue =
            leftNav.transform.Find("Button_TabAudio")?.GetComponent<Button>();
        so.FindProperty("_masterVolumeSlider").objectReferenceValue =
            audioPage.transform.Find("AudioBlock/Slider_MasterVolume")?.GetComponent<Slider>();
        so.FindProperty("_muteToggle").objectReferenceValue =
            audioPage.transform.Find("AudioBlock/RowMute/Toggle_MuteAudio")?.GetComponent<Toggle>();
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("EscSettingsPanelSetupTool: готово. Проверьте EscScene → SettingsPanel.");
    }

    static void ReparentVideoWidgets(Transform settingsPanel, Transform videoPage)
    {
        string[] names =
        {
            "ResolutionLabel",
            "ResolutionText",
            "Button_ResolutionPrev",
            "Button_ResolutionNext",
            "Button_ResolutionApply",
            "Button_CloseSettings",
        };

        foreach (string n in names)
        {
            Transform t = settingsPanel.Find(n);
            if (t == null)
                t = FindDeep(settingsPanel, n);
            if (t != null && t.parent == settingsPanel)
                Undo.SetTransformParent(t, videoPage, "Move to VideoPage");
        }
    }

    static void BuildAudioPage(Transform audioRoot)
    {
        var block = CreateChild(audioRoot, "AudioBlock");
        var blockRt = block.GetComponent<RectTransform>();
        blockRt.anchorMin = new Vector2(0.5f, 0.5f);
        blockRt.anchorMax = new Vector2(0.5f, 0.5f);
        blockRt.pivot = new Vector2(0.5f, 0.5f);
        blockRt.sizeDelta = new Vector2(320f, 140f);
        blockRt.anchoredPosition = Vector2.zero;

        var v = block.AddComponent<VerticalLayoutGroup>();
        v.spacing = 14f;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true;
        v.childControlHeight = false;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        CreateLabel(block.transform, "Label_MasterVolume", "Громкость");

        var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderGo.name = "Slider_MasterVolume";
        Undo.RegisterCreatedObjectUndo(sliderGo, "Slider_MasterVolume");
        sliderGo.transform.SetParent(block.transform, false);
        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        var rowMute = CreateChild(block.transform, "RowMute");
        var h = rowMute.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 12f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = false;
        h.childControlHeight = true;

        var toggleGo = DefaultControls.CreateToggle(new DefaultControls.Resources());
        toggleGo.name = "Toggle_MuteAudio";
        Undo.RegisterCreatedObjectUndo(toggleGo, "Toggle_MuteAudio");
        toggleGo.transform.SetParent(rowMute.transform, false);
        var le = toggleGo.AddComponent<LayoutElement>();
        le.preferredWidth = 28f;
        le.preferredHeight = 28f;

        var labelMute = CreateLabel(rowMute.transform, "Label_Mute", "Без звука");
        var labelLe = labelMute.GetComponent<LayoutElement>();
        if (labelLe != null)
            labelLe.flexibleWidth = 1f;

        Text muteHint = labelMute.GetComponent<Text>();
        if (muteHint != null)
            muteHint.alignment = TextAnchor.MiddleLeft;
    }

    static GameObject CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static void CreateTabButton(Transform parent, string name, string label)
    {
        var go = DefaultControls.CreateButton(new DefaultControls.Resources());
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 36f;
        le.preferredHeight = 40f;
        var txtTr = go.transform.Find("Text");
        if (txtTr != null)
        {
            var t = txtTr.GetComponent<Text>();
            if (t != null)
                t.text = label;
        }
    }

    static GameObject CreateLabel(Transform parent, string name, string text)
    {
        var go = DefaultControls.CreateText(new DefaultControls.Resources());
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        if (t != null)
        {
            t.text = text;
            t.fontSize = 16;
            t.alignment = TextAnchor.MiddleLeft;
        }

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 22f;
        le.preferredHeight = 24f;
        return go;
    }

    static void StretchFull(RectTransform rt, float left, float right, float top, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        rt.localScale = Vector3.one;
    }

    static GameObject FindSettingsPanelInLoadedScene()
    {
        var active = EditorSceneManager.GetActiveScene();
        foreach (var root in active.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "SettingsPanel")
                    return t.gameObject;
            }
        }

        return null;
    }

    static Transform FindDeep(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrEmpty(objectName))
            return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == objectName)
                return t;
        }

        return null;
    }
}
#endif
