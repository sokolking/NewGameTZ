using UnityEngine;

/// <summary>
/// Поворачивает directional light в ту же сторону, куда смотрит основная камера.
/// Просто повесь этот скрипт на Directional Light и укажи ссылку на камеру (если null — возьмётся Camera.main).
/// </summary>
public class DirectionalLightFollowCamera : MonoBehaviour
{
    [SerializeField] private Camera _targetCamera;

    private void Awake()
    {
        if (_targetCamera == null)
            _targetCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (_targetCamera == null) return;

        // Свет направлен туда же, куда смотрит камера.
        transform.rotation = Quaternion.LookRotation(_targetCamera.transform.forward, Vector3.up);
    }
}

