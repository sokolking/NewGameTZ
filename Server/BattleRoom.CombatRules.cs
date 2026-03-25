using BattleServer.Models;

namespace BattleServer;

/// <summary>
/// Зафиксированные правила боя (дизайн).
/// <list type="bullet">
/// <item><description>2.5 — метательное и гранаты: то же действие <see cref="BattleRoom.ActionAttack"/> и тот же гексовый прицел, без отдельного типа хода.</description></item>
/// <item><description>2.6 — граната: три броска слота силуэта <b>с возвращением</b> (один слот может выпасть несколько раз).</description></item>
/// <item><description>5.15 — шанс попадания: <c>p = clamp01(p_дистанция × множители_укрытия + бонус_меткости − штраф_кучности)</c>.</description></item>
/// <item><description>7.20 — ЛС по стенам: <see cref="UnitStateDto.WeaponTrajectoryHeight"/> (0…2) и тег стены <c>wall</c>/<c>wall_low</c>; выстрел блокируется, если высота преграды по ЛС не ниже траектории (реализация в <c>BattleRoom.LineOfFire.cs</c>).</description></item>
/// </list>
/// </summary>
public partial class BattleRoom
{
    /// <summary>Бонус к вероятности за пункт меткости (как раньше: +2% за пункт).</summary>
    private const double AccuracyToHitBonusPerPoint = 0.02;

    /// <summary>
    /// p_дистанция уже учитывает штраф за гексы за пределами номинальной дальности (см. <see cref="GetBaseHitProbabilityFromRange"/>).
    /// Укрытие: дерево и камень дают множители &lt; 1; формула — произведение, затем аддитивно меткость и кучность.
    /// </summary>
    private static double CombineHitProbability(
        double pDistance,
        bool anyTreeOnCoverLine,
        bool anyRockOnCoverLineAndTargetSitHide,
        BattleObstacleBalanceRowDto bal,
        int accuracy,
        double weaponSpreadPenalty)
    {
        double treeF = anyTreeOnCoverLine ? 1.0 - bal.TreeCoverMissPercent / 100.0 : 1.0;
        if (treeF < 0)
            treeF = 0;
        double rockF = anyRockOnCoverLineAndTargetSitHide ? 1.0 - bal.RockCoverMissPercent / 100.0 : 1.0;
        if (rockF < 0)
            rockF = 0;
        double coverMul = treeF * rockF;
        double accBonus = Math.Max(0, accuracy) * AccuracyToHitBonusPerPoint;
        double spread = Math.Clamp(weaponSpreadPenalty, 0.0, 0.95);
        double p = pDistance * coverMul + accBonus - spread;
        if (p < 0)
            p = 0;
        if (p > 1)
            p = 1;
        return p;
    }
}
