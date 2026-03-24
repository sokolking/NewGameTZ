using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    /// <summary>Стоимость n-го шага (как в клиентском Player.GetStepCost).</summary>
    public static int GetStepCost(int stepIndex)
    {
        if (stepIndex <= 0)
            return 0;

        float n = stepIndex;
        float val = (5f * n * n - 8f * n + 21f) / 3f;
        return Math.Max(1, (int)Math.Round(val));
    }

    private static int GetMoveCost(int fromStepIndex, int steps)
    {
        if (steps <= 0)
            return 0;

        return GetStepCost(fromStepIndex + steps) - GetStepCost(fromStepIndex);
    }

    private static string NormalizePosture(string? posture)
    {
        if (string.IsNullOrWhiteSpace(posture))
            return PostureWalk;

        return posture.Trim().ToLowerInvariant() switch
        {
            PostureRun => PostureRun,
            PostureSit => PostureSit,
            PostureHide => PostureHide,
            _ => PostureWalk
        };
    }

    private static bool CanMoveInPosture(string? posture) => NormalizePosture(posture) != PostureHide;

    private static int GetMovementStepCost(string? posture, int stepIndex)
    {
        int baseCost = GetMoveCost(stepIndex - 1, 1);
        return NormalizePosture(posture) switch
        {
            PostureRun => Math.Max(1, (int)Math.Ceiling(baseCost * RunCostMultiplier)),
            PostureSit or PostureHide => Math.Max(1, (int)Math.Floor(baseCost * SitCostMultiplier)),
            _ => Math.Max(1, baseCost)
        };
    }

    private static int GetMovementCost(string? posture, int fromStepIndex, int steps)
    {
        if (steps <= 0)
            return 0;

        int total = 0;
        for (int i = 1; i <= steps; i++)
            total += GetMovementStepCost(posture, fromStepIndex + i);
        return total;
    }

    private static int GetMaxReachableStepsForPosture(string? posture, int stepsAlready, int currentAp)
    {
        if (!CanMoveInPosture(posture))
            return 0;

        int maxSteps = 0;
        for (int steps = 1; ; steps++)
        {
            if (GetMovementCost(posture, stepsAlready, steps) > currentAp)
                break;
            maxSteps = steps;
        }
        return maxSteps;
    }

    private static int GetUnitMaxAp(UnitStateDto unit)
    {
        if (unit == null)
            return MaxAp;
        return unit.UnitType == UnitType.Mob ? MobMaxAp : Math.Max(1, unit.MaxAp > 0 ? unit.MaxAp : MaxAp);
    }

    private static int GetFatigueAp(UnitStateDto unit)
    {
        if (unit == null || unit.UnitType == UnitType.Mob)
            return 0;

        int maxAp = GetUnitMaxAp(unit);
        int maxPenaltyAp = (int)Math.Floor(maxAp * MaxPenaltyFraction);
        return Math.Clamp((int)Math.Round(Math.Clamp(unit.PenaltyFraction, 0f, MaxPenaltyFraction) * maxAp), 0, maxPenaltyAp);
    }

    private static void SetFatigueAp(UnitStateDto unit, int fatigueAp, int maxAp)
    {
        if (unit == null || unit.UnitType == UnitType.Mob)
            return;

        int maxPenaltyAp = (int)Math.Floor(maxAp * MaxPenaltyFraction);
        unit.PenaltyFraction = maxAp > 0 ? Math.Clamp(fatigueAp, 0, maxPenaltyAp) / (float)maxAp : 0f;
    }

    private static int GetNextRoundAp(UnitStateDto unit, int fatigueAp)
    {
        int maxAp = GetUnitMaxAp(unit);
        if (unit.UnitType == UnitType.Mob)
            return MobMaxAp;
        return Math.Max(0, maxAp - Math.Clamp(fatigueAp, 0, maxAp));
    }

    private static int GetRunPenaltyThreshold(int maxAp) =>
        Math.Max(1, (int)Math.Ceiling(maxAp * RunPenaltyThresholdFraction));

    private static int CalculateRunPenaltyIncreaseAp(int maxAp, int normalRunHexCount, int penaltyRunHexCount)
    {
        if (maxAp <= 0 || (normalRunHexCount <= 0 && penaltyRunHexCount <= 0))
            return 0;

        double totalFraction =
            normalRunHexCount * RunStepPenaltyFraction +
            penaltyRunHexCount * RunStepPenaltyHexFraction;
        int maxPenaltyAp = (int)Math.Floor(maxAp * MaxPenaltyFraction);
        return Math.Clamp((int)Math.Round(maxAp * totalFraction), 0, maxPenaltyAp);
    }

    private static int ApplyRestRecovery(int currentAp, int maxAp)
    {
        maxAp = Math.Max(0, maxAp);
        currentAp = Math.Clamp(currentAp, 0, maxAp);
        int missingAp = Math.Max(0, maxAp - currentAp);
        if (missingAp <= 0)
            return currentAp;

        int recovery = Math.Max(RestRecoveryMinAp, (int)Math.Ceiling(missingAp * RestRecoveryFraction));
        return Math.Min(maxAp, currentAp + recovery);
    }

    private static int GetUnitMaxAp(UnitType unitType) =>
        unitType == UnitType.Mob ? MobMaxAp : MaxAp;

    private static int GetUnitRoundStartAp(UnitType unitType, float penaltyFraction) =>
        unitType == UnitType.Mob
            ? MobMaxAp
            : Math.Max(0, (int)Math.Round(MaxAp * (1.0 - penaltyFraction)));

    private static int GetMaxReachableSteps(int currentAp)
    {
        int maxSteps = 0;
        for (int steps = 1; ; steps++)
        {
            if (GetMoveCost(0, steps) > currentAp)
                break;
            maxSteps = steps;
        }
        return maxSteps;
    }

    private static string FormatPath(HexPositionDto[]? path)
    {
        if (path == null || path.Length == 0) return "[]";
        return "[" + string.Join(" -> ", path.Select(p => $"({p.Col},{p.Row})")) + "]";
    }
}
