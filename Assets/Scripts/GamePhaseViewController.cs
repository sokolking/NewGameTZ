using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Два режима: планирование (вид сверху) и просмотр (3-е лицо).
/// <see cref="UiHierarchyNames.ModeButton"/> — подпись текущего режима и переключение режима/камеры.
/// После анимации просмотра камера не уходит автоматически; <see cref="UiHierarchyNames.ModeButton"/> плавно «мигает» (альфа).
/// </summary>
public class GamePhaseViewController : MonoBehaviour
{
    public static GamePhaseViewController Instance { get; private set; }

    [Tooltip("Если пусто — ищется через UiHierarchyFind по UiHierarchyNames.ModeButton.")]
    [SerializeField] private Button _modeButton;
    [Tooltip("Необязательно: отдельный текст; иначе берётся TMP/Legacy Text на дочерних ModeButton.")]
    [SerializeField] private TextMeshProUGUI _modeLabel;

    [Header("Мигание ModeButton после анимации просмотра")]
    [Tooltip("Минимальная альфа при пульсации (0…1).")]
    [SerializeField] [Range(0.05f, 1f)] private float _modeButtonPulseMinAlpha = 0.35f;
    [Tooltip("Скорость плавного пульса (чем больше — быстрее «мигание»).")]
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
        string text = view ? "Режим просмотра" : "Режим планирования";
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
        if (cam == null)
            yield break;

        if (cam.IsFollowThirdPersonFullyActive)
            yield return cam.ExitThirdPersonFollowRoutine();
        else if (HexGridCamera.ThirdPersonFollowActive)
            cam.EndThirdPersonFollowImmediate();
    }

    private IEnumerator EnterViewPhaseRoutine()
    {
        Player local = FindFirstObjectByType<Player>();
        if (local == null || local.IsDead || local.Grid == null)
            yield break;

        HexGridCamera cam = FindFirstObjectByType<HexGridCamera>();
        if (cam == null)
            yield break;

        Vector3 fh = local.transform.forward;
        fh.y = 0f;
        if (fh.sqrMagnitude < 1e-6f)
            fh = Vector3.forward;
        else
            fh.Normalize();

        yield return cam.EnterThirdPersonFollowRoutine(local.transform, fh);
    }
}
