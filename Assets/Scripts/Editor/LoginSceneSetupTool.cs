#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Базовая вёрстка LoginScene (как в <c>Assets/Scenes/LoginScene.unity</c>). Точка входа: <b>Tools → Hope → Create LoginScene</b>.
/// Если сцена уже содержит логин-поля и <see cref="LoginSceneController"/> — только добавляет недостающий ClientUpdatePanel.
/// </summary>
public static class LoginSceneSetupTool
{
    public static void PerformFullLoginSceneLayout()
    {
#if UNITY_2023_1_OR_NEWER
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        LoginSceneController ctrl = Object.FindFirstObjectByType<LoginSceneController>();
#else
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        LoginSceneController ctrl = Object.FindObjectOfType<LoginSceneController>();
#endif
        if (canvas != null && ctrl != null && GameObject.Find("LoginInputField") != null)
        {
            ClientUpdatePanelSetupTool.TryEnsureClientUpdatePanel(canvas);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return;
        }

        BuildLoginSceneFromScratch();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static void BuildLoginSceneFromScratch()
    {
        EnsureEventSystem();
        EnsureMainCameraAndLight();

#if UNITY_2023_1_OR_NEWER
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
#else
        Canvas canvas = Object.FindObjectOfType<Canvas>();
#endif
        GameObject canvasGo;
        if (canvas != null)
        {
            canvasGo = canvas.gameObject;
            for (int i = canvasGo.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(canvasGo.transform.GetChild(i).gameObject);
            var gate = canvasGo.GetComponent<ClientUpdateGate>();
            if (gate != null)
                Object.DestroyImmediate(gate);
            var loginCtrl = canvasGo.GetComponent<LoginSceneController>();
            if (loginCtrl != null)
                Object.DestroyImmediate(loginCtrl);
        }
        else
        {
            canvasGo = new GameObject("Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGo, "Login Canvas");
        }

        canvasGo.AddComponent<LoginSceneController>();
        canvasGo.AddComponent<ClientUpdateGate>();

        var form = new GameObject("LoginForm", typeof(RectTransform));
        form.transform.SetParent(canvasGo.transform, false);
        var formRt = form.GetComponent<RectTransform>();
        formRt.anchorMin = new Vector2(0.5f, 0.5f);
        formRt.anchorMax = new Vector2(0.5f, 0.5f);
        formRt.pivot = new Vector2(0.5f, 0.5f);
        formRt.anchoredPosition = Vector2.zero;
        formRt.sizeDelta = new Vector2(400f, 320f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        InputField loginField = MainMenuSetupTool.CreateAuthInputRow(
            formRt, "Login", "LoginInputField", "Login", "test", new Vector2(0f, 80f), false);
        InputField passField = MainMenuSetupTool.CreateAuthInputRow(
            formRt, "Password", "PasswordInputField", "Password", "test", new Vector2(0f, 30f), true);

        Toggle solo = MainMenuSetupTool.CreateMenuToggle(canvasGo.transform, "Toggle_SoloVsMonster", "Monster battle (solo)", 20f);
        RectTransform soloRt = solo.GetComponent<RectTransform>();
        soloRt.anchorMin = new Vector2(0.5f, 0.5f);
        soloRt.anchorMax = new Vector2(0.5f, 0.5f);
        soloRt.anchoredPosition = new Vector2(0f, -40f);

        Toggle dbg = MainMenuSetupTool.CreateMenuToggle(canvasGo.transform, "Toggle_Debug", "Debug (localhost)", -10f);
        RectTransform dbgRt = dbg.GetComponent<RectTransform>();
        dbgRt.anchorMin = new Vector2(0.5f, 0.5f);
        dbgRt.anchorMax = new Vector2(0.5f, 0.5f);
        dbgRt.anchoredPosition = new Vector2(0f, -75f);

        GameObject enterGo = new GameObject("Button_Enter", typeof(RectTransform), typeof(Image), typeof(Button));
        enterGo.transform.SetParent(form.transform, false);
        var enterRt = enterGo.GetComponent<RectTransform>();
        enterRt.anchorMin = new Vector2(0.5f, 0.5f);
        enterRt.anchorMax = new Vector2(0.5f, 0.5f);
        enterRt.pivot = new Vector2(0.5f, 0.5f);
        enterRt.anchoredPosition = new Vector2(0f, -50f);
        enterRt.sizeDelta = new Vector2(200f, 40f);
        enterGo.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
        var enterBtn = enterGo.GetComponent<Button>();
        GameObject enterTxt = new GameObject("Text", typeof(RectTransform), typeof(Text));
        enterTxt.transform.SetParent(enterGo.transform, false);
        var etr = enterTxt.GetComponent<RectTransform>();
        etr.anchorMin = Vector2.zero;
        etr.anchorMax = Vector2.one;
        etr.offsetMin = Vector2.zero;
        etr.offsetMax = Vector2.zero;
        var etx = enterTxt.GetComponent<Text>();
        etx.font = font;
        etx.text = "Enter";
        etx.alignment = TextAnchor.MiddleCenter;
        etx.color = Color.white;

        GameObject errGo = new GameObject("ErrorText", typeof(RectTransform), typeof(Text));
        errGo.transform.SetParent(form.transform, false);
        var errRt = errGo.GetComponent<RectTransform>();
        errRt.anchorMin = new Vector2(0.5f, 0.5f);
        errRt.anchorMax = new Vector2(0.5f, 0.5f);
        errRt.pivot = new Vector2(0.5f, 0.5f);
        errRt.anchoredPosition = new Vector2(0f, -120f);
        errRt.sizeDelta = new Vector2(380f, 60f);
        var errTx = errGo.GetComponent<Text>();
        errTx.font = font;
        errTx.fontSize = 14;
        errTx.color = new Color(1f, 0.4f, 0.4f, 1f);
        errTx.alignment = TextAnchor.MiddleCenter;
        errTx.text = "";

        var loginSceneController = canvasGo.GetComponent<LoginSceneController>();
        SerializedObject so = new SerializedObject(loginSceneController);
        so.FindProperty("_loginInputField").objectReferenceValue = loginField;
        so.FindProperty("_passwordInputField").objectReferenceValue = passField;
        so.FindProperty("_soloVsMonsterToggle").objectReferenceValue = solo;
        so.FindProperty("_debugLocalhostToggle").objectReferenceValue = dbg;
        so.FindProperty("_enterButton").objectReferenceValue = enterBtn;
        so.FindProperty("_errorTextLegacy").objectReferenceValue = errTx;
        so.ApplyModifiedPropertiesWithoutUndo();

        ClientUpdatePanelSetupTool.TryEnsureClientUpdatePanel(canvas);

        EditorUtility.SetDirty(canvasGo);
    }

    static void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        if (Object.FindFirstObjectByType<EventSystem>() != null)
            return;
#else
        if (Object.FindObjectOfType<EventSystem>() != null)
            return;
#endif
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(es, "EventSystem");
    }

    static void EnsureMainCameraAndLight()
    {
#if UNITY_2023_1_OR_NEWER
        if (Object.FindFirstObjectByType<Camera>() == null)
#else
        if (Object.FindObjectOfType<Camera>() == null)
#endif
        {
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
            Undo.RegisterCreatedObjectUndo(camGo, "Main Camera");
        }

#if UNITY_2023_1_OR_NEWER
        if (Object.FindFirstObjectByType<Light>() == null)
#else
        if (Object.FindObjectOfType<Light>() == null)
#endif
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            Undo.RegisterCreatedObjectUndo(lightGo, "Directional Light");
        }
    }
}
#endif
