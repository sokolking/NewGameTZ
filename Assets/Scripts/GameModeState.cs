using UnityEngine;

/// <summary>
/// Глобальный режим игры между сценами: одиночная игра (против ИИ) или онлайн.
/// </summary>
public static class GameModeState
{
    private const string PrefsKeySinglePlayer = "GameMode_SinglePlayer";

    public static bool IsSinglePlayer { get; private set; }

    static GameModeState()
    {
        IsSinglePlayer = PlayerPrefs.GetInt(PrefsKeySinglePlayer, 0) == 1;
    }

    public static void SetSinglePlayer(bool isSingle)
    {
        IsSinglePlayer = isSingle;
        PlayerPrefs.SetInt(PrefsKeySinglePlayer, isSingle ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[GameModeState] SinglePlayer = {IsSinglePlayer}");
    }
}

