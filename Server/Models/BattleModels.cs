using System;
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

/// <summary>One row from <c>body_parts</c> (id + stable English code).</summary>
public class BattleBodyPartRowDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

public class QueuedBattleActionDto
{
    public string ActionType { get; set; } = "";
    public HexPositionDto? TargetPosition { get; set; }
    public string? TargetUnitId { get; set; }
    /// <summary>FK to <c>body_parts.id</c>; 0 = not specified.</summary>
    public int BodyPart { get; set; }
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

/// <summary>Строка из таблицы weapons (список/поиск по коду).</summary>
public class BattleWeaponBrowseRowDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Диапазон урона в бою (случайное целое inclusive).</summary>
    public int DamageMin { get; set; } = 1;
    public int DamageMax { get; set; } = 1;
    public int Range { get; set; }
    public string IconKey { get; set; } = "";
    /// <summary>Одиночный выстрел: стоимость в ОД.</summary>
    public int AttackApCost { get; set; } = 1;
    /// <summary>Штраф кучности (0…1), вычитается из p попадания после дистанции и укрытий.</summary>
    public double SpreadPenalty { get; set; }
    /// <summary>Высота траектории 0…2 для правил ЛС (см. сервер 7.20).</summary>
    public int TrajectoryHeight { get; set; } = 1;
    /// <summary>Только БД; в бой пока не входит.</summary>
    public int Quality { get; set; } = 100;
    /// <summary>Только БД; в бой пока не входит. Колонка <c>weapon_condition</c>.</summary>
    public int WeaponCondition { get; set; } = 100;
    /// <summary>Ослабленный штраф к p за дистанцию за пределами <see cref="Range"/> (кривая «снайпер»).</summary>
    public bool IsSniper { get; set; }
    public double Mass { get; set; }
    public string Caliber { get; set; } = "";
    public int ArmorPierce { get; set; }
    public int MagazineSize { get; set; }
    /// <summary>Перезарядка (ОД).</summary>
    public int ReloadApCost { get; set; }
    public string Category { get; set; } = "cold";
    /// <summary>Мин. уровень персонажа (= уровень оружия).</summary>
    public int ReqLevel { get; set; } = 1;
    public int ReqStrength { get; set; }
    public int ReqEndurance { get; set; }
    public int ReqAccuracy { get; set; }
    /// <summary>Владение по категории (ключ навыка).</summary>
    public string ReqMasteryCategory { get; set; } = "";
    /// <summary>Дельты к характеристикам (негативные эффекты оружия).</summary>
    public int StatEffectStrength { get; set; }
    public int StatEffectEndurance { get; set; }
    public int StatEffectAccuracy { get; set; }
    public string DamageType { get; set; } = "physical";
    /// <summary>Очередь: число пуль за один режим (0 — режим не задан).</summary>
    public int BurstRounds { get; set; }
    /// <summary>Очередь: стоимость в ОД.</summary>
    public int BurstApCost { get; set; }
}

/// <summary>Distinct <c>damage_type</c> / <c>category</c> values for weapons admin UI.</summary>
public sealed class BattleWeaponMetaDto
{
    public IReadOnlyList<string> DamageTypes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
}

/// <summary>Полная запись для upsert в <c>weapons</c>.</summary>
public sealed class BattleWeaponUpsertDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int DamageMin { get; set; } = 1;
    public int DamageMax { get; set; } = 1;
    public int Range { get; set; }
    public string IconKey { get; set; } = "";
    public int AttackApCost { get; set; } = 1;
    public double SpreadPenalty { get; set; }
    public int TrajectoryHeight { get; set; } = 1;
    public int Quality { get; set; } = 100;
    public int WeaponCondition { get; set; } = 100;
    public bool IsSniper { get; set; }
    public double Mass { get; set; }
    public string Caliber { get; set; } = "";
    public int ArmorPierce { get; set; }
    public int MagazineSize { get; set; }
    public int ReloadApCost { get; set; }
    public string Category { get; set; } = "cold";
    public int ReqLevel { get; set; } = 1;
    public int ReqStrength { get; set; }
    public int ReqEndurance { get; set; }
    public int ReqAccuracy { get; set; }
    public string ReqMasteryCategory { get; set; } = "";
    public int StatEffectStrength { get; set; }
    public int StatEffectEndurance { get; set; }
    public int StatEffectAccuracy { get; set; }
    public string DamageType { get; set; } = "physical";
    public int BurstRounds { get; set; }
    public int BurstApCost { get; set; }
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
    /// <summary>FK to <c>body_parts.id</c>; 0 = not specified.</summary>
    public int BodyPart { get; set; }
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
    public int WeaponDamageMin { get; set; } = 1;
    public int WeaponDamage { get; set; } = 1;
    public int WeaponRange { get; set; } = 1;
    /// <summary>Стоимость атаки (ОД), фиксированная логикой боя.</summary>
    public int WeaponAttackApCost { get; set; } = 1;
    /// <summary>Меткость: аддитивный бонус к p попадания (+2% за пункт после множителей дистанции и укрытия).</summary>
    public int Accuracy { get; set; } = 10;
    /// <summary>Кучность оружия: вычитается из p (0…1).</summary>
    public double WeaponSpreadPenalty { get; set; }
    /// <summary>Высота траектории выстрела для ЛС и стен (0 низкая, 1 обычная, 2 высокая).</summary>
    public int WeaponTrajectoryHeight { get; set; } = 1;
    /// <summary>Снайперское оружие: иная кривая p по дистанции за пределами дальности (урон без изменений).</summary>
    public bool WeaponIsSniper { get; set; }
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
    public int WeaponDamageMin { get; set; } = 1;
    public int WeaponDamage { get; set; } = 1;
    public int WeaponRange { get; set; } = 1;
    /// <summary>Стоимость атаки (ОД), фиксированная логикой боя.</summary>
    public int WeaponAttackApCost { get; set; } = 1;
    public double WeaponSpreadPenalty { get; set; }
    public int WeaponTrajectoryHeight { get; set; } = 1;
    public bool WeaponIsSniper { get; set; }
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
    /// <summary>Параллельно <see cref="SpawnWeaponDamages"/> (макс.); мин. урон для отображения/логики клиента.</summary>
    public int[]? SpawnWeaponDamageMins { get; set; }
    public int[]? SpawnWeaponRanges { get; set; }
    public int[]? SpawnWeaponAttackApCosts { get; set; }
    public double[]? SpawnWeaponSpreadPenalties { get; set; }
    public int[]? SpawnWeaponTrajectoryHeights { get; set; }
    public bool[]? SpawnWeaponIsSnipers { get; set; }
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
    public int Experience { get; set; }
    public int Level { get; set; }
    public int Strength { get; set; }
    public int Endurance { get; set; }
    public int Accuracy { get; set; }
    public int MaxHp { get; set; }
    public int MaxAp { get; set; }
}

/// <summary>Обновление пользователя из админки /users (игрок сам характеристики не меняет). Пароль: null — не менять.</summary>
public class UserUpdateRequest
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public int Experience { get; set; }
    public int Strength { get; set; }
    public int Endurance { get; set; }
    public int Accuracy { get; set; }
    public int MaxHp { get; set; }
    public int MaxAp { get; set; }
    public string WeaponCode { get; set; } = "fist";
}

/// <summary>Публичный срез прогресса. Характеристики меняются только через БД/админку, не игроком.</summary>
public class UserProgressProfileDto
{
    public string Username { get; set; } = "";
    public int Experience { get; set; }
    public int Level { get; set; }
    public int Strength { get; set; }
    public int Endurance { get; set; }
    public int Accuracy { get; set; }
    public int MaxHp { get; set; }
    public int MaxAp { get; set; }
    public int HitBonusPercent { get; set; }
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
