using TMPro;
using UnityEngine;

/// <summary>
/// Плашка урона над юнитом (по образу CharacterNameplate, но без HP-бара).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(40)]
public sealed class DamagePopupView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _damageText;
    [Tooltip("Смещение от точки следования (мировые координаты). Компонента Z переопределяется режимом камеры.")]
    [SerializeField] private Vector3 _worldOffset = new Vector3(0.9f, 2.1f, 0f);
    [SerializeField] private float _planningWorldOffsetZ = 1f;
    [SerializeField] private float _viewWorldOffsetZ = 0f;
    [SerializeField] private bool _faceCamera = true;
    [SerializeField] private float _extraBillboardYawDegrees = 180f;
    [Tooltip("Если у юнита есть CharacterNameplateView, плашка урона фиксируется слева от него на это расстояние (в мировых единицах).")]
    [SerializeField] private float _leftOffsetFromNameplate = 2.35f;
    [Tooltip("Дополнительное смещение вверх в world units (для очереди урона).")]
    [SerializeField] private float _stackRiseWorld = 0f;
    [SerializeField] private RectTransform _panelRect;

    private Transform _follow;
    private Camera _camera;
    private CharacterNameplateView _nameplateView;

    private void Awake()
    {
        if (_camera == null)
            _camera = Camera.main;
    }

    public void Bind(Transform follow)
    {
        _follow = follow;
        _nameplateView = null;
    }

    public void ShowDamage(int damage)
    {
        if (_damageText != null)
        {
            _damageText.text = "-" + damage;
            _damageText.color = Color.yellow;
        }
    }

    public void SetStackRiseWorld(float riseWorld)
    {
        _stackRiseWorld = Mathf.Max(0f, riseWorld);
    }

    public float GetPanelHeightPixels(float fallback = 48f)
    {
        if (_panelRect != null)
            return Mathf.Max(1f, _panelRect.sizeDelta.y);
        return fallback;
    }

    private void LateUpdate()
    {
        if (_follow == null)
            return;

        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = ResolveCamera();
        if (_camera == null)
            return;

        Quaternion screenPlane = Quaternion.LookRotation(-_camera.transform.forward, _camera.transform.up);
        Quaternion finalRotation = _faceCamera
            ? screenPlane * Quaternion.Euler(0f, _extraBillboardYawDegrees, 0f)
            : transform.rotation;
        if (_faceCamera)
            transform.rotation = finalRotation;

        if (TryGetNameplateTransform(out var nameplateTransform))
        {
            Vector3 left = -(finalRotation * Vector3.right);
            Vector3 up = finalRotation * Vector3.up;
            transform.position = nameplateTransform.position + left * _leftOffsetFromNameplate + up * _stackRiseWorld;
            return;
        }

        Vector3 off = _worldOffset;
        off.z = HexGridCamera.ThirdPersonFollowActive ? _viewWorldOffsetZ : _planningWorldOffsetZ;
        transform.position = _follow.position + off + (finalRotation * Vector3.up) * _stackRiseWorld;
    }

    private bool TryGetNameplateTransform(out Transform nameplateTransform)
    {
        nameplateTransform = null;
        if (_follow == null)
            return false;

        if (_nameplateView == null)
            _nameplateView = _follow.GetComponentInChildren<CharacterNameplateView>(true);
        if (_nameplateView == null)
            return false;

        nameplateTransform = _nameplateView.transform;
        return nameplateTransform != null;
    }

    private static Camera ResolveCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;
#if UNITY_2023_1_OR_NEWER
        var hex = UnityEngine.Object.FindFirstObjectByType<HexGridCamera>();
#else
        var hex = UnityEngine.Object.FindObjectOfType<HexGridCamera>();
#endif
        if (hex != null)
        {
            var c = hex.GetComponent<Camera>();
            if (c != null && c.isActiveAndEnabled)
                return c;
        }
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<Camera>();
#else
        return UnityEngine.Object.FindObjectOfType<Camera>();
#endif
    }
}
