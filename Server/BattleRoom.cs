using BattleServer.Models;

namespace BattleServer;

/// <summary>Состояние одного боя (2 игрока). Этап 3: пошаговая симуляция, приоритет по порядку отправки хода.</summary>
public class BattleRoom
{
    private const string PostureWalk = "walk";
    private const string PostureRun = "run";
    private const string PostureSit = "sit";
    private const string PostureHide = "hide";
    private const string ActionMoveStep = "MoveStep";
    private const string ActionAttack = "Attack";
    private const string ActionChangePosture = "ChangePosture";
    private const string ActionWait = "Wait";
    private const int ChangePostureCost = 2;
    private const float RunCostMultiplier = 0.5f;
    private const float SitCostMultiplier = 1.5f;
    private const float RunStepPenaltyFraction = 0.02f;
    private const float RunStepPenaltyHexFraction = 0.05f;
    private const float RestRecoveryFraction = 0.33f;
    private const int RestRecoveryMinAp = 5;
    private const float RunPenaltyThresholdFraction = 0.85f;
    private const float MaxPenaltyFraction = 0.95f;

    public string BattleId { get; }
    public const float RoundDuration = 100f;
    public const int MaxAp = 100;
    public const int MobMaxAp = 15;
    public const int DefaultPlayerMaxHp = 10;
    public const int DefaultMobMaxHp = 10;
    public const string DefaultWeaponCode = "fist";
    public const int DefaultWeaponDamage = 1;
    public const int DefaultWeaponRange = 1;
    public const int MaxObstacleChains = 10;
    public int RoundIndex { get; set; }
    public float RoundTimeLeft { get; set; }
    public long RoundDeadlineUtcMs { get; set; }
    public bool RoundInProgress { get; set; }

    /// <summary>Флаг: бой создан как одиночный (1 игрок + серверный моб), а не матчмейкинг 1v1.</summary>
    public bool IsSolo { get; set; }

    /// <summary>Все юниты боя (игроки и мобы) по unitId.</summary>
    public Dictionary<string, UnitStateDto> Units { get; } = new();

    /// <summary>Соответствие playerId → unitId управляемого юнита.</summary>
    public Dictionary<string, string> PlayerToUnitId { get; } = new();

    /// <summary>Команды юнитов на текущий раунд (unitId → команда).</summary>
    public Dictionary<string, UnitCommandDto> UnitCommands { get; } = new();

    /// <summary>Кто уже отправил ход в текущем раунде (playerId -> payload).</summary>
    public Dictionary<string, SubmitTurnPayloadDto> Submissions { get; } = new();

    /// <summary>Результат последнего завершённого раунда (отдаём при poll, потом очищаем).</summary>
    public TurnResultPayloadDto? LastTurnResult { get; set; }

    /// <summary>Участники: playerId -> начальная позиция (col, row).</summary>
    public Dictionary<string, (int col, int row)> Players { get; } = new();

    /// <summary>Текущее состояние каждого игрока (позиция, ОД, штраф). Обновляется после каждого раунда.</summary>
    public Dictionary<string, PlayerBattleState> CurrentState { get; } = new();
    public Dictionary<string, (int maxHp, int maxAp, string weaponCode, int weaponDamage, int weaponRange)> PlayerCombatProfiles { get; } = new();

    /// <summary>Порядок отправки хода в текущем раунде (кто раньше отправил — выше приоритет на клетку).</summary>
    public List<string> SubmissionOrder { get; } = new();

    /// <summary>Стабильный список участников боя (порядок присоединения).</summary>
    public List<string> ParticipantIds { get; } = new();

    /// <summary>Кто в этом раунде завершил ход досрочно (пока таймер не истёк).</summary>
    public Dictionary<string, bool> EndedTurnEarlyThisRound { get; } = new();
    public HashSet<(int col, int row)> Obstacles { get; } = new();

    private readonly Random _rng;

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

    private static bool AreAdjacent((int col, int row) a, (int col, int row) b) =>
        HexSpawn.HexDistance(a.col, a.row, b.col, b.row) == 1;

    private static bool AreEnemies(UnitStateDto a, UnitStateDto b)
    {
        if (a == null || b == null || a.UnitId == b.UnitId) return false;
        if (a.UnitType == UnitType.Mob && b.UnitType == UnitType.Mob) return false;
        return true;
    }

    private string? ResolveUnitId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return null;
        if (Units.ContainsKey(rawId))
            return rawId;
        if (PlayerToUnitId.TryGetValue(rawId, out var mapped) && Units.ContainsKey(mapped))
            return mapped;
        return null;
    }

    private static IEnumerable<(int col, int row)> EnumerateNeighbors(int col, int row)
    {
        for (int dir = 0; dir < 6; dir++)
        {
            HexSpawn.GetNeighbor(col, row, dir, out int nc, out int nr);
            if (nc < 0 || nr < 0 || nc >= HexSpawn.DefaultGridWidth || nr >= HexSpawn.DefaultGridLength)
                continue;
            yield return (nc, nr);
        }
    }

    private List<(int col, int row)>? FindShortestPathAvoidingBlocked((int col, int row) start, (int col, int row) end, HashSet<(int col, int row)> blocked)
    {
        var queue = new Queue<(int col, int row)>();
        var visited = new HashSet<(int col, int row)> { start };
        var prev = new Dictionary<(int col, int row), (int col, int row)>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (cell == end)
            {
                var reverse = new List<(int col, int row)> { end };
                var cursor = end;
                while (cursor != start)
                {
                    if (!prev.TryGetValue(cursor, out cursor))
                        break;
                    reverse.Add(cursor);
                }
                reverse.Reverse();
                return reverse;
            }

            foreach (var neighbor in EnumerateNeighbors(cell.col, cell.row))
            {
                if (blocked.Contains(neighbor) && neighbor != end)
                    continue;
                if (!visited.Add(neighbor))
                    continue;
                prev[neighbor] = cell;
                queue.Enqueue(neighbor);
            }
        }

        return null;
    }

    private QueuedBattleActionDto[] BuildMobActionQueue(UnitStateDto mob, UnitStateDto target)
    {
        if (mob == null || target == null || mob.CurrentAp <= 0)
            return Array.Empty<QueuedBattleActionDto>();

        var actions = new List<QueuedBattleActionDto>();
        var blocked = new HashSet<(int col, int row)>(Obstacles);
        foreach (var unit in Units.Values)
        {
            if (unit == null || unit.UnitId == mob.UnitId || unit.UnitId == target.UnitId || unit.CurrentHp <= 0)
                continue;
            blocked.Add((unit.Col, unit.Row));
        }

        (int col, int row) start = (mob.Col, mob.Row);
        (int col, int row) targetPos = (target.Col, target.Row);
        var attackCells = EnumerateNeighbors(targetPos.col, targetPos.row)
            .Where(cell => !Obstacles.Contains(cell) && !blocked.Contains(cell))
            .ToList();

        List<(int col, int row)>? bestRoute = null;
        int bestLen = int.MaxValue;
        foreach (var attackCell in attackCells)
        {
            var route = FindShortestPathAvoidingBlocked(start, attackCell, blocked);
            if (route == null || route.Count == 0)
                continue;
            int routeLen = route.Count - 1;
            if (routeLen < bestLen)
            {
                bestLen = routeLen;
                bestRoute = route;
            }
        }

        int apLeft = mob.CurrentAp;
        if (bestRoute != null && bestRoute.Count > 1)
        {
            for (int i = 1; i < bestRoute.Count && apLeft > 0; i++)
            {
                actions.Add(new QueuedBattleActionDto
                {
                    ActionType = "MoveStep",
                    TargetPosition = new HexPositionDto { Col = bestRoute[i].col, Row = bestRoute[i].row },
                    Cost = 1
                });
                apLeft--;
            }
        }

        var finalPos = bestRoute != null && bestRoute.Count > 0 ? bestRoute[^1] : start;
        bool inRange = HexSpawn.HexDistance(finalPos.col, finalPos.row, targetPos.col, targetPos.row) <= DefaultWeaponRange;
        while (apLeft > 0 && inRange)
        {
            actions.Add(new QueuedBattleActionDto
            {
                ActionType = "Attack",
                TargetUnitId = target.UnitId,
                Cost = 1
            });
            apLeft--;
        }

        return actions.ToArray();
    }

    private QueuedBattleActionDto? BuildMobActionForCurrentState(
        string mobUnitId,
        Dictionary<string, (int col, int row)> positions,
        Dictionary<string, bool> alive,
        HashSet<(int col, int row)> occupied)
    {
        if (!Units.TryGetValue(mobUnitId, out var mob) || mob.UnitType != UnitType.Mob)
            return null;
        if (!positions.TryGetValue(mobUnitId, out var mobPos))
            return null;

        string? targetId = Units.Values
            .Where(u => u.UnitType == UnitType.Player
                && alive.TryGetValue(u.UnitId, out var isAlive) && isAlive
                && positions.ContainsKey(u.UnitId))
            .OrderBy(u => HexSpawn.HexDistance(mobPos.col, mobPos.row, positions[u.UnitId].col, positions[u.UnitId].row))
            .Select(u => u.UnitId)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(targetId) || !positions.TryGetValue(targetId, out var targetPos))
            return null;

        if (HexSpawn.HexDistance(mobPos.col, mobPos.row, targetPos.col, targetPos.row) <= Math.Max(0, mob.WeaponRange))
        {
            return new QueuedBattleActionDto
            {
                ActionType = "Attack",
                TargetUnitId = targetId,
                Cost = 1
            };
        }

        var blocked = new HashSet<(int col, int row)>(Obstacles);
        foreach (var kv in positions)
        {
            if (kv.Key == mobUnitId || kv.Key == targetId)
                continue;
            if (!alive.TryGetValue(kv.Key, out var isAlive) || !isAlive)
                continue;
            blocked.Add(kv.Value);
        }

        List<(int col, int row)>? bestRoute = null;
        int bestLen = int.MaxValue;
        foreach (var attackCell in EnumerateNeighbors(targetPos.col, targetPos.row))
        {
            if (Obstacles.Contains(attackCell))
                continue;
            if (occupied.Contains(attackCell) && attackCell != mobPos)
                continue;

            var route = FindShortestPathAvoidingBlocked(mobPos, attackCell, blocked);
            if (route == null || route.Count <= 1)
                continue;

            int routeLen = route.Count - 1;
            if (routeLen < bestLen)
            {
                bestLen = routeLen;
                bestRoute = route;
            }
        }

        if (bestRoute == null || bestRoute.Count <= 1)
            return null;

        var next = bestRoute[1];
        return new QueuedBattleActionDto
        {
            ActionType = "MoveStep",
            TargetPosition = new HexPositionDto { Col = next.col, Row = next.row },
            Cost = 1
        };
    }

    private static HexPositionDto[] LimitPathByAvailableAp(UnitStateDto unit, HexPositionDto[]? rawPath)
    {
        var start = new HexPositionDto { Col = unit.Col, Row = unit.Row };
        if (rawPath == null || rawPath.Length == 0)
            return new[] { start };

        var limited = new List<HexPositionDto> { start };
        int maxSteps = GetMaxReachableSteps(unit.CurrentAp);
        int stepsAdded = 0;

        for (int i = 1; i < rawPath.Length && stepsAdded < maxSteps; i++)
        {
            limited.Add(new HexPositionDto { Col = rawPath[i].Col, Row = rawPath[i].Row });
            stepsAdded++;
        }

        return limited.ToArray();
    }

    private HexPositionDto[] NormalizePath(UnitStateDto unit, HexPositionDto[]? rawPath)
    {
        var start = new HexPositionDto { Col = unit.Col, Row = unit.Row };
        if (rawPath == null || rawPath.Length == 0)
            return new[] { start };

        var limited = new List<HexPositionDto> { start };
        int maxSteps = GetMaxReachableSteps(unit.CurrentAp);
        int stepsAdded = 0;
        var current = (unit.Col, unit.Row);

        for (int i = 1; i < rawPath.Length && stepsAdded < maxSteps; i++)
        {
            var next = (rawPath[i].Col, rawPath[i].Row);
            if (next == current)
                continue;
            if (next.Item1 < 0 || next.Item2 < 0 || next.Item1 >= HexSpawn.DefaultGridWidth || next.Item2 >= HexSpawn.DefaultGridLength)
                break;
            if (Obstacles.Contains(next))
                break;
            if (!AreAdjacent(current, next))
                break;

            limited.Add(new HexPositionDto { Col = next.Item1, Row = next.Item2 });
            current = next;
            stepsAdded++;
        }

        return limited.ToArray();
    }

    private HexPositionDto[] BuildPathFromActions(UnitStateDto unit, QueuedBattleActionDto[]? actions)
    {
        var start = new HexPositionDto { Col = unit.Col, Row = unit.Row };
        if (actions == null || actions.Length == 0)
            return new[] { start };

        var path = new List<HexPositionDto> { start };
        var current = (unit.Col, unit.Row);
        foreach (var action in actions)
        {
            if (action == null || !string.Equals(action.ActionType, "MoveStep", StringComparison.OrdinalIgnoreCase))
                continue;
            if (action.TargetPosition == null)
                continue;
            var next = (action.TargetPosition.Col, action.TargetPosition.Row);
            if (next == current)
                continue;
            path.Add(new HexPositionDto { Col = next.Item1, Row = next.Item2 });
            current = next;
        }
        return NormalizePath(unit, path.ToArray());
    }

    private static QueuedBattleActionDto[] BuildMoveActionsFromPath(HexPositionDto[]? path)
    {
        if (path == null || path.Length <= 1)
            return Array.Empty<QueuedBattleActionDto>();

        var actions = new List<QueuedBattleActionDto>();
        for (int i = 1; i < path.Length; i++)
        {
            actions.Add(new QueuedBattleActionDto
            {
                ActionType = "MoveStep",
                TargetPosition = new HexPositionDto { Col = path[i].Col, Row = path[i].Row },
                Cost = GetMoveCost(i - 1, 1)
            });
        }
        return actions.ToArray();
    }

    public BattleRoom(string battleId)
    {
        BattleId = battleId;
        _rng = new Random(Guid.NewGuid().GetHashCode());
    }

    private static long GetUtcNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void ResetRoundTimer()
    {
        RoundDeadlineUtcMs = GetUtcNowMs() + (long)Math.Round(RoundDuration * 1000f);
        RoundTimeLeft = RoundDuration;
    }

    private void RefreshRoundTimeLeft()
    {
        if (RoundDeadlineUtcMs <= 0)
        {
            RoundTimeLeft = 0f;
            return;
        }

        long remainingMs = RoundDeadlineUtcMs - GetUtcNowMs();
        RoundTimeLeft = Math.Max(0f, remainingMs / 1000f);
    }

    /// <summary>Сгенерировать команды для всех мобов в текущем раунде.
    /// Моб строит маршрут по траектории ближайшего игрока в этом же раунде, чтобы догонять не старую позицию, а движение цели.
    /// </summary>
    private void EnsureMobCommandsForCurrentRound()
    {
        var playerUnits = Units.Values.Where(u => u.UnitType == UnitType.Player).ToList();
        if (playerUnits.Count == 0) return;

        foreach (var mob in Units.Values.Where(u => u.UnitType == UnitType.Mob))
        {
            // ближайший игрок по hex-distance
            var target = playerUnits
                .OrderBy(p => HexSpawn.HexDistance(mob.Col, mob.Row, p.Col, p.Row))
                .First();
            var actions = BuildMobActionQueue(mob, target);
            int moveCount = actions.Count(a => a != null && a.ActionType == "MoveStep");
            int attackCount = actions.Count(a => a != null && a.ActionType == "Attack");
            Console.WriteLine(
                $"[mobAI] battleId={BattleId} mob={mob.UnitId} mobPos=({mob.Col},{mob.Row}) ap={mob.CurrentAp} " +
                $"target={target.UnitId} targetPos=({target.Col},{target.Row}) moveActions={moveCount} attackActions={attackCount}");

            UnitCommands[mob.UnitId] = new UnitCommandDto
            {
                UnitId = mob.UnitId,
                CommandType = "Queue",
                Actions = actions
            };
        }
    }

    private void GenerateObstaclesIfNeeded()
    {
        if (Obstacles.Count > 0) return;

        int targetChains = _rng.Next(6, MaxObstacleChains + 1);
        int attempts = 0;
        int chainsPlaced = 0;
        var reserved = new HashSet<(int col, int row)>();
        foreach (var unit in Units.Values)
        {
            var origin = (col: unit.Col, row: unit.Row);
            reserved.Add(origin);
            for (int col = 0; col < HexSpawn.DefaultGridWidth; col++)
            {
                for (int row = 0; row < HexSpawn.DefaultGridLength; row++)
                {
                    if (HexSpawn.HexDistance(origin.col, origin.row, col, row) <= 2)
                        reserved.Add((col, row));
                }
            }
        }

        while (chainsPlaced < targetChains && attempts < 300)
        {
            attempts++;
            int length = _rng.Next(1, 4);
            int dir = _rng.Next(0, 6);
            int startCol = _rng.Next(0, HexSpawn.DefaultGridWidth);
            int startRow = _rng.Next(0, HexSpawn.DefaultGridLength);

            var chain = new List<(int col, int row)>();
            int col = startCol;
            int row = startRow;
            bool ok = true;

            for (int i = 0; i < length; i++)
            {
                var cell = (col, row);
                if (col < 0 || row < 0 || col >= HexSpawn.DefaultGridWidth || row >= HexSpawn.DefaultGridLength
                    || Obstacles.Contains(cell) || reserved.Contains(cell))
                {
                    ok = false;
                    break;
                }

                chain.Add(cell);
                HexSpawn.GetNeighbor(col, row, dir, out col, out row);
            }

            if (!ok) continue;

            foreach (var cell in chain)
                Obstacles.Add(cell);
            chainsPlaced++;
        }
    }

    /// <summary>Инициализация юнитов (P1/P2 + серверный моб) при старте боя.</summary>
    private void EnsureUnitsInitialized()
    {
        if (Units.Count > 0) return;
        if (Players.Count == 0) return;

        // Игроки как юниты.
        foreach (var kv in Players)
        {
            var playerId = kv.Key;
            var (col, row) = kv.Value;
            var unitId = playerId + "_UNIT";
            var profile = PlayerCombatProfiles.TryGetValue(playerId, out var p)
                ? p
                : (DefaultPlayerMaxHp, MaxAp, DefaultWeaponCode, DefaultWeaponDamage, DefaultWeaponRange);
            PlayerToUnitId[playerId] = unitId;
            Units[unitId] = new UnitStateDto
            {
                UnitId = unitId,
                UnitType = UnitType.Player,
                Col = col,
                Row = row,
                MaxAp = profile.Item2,
                CurrentAp = profile.Item2,
                PenaltyFraction = 0f,
                MaxHp = profile.Item1,
                CurrentHp = profile.Item1,
                WeaponCode = profile.Item3,
                WeaponDamage = profile.Item4,
                WeaponRange = profile.Item5,
                Posture = PostureWalk
            };
        }

        // Серверный моб есть только в одиночном бою.
        if (IsSolo && Players.TryGetValue("P1", out var p1Pos))
        {
            var (mobCol, mobRow) = HexSpawn.FindOpponentSpawn(
                p1Pos.col,
                p1Pos.row,
                HexSpawn.DefaultGridWidth,
                HexSpawn.DefaultGridLength,
                HexSpawn.MinSpawnHexDistance
            );

            const string mobId = "MOB_1";
            if (!Units.ContainsKey(mobId))
            {
                Units[mobId] = new UnitStateDto
                {
                    UnitId = mobId,
                    UnitType = UnitType.Mob,
                    Col = mobCol,
                    Row = mobRow,
                    MaxAp = MobMaxAp,
                    CurrentAp = MobMaxAp,
                    PenaltyFraction = 0f,
                    MaxHp = DefaultMobMaxHp,
                    CurrentHp = DefaultMobMaxHp,
                    WeaponCode = DefaultWeaponCode,
                    WeaponDamage = DefaultWeaponDamage,
                    WeaponRange = DefaultWeaponRange,
                    Posture = PostureWalk
                };
            }
        }

        GenerateObstaclesIfNeeded();
    }

    public void StartFirstRound()
    {
        EnsureUnitsInitialized();
        RoundIndex = 0;
        ResetRoundTimer();
        RoundInProgress = true;
        Submissions.Clear();
        SubmissionOrder.Clear();
        EndedTurnEarlyThisRound.Clear();
        UnitCommands.Clear();
        CurrentState.Clear();
        // Синхронизируем состояние только для игроков (P1/P2) на основе Units.
        foreach (var kv in Players)
        {
            var playerId = kv.Key;
            int col = kv.Value.col;
            int row = kv.Value.row;
            // Если есть юнит, берём его состояние; иначе — дефолт.
            PlayerBattleState st;
            if (PlayerToUnitId.TryGetValue(playerId, out var unitId) &&
                Units.TryGetValue(unitId, out var us))
            {
                st = new PlayerBattleState
                {
                    Col = us.Col,
                    Row = us.Row,
                    CurrentAp = us.CurrentAp,
                    PenaltyFraction = us.PenaltyFraction
                };
            }
            else
            {
                st = new PlayerBattleState
                {
                    Col = col,
                    Row = row,
                    CurrentAp = GetUnitRoundStartAp(UnitType.Player, 0f),
                    PenaltyFraction = 0f
                };
            }
            CurrentState[playerId] = st;
        }

        // Для новой модели: сразу создать команды для мобов в первом раунде.
        EnsureMobCommandsForCurrentRound();
        Console.WriteLine($"[tzInfo] StartFirstRound: battleId={BattleId}, roundIndex={RoundIndex}, players={Players.Count}, units={Units.Count}");
    }

    public void AddPlayer(string playerId, int col, int row)
    {
        Players[playerId] = (col, row);
        if (!ParticipantIds.Contains(playerId))
            ParticipantIds.Add(playerId);
    }

    public void SetPlayerCombatProfile(string playerId, int maxHp, int maxAp, string weaponCode, int weaponDamage, int weaponRange)
    {
        PlayerCombatProfiles[playerId] = (
            Math.Max(1, maxHp),
            Math.Max(1, maxAp),
            string.IsNullOrWhiteSpace(weaponCode) ? DefaultWeaponCode : weaponCode,
            Math.Max(0, weaponDamage),
            Math.Max(0, weaponRange));
    }

    public void FillSpawnArrays(out string[] ids, out int[] cols, out int[] rows, out int[] currentAps, out int[] maxHps, out int[] currentHps, out string[] currentPostures)
    {
        EnsureUnitsInitialized();

        var items = new List<(string id, int col, int row, int currentAp, int maxHp, int currentHp, string posture)>();

        foreach (var playerId in ParticipantIds.Where(Players.ContainsKey))
        {
            if (PlayerToUnitId.TryGetValue(playerId, out var unitId) && Units.TryGetValue(unitId, out var unit))
                items.Add((playerId, unit.Col, unit.Row, unit.CurrentAp, unit.MaxHp, unit.CurrentHp, NormalizePosture(unit.Posture)));
            else
                items.Add((playerId, Players[playerId].col, Players[playerId].row, MaxAp, DefaultPlayerMaxHp, DefaultPlayerMaxHp, PostureWalk));
        }

        foreach (var unit in Units.Values.Where(u => u.UnitType == UnitType.Mob))
            items.Add((unit.UnitId, unit.Col, unit.Row, unit.CurrentAp, unit.MaxHp, unit.CurrentHp, NormalizePosture(unit.Posture)));

        ids = items.Select(x => x.id).ToArray();
        cols = items.Select(x => x.col).ToArray();
        rows = items.Select(x => x.row).ToArray();
        currentAps = items.Select(x => x.currentAp).ToArray();
        maxHps = items.Select(x => x.maxHp).ToArray();
        currentHps = items.Select(x => x.currentHp).ToArray();
        currentPostures = items.Select(x => x.posture).ToArray();
    }

    public BattleStartedPayloadDto BuildBattleStartedFor(string playerId)
    {
        EnsureUnitsInitialized();
        var players = Players.Select(p => new BattlePlayerInfoDto
        {
            PlayerId = p.Key,
            Col = p.Value.col,
            Row = p.Value.row
        }).ToArray();
        FillSpawnArrays(out var sid, out var sc, out var sr, out var sap, out var smh, out var sch, out var spos);
        var obstacleCols = Obstacles.Select(x => x.col).ToArray();
        var obstacleRows = Obstacles.Select(x => x.row).ToArray();
        return new BattleStartedPayloadDto
        {
            BattleId = BattleId,
            PlayerId = playerId,
            Players = players,
            RoundDuration = RoundDuration,
            RoundDeadlineUtcMs = RoundDeadlineUtcMs,
            SpawnPlayerIds = sid,
            SpawnCols = sc,
            SpawnRows = sr,
            SpawnCurrentAps = sap,
            SpawnMaxHps = smh,
            SpawnCurrentHps = sch,
            SpawnCurrentPostures = spos,
            ObstacleCols = obstacleCols,
            ObstacleRows = obstacleRows
        };
    }

    /// <summary>Принять ход. Возвращает true, если все участники сдали ход и раунд нужно закрыть.</summary>
    public bool SubmitTurn(SubmitTurnPayloadDto payload)
    {
        if (payload.RoundIndex != RoundIndex) return false;
        if (!Players.ContainsKey(payload.PlayerId)) return false;
        if (Submissions.ContainsKey(payload.PlayerId)) return false; // дубликат — не закрывать раунд

        // Сформировать команду юнита для новой серверной модели.
        if (!PlayerToUnitId.TryGetValue(payload.PlayerId, out var unitId) || string.IsNullOrEmpty(unitId))
        {
            // Fallback: если по какой-то причине маппинг ещё не задан, используем UnitId из payload или производное имя.
            unitId = !string.IsNullOrEmpty(payload.UnitId) ? payload.UnitId : payload.PlayerId + "_UNIT";
            PlayerToUnitId[payload.PlayerId] = unitId;
            if (!Units.ContainsKey(unitId) && Players.TryGetValue(payload.PlayerId, out var pos))
            {
                Units[unitId] = new UnitStateDto
                {
                    UnitId = unitId,
                    UnitType = UnitType.Player,
                    Col = pos.col,
                    Row = pos.row,
                    MaxAp = MaxAp,
                    CurrentAp = GetUnitRoundStartAp(UnitType.Player, 0f),
                    PenaltyFraction = 0f,
                    MaxHp = DefaultPlayerMaxHp,
                    CurrentHp = DefaultPlayerMaxHp,
                    WeaponCode = DefaultWeaponCode,
                    WeaponDamage = DefaultWeaponDamage,
                    WeaponRange = DefaultWeaponRange,
                    Posture = PostureWalk
                };
            }
        }

        UnitCommands[unitId] = new UnitCommandDto
        {
            UnitId = unitId,
            CommandType = "Queue",
            Actions = payload.Actions ?? Array.Empty<QueuedBattleActionDto>()
        };

        Submissions[payload.PlayerId] = payload;
        SubmissionOrder.Add(payload.PlayerId);
        RefreshRoundTimeLeft();
        if (RoundTimeLeft > 0.01f)
            EndedTurnEarlyThisRound[payload.PlayerId] = true;

        // Убедиться, что у всех мобов есть команды на этот раунд.
        EnsureMobCommandsForCurrentRound();

        // Все игроки прислали ходы?
        bool allPlayersSubmitted = Submissions.Count >= Players.Count;

        // Все мобы имеют команды?
        bool allMobsHaveCommands = Units.Values
            .Where(u => u.UnitType == UnitType.Mob)
            .All(m => UnitCommands.ContainsKey(m.UnitId));

        return allPlayersSubmitted && allMobsHaveCommands;
    }

    /// <summary>Статусы участников для опроса: кто сдал ход, кто досрочно.</summary>
    public BattleParticipantStatusDto[] BuildParticipantStatuses()
    {
        return ParticipantIds
            .Where(Players.ContainsKey)
            .Select(pid =>
            {
                bool isMob = PlayerToUnitId.TryGetValue(pid, out var uid) &&
                             Units.TryGetValue(uid, out var us) &&
                             us.UnitType == UnitType.Mob;

                return new BattleParticipantStatusDto
                {
                    PlayerId = pid,
                    HasSubmitted = isMob ? true : Submissions.ContainsKey(pid),
                    EndedTurnEarly = isMob ? true : EndedTurnEarlyThisRound.GetValueOrDefault(pid)
                };
            })
            .ToArray();
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
                        BodyPart = action.BodyPart,
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
                            else if (Obstacles.Contains(targetCell))
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
                            executed.FailureReason = "Attack target missing";
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
                        else if (HexSpawn.HexDistance(currentPos.col, currentPos.row, targetPos.col, targetPos.row) > Math.Max(0, unit.WeaponRange))
                        {
                            executed.FailureReason = "Attack target out of range";
                        }
                        else
                        {
                            int damage = Math.Max(0, unit.WeaponDamage);
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

        LastTurnResult = new TurnResultPayloadDto
        {
            BattleId = BattleId,
            RoundIndex = RoundIndex,
            Results = results.ToArray(),
            RoundResolveReason = resolveReason,
            BattleFinished = battleFinished
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

    /// <summary>Вызывается в конце CloseRound — пуш по WebSocket.</summary>
    public static event Action<BattleRoom>? RoundClosedForPush;

    public void Tick(float deltaSeconds)
    {
        if (!RoundInProgress) return;
        RefreshRoundTimeLeft();
        if (RoundTimeLeft <= 0)
        {
            RoundTimeLeft = 0;
            Console.WriteLine($"[tzInfo] Round timer expired: battleId={BattleId}, roundIndex={RoundIndex}");
            CloseRound(fromTimer: true);
        }
    }
}
