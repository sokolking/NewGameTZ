#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Одноразовая сборка SettingsPanel в EscScene: слева вкладки «Аудио» / «Видео», справа страницы с теми же именами.
/// Запуск: <b>Tools → Esc Scene → Setup Settings Panel Tabs</b> (откройте EscScene заранее).
/// Язык и локализация Esc: <b>Tools → Esc Scene → Setup Language Page + Esc UI localization</b> (не трогает RectTransform существующего LanguagePage).
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

    /// <summary>
    /// Добавляет на LanguagePage виджеты как у разрешения (дубликаты RectTransform с VideoPage),
    /// вешает <see cref="EscLanguagePickerUI"/>, проставляет <see cref="LocalizedText"/> по EscScene.
    /// Не меняет <see cref="RectTransform"/> существующего LanguagePage (только дочерние объекты).
    /// </summary>
    [MenuItem("Tools/Esc Scene/Setup Language Page + Esc UI localization")]
    public static void SetupLanguagePageAndEscLocalization()
    {
        var scene = EditorSceneManager.OpenScene(EscScenePath, OpenSceneMode.Single);
        GameObject panelGo = FindSettingsPanelInLoadedScene();
        if (panelGo == null)
        {
            Debug.LogError("EscSettingsPanelSetupTool: не найден GameObject «SettingsPanel».");
            return;
        }

        Transform leftNav = FindDeep(panelGo.transform, "LeftNav");
        Transform rightStack = FindDeep(panelGo.transform, "RightStack");
        Transform videoPage = FindDeep(panelGo.transform, "VideoPage");
        if (leftNav == null || rightStack == null || videoPage == null)
        {
            Debug.LogError("EscSettingsPanelSetupTool: не найдены LeftNav, RightStack или VideoPage.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(panelGo, "Esc language + localization");

        Transform langTr = FindDeep(panelGo.transform, "LanguagePage");
        GameObject languagePage = langTr != null ? langTr.gameObject : null;
        if (languagePage == null)
        {
            languagePage = CreateChild(rightStack, "LanguagePage");
            StretchFull(languagePage.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            languagePage.SetActive(false);
        }
        else if (langTr.parent != rightStack)
        {
            Debug.LogWarning(
                "EscSettingsPanelSetupTool: LanguagePage не под RightStack — переносим под RightStack (как VideoPage).");
            Undo.SetTransformParent(langTr, rightStack, "Move LanguagePage under RightStack");
            StretchFull(langTr.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            languagePage.SetActive(false);
        }

        RemoveLegacyLanguageBlockIfPresent(languagePage.transform);
        BuildLanguagePageFromVideoTemplates(videoPage, languagePage.transform);

        GameObject tabGo = leftNav.Find("Button_TabLanguage")?.gameObject;
        if (tabGo == null)
        {
            tabGo = DefaultControls.CreateButton(new DefaultControls.Resources());
            tabGo.name = "Button_TabLanguage";
            Undo.RegisterCreatedObjectUndo(tabGo, "Button_TabLanguage");
            tabGo.transform.SetParent(leftNav, false);
            var le = tabGo.AddComponent<LayoutElement>();
            le.minHeight = 36f;
            le.preferredHeight = 40f;
            Transform txtTr = tabGo.transform.Find("Text");
            if (txtTr != null)
            {
                var t = txtTr.GetComponent<Text>();
                if (t != null)
                    t.text = "Language";
                AddLocalizedTextOnGameObject(txtTr.gameObject, "esc.settings.menu.language");
            }
        }
        else
        {
            Transform tabTxt = tabGo.transform.Find("Text") ?? tabGo.transform.Find("Text (Legacy)");
            if (tabTxt != null)
            {
                var te = tabTxt.GetComponent<Text>();
                if (te != null)
                    te.text = "Language";
                AddLocalizedTextOnGameObject(tabTxt.gameObject, "esc.settings.menu.language");
            }
        }

        EscLanguagePickerUI picker = languagePage.GetComponent<EscLanguagePickerUI>();
        if (picker == null)
            picker = Undo.AddComponent<EscLanguagePickerUI>(languagePage);
        SerializedObject pSo = new SerializedObject(picker);
        pSo.FindProperty("_languageText").objectReferenceValue =
            languagePage.transform.Find("LanguageText")?.GetComponent<Text>();
        pSo.FindProperty("_btnPrev").objectReferenceValue =
            languagePage.transform.Find("Button_LanguagePrev")?.GetComponent<Button>();
        pSo.FindProperty("_btnNext").objectReferenceValue =
            languagePage.transform.Find("Button_LanguageNext")?.GetComponent<Button>();
        pSo.FindProperty("_btnApply").objectReferenceValue =
            languagePage.transform.Find("Button_LanguageApply")?.GetComponent<Button>();
        pSo.ApplyModifiedPropertiesWithoutUndo();

        WireEscSceneLocalizedTexts(panelGo.transform);

        var tabs = panelGo.GetComponent<EscSettingsTabsController>();
        if (tabs == null)
            tabs = Undo.AddComponent<EscSettingsTabsController>(panelGo);

        SerializedObject so = new SerializedObject(tabs);
        so.FindProperty("_languagePage").objectReferenceValue = languagePage;
        so.FindProperty("_tabLanguageButton").objectReferenceValue = tabGo.GetComponent<Button>();
        so.FindProperty("_languagePicker").objectReferenceValue = picker;
        if (so.FindProperty("_videoPage").objectReferenceValue == null)
            so.FindProperty("_videoPage").objectReferenceValue = videoPage.gameObject;
        if (so.FindProperty("_audioPage").objectReferenceValue == null)
            so.FindProperty("_audioPage").objectReferenceValue =
                FindDeep(rightStack, "AudioPage")?.gameObject;
        if (so.FindProperty("_tabVideoButton").objectReferenceValue == null)
            so.FindProperty("_tabVideoButton").objectReferenceValue =
                leftNav.Find("Button_TabVideo")?.GetComponent<Button>();
        if (so.FindProperty("_tabAudioButton").objectReferenceValue == null)
            so.FindProperty("_tabAudioButton").objectReferenceValue =
                leftNav.Find("Button_TabAudio")?.GetComponent<Button>();
        if (so.FindProperty("_masterVolumeSlider").objectReferenceValue == null)
        {
            Transform audioPage = FindDeep(rightStack, "AudioPage");
            if (audioPage != null)
                so.FindProperty("_masterVolumeSlider").objectReferenceValue =
                    FindDeep(audioPage, "Slider_MasterVolume")?.GetComponent<Slider>();
        }

        if (so.FindProperty("_muteToggle").objectReferenceValue == null)
        {
            Transform audioPage = FindDeep(rightStack, "AudioPage");
            if (audioPage != null)
                so.FindProperty("_muteToggle").objectReferenceValue =
                    FindDeep(audioPage, "Toggle_MuteAudio")?.GetComponent<Toggle>();
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "EscSettingsPanelSetupTool: LanguagePage (как Resolution) + локализация Esc. Проверьте EscScene.");
    }

    static void RemoveLegacyLanguageBlockIfPresent(Transform languagePageRoot)
    {
        Transform block = languagePageRoot.Find("LanguageBlock");
        if (block != null)
            Undo.DestroyObjectImmediate(block.gameObject);
    }

    /// <summary>Копирует иерархию разрешения: позиции/якоря сохраняются как у шаблонов на VideoPage.</summary>
    static void BuildLanguagePageFromVideoTemplates(Transform videoPage, Transform languagePageRoot)
    {
        DuplicateIfMissing(videoPage, languagePageRoot, "ResolutionLabel", "LanguageLabel", (clone, _) =>
        {
            var te = clone.GetComponent<Text>();
            if (te != null)
                te.text = "Interface language";
            AddLocalizedTextOnGameObject(clone, "esc.settings.language_heading");
        });

        DuplicateIfMissing(videoPage, languagePageRoot, "ResolutionText", "LanguageText", (clone, _) =>
        {
            var te = clone.GetComponent<Text>();
            if (te != null)
                te.text = "";
            RemoveLocalizedTextIfAny(clone);
        });

        DuplicateIfMissing(videoPage, languagePageRoot, "Button_ResolutionPrev", "Button_LanguagePrev",
            (clone, _) => ClearButtonPersistentCalls(clone.GetComponent<Button>()));
        DuplicateIfMissing(videoPage, languagePageRoot, "Button_ResolutionNext", "Button_LanguageNext",
            (clone, _) => ClearButtonPersistentCalls(clone.GetComponent<Button>()));
        DuplicateIfMissing(videoPage, languagePageRoot, "Button_ResolutionApply", "Button_LanguageApply", (clone, _) =>
        {
            ClearButtonPersistentCalls(clone.GetComponent<Button>());
            Transform txtTr = clone.transform.Find("Text") ?? clone.transform.Find("Text (Legacy)");
            if (txtTr != null)
            {
                var t = txtTr.GetComponent<Text>();
                if (t != null)
                    t.text = "Apply";
                AddLocalizedTextOnGameObject(txtTr.gameObject, "esc.settings.apply");
            }
        });
    }

    static void DuplicateIfMissing(Transform videoPage, Transform languagePageRoot, string sourceName,
        string cloneName, Action<GameObject, Transform> afterDuplicate)
    {
        if (languagePageRoot.Find(cloneName) != null)
            return;
        Transform template = videoPage.Find(sourceName);
        if (template == null)
        {
            Debug.LogError("EscSettingsPanelSetupTool: на VideoPage нет «" + sourceName + "».");
            return;
        }

        GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, languagePageRoot);
        clone.name = cloneName;
        Undo.RegisterCreatedObjectUndo(clone, "Create " + cloneName);
        clone.transform.SetAsLastSibling();
        afterDuplicate?.Invoke(clone, languagePageRoot);
    }

    static void ClearButtonPersistentCalls(Button b)
    {
        if (b == null)
            return;
        SerializedObject so = new SerializedObject(b);
        so.FindProperty("m_OnClick").FindPropertyRelative("m_PersistentCalls.m_Calls").ClearArray();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void RemoveLocalizedTextIfAny(GameObject go)
    {
        var lt = go.GetComponent<LocalizedText>();
        if (lt != null)
            Undo.DestroyObjectImmediate(lt);
    }

    static void WireEscSceneLocalizedTexts(Transform settingsPanel)
    {
        Transform root = settingsPanel.root;

        TryLocalizedOnNamedText(settingsPanel, "Title", "esc.settings.title", "Settings");

        TryLocalizedDeepText(root, "ResolutionLabel", "esc.settings.resolution_label", "Resolution");
        TryButtonChildLocalized(root, "Button_ResolutionApply", "esc.settings.apply", "Apply");
        TryButtonChildLocalized(root, "Button_CloseSettings", "esc.settings.close", "Close");

        TryButtonChildLocalized(root, "Button_Resume", "esc.menu.resume", "Resume");
        TryButtonChildLocalized(root, "Button_Surrend_Battle", "esc.menu.surrender_battle", "Surrender battle");
        TryButtonChildLocalized(root, "Button_Exit_Game", "esc.menu.exit_game", "Exit game");
        TryButtonChildLocalized(root, "Button_Settings", "esc.menu.settings", "Settings");

        TryButtonChildLocalized(root, "Button_TabAudio", "esc.settings.menu.audio", "Audio");
        TryButtonChildLocalized(root, "Button_TabVideo", "esc.settings.menu.video", "Video");
        TryButtonChildLocalized(root, "Button_TabLanguage", "esc.settings.menu.language", "Language");

        TryLocalizedDeepText(root, "Label_MasterVolume", "esc.master_volume", "Master volume");
        TryLocalizedDeepText(root, "Label_Mute", "esc.mute", "No sound");
    }

    static void TryLocalizedOnNamedText(Transform searchRoot, string objectName, string key, string english)
    {
        Transform t = searchRoot.Find(objectName);
        if (t == null)
            return;
        var te = t.GetComponent<Text>();
        if (te != null)
            te.text = english;
        AddLocalizedTextOnGameObject(t.gameObject, key);
    }

    static void TryLocalizedDeepText(Transform root, string objectName, string key, string english)
    {
        Transform t = FindDeep(root, objectName);
        if (t == null)
            return;
        var te = t.GetComponent<Text>();
        if (te != null)
            te.text = english;
        AddLocalizedTextOnGameObject(t.gameObject, key);
    }

    static void TryButtonChildLocalized(Transform root, string buttonName, string key, string english)
    {
        Transform btn = FindDeep(root, buttonName);
        if (btn == null)
            return;
        Transform txtTr = btn.Find("Text") ?? btn.Find("Text (Legacy)");
        if (txtTr == null)
            return;
        var te = txtTr.GetComponent<Text>();
        if (te != null)
            te.text = english;
        AddLocalizedTextOnGameObject(txtTr.gameObject, key);
    }

    static void AddLocalizedTextOnGameObject(GameObject go, string key)
    {
        var existing = go.GetComponent<LocalizedText>();
        if (existing == null)
            existing = Undo.AddComponent<LocalizedText>(go);
        SerializedObject ltSo = new SerializedObject(existing);
        ltSo.FindProperty("_localizationKey").stringValue = key;
        ltSo.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
