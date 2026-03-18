using BattleServer.Models;

namespace BattleServer;

/// <summary>Состояние одного боя (2 игрока). Этап 3: пошаговая симуляция, приоритет по порядку отправки хода.</summary>
public class BattleRoom
{
    public string BattleId { get; }
    public const float RoundDuration = 30f;
    public const int MaxAp = 100;
    public const int MobMaxAp = 75;
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

    /// <summary>Порядок отправки хода в текущем раунде (кто раньше отправил — выше приоритет на клетку).</summary>
    public List<string> SubmissionOrder { get; } = new();

    /// <summary>Стабильный список участников боя (порядок присоединения).</summary>
    public List<string> ParticipantIds { get; } = new();

    /// <summary>Кто в этом раунде завершил ход досрочно (пока таймер не истёк).</summary>
    public Dictionary<string, bool> EndedTurnEarlyThisRound { get; } = new();

    /// <summary>Стоимость n-го шага (формула как в Unity Player.GetStepCost).</summary>
    public static int GetStepCost(int stepIndex)
    {
        if (stepIndex <= 0) return 0;
        float n = stepIndex;
        float val = (5f * n * n - 8f * n + 21f) / 3f;
        return Math.Max(1, (int)Math.Round(val));
    }

    private static int GetMoveCost(int fromStepIndex, int steps)
    {
        if (steps <= 0) return 0;
        return GetStepCost(fromStepIndex + steps) - GetStepCost(fromStepIndex);
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

    private static List<((int col, int row) pos, int targetIndex)> BuildPursuitTargets(HexPositionDto[] targetPath)
    {
        var targets = new List<((int col, int row), int)>();
        var seen = new HashSet<(int col, int row)>();
        if (targetPath == null || targetPath.Length == 0)
            return targets;

        for (int i = 0; i < targetPath.Length - 1; i++)
        {
            var pos = (targetPath[i].Col, targetPath[i].Row);
            if (seen.Add(pos))
                targets.Add((pos, i));
        }

        var finalPos = (targetPath[^1].Col, targetPath[^1].Row);
        foreach (var neighbor in EnumerateNeighbors(finalPos.Item1, finalPos.Item2))
        {
            if (seen.Add(neighbor))
                targets.Add((neighbor, targetPath.Length - 1));
        }

        // Fallback для неожиданного пустого набора целей.
        if (targets.Count == 0)
        {
            foreach (var neighbor in EnumerateNeighbors(finalPos.Item1, finalPos.Item2))
            {
                if (seen.Add(neighbor))
                    targets.Add((neighbor, targetPath.Length - 1));
            }
        }

        return targets;
    }

    private static ((int col, int row) endCell, Dictionary<(int col, int row), (int col, int row)> prev)? FindBestReachablePursuitCell(
        UnitStateDto mob,
        HexPositionDto[] targetPath)
    {
        var pursuitTargets = BuildPursuitTargets(targetPath);
        if (pursuitTargets.Count == 0)
            return null;

        int maxSteps = GetMaxReachableSteps(mob.CurrentAp);
        var start = (mob.Col, mob.Row);
        var queue = new Queue<((int col, int row) pos, int steps)>();
        var visited = new HashSet<(int col, int row)> { start };
        var prev = new Dictionary<(int col, int row), (int col, int row)>();

        queue.Enqueue((start, 0));

        (int distanceToTarget, int targetIndex, int stepsUsed)? bestScore = null;
        (int col, int row) bestCell = start;

        while (queue.Count > 0)
        {
            var (cell, stepsUsed) = queue.Dequeue();

            foreach (var target in pursuitTargets)
            {
                int distance = HexSpawn.HexDistance(cell.col, cell.row, target.pos.col, target.pos.row);
                var score = (distance, target.targetIndex, stepsUsed);
                if (bestScore == null
                    || score.distance < bestScore.Value.distanceToTarget
                    || (score.distance == bestScore.Value.distanceToTarget && score.targetIndex > bestScore.Value.targetIndex)
                    || (score.distance == bestScore.Value.distanceToTarget && score.targetIndex == bestScore.Value.targetIndex && score.stepsUsed < bestScore.Value.stepsUsed))
                {
                    bestScore = score;
                    bestCell = cell;
                }
            }

            if (stepsUsed >= maxSteps)
                continue;

            foreach (var neighbor in EnumerateNeighbors(cell.col, cell.row))
            {
                if (!visited.Add(neighbor))
                    continue;
                prev[neighbor] = cell;
                queue.Enqueue((neighbor, stepsUsed + 1));
            }
        }

        return (bestCell, prev);
    }

    private static HexPositionDto[] BuildMobChasePath(UnitStateDto mob, HexPositionDto[] targetPath)
    {
        var path = new List<HexPositionDto>
        {
            new() { Col = mob.Col, Row = mob.Row }
        };

        if (targetPath == null || targetPath.Length == 0)
            return path.ToArray();

        var pursuit = FindBestReachablePursuitCell(mob, targetPath);
        if (pursuit == null)
            return path.ToArray();

        var (endCell, prev) = pursuit.Value;
        if (endCell.col == mob.Col && endCell.row == mob.Row)
            return path.ToArray();

        var reverse = new List<(int col, int row)>();
        var cursor = endCell;
        while (cursor != (mob.Col, mob.Row))
        {
            reverse.Add(cursor);
            if (!prev.TryGetValue(cursor, out cursor))
                break;
        }

        reverse.Reverse();
        foreach (var step in reverse)
            path.Add(new HexPositionDto { Col = step.col, Row = step.row });

        return path.ToArray();
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

    public BattleRoom(string battleId)
    {
        BattleId = battleId;
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

            HexPositionDto[] targetPath;
            if (UnitCommands.TryGetValue(target.UnitId, out var targetCommand) && targetCommand.Path != null && targetCommand.Path.Length > 0)
                targetPath = LimitPathByAvailableAp(target, targetCommand.Path);
            else
                targetPath = new[] { new HexPositionDto { Col = target.Col, Row = target.Row } };

            var path = BuildMobChasePath(mob, targetPath);

            UnitCommands[mob.UnitId] = new UnitCommandDto
            {
                UnitId = mob.UnitId,
                CommandType = "Move",
                Path = path
            };
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
            PlayerToUnitId[playerId] = unitId;
            Units[unitId] = new UnitStateDto
            {
                UnitId = unitId,
                UnitType = UnitType.Player,
                Col = col,
                Row = row,
                CurrentAp = MaxAp,
                PenaltyFraction = 0f
            };
        }

        // Серверный моб: ставим далеко от P1 (для начала достаточно зеркального спавна).
        if (Players.TryGetValue("P1", out var p1Pos))
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
                    CurrentAp = MobMaxAp,
                    PenaltyFraction = 0f
                };
            }
        }
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

    public void FillSpawnArrays(out string[] ids, out int[] cols, out int[] rows)
    {
        EnsureUnitsInitialized();

        var items = new List<(string id, int col, int row)>();

        foreach (var playerId in ParticipantIds.Where(Players.ContainsKey))
        {
            if (PlayerToUnitId.TryGetValue(playerId, out var unitId) && Units.TryGetValue(unitId, out var unit))
                items.Add((playerId, unit.Col, unit.Row));
            else
                items.Add((playerId, Players[playerId].col, Players[playerId].row));
        }

        foreach (var unit in Units.Values.Where(u => u.UnitType == UnitType.Mob))
            items.Add((unit.UnitId, unit.Col, unit.Row));

        ids = items.Select(x => x.id).ToArray();
        cols = items.Select(x => x.col).ToArray();
        rows = items.Select(x => x.row).ToArray();
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
        FillSpawnArrays(out var sid, out var sc, out var sr);
        return new BattleStartedPayloadDto
        {
            BattleId = BattleId,
            PlayerId = playerId,
            Players = players,
            RoundDuration = RoundDuration,
            RoundDeadlineUtcMs = RoundDeadlineUtcMs,
            SpawnPlayerIds = sid,
            SpawnCols = sc,
            SpawnRows = sr
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
                    CurrentAp = GetUnitRoundStartAp(UnitType.Player, 0f),
                    PenaltyFraction = 0f
                };
            }
        }

        UnitCommands[unitId] = new UnitCommandDto
        {
            UnitId = unitId,
            CommandType = "Move",
            Path = payload.Path
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

        // Собираем список всех юнитов (игроки + мобы), которые участвуют в симуляции.
        var unitIds = Units.Keys.ToList();
        if (unitIds.Count == 0)
        {
            // Fallback к старому поведению, если Units ещё не инициализированы.
            unitIds = Players.Keys.Select(pid => PlayerToUnitId.TryGetValue(pid, out var uid) ? uid : pid + "_UNIT").ToList();
        }

        // Путь каждого юнита: из UnitCommands или одна клетка (текущая позиция).
        var paths = new Dictionary<string, HexPositionDto[]>();
        foreach (var uid in unitIds)
        {
            UnitStateDto? us;
            if (!Units.TryGetValue(uid, out us))
                continue;

            if (UnitCommands.TryGetValue(uid, out var cmd) && cmd.Path != null && cmd.Path.Length > 0)
            {
                paths[uid] = LimitPathByAvailableAp(us, cmd.Path);
            }
            else
            {
                paths[uid] = new[]
                {
                    new HexPositionDto { Col = us.Col, Row = us.Row }
                };
            }
        }

        // Порядок: сначала игроки по SubmissionOrder, затем остальные игроки, затем мобы.
        var order = new List<string>();
        foreach (var pid in SubmissionOrder)
        {
            if (PlayerToUnitId.TryGetValue(pid, out var uid) && !order.Contains(uid))
                order.Add(uid);
        }
        foreach (var kv in PlayerToUnitId)
        {
            var uid = kv.Value;
            if (!order.Contains(uid))
                order.Add(uid);
        }
        foreach (var mob in Units.Values.Where(u => u.UnitType == UnitType.Mob))
        {
            if (!order.Contains(mob.UnitId))
                order.Add(mob.UnitId);
        }

        var pos = new Dictionary<string, (int col, int row)>();
        var actualPaths = new Dictionary<string, List<HexPositionDto>>();
        var accepted = new Dictionary<string, bool>();
        var rejectedReason = new Dictionary<string, string?>();

        foreach (var uid in order)
        {
            if (!paths.TryGetValue(uid, out var path) || path.Length == 0)
                continue;
            var start = path[0];
            pos[uid] = (start.Col, start.Row);
            actualPaths[uid] = new List<HexPositionDto> { new HexPositionDto { Col = start.Col, Row = start.Row } };
            accepted[uid] = true;
            rejectedReason[uid] = null;
        }

        var occupied = new HashSet<(int col, int row)>(pos.Values);
        int maxSteps = paths.Values.Select(p => p.Length).DefaultIfEmpty(0).Max() - 1;
        if (maxSteps < 0) maxSteps = 0;

        for (int step = 1; step <= maxSteps; step++)
        {
            foreach (var uid in order)
            {
                if (!paths.TryGetValue(uid, out var path) || path.Length == 0)
                    continue;

                int stepIndex = step < path.Length ? step : path.Length - 1;
                var target = path[stepIndex];
                var targetCell = (target.Col, target.Row);
                if (!pos.TryGetValue(uid, out var current))
                    continue;

                if (targetCell == current)
                    continue;

                if (occupied.Contains(targetCell))
                {
                    actualPaths[uid].Add(new HexPositionDto { Col = current.col, Row = current.row });
                    accepted[uid] = false;
                    rejectedReason[uid] = $"Target hex ({target.Col},{target.Row}) already occupied";
                    continue;
                }

                occupied.Remove(current);
                occupied.Add(targetCell);
                pos[uid] = targetCell;
                actualPaths[uid].Add(new HexPositionDto { Col = target.Col, Row = target.Row });
            }
        }

        var results = new List<PlayerTurnResultDto>();
        foreach (var uid in order)
        {
            if (!pos.TryGetValue(uid, out var final))
                continue;
            if (!actualPaths.TryGetValue(uid, out var listPath))
                continue;

            var actualPath = listPath.ToArray();
            int stepsTaken = actualPath.Length - 1;
            int apSpent = 0;
            for (int k = 1; k <= stepsTaken; k++)
                apSpent += GetMoveCost(k - 1, 1);

            UnitStateDto us;
            if (!Units.TryGetValue(uid, out us!))
            {
                // fallback: создать временное состояние
                us = new UnitStateDto
                {
                    UnitId = uid,
                    UnitType = UnitType.Player,
                    Col = final.col,
                    Row = final.row,
                    CurrentAp = GetUnitRoundStartAp(UnitType.Player, 0f),
                    PenaltyFraction = 0f
                };
            }

            float newPenalty = us.UnitType == UnitType.Mob ? 0f : us.PenaltyFraction;
            if (us.UnitType != UnitType.Mob && stepsTaken > 0 && us.CurrentAp >= apSpent)
            {
                int n = 0;
                while (GetStepCost(n + 1) <= MaxAp) n++;
                if (n >= 1)
                {
                    int lastCost = GetStepCost(n);
                    int prelastCost = n >= 2 ? GetStepCost(n - 1) : 0;
                    if (apSpent == prelastCost) newPenalty = Math.Min(0.9f, newPenalty + 0.05f);
                    if (apSpent >= lastCost) newPenalty = Math.Min(0.9f, newPenalty + 0.08f);
                }
            }

            if (us.UnitType != UnitType.Mob)
            {
                int nPen = 0;
                while (GetStepCost(nPen + 1) <= MaxAp) nPen++;
                int lastStepCost = nPen >= 1 ? GetStepCost(nPen) : 0;
                int prelastStepCost = nPen >= 2 ? GetStepCost(nPen - 1) : 0;
                bool endedOnPenaltyHex = apSpent == prelastStepCost || apSpent == lastStepCost;
                const float recoveryFraction = 0.05f;
                if (!endedOnPenaltyHex)
                    newPenalty = Math.Max(0f, newPenalty - recoveryFraction);
                newPenalty = Math.Min(0.9f, newPenalty);
            }

            int nextRoundAp = GetUnitRoundStartAp(us.UnitType, newPenalty);

            // Обновляем Units (серверное состояние) для следующего раунда.
            us.Col = final.col;
            us.Row = final.row;
            us.CurrentAp = nextRoundAp;
            us.PenaltyFraction = newPenalty;
            Units[uid] = us;

            // Для текущей клиентской модели по-прежнему формируем результат по playerId.
            string playerId = PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key ?? uid;

            results.Add(new PlayerTurnResultDto
            {
                UnitId = uid,
                UnitType = us.UnitType,
                PlayerId = playerId,
                Accepted = accepted.TryGetValue(uid, out var ok) && ok,
                FinalPosition = new HexPositionDto { Col = final.col, Row = final.row },
                ActualPath = actualPath,
                CurrentAp = nextRoundAp,
                PenaltyFraction = newPenalty,
                ApSpentThisTurn = apSpent,
                RejectedReason = rejectedReason.TryGetValue(uid, out var rr) ? rr : null
            });
        }

        LastTurnResult = new TurnResultPayloadDto
        {
            BattleId = BattleId,
            RoundIndex = RoundIndex,
            Results = results.ToArray(),
            RoundResolveReason = resolveReason
        };

        RoundIndex++;
        ResetRoundTimer();
        Submissions.Clear();
        SubmissionOrder.Clear();
        EndedTurnEarlyThisRound.Clear();
        UnitCommands.Clear();
        RoundInProgress = true;
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
