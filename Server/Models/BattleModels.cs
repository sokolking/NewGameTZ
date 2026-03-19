using System.Collections.Generic;

namespace BattleServer.Models;

/// <summary>Тип юнита в бою.</summary>
public enum UnitType
{
    Player = 0,
    Mob = 1
}

/// <summary>Текущее состояние юнита на сервере (позиция, ОД, штраф) — для пошаговой симуляции.</summary>
public class PlayerBattleState
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int CurrentAp { get; set; }
    public float PenaltyFraction { get; set; }
}

/// <summary>Совместимо с Unity (сериализация в camelCase).</summary>
public class HexPositionDto
{
    public int Col { get; set; }
    public int Row { get; set; }
}

public class SubmitTurnPayloadDto
{
    public string BattleId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public int RoundIndex { get; set; }
    /// <summary>Идентификатор юнита, которым управляет игрок (опционально, для будущего PvE).</summary>
    public string? UnitId { get; set; }
    public HexPositionDto[]? Path { get; set; }
    public int ApSpentThisTurn { get; set; }
    public int StepsTakenThisTurn { get; set; }
}

/// <summary>Команда юнита на один раунд (расширяемо под разные типы действий).</summary>
public class UnitCommandDto
{
    public string UnitId { get; set; } = "";
    public string CommandType { get; set; } = "Move"; // пока только Move
    public HexPositionDto[]? Path { get; set; }
}

/// <summary>Состояние юнита для отдачи клиенту (срез).</summary>
public class UnitStateDto
{
    public string UnitId { get; set; } = "";
    public UnitType UnitType { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public int CurrentAp { get; set; }
    public float PenaltyFraction { get; set; }
}

public class PlayerTurnResultDto
{
    /// <summary>Идентификатор юнита на сервере (для будущей расширяемости).</summary>
    public string UnitId { get; set; } = "";
    public UnitType UnitType { get; set; } = UnitType.Player;
    public string PlayerId { get; set; } = "";
    public bool Accepted { get; set; }
    public HexPositionDto? FinalPosition { get; set; }
    public HexPositionDto[]? ActualPath { get; set; }
    public int CurrentAp { get; set; }
    public float PenaltyFraction { get; set; }
    public int ApSpentThisTurn { get; set; }
    public string? RejectedReason { get; set; }
}

public class TurnResultPayloadDto
{
    public string BattleId { get; set; } = "";
    public int RoundIndex { get; set; }
    public PlayerTurnResultDto[]? Results { get; set; }
    /// <summary>allSubmitted — все сдали ход до таймера; timerExpired — время раунда вышло.</summary>
    public string RoundResolveReason { get; set; } = "";
}

/// <summary>Статус участника в текущем раунде (для GET состояния боя).</summary>
public class BattleParticipantStatusDto
{
    public string PlayerId { get; set; } = "";
    public bool HasSubmitted { get; set; }
    /// <summary>Игрок нажал «завершить ход», пока таймер раунда ещё шёл.</summary>
    public bool EndedTurnEarly { get; set; }
}

public class BattlePlayerInfoDto
{
    public string PlayerId { get; set; } = "";
    public int Col { get; set; }
    public int Row { get; set; }
}

public class BattleStartedPayloadDto
{
    public string BattleId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public BattlePlayerInfoDto[]? Players { get; set; }
    public float RoundDuration { get; set; }
    public long RoundDeadlineUtcMs { get; set; }
    /// <summary>Дублирование спавна для Unity JsonUtility (массив объектов в JSON часто не парсится).</summary>
    public string[]? SpawnPlayerIds { get; set; }
    public int[]? SpawnCols { get; set; }
    public int[]? SpawnRows { get; set; }
    public int[]? ObstacleCols { get; set; }
    public int[]? ObstacleRows { get; set; }
}

public class BattleTurnHistoryStateDto
{
    public string[]? TurnHistoryIds { get; set; }
    public int CurrentTurnPointer { get; set; }
}

public class BattleRecordDto
{
    public string BattleId { get; set; } = "";
    public List<string> TurnIds { get; set; } = new();
}

public class BattleTurnRecordDto
{
    public string TurnId { get; set; } = "";
    public string BattleId { get; set; } = "";
    public TurnResultPayloadDto TurnResult { get; set; } = new();
}

public class BattleBrowseRowDto
{
    public string BattleId { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public int TurnCount { get; set; }
    public string? LatestTurnId { get; set; }
}

public class BattleTurnBrowseRowDto
{
    public string TurnId { get; set; } = "";
    public string BattleId { get; set; } = "";
    public int TurnIndex { get; set; }
    public int RoundIndex { get; set; }
    public string RoundResolveReason { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}

public class BattleTurnDetailDto
{
    public string TurnId { get; set; } = "";
    public string BattleId { get; set; } = "";
    public int TurnIndex { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string RawJson { get; set; } = "";
    public TurnResultPayloadDto TurnResult { get; set; } = new();
}

public class BattleUserBrowseRowDto
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
