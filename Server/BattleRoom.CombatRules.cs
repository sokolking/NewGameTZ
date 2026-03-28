using BattleServer.Models;

namespace BattleServer;

/// <summary>
/// Зафиксированные правила боя (дизайн).
/// <list type="bullet">
/// <item><description>2.5 — метательное и гранаты: то же действие <see cref="BattleRoom.ActionAttack"/> и тот же гексовый прицел, без отдельного типа хода.</description></item>
/// <item><description>2.6 — граната: три броска слота силуэта <b>с возвращением</b> (один слот может выпасть несколько раз).</description></item>
/// <item><description>5.13 — флаг <c>is_sniper</c> у оружия (<see cref="UnitStateDto.WeaponIsSniper"/>): ослабленный штраф к <c>p_дистанция</c> за гексы за пределами дальности (0.65^N вместо 0.5^N); урон за пределами дальности без изменений.</description></item>
    /// <item><description>5.15 — шанс попадания: <c>p = clamp01(p_дистанция × множители_укрытия + бонус_меткости − (1 − кучность_оружия))</c>; кучность в данных 0…1 (выше — кучнее).</description></item>
/// <item><description>7.20 — ЛС по стенам: <see cref="UnitStateDto.WeaponTrajectoryHeight"/> (0…2) и тег стены <c>wall</c>/<c>wall_low</c>; выстрел блокируется, если высота преграды по ЛС не ниже траектории (реализация в <c>BattleRoom.LineOfFire.cs</c>).</description></item>
/// </list>
/// </summary>
public partial class BattleRoom
{
    /// <summary>Explicit struct (no positional record) so debug fields cannot be confused in tooling.</summary>
    private readonly struct HitFormulaDebug
    {
        public HitFormulaDebug(
            double probability,
            double basePDistance,
            double treeF,
            double rockF,
            double coverMul,
            double accBonus,
            double weaponTightness,
            double spreadRaw,
            double spread)
        {
            Probability = probability;
            BasePDistance = basePDistance;
            TreeF = treeF;
            RockF = rockF;
            CoverMul = coverMul;
            AccBonus = accBonus;
            WeaponTightness = weaponTightness;
            SpreadRaw = spreadRaw;
            Spread = spread;
        }

        public double Probability { get; }
        /// <summary><see cref="GetBaseHitProbabilityFromRange"/> output (distance only, before cover/acc/spread).</summary>
        public double BasePDistance { get; }
        public double TreeF { get; }
        public double RockF { get; }
        public double CoverMul { get; }
        public double AccBonus { get; }
        public double WeaponTightness { get; }
        public double SpreadRaw { get; }
        public double Spread { get; }
    }

    /// <summary>Бонус к вероятности за пункт меткости (как раньше: +2% за пункт).</summary>
    private const double AccuracyToHitBonusPerPoint = 0.02;

    /// <summary>
    /// Кучность оружия в БД/UI: 0…1, <b>чем больше — тем кучнее</b> (выше шанс попадания).
    /// В формуле боя вычитается штраф <c>1 − T</c> (см. <see cref="CombineHitProbability"/>).
    /// </summary>
    public static double SpreadPenaltyFromTightness(double tightness)
    {
        double t = Math.Clamp(tightness, 0.0, 1.0);
        return Math.Clamp(1.0 - t, 0.0, 1.0);
    }

    /// <summary>
    /// p_дистанция уже учитывает штраф за гексы за пределами номинальной дальности (см. <see cref="GetBaseHitProbabilityFromRange"/>).
    /// Укрытие: дерево и камень дают множители &lt; 1; формула — произведение, затем аддитивно меткость; из кучности <paramref name="weaponTightness"/> вычитается <c>clamp(1 − T)</c> (см. <see cref="SpreadPenaltyFromTightness"/>), затем clamp до 0.95.
    /// </summary>
    private static HitFormulaDebug BuildHitFormulaDebug(
        double pDistance,
        bool anyTreeOnCoverLine,
        bool anyRockOnCoverLineAndTargetSitHide,
        BattleObstacleBalanceRowDto bal,
        int accuracy,
        double weaponTightness)
    {
        double treeF = anyTreeOnCoverLine ? 1.0 - bal.TreeCoverMissPercent / 100.0 : 1.0;
        if (treeF < 0)
            treeF = 0;
        double rockF = anyRockOnCoverLineAndTargetSitHide ? 1.0 - bal.RockCoverMissPercent / 100.0 : 1.0;
        if (rockF < 0)
            rockF = 0;
        double coverMul = treeF * rockF;
        double accBonus = Math.Max(0, accuracy) * AccuracyToHitBonusPerPoint;
        double t = Math.Clamp(weaponTightness, 0.0, 1.0);
        double spreadRaw = SpreadPenaltyFromTightness(t);
        double spread = Math.Clamp(spreadRaw, 0.0, 0.95);
        double p = pDistance * coverMul + accBonus - spread;
        if (p < 0)
            p = 0;
        if (p > 1)
            p = 1;
        return new HitFormulaDebug(
            probability: p,
            basePDistance: pDistance,
            treeF: treeF,
            rockF: rockF,
            coverMul: coverMul,
            accBonus: accBonus,
            weaponTightness: t,
            spreadRaw: spreadRaw,
            spread: spread);
    }

    private static double CombineHitProbability(
        double pDistance,
        bool anyTreeOnCoverLine,
        bool anyRockOnCoverLineAndTargetSitHide,
        BattleObstacleBalanceRowDto bal,
        int accuracy,
        double weaponTightness)
    {
        return BuildHitFormulaDebug(pDistance, anyTreeOnCoverLine, anyRockOnCoverLineAndTargetSitHide, bal, accuracy, weaponTightness).Probability;
    }
}
