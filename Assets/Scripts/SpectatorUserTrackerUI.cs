using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spectator-only panel: shows observed player name and cycles camera target (prev/next).
/// Built at runtime under <c>TopRightCanvas</c> so scene YAML stays small.
/// </summary>
[AddComponentMenu("Hex Grid/Spectator User Tracker UI")]
[DefaultExecutionOrder(50)]
public sealed class SpectatorUserTrackerUI : MonoBehaviour
{
    private GameObject _panelRoot;
    private Text _label;
    private Button _prev;
    private Button _next;

    private IEnumerator Start()
    {
        yield return null;
        if (GameSession.Active == null || !GameSession.Active.IsSpectatorMode)
        {
            enabled = false;
            yield break;
        }

        Transform parent = GameObject.Find("TopRightCanvas")?.transform;
        if (parent == null)
        {
            enabled = false;
            yield break;
        }

        BuildPanel(parent);
        GameSession.OnBattleFinished += OnBattleFinished;
    }

    private void OnDestroy()
    {
        GameSession.OnBattleFinished -= OnBattleFinished;
        if (_prev != null)
            _prev.onClick.RemoveListener(OnPrev);
        if (_next != null)
            _next.onClick.RemoveListener(OnNext);
    }

    private void OnBattleFinished(bool _)
    {
        if (_panelRoot != null)
            _panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (GameSession.Active == null || !GameSession.Active.IsSpectatorMode || _label == null)
            return;
        _label.text = GameSession.Active.GetSpectatedHumanDisplayName();
    }

    private void OnPrev() => GameSession.Active?.TrySpectatePrevHuman();

    private void OnNext() => GameSession.Active?.TrySpectateNextHuman();

    private void BuildPanel(Transform parent)
    {
        _panelRoot = new GameObject("UserTrackerPanel", typeof(RectTransform));
        var rt = _panelRoot.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.SetAsLastSibling();

        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 32f);
        rt.anchoredPosition = Vector2.zero;

        var hlg = _panelRoot.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 6f;
        hlg.padding = new RectOffset(4, 4, 2, 2);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        _prev = CreateSmallButton(rt, "UserTrackerPrevButton", "<");
        _prev.onClick.AddListener(OnPrev);

        var textGo = new GameObject("UserTrackerText", typeof(RectTransform));
        textGo.transform.SetParent(rt, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.sizeDelta = new Vector2(120f, 28f);
        var le = textGo.AddComponent<LayoutElement>();
        le.preferredWidth = 140f;
        le.flexibleWidth = 1f;
        le.minHeight = 28f;
        _label = textGo.AddComponent<Text>();
        _label.alignment = TextAnchor.MiddleCenter;
        _label.color = Color.white;
        _label.fontSize = 14;
        _label.raycastTarget = false;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            _label.font = font;
        _label.text = "—";

        _next = CreateSmallButton(rt, "UserTrackerNextButton", ">");
        _next.onClick.AddListener(OnNext);
    }

    private static Button CreateSmallButton(Transform parent, string objectName, string caption)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(32f, 28f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 32f;
        le.preferredHeight = 28f;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.55f);
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var capGo = new GameObject("Caption", typeof(RectTransform));
        capGo.transform.SetParent(go.transform, false);
        var capRt = capGo.GetComponent<RectTransform>();
        capRt.anchorMin = Vector2.zero;
        capRt.anchorMax = Vector2.one;
        capRt.offsetMin = Vector2.zero;
        capRt.offsetMax = Vector2.zero;
        var cap = capGo.AddComponent<Text>();
        cap.text = caption;
        cap.alignment = TextAnchor.MiddleCenter;
        cap.color = Color.white;
        cap.fontSize = 16;
        cap.raycastTarget = false;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            cap.font = font;

        return btn;
    }
}
