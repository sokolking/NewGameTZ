using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Показывает <see cref="UiHierarchyNames.BlockOverlay"/> при открытии любой из панелей:
/// PauseMenuPanel, RoundWaitPanel, SkipDialogPanel (логика совпадает с <see cref="GameplayMapInputBlock"/>).
/// Скрывает оверлей, когда все они закрыты.
/// Повесь на корневой Canvas (тот же, где ActionPointsUI) — или компонент добавится из <see cref="ActionPointsUI"/> автоматически.
/// Если объекта BlockOverlay нет в сцене, он будет создан при старте.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiBlockOverlaySync : MonoBehaviour
{
    [Tooltip("Если пусто — ищется объект с именем BlockOverlay под этим Canvas; при отсутствии создаётся автоматически.")]
    [SerializeField] private GameObject _blockOverlay;

    [Tooltip("Прозрачность затемнения (0 = только raycast, без видимой заливки).")]
    [Range(0f, 1f)]
    [SerializeField] private float _dimAlpha = 0.35f;

    private void Awake()
    {
        ResolveOrCreateOverlay();
        if (_blockOverlay != null)
        {
            ApplyDimColor();
            _blockOverlay.SetActive(false);
            EnsureOverlayBehindModalPanels();
        }
    }

    private void ResolveOrCreateOverlay()
    {
        if (_blockOverlay == null)
        {
            foreach (Transform t in transform.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == UiHierarchyNames.BlockOverlay)
                {
                    _blockOverlay = t.gameObject;
                    break;
                }
            }
        }

        if (_blockOverlay != null)
            return;

        // В сцене часто забывают добавить BlockOverlay — создаём под тем же Canvas, что и ActionPointsUI.
        Transform root = transform;
        GameObject go = new GameObject(UiHierarchyNames.BlockOverlay, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);

        Transform front = root.Find(UiHierarchyNames.FrontContentMaker);
        int insertIndex = front != null ? front.GetSiblingIndex() + 1 : 0;
        go.transform.SetSiblingIndex(insertIndex);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.raycastTarget = true;

        _blockOverlay = go;
    }

    private void ApplyDimColor()
    {
        if (_blockOverlay == null)
            return;
        Image img = _blockOverlay.GetComponent<Image>();
        if (img != null)
            img.color = new Color(0f, 0f, 0f, _dimAlpha);
    }

    private void LateUpdate()
    {
        if (_blockOverlay == null)
            return;

        bool need = GameplayMapInputBlock.IsBlocked;
        if (_blockOverlay.activeSelf != need)
            _blockOverlay.SetActive(need);

        // Если BlockOverlay последний в Canvas — он рисуется поверх PauseMenu / диалогов и съедает raycast.
        if (_blockOverlay.activeSelf)
            EnsureOverlayBehindModalPanels();
    }

    /// <summary>
    /// Держим оверлей сразу под Front Content Maker, чтобы модальные панели (пауза, ожидание, skip) оставались сверху.
    /// </summary>
    private void EnsureOverlayBehindModalPanels()
    {
        Transform root = transform;
        if (_blockOverlay.transform.parent != root)
            return;

        Transform front = root.Find(UiHierarchyNames.FrontContentMaker);
        if (front == null)
            return;

        int desired = front.GetSiblingIndex() + 1;
        if (_blockOverlay.transform.GetSiblingIndex() != desired)
            _blockOverlay.transform.SetSiblingIndex(desired);
    }
}
