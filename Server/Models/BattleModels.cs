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
    public QueuedBattleActionDto[]? Actions { get; set; }
}

public class QueuedBattleActionDto
{
    public string ActionType { get; set; } = "";
    public HexPositionDto? TargetPosition { get; set; }
    public string? TargetUnitId { get; set; }
    public string? BodyPart { get; set; }
    public string? Posture { get; set; }
    public int Cost { get; set; } = 1;
    /// <summary>Для EquipWeapon: код оружия из БД.</summary>
    public string? WeaponCode { get; set; }
    /// <summary>Для отмены EquipWeapon: стоимость атаки предыдущего оружия (клиент).</summary>
    public int PreviousWeaponAttackApCost { get; set; }
    /// <summary>Для EquipWeapon: стоимость атаки нового оружия (клиент).</summary>
    public int WeaponAttackApCost { get; set; }
}

/// <summary>Баланс препятствий (таблица battle_obstacle_balance).</summary>
public class BattleObstacleBalanceRowDto
{
    public int WallMaxHp { get; set; } = 35;
    /// <summary>Снижение шанса попадания (0–95), если цель за деревом.</summary>
    public int TreeCoverMissPercent { get; set; } = 15;
    /// <summary>Снижение шанса попадания (0–95), если цель за камнем и поза sit/hide.</summary>
    public int RockCoverMissPercent { get; set; } = 20;
    public int WallSegmentsCount { get; set; } = 10;
    public int RockCount { get; set; } = 5;
    public int TreeCount { get; set; } = 5;

    public static BattleObstacleBalanceRowDto Defaults => new BattleObstacleBalanceRowDto();
}

public class ExecutedBattleActionDto
{
    public string UnitId { get; set; } = "";
    public string ActionType { get; set; } = "";
    public int Tick { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public HexPositionDto? FromPosition { get; set; }
    public HexPositionDto? ToPosition { get; set; }
    public string? TargetUnitId { get; set; }
    public string? BodyPart { get; set; }
    public string? Posture { get; set; }
    public int Damage { get; set; }
    public bool TargetDied { get; set; }
}

/// <summary>Команда юнита на один раунд (расширяемо под разные типы действий).</summary>
public class UnitCommandDto
{
    public string UnitId { get; set; } = "";
    public string CommandType { get; set; } = "Move"; // пока только Move
    public QueuedBattleActionDto[]? Actions { get; set; }
}

/// <summary>Состояние юнита для отдачи клиенту (срез).</summary>
public class UnitStateDto
{
    public string UnitId { get; set; } = "";
    public UnitType UnitType { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public int MaxAp { get; set; }
    public int CurrentAp { get; set; }
    public float PenaltyFraction { get; set; }
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public string WeaponCode { get; set; } = "fist";
    public int WeaponDamage { get; set; } = 1;
    public int WeaponRange { get; set; } = 1;
    /// <summary>Стоимость атаки (ОД) из weapons.attack_ap_cost.</summary>
    public int WeaponAttackApCost { get; set; } = 1;
    public string Posture { get; set; } = "walk";
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
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public bool IsDead { get; set; }
    public string? AttackTargetUnitId { get; set; }
    public int DamageDealt { get; set; }
    public string CurrentPosture { get; set; } = "walk";
    public string WeaponCode { get; set; } = "fist";
    public int WeaponDamage { get; set; } = 1;
    public int WeaponRange { get; set; } = 1;
    /// <summary>Стоимость атаки (ОД) из weapons.attack_ap_cost.</summary>
    public int WeaponAttackApCost { get; set; } = 1;
    public ExecutedBattleActionDto[]? ExecutedActions { get; set; }
}

public enum CellObjectState
{
    None = 0,
    Full = 1,
    Damaged = 2
}

public class MapUpdateDto
{
    public int Tick { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public CellObjectState NewState { get; set; }
}

public class CellObject
{
    public HexPositionDto? Hex { get; set; }
    public CellObjectState State { get; set; }
}

public class TurnResultPayloadDto
{
    public string BattleId { get; set; } = "";
    public int RoundIndex { get; set; }
    public PlayerTurnResultDto[]? Results { get; set; }
    /// <summary>allSubmitted — все сдали ход до таймера; timerExpired — время раунда вышло.</summary>
    public string RoundResolveReason { get; set; } = "";
    public bool BattleFinished { get; set; }
    public CellObject[]? MapState { get; set; }
    public MapUpdateDto[]? MapUpdates { get; set; }
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
    public int[]? SpawnCurrentAps { get; set; }
    public int[]? SpawnMaxHps { get; set; }
    public int[]? SpawnCurrentHps { get; set; }
    public string[]? SpawnCurrentPostures { get; set; }
    public string[]? SpawnWeaponCodes { get; set; }
    public int[]? SpawnWeaponDamages { get; set; }
    public int[]? SpawnWeaponRanges { get; set; }
    public int[]? SpawnWeaponAttackApCosts { get; set; }
    public string[]? SpawnDisplayNames { get; set; }
    public int[]? SpawnLevels { get; set; }
    public int[]? ObstacleCols { get; set; }
    public int[]? ObstacleRows { get; set; }
    /// <summary>Теги: wall | tree | rock — параллельно obstacleCols/Rows.</summary>
    public string[]? ObstacleTags { get; set; }
    /// <summary>Yaw стен (градусы вокруг Y), параллельно obstacleCols/Rows.</summary>
    public float[]? ObstacleWallYaws { get; set; }
    public CellObject[]? MapState { get; set; }
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
    public int MaxHp { get; set; }
    public int MaxAp { get; set; }
    public string WeaponCode { get; set; } = "fist";
}

/// <summary>Обновление пользователя из админки /users. Пароль: null — не менять.</summary>
public class UserUpdateRequest
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public int MaxHp { get; set; }
    public int MaxAp { get; set; }
    public string WeaponCode { get; set; } = "fist";
}

public class BattleWeaponBrowseRowDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Damage { get; set; }
    public int Range { get; set; }
    /// <summary>Ключ спрайта на клиенте (Resources/WeaponIcons/{iconKey}).</summary>
    public string IconKey { get; set; } = "fist";
    /// <summary>Стоимость атаки этим оружием (ОД). По умолчанию 1; для fist — 3, для stone — 7.</summary>
    public int AttackApCost { get; set; } = 1;
}

/// <summary>Одна ячейка инвентаря пользователя (0..11).</summary>
public class UserInventorySlotDto
{
    public int SlotIndex { get; set; }
    public long? WeaponId { get; set; }
    public string? WeaponCode { get; set; }
    public string? WeaponName { get; set; }
    public int Damage { get; set; }
    public int Range { get; set; }
    public string IconKey { get; set; } = "fist";
    public int AttackApCost { get; set; }
}
