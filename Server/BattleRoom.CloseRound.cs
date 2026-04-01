using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private static int GetHexesBeyondWeaponRange(int hexDistance, int weaponRange) =>
        Math.Max(0, hexDistance - Math.Max(0, weaponRange));

    /// <summary>За каждый гекс дальше номинальной дальности оружия урон умножается на 0.5.</summary>
    private static int ApplyOverRangeDamage(int rawDamage, int hexDistanceToImpact, int weaponRange)
    {
        if (rawDamage <= 0)
            return 0;
        int over = GetHexesBeyondWeaponRange(hexDistanceToImpact, weaponRange);
        double mult = Math.Pow(0.5, over);
        return Math.Max(0, (int)Math.Floor(rawDamage * mult));
    }

    private int RollWeaponDamageInclusive(UnitStateDto unit)
    {
        int min = Math.Max(0, unit.WeaponDamageMin);
        int max = Math.Max(0, unit.WeaponDamage);
        if (min > max)
            (min, max) = (max, min);
        if (max <= 0)
            return 0;
        return _rng.Next(min, max + 1);
    }

    /// <summary>Только p_дистанция (0…1). За пределами <paramref name="weaponRange"/>: обычное оружие — 0.5^N, снайпер — 0.65^N (только p, урон всегда 0.5^N). Итог — <see cref="CombineHitProbability"/>.</summary>
    private static double GetBaseHitProbabilityFromRange(int hexDistance, int weaponRange, bool weaponIsSniper)
    {
        int wr = Math.Max(1, weaponRange);
        if (hexDistance <= 1)
            return 1.0;
        int dClamped = Math.Min(hexDistance, Math.Max(0, weaponRange));
        double p = (wr + 1 - dClamped) / (double)wr;
        if (p < 0)
            p = 0;
        if (p > 1)
            p = 1;
        int over = GetHexesBeyondWeaponRange(hexDistance, weaponRange);
        if (over > 0)
        {
            double perHex = weaponIsSniper ? 0.65 : 0.5;
            p *= Math.Pow(perHex, over);
        }
        return p;
    }

    /// <summary>Закрыть раунд: пошаговая симуляция (приоритет по порядку SubmitTurn), пересчёт ОД по actualPath, TurnResult.</summary>
    /// <param name="fromTimer">true — время вышло; false — все участники сдали ход досрочно.</param>
    public void CloseRound(bool fromTimer = false)
    {
        var resolveReason = fromTimer ? "timerExpired" : "allSubmitted";
        Console.WriteLine($"[tzInfo] CloseRound begin: battleId={BattleId}, roundIndex={RoundIndex}, reason={resolveReason}, submissions={Submissions.Count}, units={Units.Count}");
        EnsureMobCommandsForCurrentRound();

        var escapeChannelAtRoundStart = new Dictionary<string, int>(EscapingPlayers);

        var order = new List<string>();
        foreach (var pid in SubmissionOrder)
        {
            if (PlayerToUnitId.TryGetValue(pid, out var submittedUid) && Units.ContainsKey(submittedUid) && !order.Contains(submittedUid))
                order.Add(submittedUid);
        }
        foreach (var kv in PlayerToUnitId)
        {
            if (Units.ContainsKey(kv.Value) && !order.Contains(kv.Value))
                order.Add(kv.Value);
        }
        foreach (var mob in Units.Values.Where(u => u.UnitType == UnitType.Mob))
        {
            if (!order.Contains(mob.UnitId))
                order.Add(mob.UnitId);
        }

        var positions = new Dictionary<string, (int col, int row)>();
        var alive = new Dictionary<string, bool>();
        var actualPaths = new Dictionary<string, List<HexPositionDto>>();
        var executedActions = new Dictionary<string, List<ExecutedBattleActionDto>>();
        var accepted = new Dictionary<string, bool>();
        var rejectedReason = new Dictionary<string, string?>();
        var damageByUnit = new Dictionary<string, int>();
        var attackTargetByUnit = new Dictionary<string, string>();
        var actionBudget = new Dictionary<string, float>();
        var actionCursor = new Dictionary<string, int>();
        var actionQueues = new Dictionary<string, QueuedBattleActionDto[]>();
        var roundStartAp = new Dictionary<string, int>();
        var postureByUnit = new Dictionary<string, string>();
        var roundStartPostureByUnit = new Dictionary<string, string>();
        var movementStepsTaken = new Dictionary<string, int>();
        var lastMovePostureByUnit = new Dictionary<string, string?>();
        var hadRunMovementByUnit = new Dictionary<string, bool>();
        var runMovementApSpent = new Dictionary<string, int>();
        var runNormalHexCount = new Dictionary<string, int>();
        var runPenaltyHexCount = new Dictionary<string, int>();
        var apSpentByUnit = new Dictionary<string, int>();
        var reloadedByPlayerAndAmmoTypeId = new Dictionary<string, Dictionary<long, int>>(StringComparer.OrdinalIgnoreCase);
        var consumedByPlayerAndItemId = new Dictionary<string, Dictionary<long, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var uid in order)
        {
            if (!Units.TryGetValue(uid, out var us))
                continue;

            if (us.UnitType == UnitType.Player)
            {
                string pid = PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key ?? uid;
                if (_userDb != null && TryGetBattlePlayerUserId(pid, out long dbUid) &&
                    _userDb.TryGetUserWeaponChamberRoundsByItemId(dbUid, us.WeaponItemId, out int dbChamber))
                    us.CurrentMagazineRounds = Math.Max(0, dbChamber);
                Units[uid] = us;
            }

            positions[uid] = (us.Col, us.Row);
            alive[uid] = us.CurrentHp > 0;
            actualPaths[uid] = new List<HexPositionDto> { new HexPositionDto { Col = us.Col, Row = us.Row } };
            executedActions[uid] = new List<ExecutedBattleActionDto>();
            accepted[uid] = true;
            rejectedReason[uid] = null;
            actionBudget[uid] = 0f;
            actionCursor[uid] = 0;
            roundStartAp[uid] = Math.Max(0, us.CurrentAp);
            postureByUnit[uid] = NormalizePosture(us.Posture);
            roundStartPostureByUnit[uid] = postureByUnit[uid];
            movementStepsTaken[uid] = 0;
            lastMovePostureByUnit[uid] = null;
            hadRunMovementByUnit[uid] = false;
            runMovementApSpent[uid] = 0;
            runNormalHexCount[uid] = 0;
            runPenaltyHexCount[uid] = 0;
            apSpentByUnit[uid] = 0;
            var queued = UnitCommands.TryGetValue(uid, out var cmd) && cmd.Actions != null
                ? cmd.Actions
                : Array.Empty<QueuedBattleActionDto>();
            if (us.UnitType == UnitType.Player)
            {
                string escPid = PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key ?? "";
                if (!string.IsNullOrEmpty(escPid) && EscapingPlayers.ContainsKey(escPid))
                {
                    queued = Array.Empty<QueuedBattleActionDto>();
                    roundStartAp[uid] = 0;
                }
            }

            actionQueues[uid] = queued;
        }

        var occupied = new HashSet<(int col, int row)>(positions
            .Where(kv => alive.TryGetValue(kv.Key, out var isAlive) && isAlive)
            .Select(kv => kv.Value));
        int lifecycleTickCount = Math.Max(1, roundStartAp.Values.DefaultIfEmpty(1).Max());
        var mapUpdates = new List<MapUpdateDto>();

        for (int tick = 1; tick <= lifecycleTickCount; tick++)
        {
            foreach (var uid in order)
            {
                if (!alive.TryGetValue(uid, out var isAlive) || !isAlive)
                    continue;
                if (!Units.TryGetValue(uid, out var unit))
                    continue;

                actionBudget[uid] += roundStartAp[uid] / (float)lifecycleTickCount;
            }

            bool executedAnyActionThisPass;
            do
            {
                executedAnyActionThisPass = false;

                foreach (var uid in order)
                {
                    if (!alive.TryGetValue(uid, out var isAlive) || !isAlive)
                        continue;
                    if (!Units.TryGetValue(uid, out var unit))
                        continue;

                    QueuedBattleActionDto? action = null;
                    int cost = 1;
                    string currentPosture = postureByUnit.TryGetValue(uid, out var storedPosture) ? storedPosture : PostureWalk;

                    if (unit.UnitType == UnitType.Mob)
                    {
                        if (actionBudget[uid] + 0.0001f < 1f)
                            continue;
                        action = BuildMobActionForCurrentState(uid, positions, alive, occupied);
                        if (action == null)
                            continue;
                        cost = Math.Max(1, action.Cost);
                    }
                    else
                    {
                        var queue = actionQueues[uid];
                        if (actionCursor[uid] >= queue.Length)
                            continue;

                        action = queue[actionCursor[uid]];
                        string queuedActionType = action?.ActionType ?? string.Empty;
                        if (string.Equals(queuedActionType, ActionMoveStep, StringComparison.OrdinalIgnoreCase))
                        {
                            cost = GetMovementStepCost(currentPosture, movementStepsTaken[uid]);
                            var moveTp = action?.TargetPosition;
                            if (moveTp != null
                                && unit.UnitType == UnitType.Player
                                && positions.TryGetValue(uid, out var moveFrom)
                                && !IsEscapeBorderHex(moveFrom.col, moveFrom.row)
                                && IsEscapeBorderHex(moveTp.Col, moveTp.Row))
                            {
                                int remainingAp = Math.Max(0, roundStartAp[uid] - apSpentByUnit.GetValueOrDefault(uid));
                                if (remainingAp >= 1)
                                    cost = remainingAp;
                            }
                        }
                        else if (string.Equals(queuedActionType, ActionChangePosture, StringComparison.OrdinalIgnoreCase))
                            cost = ChangePostureCost;
                        else if (string.Equals(queuedActionType, ActionWait, StringComparison.OrdinalIgnoreCase))
                            cost = Math.Max(1, action?.Cost ?? 1);
                        else if (string.Equals(queuedActionType, ActionReload, StringComparison.OrdinalIgnoreCase))
                            cost = Math.Max(1, action?.Cost ?? 1);
                        else if (string.Equals(queuedActionType, ActionEquipWeapon, StringComparison.OrdinalIgnoreCase))
                            cost = Math.Max(1, action?.Cost ?? 2);
                        else if (string.Equals(queuedActionType, ActionUseItem, StringComparison.OrdinalIgnoreCase))
                            cost = Math.Max(1, action?.Cost ?? 1);
                        else
                            cost = Math.Max(1, action?.Cost ?? 1);
                        if (actionBudget[uid] + 0.0001f < cost)
                            continue;
                        actionCursor[uid]++;
                    }

                    if (action == null)
                        continue;

                    actionBudget[uid] -= cost;
                    apSpentByUnit[uid] = apSpentByUnit.GetValueOrDefault(uid) + Math.Max(0, cost);
                    executedAnyActionThisPass = true;

                    var currentPos = positions[uid];
                    string actionType = action.ActionType ?? string.Empty;
                    var executed = new ExecutedBattleActionDto
                    {
                        UnitId = uid,
                        ActionType = actionType,
                        Tick = tick,
                        Succeeded = false,
                        FromPosition = new HexPositionDto { Col = currentPos.col, Row = currentPos.row },
                        ToPosition = new HexPositionDto { Col = currentPos.col, Row = currentPos.row },
                        TargetUnitId = action.TargetUnitId,
                        BodyPart = NormalizeBodyPartId(action.BodyPart),
                        Posture = currentPosture
                    };

                    if (string.Equals(actionType, ActionMoveStep, StringComparison.OrdinalIgnoreCase))
                    {
                        if (action.TargetPosition == null)
                        {
                            executed.FailureReason = "Move target missing";
                        }
                        else if (!CanMoveInPosture(currentPosture))
                        {
                            executed.FailureReason = "Current posture cannot move";
                        }
                        else
                        {
                            var targetCell = (action.TargetPosition.Col, action.TargetPosition.Row);
                            if (!IsLegalMoveDestinationHex(targetCell.Item1, targetCell.Item2))
                                executed.FailureReason = "Move target out of bounds";
                            else if (!IsInActiveZone(currentPos.col, currentPos.row) && !IsEscapeBorderHex(currentPos.col, currentPos.row))
                                executed.FailureReason = "Invalid move origin";
                            else if (_obstacleTags.ContainsKey(targetCell))
                                executed.FailureReason = "Move blocked by obstacle";
                            else if (!AreAdjacent(currentPos, targetCell))
                                executed.FailureReason = "Move target is not adjacent";
                            else if (occupied.Contains(targetCell))
                                executed.FailureReason = "Move target already occupied";
                            else
                            {
                                occupied.Remove(currentPos);
                                occupied.Add(targetCell);
                                positions[uid] = targetCell;
                                actualPaths[uid].Add(new HexPositionDto { Col = targetCell.Item1, Row = targetCell.Item2 });
                                movementStepsTaken[uid] = movementStepsTaken.GetValueOrDefault(uid) + 1;
                                // Locomotion for replay must match the client's queued step (planning), not only sim posture
                                // (e.g. final ChangePosture must not affect earlier MoveStep visuals).
                                string replayMovePosture = !string.IsNullOrWhiteSpace(action.Posture)
                                    ? NormalizePosture(action.Posture)
                                    : currentPosture;
                                executed.Posture = replayMovePosture;
                                lastMovePostureByUnit[uid] = replayMovePosture;
                                executed.Succeeded = true;
                                executed.ToPosition = new HexPositionDto { Col = targetCell.Item1, Row = targetCell.Item2 };

                                if (string.Equals(currentPosture, PostureRun, StringComparison.OrdinalIgnoreCase))
                                {
                                    hadRunMovementByUnit[uid] = true;
                                    runMovementApSpent[uid] = runMovementApSpent.GetValueOrDefault(uid) + cost;
                                    if (runMovementApSpent[uid] >= GetRunPenaltyThreshold(GetUnitMaxAp(unit)))
                                    {
                                        runPenaltyHexCount[uid] = runPenaltyHexCount.GetValueOrDefault(uid) + 1;
                                    }
                                    else
                                    {
                                        runNormalHexCount[uid] = runNormalHexCount.GetValueOrDefault(uid) + 1;
                                    }
                                }

                            }
                        }
                    }
                    else if (string.Equals(actionType, ActionAttack, StringComparison.OrdinalIgnoreCase))
                    {
                        int magazineSize = GetWeaponMagazineSizeFromDbByItemId(unit.WeaponItemId);
                        if (magazineSize > 0 && unit.CurrentMagazineRounds <= 0)
                        {
                            executed.FailureReason = "Magazine is empty";
                        }
                        else
                        {
                        string? resolvedTargetId = ResolveUnitId(action.TargetUnitId);
                        if (string.IsNullOrEmpty(resolvedTargetId))
                        {
                            // Выстрел по гексу без цели-юнита: направление и урон по стене на ЛС — TargetPosition.
                            if (action.TargetPosition == null)
                            {
                                executed.FailureReason = "Attack aim hex missing";
                            }
                            else
                            {
                                int ac = action.TargetPosition.Col;
                                int ar = action.TargetPosition.Row;
                                if (!IsInActiveZone(ac, ar))
                                {
                                    executed.FailureReason = "Attack aim out of bounds";
                                }
                                else
                                {
                                    executed.ToPosition = new HexPositionDto { Col = ac, Row = ar };
                                    int dist = HexSpawn.HexDistance(currentPos.col, currentPos.row, ac, ar);
                                    int weaponRange = Math.Max(0, unit.WeaponRange);
                                    int rawDamage = RollWeaponDamageInclusive(unit);
                                    var bal = _obstacleDb?.GetBalance() ?? BattleObstacleBalanceRowDto.Defaults;

                                    HexSpawn.GetHexLineBetweenExclusive(currentPos.col, currentPos.row, ac, ar, _hexLineBuffer);

                                        (int col, int row)? firstWall = null;
                                        foreach (var cell in _hexLineBuffer)
                                        {
                                            if (_obstacleTags.TryGetValue(cell, out var tag) && CellBlocksLineOfFire(tag, unit.WeaponTrajectoryHeight))
                                            {
                                                firstWall = cell;
                                                break;
                                            }
                                        }

                                        bool wallDone = false;
                                        if (firstWall.HasValue)
                                        {
                                            var wc = firstWall.Value;
                                            executed.Succeeded = true;
                                            executed.Damage = 0;
                                            int dWall = HexSpawn.HexDistance(currentPos.col, currentPos.row, wc.col, wc.row);
                                            ApplyWallDamageAndRecord(tick, wc, ApplyOverRangeDamage(rawDamage, dWall, weaponRange), bal, mapUpdates);
                                            wallDone = true;
                                        }
                                        else if (_obstacleTags.TryGetValue((ac, ar), out var aimTag) && CellBlocksLineOfFire(aimTag, unit.WeaponTrajectoryHeight))
                                        {
                                            // Соседний гекс: линия «между» пуста — стена на гексе прицела.
                                            (int col, int row) wc = (ac, ar);
                                            executed.Succeeded = true;
                                            executed.Damage = 0;
                                            int dWall = HexSpawn.HexDistance(currentPos.col, currentPos.row, wc.col, wc.row);
                                            ApplyWallDamageAndRecord(tick, wc, ApplyOverRangeDamage(rawDamage, dWall, weaponRange), bal, mapUpdates);
                                            wallDone = true;
                                        }

                                        if (!wallDone)
                                        {
                                            // Первый враг на линии к гексу прицела (Ctrl+hex) — та же логика, что при атаке по юниту за укрытием.
                                            string? hexHitId = null;
                                            UnitStateDto? hexHitUnit = null;
                                            (int col, int row) hexHitPos = default;

                                            foreach (var kv in positions)
                                            {
                                                if (kv.Value.col != ac || kv.Value.row != ar)
                                                    continue;
                                                string oid = kv.Key;
                                                if (oid == uid)
                                                    continue;
                                                if (!alive.GetValueOrDefault(oid))
                                                    continue;
                                                if (!Units.TryGetValue(oid, out var ou))
                                                    continue;
                                                if (!AreEnemies(unit, ou))
                                                    continue;
                                                hexHitId = oid;
                                                hexHitUnit = ou;
                                                hexHitPos = kv.Value;
                                                break;
                                            }

                                            if (hexHitId == null)
                                            {
                                                foreach (var cell in _hexLineBuffer)
                                                {
                                                    foreach (var kv in positions)
                                                    {
                                                        if (kv.Value.col != cell.col || kv.Value.row != cell.row)
                                                            continue;
                                                        string oid = kv.Key;
                                                        if (oid == uid)
                                                            continue;
                                                        if (!alive.GetValueOrDefault(oid))
                                                            continue;
                                                        if (!Units.TryGetValue(oid, out var ou))
                                                            continue;
                                                        if (!AreEnemies(unit, ou))
                                                            continue;
                                                        hexHitId = oid;
                                                        hexHitUnit = ou;
                                                        hexHitPos = kv.Value;
                                                        break;
                                                    }

                                                    if (hexHitId != null)
                                                        break;
                                                }
                                            }

                                            if (hexHitId == null || hexHitUnit == null)
                                            {
                                                executed.Succeeded = true;
                                                executed.Damage = 0;
                                            }
                                            else
                                            {
                                                executed.TargetUnitId = hexHitId;
                                                executed.ToPosition = new HexPositionDto { Col = hexHitPos.col, Row = hexHitPos.row };

                                                _coverLineBuffer.Clear();
                                                _coverLineBuffer.AddRange(_hexLineBuffer);

                                                int distHex = HexSpawn.HexDistance(currentPos.col, currentPos.row, hexHitPos.col, hexHitPos.row);
                                                int weaponRangeHex = Math.Max(0, unit.WeaponRange);
                                                bool anyTreeHex = false;
                                                bool anyRockHex = false;
                                                foreach (var cell in _coverLineBuffer)
                                                {
                                                    if (_obstacleTags.TryGetValue(cell, out var otag))
                                                    {
                                                        if (otag == "tree")
                                                            anyTreeHex = true;
                                                        if (otag == "rock")
                                                            anyRockHex = true;
                                                    }
                                                }

                                                if (_obstacleTags.TryGetValue((hexHitPos.col, hexHitPos.row), out var tgtObstacleTagHex))
                                                {
                                                    if (tgtObstacleTagHex == "tree")
                                                        anyTreeHex = true;
                                                    if (tgtObstacleTagHex == "rock")
                                                        anyRockHex = true;
                                                }

                                                string tgtPostureHex = postureByUnit.TryGetValue(hexHitId, out var tpHex) ? NormalizePosture(tpHex) : PostureWalk;
                                                bool rockCoverHex = anyRockHex && (string.Equals(tgtPostureHex, PostureSit, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(tgtPostureHex, PostureHide, StringComparison.OrdinalIgnoreCase));
                                                var hitDbgHex = BuildHitFormulaDebug(
                                                    GetBaseHitProbabilityFromRange(distHex, weaponRangeHex, unit.WeaponIsSniper),
                                                    anyTreeHex,
                                                    rockCoverHex,
                                                    bal,
                                                    unit.Accuracy,
                                                    unit.WeaponTightness);

                                                executed.HitProbability = hitDbgHex.Probability;
                                                executed.HitDebugDistance = distHex;
                                                executed.HitDebugPDistance = hitDbgHex.BasePDistance;
                                                executed.HitDebugTreeF = hitDbgHex.TreeF;
                                                executed.HitDebugRockF = hitDbgHex.RockF;
                                                executed.HitDebugCoverMul = hitDbgHex.CoverMul;
                                                executed.HitDebugAccBonus = hitDbgHex.AccBonus;
                                                executed.HitDebugWeaponTightness = hitDbgHex.WeaponTightness;
                                                executed.HitDebugSpreadRaw = hitDbgHex.SpreadRaw;
                                                executed.HitDebugSpread = hitDbgHex.Spread;
                                                executed.HitDebugTargetPosture = tgtPostureHex;
                                                executed.HitDebugAnyTree = anyTreeHex;
                                                executed.HitDebugAnyRock = anyRockHex;
                                                bool hitHex = _rng.NextDouble() < hitDbgHex.Probability;
                                                executed.HitSucceeded = hitHex;
                                                if (!hitHex)
                                                {
                                                    executed.Succeeded = true;
                                                    executed.TargetUnitId = hexHitId;
                                                    executed.Damage = 0;
                                                    attackTargetByUnit[uid] = hexHitId;
                                                }
                                                else
                                                {
                                                    int damageHex = ApplyOverRangeDamage(rawDamage, distHex, weaponRangeHex);
                                                    hexHitUnit.CurrentHp = Math.Max(0, hexHitUnit.CurrentHp - damageHex);
                                                    Units[hexHitId] = hexHitUnit;
                                                    executed.Succeeded = true;
                                                    executed.TargetUnitId = hexHitId;
                                                    executed.Damage = damageHex;
                                                    attackTargetByUnit[uid] = hexHitId;
                                                    damageByUnit[uid] = damageByUnit.GetValueOrDefault(uid) + damageHex;

                                                    if (hexHitUnit.CurrentHp <= 0)
                                                    {
                                                        alive[hexHitId] = false;
                                                        occupied.Remove(hexHitPos);
                                                        executed.TargetDied = true;
                                                    }
                                                }
                                            }
                                        }
                                }
                            }
                        }
                        else if (!Units.TryGetValue(resolvedTargetId, out var targetUnit))
                        {
                            executed.FailureReason = "Attack target not found";
                        }
                        else if (!alive.TryGetValue(resolvedTargetId, out var targetAlive) || !targetAlive)
                        {
                            executed.FailureReason = "Attack target already dead";
                        }
                        else if (!positions.TryGetValue(resolvedTargetId, out var targetPos))
                        {
                            executed.FailureReason = "Attack target position missing";
                        }
                        else if (!AreEnemies(unit, targetUnit))
                        {
                            executed.FailureReason = "Attack target is not an enemy";
                        }
                        else
                        {
                            executed.ToPosition = new HexPositionDto { Col = targetPos.col, Row = targetPos.row };
                            int dist = HexSpawn.HexDistance(currentPos.col, currentPos.row, targetPos.col, targetPos.row);
                            int weaponRange = Math.Max(0, unit.WeaponRange);
                            int rawDamage = RollWeaponDamageInclusive(unit);
                            var bal = _obstacleDb?.GetBalance() ?? BattleObstacleBalanceRowDto.Defaults;

                            HexSpawn.GetHexLineBetweenExclusive(currentPos.col, currentPos.row, targetPos.col, targetPos.row, _hexLineBuffer);

                                (int col, int row)? firstWall = null;
                                foreach (var cell in _hexLineBuffer)
                                {
                                    if (_obstacleTags.TryGetValue(cell, out var tag) && CellBlocksLineOfFire(tag, unit.WeaponTrajectoryHeight))
                                    {
                                        firstWall = cell;
                                        break;
                                    }
                                }

                                if (firstWall.HasValue)
                                {
                                    var wc = firstWall.Value;
                                    executed.Succeeded = true;
                                    executed.TargetUnitId = resolvedTargetId;
                                    executed.Damage = 0;
                                    int dWall = HexSpawn.HexDistance(currentPos.col, currentPos.row, wc.col, wc.row);
                                    ApplyWallDamageAndRecord(tick, wc, ApplyOverRangeDamage(rawDamage, dWall, weaponRange), bal, mapUpdates);
                                    attackTargetByUnit[uid] = resolvedTargetId;
                                }
                                else
                                {
                                    // Укрытие (дерево/камень): считаем по линии к изначально выбранной цели; после редиректа на ближнего врага _hexLineBuffer укорачивается и теряет клетки «между» врагом и целью прицела.
                                    _coverLineBuffer.Clear();
                                    _coverLineBuffer.AddRange(_hexLineBuffer);

                                    // LOS: первый враг на линии к выбранной цели (строго между стрелком и целью) получает выстрел — не «пролетать» сквозь врага.
                                    bool losRedirected = false;
                                    foreach (var cell in _hexLineBuffer)
                                    {
                                        foreach (var kv in positions)
                                        {
                                            if (kv.Value != cell)
                                                continue;
                                            string oid = kv.Key;
                                            if (oid == uid)
                                                continue;
                                            if (!alive.GetValueOrDefault(oid))
                                                continue;
                                            if (!Units.TryGetValue(oid, out var ou))
                                                continue;
                                            if (!AreEnemies(unit, ou))
                                                continue;

                                            resolvedTargetId = oid;
                                            targetUnit = ou;
                                            targetPos = positions[oid];
                                            executed.ToPosition = new HexPositionDto { Col = targetPos.col, Row = targetPos.row };
                                            dist = HexSpawn.HexDistance(currentPos.col, currentPos.row, targetPos.col, targetPos.row);
                                            HexSpawn.GetHexLineBetweenExclusive(currentPos.col, currentPos.row, targetPos.col, targetPos.row, _hexLineBuffer);
                                            losRedirected = true;
                                            break;
                                        }

                                        if (losRedirected)
                                            break;
                                    }

                                    bool anyTree = false;
                                    bool anyRock = false;
                                    foreach (var cell in _coverLineBuffer)
                                    {
                                        if (_obstacleTags.TryGetValue(cell, out var otag))
                                        {
                                            if (otag == "tree")
                                                anyTree = true;
                                            if (otag == "rock")
                                                anyRock = true;
                                        }
                                    }

                                    // Дерево/камень на гексе цели тоже влияют на укрытие (линия «между» их не включает).
                                    if (_obstacleTags.TryGetValue((targetPos.col, targetPos.row), out var tgtObstacleTag))
                                    {
                                        if (tgtObstacleTag == "tree")
                                            anyTree = true;
                                        if (tgtObstacleTag == "rock")
                                            anyRock = true;
                                    }

                                    string tgtPosture = postureByUnit.TryGetValue(resolvedTargetId, out var tp) ? NormalizePosture(tp) : PostureWalk;
                                    bool rockCover = anyRock && (string.Equals(tgtPosture, PostureSit, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(tgtPosture, PostureHide, StringComparison.OrdinalIgnoreCase));
                                    var hitDbg = BuildHitFormulaDebug(
                                        GetBaseHitProbabilityFromRange(dist, weaponRange, unit.WeaponIsSniper),
                                        anyTree,
                                        rockCover,
                                        bal,
                                        unit.Accuracy,
                                        unit.WeaponTightness);

                                    executed.HitProbability = hitDbg.Probability;
                                    executed.HitDebugDistance = dist;
                                    executed.HitDebugPDistance = hitDbg.BasePDistance;
                                    executed.HitDebugTreeF = hitDbg.TreeF;
                                    executed.HitDebugRockF = hitDbg.RockF;
                                    executed.HitDebugCoverMul = hitDbg.CoverMul;
                                    executed.HitDebugAccBonus = hitDbg.AccBonus;
                                    executed.HitDebugWeaponTightness = hitDbg.WeaponTightness;
                                    executed.HitDebugSpreadRaw = hitDbg.SpreadRaw;
                                    executed.HitDebugSpread = hitDbg.Spread;
                                    executed.HitDebugTargetPosture = tgtPosture;
                                    executed.HitDebugAnyTree = anyTree;
                                    executed.HitDebugAnyRock = anyRock;
                                    bool hit = _rng.NextDouble() < hitDbg.Probability;
                                    executed.HitSucceeded = hit;
                                    if (!hit)
                                    {
                                        executed.Succeeded = true;
                                        executed.TargetUnitId = resolvedTargetId;
                                        executed.Damage = 0;
                                        attackTargetByUnit[uid] = resolvedTargetId;
                                    }
                                    else
                                    {
                                        int damage = ApplyOverRangeDamage(rawDamage, dist, weaponRange);
                                        targetUnit.CurrentHp = Math.Max(0, targetUnit.CurrentHp - damage);
                                        Units[resolvedTargetId] = targetUnit;
                                        executed.Succeeded = true;
                                        executed.TargetUnitId = resolvedTargetId;
                                        executed.Damage = damage;
                                        attackTargetByUnit[uid] = resolvedTargetId;
                                        damageByUnit[uid] = damageByUnit.GetValueOrDefault(uid) + damage;

                                        if (targetUnit.CurrentHp <= 0)
                                        {
                                            alive[resolvedTargetId] = false;
                                            occupied.Remove(targetPos);
                                            executed.TargetDied = true;
                                        }
                                    }
                                }
                        }
                        }
                        if (executed.Succeeded && magazineSize > 0)
                        {
                            unit.CurrentMagazineRounds = Math.Max(0, unit.CurrentMagazineRounds - 1);
                            Units[uid] = unit;
                        }
                    }
                    else if (string.Equals(actionType, ActionChangePosture, StringComparison.OrdinalIgnoreCase))
                    {
                        string nextPosture = NormalizePosture(action.Posture);
                        postureByUnit[uid] = nextPosture;
                        executed.Succeeded = true;
                        executed.Posture = nextPosture;
                    }
                    else if (string.Equals(actionType, ActionWait, StringComparison.OrdinalIgnoreCase))
                    {
                        executed.Succeeded = true;
                    }
                    else if (string.Equals(actionType, ActionReload, StringComparison.OrdinalIgnoreCase))
                    {
                        executed.Succeeded = true;
                        int magazineSize = GetWeaponMagazineSizeFromDbByItemId(unit.WeaponItemId);
                        int before = Math.Max(0, unit.CurrentMagazineRounds);
                        int after = Math.Max(0, magazineSize);
                        long reloadAmmoTypeId = 0;
                        if (_weaponDb != null && _weaponDb.TryGetWeaponByItemId(unit.WeaponItemId, out var reloadWeaponDef))
                            reloadAmmoTypeId = reloadWeaponDef.AmmoTypeId ?? 0;
                        if (unit.UnitType == UnitType.Player && _userDb != null && reloadAmmoTypeId > 0)
                        {
                            string pid = PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key ?? uid;
                            int reserveRounds = 0;
                            if (TryGetBattlePlayerUserId(pid, out long reloadUid))
                                _userDb.TryGetUserAmmoRoundsByAmmoTypeId(reloadUid, reloadAmmoTypeId, out reserveRounds);
                            int canLoad = Math.Max(0, reserveRounds);
                            int need = Math.Max(0, magazineSize - before);
                            after = before + Math.Min(need, canLoad);
                        }
                        int loaded = Math.Max(0, after - before);
                        unit.CurrentMagazineRounds = after;
                        Units[uid] = unit;
                        if (loaded > 0 && unit.UnitType == UnitType.Player)
                        {
                            string pid = PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key ?? uid;
                            if (reloadAmmoTypeId > 0)
                            {
                                if (!reloadedByPlayerAndAmmoTypeId.TryGetValue(pid, out var byAmmoType))
                                {
                                    byAmmoType = new Dictionary<long, int>();
                                    reloadedByPlayerAndAmmoTypeId[pid] = byAmmoType;
                                }
                                byAmmoType[reloadAmmoTypeId] = byAmmoType.GetValueOrDefault(reloadAmmoTypeId) + loaded;
                            }
                        }
                    }
                    else if (string.Equals(actionType, ActionEquipWeapon, StringComparison.OrdinalIgnoreCase))
                    {
                        long weaponItemId = action.WeaponItemId ?? 0;
                        if (_weaponDb == null && _medicineDb == null)
                        {
                            executed.FailureReason = "Weapon database missing";
                        }
                        else if (weaponItemId <= 0)
                        {
                            executed.FailureReason = "Weapon item id missing";
                        }
                        else
                        {
                            string? pid = null;
                            foreach (var kv in PlayerToUnitId)
                            {
                                if (kv.Value == uid)
                                {
                                    pid = kv.Key;
                                    break;
                                }
                            }

                            long equipUid = 0;
                            bool hasDbUser = !string.IsNullOrEmpty(pid) && TryGetBattlePlayerUserId(pid, out equipUid);

                            bool applied = false;
                            if (_weaponDb != null && _weaponDb.TryGetWeaponByItemId(weaponItemId, out var wpn))
                            {
                                if (_userDb != null && hasDbUser && !_userDb.TryValidateEquippedWeaponForRegisteredUserByItemId(equipUid, wpn.Id, out var invErr))
                                    executed.FailureReason = invErr ?? "weapon not in inventory";
                                else
                                {
                                    unit.WeaponItemId = wpn.Id;
                                    unit.WeaponDamageMin = wpn.DamageMin;
                                    unit.WeaponDamage = wpn.DamageMax;
                                    unit.WeaponRange = wpn.Range;
                                    unit.WeaponAttackApCost = Math.Max(1, wpn.AttackApCost);
                                    int equippedMag = GetWeaponMagazineSizeFromDbByItemId(wpn.Id);
                                    if (_userDb != null && hasDbUser)
                                    {
                                        int chamberFromDb;
                                        if (_userDb.TryGetUserWeaponChamberRoundsByItemId(equipUid, wpn.Id, out chamberFromDb))
                                            equippedMag = Math.Clamp(chamberFromDb, 0, Math.Max(0, wpn.MagazineSize));
                                    }
                                    unit.CurrentMagazineRounds = Math.Max(0, equippedMag);
                                    unit.WeaponTightness = Math.Clamp(wpn.Tightness, 0.0, 1.0);
                                    unit.WeaponTrajectoryHeight = Math.Clamp(wpn.TrajectoryHeight, 0, 3);
                                    unit.WeaponIsSniper = wpn.IsSniper;
                                    Units[uid] = unit;

                                    if (!string.IsNullOrEmpty(pid) && PlayerCombatProfiles.TryGetValue(pid, out var prof))
                                        PlayerCombatProfiles[pid] = (prof.Item1, prof.Item2, wpn.Id, wpn.DamageMin, wpn.DamageMax, wpn.Range, Math.Max(1, wpn.AttackApCost), prof.Item8, unit.WeaponTightness, unit.WeaponTrajectoryHeight, unit.WeaponIsSniper);
                                    if (_userDb != null && hasDbUser)
                                        _userDb.SyncEquippedWeaponForRegisteredUserByItemId(equipUid, wpn.Id);
                                    applied = true;
                                }
                            }
                            else if (_medicineDb != null && _medicineDb.TryGetMedicineByItemId(weaponItemId, out var med))
                            {
                                if (_userDb != null && hasDbUser && !_userDb.TryValidateEquippedWeaponForRegisteredUserByItemId(equipUid, med.Id, out var invErr))
                                    executed.FailureReason = invErr ?? "weapon not in inventory";
                                else
                                {
                                    unit.WeaponItemId = med.Id;
                                    unit.WeaponDamageMin = 0;
                                    unit.WeaponDamage = 0;
                                    unit.WeaponRange = 0;
                                    unit.WeaponAttackApCost = Math.Max(1, med.AttackApCost);
                                    unit.CurrentMagazineRounds = 0;
                                    unit.WeaponTightness = 1.0;
                                    unit.WeaponTrajectoryHeight = 0;
                                    unit.WeaponIsSniper = false;
                                    Units[uid] = unit;

                                    if (!string.IsNullOrEmpty(pid) && PlayerCombatProfiles.TryGetValue(pid, out var prof))
                                        PlayerCombatProfiles[pid] = (prof.Item1, prof.Item2, med.Id, 0, 0, 0, Math.Max(1, med.AttackApCost), prof.Item8, unit.WeaponTightness, unit.WeaponTrajectoryHeight, unit.WeaponIsSniper);
                                    if (_userDb != null && hasDbUser)
                                        _userDb.SyncEquippedWeaponForRegisteredUserByItemId(equipUid, med.Id);
                                    applied = true;
                                }
                            }
                            else
                                executed.FailureReason = "Unknown weapon";

                            if (applied)
                                executed.Succeeded = true;
                        }
                    }
                    else if (string.Equals(actionType, ActionUseItem, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_medicineDb == null || !_medicineDb.TryGetMedicineByItemId(unit.WeaponItemId, out var med))
                        {
                            executed.FailureReason = "Active item not found";
                        }
                        else if (!string.Equals(med.EffectType, "hp", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(med.EffectSign, "positive", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(med.EffectTarget, "self", StringComparison.OrdinalIgnoreCase))
                        {
                            executed.FailureReason = "Item affect config invalid";
                        }
                        else if (med.EffectMax < med.EffectMin)
                        {
                            executed.FailureReason = "Item affect range invalid";
                        }
                        else if (unit.UnitType != UnitType.Player)
                        {
                            executed.FailureReason = "Only players can use this item";
                        }
                        else if (_userDb == null)
                        {
                            executed.FailureReason = "User database unavailable";
                        }
                        else
                        {
                            string pid = PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key ?? uid;
                            int availableQty = 0;
                            if (TryGetBattlePlayerUserId(pid, out long itemUid))
                                _userDb.TryGetMedicineUseQuantity(itemUid, med.Id, out availableQty);
                            if (consumedByPlayerAndItemId.TryGetValue(pid, out var consumedByItem)
                                && consumedByItem.TryGetValue(med.Id, out var alreadyConsumed))
                            {
                                availableQty = Math.Max(0, availableQty - alreadyConsumed);
                            }
                            if (availableQty <= 0)
                            {
                                executed.FailureReason = "Item quantity is empty";
                            }
                            else
                            {
                                int min = Math.Max(0, med.EffectMin);
                                int max = Math.Max(min, med.EffectMax);
                                int heal = _rng.Next(min, max + 1);
                                int beforeHp = Math.Max(0, unit.CurrentHp);
                                unit.CurrentHp = Math.Clamp(beforeHp + heal, 0, Math.Max(1, unit.MaxHp));
                                executed.Healed = Math.Max(0, unit.CurrentHp - beforeHp);
                                Units[uid] = unit;
                                executed.Succeeded = true;
                                if (!consumedByPlayerAndItemId.TryGetValue(pid, out consumedByItem))
                                {
                                    consumedByItem = new Dictionary<long, int>();
                                    consumedByPlayerAndItemId[pid] = consumedByItem;
                                }
                                consumedByItem[med.Id] = consumedByItem.GetValueOrDefault(med.Id) + 1;
                            }
                        }
                    }
                    else
                    {
                        executed.FailureReason = "Unknown action type";
                    }

                    if (!executed.Succeeded)
                    {
                        accepted[uid] = false;
                        rejectedReason[uid] ??= executed.FailureReason;
                    }

                    executedActions[uid].Add(executed);
                }
            } while (executedAnyActionThisPass);
        }

        var results = new List<PlayerTurnResultDto>();
        var nextEscapeChannel = new Dictionary<string, int>();
        var fledPlayerIds = new List<string>();
        foreach (var uid in order)
        {
            if (!positions.TryGetValue(uid, out var final))
                continue;
            if (!Units.TryGetValue(uid, out var us))
                continue;

            int fatigueAp = GetFatigueAp(us);
            int maxAp = GetUnitMaxAp(us);
            if (us.UnitType != UnitType.Mob)
            {
                fatigueAp = Math.Min(maxAp, fatigueAp + CalculateRunPenaltyIncreaseAp(
                    maxAp,
                    runNormalHexCount.GetValueOrDefault(uid),
                    runPenaltyHexCount.GetValueOrDefault(uid)));
                int currentApAfterPenalty = GetNextRoundAp(us, fatigueAp);
                string? lastMovePosture = lastMovePostureByUnit.GetValueOrDefault(uid);
                bool hadRunMovement = hadRunMovementByUnit.GetValueOrDefault(uid);
                bool canRecoverAfterRun = string.Equals(lastMovePosture, PostureWalk, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(lastMovePosture, PostureSit, StringComparison.OrdinalIgnoreCase);
                if (!hadRunMovement || canRecoverAfterRun)
                    currentApAfterPenalty = ApplyRestRecovery(currentApAfterPenalty, maxAp);
                fatigueAp = Math.Max(0, maxAp - currentApAfterPenalty);
            }

            us.Col = final.col;
            us.Row = final.row;
            us.Posture = postureByUnit.TryGetValue(uid, out var finalPosture) ? NormalizePosture(finalPosture) : PostureWalk;
            SetFatigueAp(us, fatigueAp, maxAp);
            us.CurrentAp = GetNextRoundAp(us, fatigueAp);
            Units[uid] = us;

            string playerId = PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key ?? uid;
            bool isEscaping = false;
            int escapeRoundsRemaining = 0;
            bool hasFled = false;
            bool playerMoveAccepted = accepted.TryGetValue(uid, out var moveAcc) && moveAcc;
            bool finalOnEscape = us.UnitType == UnitType.Player && IsEscapeBorderHex(final.col, final.row);

            if (us.UnitType == UnitType.Player)
            {
                bool hadChannel = escapeChannelAtRoundStart.TryGetValue(playerId, out int escStart);
                if (hadChannel && finalOnEscape)
                {
                    isEscaping = true;
                    int nl = escStart - 1;
                    escapeRoundsRemaining = Math.Max(0, nl);
                    hasFled = nl <= 0;
                    if (hasFled)
                        fledPlayerIds.Add(playerId);
                    else
                        nextEscapeChannel[playerId] = nl;
                }
                else if (!hadChannel && finalOnEscape && playerMoveAccepted)
                {
                    isEscaping = true;
                    escapeRoundsRemaining = EscapeChannelRounds;
                    nextEscapeChannel[playerId] = EscapeChannelRounds;
                }
            }

            if (us.UnitType == UnitType.Player && nextEscapeChannel.ContainsKey(playerId))
            {
                us.CurrentAp = 0;
                Units[uid] = us;
            }

            var unitActions = executedActions.TryGetValue(uid, out var executed) ? executed : new List<ExecutedBattleActionDto>();
            if (us.UnitType == UnitType.Player && TryGetBattlePlayerUserId(playerId, out long persistUid))
            {
                int chamberRounds = Math.Max(0, us.CurrentMagazineRounds);
                if (Submissions.TryGetValue(playerId, out var submitted) && submitted != null)
                    chamberRounds = Math.Max(0, submitted.CurrentMagazineRounds);
                _userDb?.TrySetUserWeaponChamberRoundsByItemId(persistUid, us.WeaponItemId, chamberRounds, out _);
                if (reloadedByPlayerAndAmmoTypeId.TryGetValue(playerId, out var byAmmoType))
                {
                    foreach (var kv in byAmmoType)
                    {
                        int currentRounds = 0;
                        _userDb?.TryGetUserAmmoRoundsByAmmoTypeId(persistUid, kv.Key, out currentRounds);
                        int nextRounds = Math.Max(0, currentRounds - Math.Max(0, kv.Value));
                        _userDb?.TrySetUserAmmoRoundsByAmmoTypeId(persistUid, kv.Key, nextRounds, out _);
                    }
                }
                if (consumedByPlayerAndItemId.TryGetValue(playerId, out var consumedByItem))
                {
                    foreach (var kv in consumedByItem)
                    {
                        _userDb?.TryConsumeMedicineUse(persistUid, kv.Key, Math.Max(0, kv.Value), out _);
                    }
                }
            }
            string weaponCategory = "cold";
            if (_weaponDb != null && _weaponDb.TryGetWeaponByItemId(us.WeaponItemId, out var weaponRowForCat))
            {
                if (!string.IsNullOrWhiteSpace(weaponRowForCat.Category))
                    weaponCategory = weaponRowForCat.Category.Trim();
                else if (string.Equals(weaponRowForCat.ItemType, "medicine", StringComparison.OrdinalIgnoreCase))
                    weaponCategory = "medicine";
            }
            else if (_medicineDb != null && _medicineDb.TryGetMedicineByItemId(us.WeaponItemId, out _))
            {
                // Items only in medicine table (not in weapons) must still report category for clients.
                weaponCategory = "medicine";
            }

            results.Add(new PlayerTurnResultDto
            {
                UnitId = uid,
                UnitType = us.UnitType,
                PlayerId = playerId,
                Accepted = accepted.TryGetValue(uid, out var ok) && ok,
                FinalPosition = new HexPositionDto { Col = final.col, Row = final.row },
                ActualPath = actualPaths.TryGetValue(uid, out var path) ? path.ToArray() : new[] { new HexPositionDto { Col = final.col, Row = final.row } },
                CurrentAp = us.CurrentAp,
                PenaltyFraction = us.PenaltyFraction,
                ApSpentThisTurn = apSpentByUnit.GetValueOrDefault(uid),
                RejectedReason = rejectedReason.TryGetValue(uid, out var rr) ? rr : null,
                MaxHp = us.MaxHp,
                CurrentHp = us.CurrentHp,
                IsDead = !alive.GetValueOrDefault(uid, us.CurrentHp > 0),
                AttackTargetUnitId = attackTargetByUnit.TryGetValue(uid, out var targetId) ? targetId : null,
                DamageDealt = damageByUnit.TryGetValue(uid, out var dealt) ? dealt : 0,
                PostureAtRoundStart = roundStartPostureByUnit.TryGetValue(uid, out var prs) ? prs : PostureWalk,
                CurrentPosture = us.Posture,
                WeaponItemId = us.WeaponItemId,
                WeaponCategory = weaponCategory,
                WeaponDamageMin = us.WeaponDamageMin,
                WeaponDamage = us.WeaponDamage,
                WeaponRange = us.WeaponRange,
                WeaponAttackApCost = Math.Max(1, us.WeaponAttackApCost),
                CurrentMagazineRounds = us.CurrentMagazineRounds,
                WeaponTightness = us.WeaponTightness,
                WeaponTrajectoryHeight = us.WeaponTrajectoryHeight,
                WeaponIsSniper = us.WeaponIsSniper,
                ExecutedActions = unitActions.ToArray(),
                IsEscaping = isEscaping,
                EscapeRoundsRemaining = escapeRoundsRemaining,
                HasFled = hasFled,
                TeamId = us.UnitType == UnitType.Player ? us.TeamId : -1
            });
        }

        EscapingPlayers.Clear();
        foreach (var kv in nextEscapeChannel)
            EscapingPlayers[kv.Key] = kv.Value;

        var deadIds = order.Where(uid => alive.TryGetValue(uid, out var isAlive) && !isAlive).ToList();
        foreach (var deadId in deadIds)
        {
            Units.Remove(deadId);
            UnitCommands.Remove(deadId);
            foreach (var kv in PlayerToUnitId.Where(kv => kv.Value == deadId).ToList())
                PlayerToUnitId.Remove(kv.Key);
        }

        foreach (var fpid in fledPlayerIds.Distinct())
        {
            if (!PlayerToUnitId.TryGetValue(fpid, out var fuid))
                continue;
            Units.Remove(fuid);
            UnitCommands.Remove(fuid);
            PlayerToUnitId.Remove(fpid);
            Players.Remove(fpid);
            CurrentState.Remove(fpid);
            Submissions.Remove(fpid);
            EndedTurnEarlyThisRound.Remove(fpid);
        }

        EnsureActiveZoneInitialized();
        HexPositionDto[]? zoneShrinkCells = ApplyZoneShrinkIfNeeded(RoundIndex, mapUpdates, results);

        bool hasPlayersAlive = Units.Values.Any(u => u.UnitType == UnitType.Player);
        bool hasMobsAlive = Units.Values.Any(u => u.UnitType == UnitType.Mob);
        bool battleFinished;
        if (IsSolo)
            battleFinished = !hasPlayersAlive || !hasMobsAlive;
        else if (IsPvpTeamBattle)
        {
            var aliveTeams = Units.Values
                .Where(u => u.UnitType == UnitType.Player && u.CurrentHp > 0 && u.TeamId >= 0)
                .Select(u => u.TeamId)
                .Distinct()
                .ToList();
            battleFinished = aliveTeams.Count <= 1;
        }
        else
            battleFinished = Units.Values.Count(u => u.UnitType == UnitType.Player) <= 1;

        var mapState = new List<CellObject>();
        foreach (var kv in _obstacleTags.OrderBy(k => k.Key.col).ThenBy(k => k.Key.row))
        {
            if (!IsWallObstacleTag(kv.Value))
                continue;
            (int col, int row) wc = kv.Key;
            mapState.Add(new CellObject
            {
                Hex = new HexPositionDto { Col = wc.col, Row = wc.row },
                State = kv.Value is "damaged_wall" or "damaged_wall_low" ? CellObjectState.Damaged : CellObjectState.Full
            });
        }

        LastTurnResult = new TurnResultPayloadDto
        {
            BattleId = BattleId,
            RoundIndex = RoundIndex,
            Results = results.ToArray(),
            RoundResolveReason = resolveReason,
            BattleFinished = battleFinished,
            MapState = mapState.Count > 0 ? mapState.ToArray() : System.Array.Empty<CellObject>(),
            MapUpdates = mapUpdates.Count > 0 ? mapUpdates.ToArray() : System.Array.Empty<MapUpdateDto>(),
            ZoneShrinkCells = zoneShrinkCells,
            ActiveMinCol = _activeMinCol,
            ActiveMaxCol = _activeMaxCol,
            ActiveMinRow = _activeMinRow,
            ActiveMaxRow = _activeMaxRow
        };

        RoundIndex++;
        ResetRoundTimer();
        Submissions.Clear();
        SubmissionOrder.Clear();
        EndedTurnEarlyThisRound.Clear();
        UnitCommands.Clear();
        if (!battleFinished)
            InjectAutoSubmissionsForEscapingPlayers();
        RoundInProgress = !battleFinished;
        Console.WriteLine($"[tzInfo] CloseRound end: battleId={BattleId}, nextRoundIndex={RoundIndex}, results={results.Count}");
        RoundClosedForPush?.Invoke(this);
        // Solo / auto-submitted escapers: everyone already in Submissions — close the next round immediately (no timer wait for "mob").
        if (!battleFinished && Players.Count > 0 && Submissions.Count >= Players.Count)
            CloseRound(fromTimer: false);
    }
}
