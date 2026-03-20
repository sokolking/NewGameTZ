using UnityEngine;

/// <summary>
/// Когда видны модальные панели (пауза, ожидание раунда, диалог пропуска ОД),
/// с карты не должны обрабатываться наведение, клики, подсветка ОД, зум/пан камеры.
/// </summary>
public static class GameplayMapInputBlock
{
    public static bool IsBlocked =>
        ActionPointsUI.IsModalDialogOpen ||
        ActionPointsUI.IsRoundWaitPanelVisible ||
        InGameMenuUI.IsPauseMenuOpen;
}
