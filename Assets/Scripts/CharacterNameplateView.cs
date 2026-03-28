using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Планка над головой: «ник [уровень]» и красная полоска HP по доле текущих жизней.
/// Вешается на корень префаба (отдельный объект в сцене/префабе — можно править вручную).
/// </summary>
/// <remarks>
/// Ориентация как у камеры (плоскость экрана), а не LookRotation(toCamera, world up):
/// при виде сверху вектор к камере параллелен Vector3.up — второй аргумент LookRotation вырожден,
/// текст нечитаем. -camera.forward + camera.up стабильны для орто «сверху» и для 3-го лица.
/// </remarks>
[DisallowMultipleComponent]
[DefaultExecutionOrder(40)]
public sealed class CharacterNameplateView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _nameLevelText;
    [SerializeField] private RectTransform _hpFillRect;
    [Tooltip("Offset from follow point (world space). Z is overridden by camera mode (planning / view).")]
    [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 2.1f, 0f);
    [Tooltip("World offset Z in top-down view.")]
    [SerializeField] private float _planningWorldOffsetZ = 1f;
    [Tooltip("World offset Z in third-person view.")]
    [SerializeField] private float _viewWorldOffsetZ = 0f;
    [SerializeField] private bool _faceCamera = true;
    [Tooltip("Extra local Y rotation after aligning to screen plane. 180° fixes mirrored TMP on world-space canvas.")]
    [SerializeField] private float _extraBillboardYawDegrees = 180f;

    private Transform _follow;
    private Camera _camera;
    private Player _player;
    private RemoteBattleUnitView _remote;

    private void Awake()
    {
        if (_camera == null)
            _camera = Camera.main;
    }

    private void OnDestroy()
    {
        Unbind();
    }

    /// <summary>Привязка к локальному игроку.</summary>
    public void Bind(Player player, Transform followOverride = null)
    {
        Unbind();
        _player = player;
        _follow = followOverride != null ? followOverride : player.transform;
        if (_player != null)
        {
            _player.OnHealthChanged += OnPlayerHealthChanged;
            _player.OnDisplayProfileChanged += Refresh;
        }
        Refresh();
    }

    /// <summary>Привязка к удалённому юниту.</summary>
    public void Bind(RemoteBattleUnitView remote, Transform followOverride = null)
    {
        Unbind();
        _remote = remote;
        _follow = followOverride != null ? followOverride : remote.transform;
        if (_remote != null)
        {
            _remote.OnHealthChanged += OnRemoteHealthChanged;
            _remote.OnDisplayProfileChanged += Refresh;
        }
        Refresh();
    }

    public void Unbind()
    {
        if (_player != null)
        {
            _player.OnHealthChanged -= OnPlayerHealthChanged;
            _player.OnDisplayProfileChanged -= Refresh;
            _player = null;
        }

        if (_remote != null)
        {
            _remote.OnHealthChanged -= OnRemoteHealthChanged;
            _remote.OnDisplayProfileChanged -= Refresh;
            _remote = null;
        }

        _follow = null;
    }

    private void OnPlayerHealthChanged(int _, int __) => Refresh();

    private void OnRemoteHealthChanged(int _, int __) => Refresh();

    public void Refresh()
    {
        string name;
        int level;
        int curHp;
        int maxHp;

        if (_player != null)
        {
            name = _player.DisplayName;
            level = _player.CharacterLevel;
            curHp = _player.CurrentHp;
            maxHp = _player.MaxHp;
        }
        else if (_remote != null)
        {
            name = _remote.DisplayName;
            level = _remote.CharacterLevel;
            curHp = _remote.CurrentHp;
            maxHp = _remote.MaxHp;
        }
        else
            return;

        if (_nameLevelText != null)
            _nameLevelText.text = $"{name} [{level}]";

        if (_hpFillRect != null && maxHp > 0)
        {
            float t = Mathf.Clamp01((float)curHp / maxHp);
            _hpFillRect.anchorMin = new Vector2(0f, 0f);
            _hpFillRect.anchorMax = new Vector2(t, 1f);
            _hpFillRect.offsetMin = Vector2.zero;
            _hpFillRect.offsetMax = Vector2.zero;
        }
    }

    private void LateUpdate()
    {
        if (_follow == null)
            return;

        Vector3 off = _worldOffset;
        off.z = HexGridCamera.ThirdPersonFollowActive ? _viewWorldOffsetZ : _planningWorldOffsetZ;
        transform.position = _follow.position + off;

        if (!_faceCamera)
            return;

        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = ResolveCamera();
        if (_camera == null)
            return;

        // Плоскость экрана; затем локальный Y — иначе TMP на canvas часто выглядит зеркально.
        Quaternion screenPlane = Quaternion.LookRotation(-_camera.transform.forward, _camera.transform.up);
        transform.rotation = screenPlane * Quaternion.Euler(0f, _extraBillboardYawDegrees, 0f);
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
