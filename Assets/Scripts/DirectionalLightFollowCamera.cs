using UnityEngine;

/// <summary>
/// Поворачивает directional light в ту же сторону, куда смотрит основная камера.
/// Просто повесь этот скрипт на Directional Light и укажи ссылку на камеру (если null — возьмётся Camera.main).
/// </summary>
public class DirectionalLightFollowCamera : MonoBehaviour
{
    [SerializeField] private Camera _targetCamera;

    private Transform _cachedCamTransform;
    private Transform _selfTransform;

    private void Awake()
    {
        if (_targetCamera == null)
            _targetCamera = Camera.main;
        if (_targetCamera != null)
            _cachedCamTransform = _targetCamera.transform;
        _selfTransform = transform;
    }

    private void LateUpdate()
    {
        if (_cachedCamTransform == null)
        {
            if (_targetCamera == null) return;
            _cachedCamTransform = _targetCamera.transform;
        }

        _selfTransform.rotation = Quaternion.LookRotation(_cachedCamTransform.forward, Vector3.up);
    }
}
