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
        double p = (wr + 1 - dClamped) / wr;
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
        var movementStepsTaken = new Dictionary<string, int>();
        var lastMovePostureByUnit = new Dictionary<string, string?>();
        var hadRunMovementByUnit = new Dictionary<string, bool>();
        var runMovementApSpent = new Dictionary<string, int>();
        var runNormalHexCount = new Dictionary<string, int>();
        var runPenaltyHexCount = new Dictionary<string, int>();
        var apSpentByUnit = new Dictionary<string, int>();

        foreach (var uid in order)
        {
            if (!Units.TryGetValue(uid, out var us))
                continue;

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
            movementStepsTaken[uid] = 0;
            lastMovePostureByUnit[uid] = null;
            hadRunMovementByUnit[uid] = false;
            runMovementApSpent[uid] = 0;
            runNormalHexCount[uid] = 0;
            runPenaltyHexCount[uid] = 0;
            apSpentByUnit[uid] = 0;
            actionQueues[uid] = UnitCommands.TryGetValue(uid, out var cmd) && cmd.Actions != null
                ? cmd.Actions
                : Array.Empty<QueuedBattleActionDto>();
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
                            cost = GetMovementStepCost(currentPosture, movementStepsTaken[uid] + 1);
                        else if (string.Equals(queuedActionType, ActionChangePosture, StringComparison.OrdinalIgnoreCase))
                            cost = ChangePostureCost;
                        else if (string.Equals(queuedActionType, ActionWait, StringComparison.OrdinalIgnoreCase))
                            cost = Math.Max(1, action?.Cost ?? 1);
                        else if (string.Equals(queuedActionType, ActionEquipWeapon, StringComparison.OrdinalIgnoreCase))
                            cost = Math.Max(1, action?.Cost ?? 2);
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
                            if (targetCell.Item1 < 0 || targetCell.Item2 < 0 || targetCell.Item1 >= HexSpawn.DefaultGridWidth || targetCell.Item2 >= HexSpawn.DefaultGridLength)
                                executed.FailureReason = "Move target out of bounds";
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
                                lastMovePostureByUnit[uid] = currentPosture;
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
                                if (ac < 0 || ar < 0 || ac >= HexSpawn.DefaultGridWidth || ar >= HexSpawn.DefaultGridLength)
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
                                                double pHex = CombineHitProbability(
                                                    GetBaseHitProbabilityFromRange(distHex, weaponRangeHex, unit.WeaponIsSniper),
                                                    anyTreeHex,
                                                    anyRockHex && (string.Equals(tgtPostureHex, PostureSit, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(tgtPostureHex, PostureHide, StringComparison.OrdinalIgnoreCase)),
                                                    bal,
                                                    unit.Accuracy,
                                                    unit.WeaponSpreadPenalty);

                                                bool hitHex = _rng.NextDouble() < pHex;
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
                                    double p = CombineHitProbability(
                                        GetBaseHitProbabilityFromRange(dist, weaponRange, unit.WeaponIsSniper),
                                        anyTree,
                                        anyRock && (string.Equals(tgtPosture, PostureSit, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(tgtPosture, PostureHide, StringComparison.OrdinalIgnoreCase)),
                                        bal,
                                        unit.Accuracy,
                                        unit.WeaponSpreadPenalty);

                                    bool hit = _rng.NextDouble() < p;
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
                    else if (string.Equals(actionType, ActionEquipWeapon, StringComparison.OrdinalIgnoreCase))
                    {
                        string wc = action.WeaponCode ?? "";
                        if (string.IsNullOrWhiteSpace(wc))
                        {
                            executed.FailureReason = "Weapon code missing";
                        }
                        else if (_weaponDb == null || !_weaponDb.TryGetWeaponByCode(wc, out var wpn))
                        {
                            executed.FailureReason = "Unknown weapon";
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

                            string username = string.IsNullOrEmpty(pid) ? "" : PlayerDisplayNames.GetValueOrDefault(pid, pid);
                            if (_userDb != null && !_userDb.TryValidateEquippedWeaponForRegisteredUser(username, wpn.Code, out var invErr))
                            {
                                executed.FailureReason = invErr ?? "weapon not in inventory";
                            }
                            else
                            {
                                unit.WeaponCode = wpn.Code;
                                unit.WeaponDamageMin = wpn.DamageMin;
                                unit.WeaponDamage = wpn.DamageMax;
                                unit.WeaponRange = wpn.Range;
                                unit.WeaponAttackApCost = Math.Max(1, wpn.AttackApCost);
                                unit.WeaponSpreadPenalty = Math.Clamp(wpn.SpreadPenalty, 0.0, 1.0);
                                unit.WeaponTrajectoryHeight = Math.Clamp(wpn.TrajectoryHeight, 0, 3);
                                unit.WeaponIsSniper = wpn.IsSniper;
                                Units[uid] = unit;

                                if (!string.IsNullOrEmpty(pid) && PlayerCombatProfiles.TryGetValue(pid, out var prof))
                                    PlayerCombatProfiles[pid] = (prof.Item1, prof.Item2, wpn.Code, wpn.DamageMin, wpn.DamageMax, wpn.Range, Math.Max(1, wpn.AttackApCost), prof.Item7, unit.WeaponSpreadPenalty, unit.WeaponTrajectoryHeight, unit.WeaponIsSniper);
                                _userDb?.SyncEquippedWeaponForRegisteredUser(username, wpn.Code);
                                executed.Succeeded = true;
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
            var unitActions = executedActions.TryGetValue(uid, out var executed) ? executed : new List<ExecutedBattleActionDto>();
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
                CurrentPosture = us.Posture,
                WeaponCode = us.WeaponCode ?? DefaultWeaponCode,
                WeaponDamageMin = us.WeaponDamageMin,
                WeaponDamage = us.WeaponDamage,
                WeaponRange = us.WeaponRange,
                WeaponAttackApCost = Math.Max(1, us.WeaponAttackApCost),
                WeaponSpreadPenalty = us.WeaponSpreadPenalty,
                WeaponTrajectoryHeight = us.WeaponTrajectoryHeight,
                WeaponIsSniper = us.WeaponIsSniper,
                ExecutedActions = unitActions.ToArray()
            });
        }

        var deadIds = order.Where(uid => alive.TryGetValue(uid, out var isAlive) && !isAlive).ToList();
        foreach (var deadId in deadIds)
        {
            Units.Remove(deadId);
            UnitCommands.Remove(deadId);
            foreach (var kv in PlayerToUnitId.Where(kv => kv.Value == deadId).ToList())
                PlayerToUnitId.Remove(kv.Key);
        }

        bool hasPlayersAlive = Units.Values.Any(u => u.UnitType == UnitType.Player);
        bool hasMobsAlive = Units.Values.Any(u => u.UnitType == UnitType.Mob);
        bool battleFinished = IsSolo ? (!hasPlayersAlive || !hasMobsAlive) : Units.Values.Count(u => u.UnitType == UnitType.Player) <= 1;

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
            MapUpdates = mapUpdates.Count > 0 ? mapUpdates.ToArray() : System.Array.Empty<MapUpdateDto>()
        };

        RoundIndex++;
        ResetRoundTimer();
        Submissions.Clear();
        SubmissionOrder.Clear();
        EndedTurnEarlyThisRound.Clear();
        UnitCommands.Clear();
        RoundInProgress = !battleFinished;
        Console.WriteLine($"[tzInfo] CloseRound end: battleId={BattleId}, nextRoundIndex={RoundIndex}, results={results.Count}");
        RoundClosedForPush?.Invoke(this);
    }
}
