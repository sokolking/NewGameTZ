using UnityEngine;

/// <summary>
/// Серый/полноэкранный фон должен быть первым дочерним объектом панели, иначе он рисуется поверх кнопок и перехватывает raycast.
/// </summary>
public static class UiModalBackdropUtility
{
    private static readonly string[] s_backdropNames =
    {
        "Image",
        "Background",
        "BlockOverlay",
    };

    /// <summary>
    /// Отправляет фон первым в иерархии (под остальным контентом). Вызывать после panel.SetActive(true).
    /// </summary>
    /// <param name="panelRoot">Корень модальной панели.</param>
    /// <param name="explicitBackdrop">Если задан в инспекторе — используется в приоритете.</param>
    public static void SendBackdropToBack(Transform panelRoot, Transform explicitBackdrop = null)
    {
        if (panelRoot == null)
            return;

        if (explicitBackdrop != null && explicitBackdrop.IsChildOf(panelRoot))
        {
            explicitBackdrop.SetAsFirstSibling();
            return;
        }

        // Ищем прямых детей с типичными именами (один фон).
        foreach (string name in s_backdropNames)
        {
            Transform child = panelRoot.Find(name);
            if (child != null)
            {
                child.SetAsFirstSibling();
                return;
            }
        }

        // Частый случай: объект назван "Image" и это отдельный полноэкранный слой.
        for (int i = 0; i < panelRoot.childCount; i++)
        {
            Transform c = panelRoot.GetChild(i);
            if (c == null || c.name != "Image")
                continue;
            if (c.GetComponent<UnityEngine.UI.Image>() != null)
            {
                c.SetAsFirstSibling();
                return;
            }
        }
    }
}
