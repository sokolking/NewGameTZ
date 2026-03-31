using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Loads string maps from <c>Resources/Localization/en</c> and <c>ru</c> (JSON text assets).
/// Keys are stable English identifiers; values are translated per language.
/// </summary>
public static class Loc
{
    public const string LanguagePrefsKey = "Hope.GameLanguage";

    static Dictionary<string, string> _en = new();
    static Dictionary<string, string> _ru = new();
    static bool _loaded;

    /// <summary>Active UI language; persisted via <see cref="SetLanguage"/> / PlayerPrefs.</summary>
    public static GameLanguage Current { get; set; } = GameLanguage.English;

    /// <summary>Fired after <see cref="SetLanguage"/> (after <see cref="LocalizedText.RefreshAll"/>).</summary>
    public static event Action<GameLanguage> LanguageChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySavedLanguageEarly()
    {
        LoadSavedLanguage();
    }

    /// <summary>Apply <see cref="LanguagePrefsKey"/> if present (0 = English, 1 = Russian).</summary>
    public static void LoadSavedLanguage()
    {
        if (!PlayerPrefs.HasKey(LanguagePrefsKey))
            return;
        int v = PlayerPrefs.GetInt(LanguagePrefsKey, 0);
        if (v < 0 || v > (int)GameLanguage.Russian)
            v = 0;
        Current = (GameLanguage)v;
    }

    /// <summary>Switch language, save preference, refresh all <see cref="LocalizedText"/> in loaded scenes.</summary>
    public static void SetLanguage(GameLanguage lang)
    {
        Current = lang;
        PlayerPrefs.SetInt(LanguagePrefsKey, (int)lang);
        PlayerPrefs.Save();
        LocalizedText.RefreshAll();
        LanguageChanged?.Invoke(lang);
    }

    static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        _en = LoadDict("Localization/en");
        _ru = LoadDict("Localization/ru");
        if (_en.Count == 0)
            Debug.LogWarning("[Loc] Missing or empty Resources/Localization/en.json (expected Resources path Localization/en).");
    }

    static Dictionary<string, string> LoadDict(string resourcesPath)
    {
        var ta = Resources.Load<TextAsset>(resourcesPath);
        if (ta == null || string.IsNullOrWhiteSpace(ta.text))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(ta.text);
            return parsed ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Loc] Failed to parse " + resourcesPath + ": " + ex.Message);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Force reload (e.g. after changing language files in editor).</summary>
    public static void Reload()
    {
        _loaded = false;
        _en = new Dictionary<string, string>(StringComparer.Ordinal);
        _ru = new Dictionary<string, string>(StringComparer.Ordinal);
        EnsureLoaded();
    }

    public static string T(string key)
    {
        EnsureLoaded();
        var active = Current == GameLanguage.Russian ? _ru : _en;
        if (active.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s))
            return s;
        if (_en.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
            return s;
        return key;
    }

    public static string Tf(string key, params object[] args)
    {
        string fmt = T(key);
        try
        {
            return args is { Length: > 0 }
                ? string.Format(CultureInfo.InvariantCulture, fmt, args)
                : fmt;
        }
        catch (FormatException)
        {
            return fmt;
        }
    }
}
