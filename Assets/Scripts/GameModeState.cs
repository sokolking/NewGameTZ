using UnityEngine;

/// <summary>
/// Глобальный режим игры между сценами: одиночная игра (против ИИ) или онлайн.
/// </summary>
public static class GameModeState
{
    public static bool IsSinglePlayer { get; private set; }

    public static void SetSinglePlayer(bool isSingle)
    {
        IsSinglePlayer = isSingle;
        Debug.Log($"[GameModeState] SinglePlayer = {IsSinglePlayer}");
    }
}

