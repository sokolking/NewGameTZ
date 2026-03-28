#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Принудительно разрешает http:// в Editor (Player → Other Settings → Allow downloads over HTTP).
/// Дублирует настройку проекта на случай сброса или старых кэшей.
/// </summary>
[InitializeOnLoad]
static class HttpAllowInsecure
{
    static HttpAllowInsecure()
    {
        if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            UnityEngine.Debug.Log("[HttpAllowInsecure] insecureHttpOption = AlwaysAllowed (http:// для dev-сервера).");
        }
    }
}
#endif
