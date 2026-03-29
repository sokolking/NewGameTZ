using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Два режима: планирование (вид сверху) и просмотр (3-е лицо).
/// <see cref="UiHierarchyNames.ModeButton"/> — подпись текущего режима и переключение режима/камеры.
/// После анимации просмотра камера не уходит автоматически; <see cref="UiHierarchyNames.ModeButton"/> плавно «мигает» (альфа).
/// Переключение планирование/просмотр — без анимации камеры.
/// </summary>
public class GamePhaseViewController : MonoBehaviour
{
    public static GamePhaseViewController Instance { get; private set; }

    [Tooltip("If empty, resolved via UiHierarchyFind by UiHierarchyNames.ModeButton.")]
    [SerializeField] private Button _modeButton;
    [Tooltip("Optional separate label; otherwise TMP/Legacy Text on ModeButton children is used.")]
    [SerializeField] private TextMeshProUGUI _modeLabel;

    [Header("ModeButton pulse after replay animation")]
    [Tooltip("Minimum alpha while pulsing (0…1).")]
    [SerializeField] [Range(0.05f, 1f)] private float _modeButtonPulseMinAlpha = 0.35f;
    [Tooltip("Pulse speed (higher = faster blink).")]
    [SerializeField] private float _modeButtonPulseSpeed = 1.35f;

    private Text _legacyModeLabel;
    private CanvasGroup _modeButtonCanvasGroup;
    private Coroutine _modePulseCoroutine;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        TryResolveReferences();
        EnsureModeButtonCanvasGroup();

        if (_modeButton != null)
            _modeButton.onClick.AddListener(OnModeButtonClicked);

#if UNITY_EDITOR
        if (_modeButton == null)
            Debug.LogWarning(
                "[GamePhaseViewController] ModeButton не найдена. Назначьте в инспекторе или имя объекта = UiHierarchyNames.ModeButton.",
                this);
#endif
    }

    private void Update()
    {
        SyncModeLabel();
    }

    private void TryResolveReferences()
    {
        if (_modeButton == null)
        {
            Transform t = UiHierarchyFind.FindNamedTransform(UiHierarchyNames.ModeButton);
            if (t != null)
                _modeButton = t.GetComponent<Button>();
        }

        if (_modeLabel == null && _modeButton != null)
        {
            _modeLabel = _modeButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (_modeLabel == null)
                _legacyModeLabel = _modeButton.GetComponentInChildren<Text>(true);
        }
    }

    private void EnsureModeButtonCanvasGroup()
    {
        if (_modeButton == null)
            return;

        _modeButtonCanvasGroup = _modeButton.GetComponent<CanvasGroup>();
        if (_modeButtonCanvasGroup == null)
            _modeButtonCanvasGroup = _modeButton.gameObject.AddComponent<CanvasGroup>();

        _modeButtonCanvasGroup.alpha = 1f;
        _modeButtonCanvasGroup.interactable = true;
        _modeButtonCanvasGroup.blocksRaycasts = true;
    }

    /// <summary>После анимации в 3-м лице: камера остаётся; ModeButton плавно пульсирует по альфе.</summary>
    public static void NotifyViewAnimationEndedKeepThirdPerson()
    {
        if (Instance != null)
            Instance.StartModeButtonPulse();
    }

    public static void StopModeButtonPulseIfAny()
    {
        if (Instance != null)
            Instance.StopModeButtonPulse();
    }

    private void StartModeButtonPulse()
    {
        EnsureModeButtonCanvasGroup();
        if (_modeButtonCanvasGroup == null)
            return;

        StopModeButtonPulse();
        _modePulseCoroutine = StartCoroutine(ModeButtonPulseRoutine());
    }

    private void StopModeButtonPulse()
    {
        if (_modePulseCoroutine != null)
        {
            StopCoroutine(_modePulseCoroutine);
            _modePulseCoroutine = null;
        }

        if (_modeButtonCanvasGroup != null)
            _modeButtonCanvasGroup.alpha = 1f;
    }

    private IEnumerator ModeButtonPulseRoutine()
    {
        CanvasGroup cg = _modeButtonCanvasGroup;
        if (cg == null)
            yield break;

        float minA = Mathf.Clamp01(_modeButtonPulseMinAlpha);
        float maxA = 1f;
        float speed = Mathf.Max(0.05f, _modeButtonPulseSpeed);

        while (true)
        {
            float t = Mathf.PingPong(Time.unscaledTime * speed, 1f);
            cg.alpha = Mathf.Lerp(minA, maxA, t);
            yield return null;
        }
    }

    private void SyncModeLabel()
    {
        bool view = HexGridCamera.ThirdPersonFollowActive;
        string text = view ? "Thirf-Person View" : "Top-Down View";
        if (_modeLabel != null)
            _modeLabel.text = text;
        else if (_legacyModeLabel != null)
            _legacyModeLabel.text = text;
    }

    private void OnModeButtonClicked()
    {
        StopModeButtonPulse();

        if (HexGridCamera.ThirdPersonFollowActive)
            StartCoroutine(EnterPlanningPhaseRoutine());
        else
            StartCoroutine(EnterViewPhaseRoutine());
    }

    private IEnumerator EnterPlanningPhaseRoutine()
    {
        HexGridCamera cam = FindFirstObjectByType<HexGridCamera>();
        if (cam != null && HexGridCamera.ThirdPersonFollowActive)
            cam.EndThirdPersonFollowImmediate();
        yield break;
    }

    private IEnumerator EnterViewPhaseRoutine()
    {
        HexGridCamera cam = FindFirstObjectByType<HexGridCamera>();
        if (cam == null)
            yield break;

        GameSession session = GameSession.Active;
        if (session != null && session.IsSpectatorMode)
        {
            if (!session.TryGetSpectatedHumanFollowTransform(out Transform spectateRoot) || spectateRoot == null)
                yield break;

            Vector3 fh = spectateRoot.forward;
            fh.y = 0f;
            if (fh.sqrMagnitude < 1e-6f)
                fh = Vector3.forward;
            else
                fh.Normalize();

            cam.EnterThirdPersonFollowImmediate(spectateRoot, fh);
            yield break;
        }

        Player local = FindFirstObjectByType<Player>();
        if (local == null || local.IsDead || local.Grid == null)
            yield break;

        Vector3 fhLocal = local.transform.forward;
        fhLocal.y = 0f;
        if (fhLocal.sqrMagnitude < 1e-6f)
            fhLocal = Vector3.forward;
        else
            fhLocal.Normalize();

        cam.EnterThirdPersonFollowImmediate(local.transform, fhLocal);
        yield break;
    }
}
