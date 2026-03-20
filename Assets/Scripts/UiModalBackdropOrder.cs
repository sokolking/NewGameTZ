using UnityEngine;

/// <summary>
/// Повесь на корень модальной панели (SkipDialog, RoundWait и т.п.): при открытии отправляет фон первым в иерархии,
/// чтобы полноэкранный Image с Raycast Target не перехватывал клики с кнопок/полей.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiModalBackdropOrder : MonoBehaviour
{
    [Tooltip("Если пусто — ищется дочерний Background / Backdrop / GrayBG.")]
    [SerializeField] private Transform _backdrop;

    private void OnEnable()
    {
        UiModalBackdropUtility.SendBackdropToBack(transform, _backdrop);
    }
}
