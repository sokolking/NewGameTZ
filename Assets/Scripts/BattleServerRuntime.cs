using UnityEngine;

/// <summary>
/// Базовый URL API боя: выбор из главного меню (Debug = localhost) и сохранение в PlayerPrefs.
/// </summary>
public static class BattleServerRuntime
{
    public const string DebugLocalBaseUrl = "http://localhost:5000";
    /// <summary>Продакшен-сервер (без завершающего /).</summary>
    public const string ProductionBaseUrl = "http://178.104.63.174:5000";

    private const string PrefsKeyDebugLocal = "BattleServer_UseDebugLocalhost";

    /// <summary>
    /// Если true — подключение к <see cref="DebugLocalBaseUrl"/> (разработка).
    /// Если false — к <see cref="ProductionBaseUrl"/>.
    /// </summary>
    public static bool UseDebugLocalhost
    {
        get => PlayerPrefs.GetInt(PrefsKeyDebugLocal, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(PrefsKeyDebugLocal, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Текущий базовый URL без завершающего слэша.</summary>
    public static string CurrentBaseUrl =>
        (UseDebugLocalhost ? DebugLocalBaseUrl : ProductionBaseUrl).TrimEnd('/');

    /// <summary>Нормализованный URL (без / на конце) для HTTP/WebSocket.</summary>
    public static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return CurrentBaseUrl;
        return url.Trim().TrimEnd('/');
    }
}
