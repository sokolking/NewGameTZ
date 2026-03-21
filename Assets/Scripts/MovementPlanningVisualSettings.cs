/// <summary>
/// Визуал движения в режиме планирования хода (до отправки на сервер).
/// Значение выставляет <see cref="ActionPointsUI"/> с Toggle «показать анимацию».
/// </summary>
public static class MovementPlanningVisualSettings
{
    /// <summary>
    /// True — пошаговая анимация как сейчас; false — мгновенная телепортация по пути.
    /// </summary>
    public static bool ShowMovementAnimation { get; set; } = true;
}
