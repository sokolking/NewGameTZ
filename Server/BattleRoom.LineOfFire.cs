namespace BattleServer;

/// <summary>7.20 — явные правила пересечения линии выстрела со стенами по «высоте» (уровни 0…2).</summary>
public partial class BattleRoom
{
    /// <summary>
    /// Высота преграды для ЛС пули по гексам: 1 — низкая (можно «перебросить» высокой траекторией), 2 — полная стена.
    /// 7.20 — явное правило: выстрел блокируется, если <c>высота_стены ≥ высота_траектории_оружия</c> (0…2).
    /// </summary>
    private static int GetWallLosHeight(string wallTag) => wallTag switch
    {
        "wall_low" or "damaged_wall_low" => 1,
        "wall" or "damaged_wall" => 2,
        _ => 0
    };

    /// <summary>Стена / низкая стена / повреждённый вариант — занимают клетку и ломаются от урона.</summary>
    private static bool IsWallObstacleTag(string? tag) =>
        tag == "wall" || tag == "damaged_wall" || tag == "wall_low" || tag == "damaged_wall_low";

    /// <summary>true — луч к цели упирается в эту клетку при данной высоте траектории оружия.</summary>
    private static bool CellBlocksLineOfFire(string? obstacleTag, int weaponTrajectoryHeight)
    {
        if (string.IsNullOrEmpty(obstacleTag) || !IsWallObstacleTag(obstacleTag))
            return false;
        int wallH = GetWallLosHeight(obstacleTag);
        if (wallH <= 0)
            return false;
        int shotH = Math.Clamp(weaponTrajectoryHeight, 0, 3);
        return wallH >= shotH;
    }
}
