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
    public int CurrentMagazineRounds { get; set; }
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
    public int PreviousMagazineRounds { get; set; }
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

/// <summary>Параметры сужения игрового поля по раундам (таблица battle_zone_shrink).</summary>
public class BattleZoneShrinkRowDto
{
    /// <summary>Первый раунд (нумерация с 1), с которого действует сужение.</summary>
    public int ShrinkStartRound { get; set; } = 10;
    /// <summary>Каждые сколько раундов сужать по горизонтали (слева и справа).</summary>
    public int HorizontalShrinkInterval { get; set; } = 2;
    /// <summary>Сколько колонок убирать с каждой стороны за шаг по горизонтали.</summary>
    public int HorizontalShrinkAmount { get; set; } = 2;
    /// <summary>Каждые сколько раундов сужать по вертикали (сверху и снизу).</summary>
    public int VerticalShrinkInterval { get; set; } = 2;
    /// <summary>Сколько рядов убирать с каждой стороны за шаг по вертикали.</summary>
    public int VerticalShrinkAmount { get; set; } = 1;
    /// <summary>Минимальная ширина активной зоны (число колонок).</summary>
    public int MinWidth { get; set; } = 5;
    /// <summary>Минимальная высота активной зоны (число рядов).</summary>
    public int MinHeight { get; set; } = 3;

    public static BattleZoneShrinkRowDto Defaults => new BattleZoneShrinkRowDto();
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
    /// <summary>Weapon range in hexes. In DB/API list, <c>-1</c> means not applicable; combat resolves to adjacent melee (1).</summary>
    public int Range { get; set; }
    public string IconKey { get; set; } = "";
    /// <summary>Single attack AP cost. In DB, <c>-1</c> means N/A; combat uses 1.</summary>
    public int AttackApCost { get; set; } = 1;
    /// <summary>Кучность (0…1): чем выше — тем кучнее, тем выше шанс попадания. Колонка БД <c>spread_penalty</c> (историческое имя).</summary>
    public double Tightness { get; set; } = 1.0;
    /// <summary>Trajectory height for LoS (0…3). In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int TrajectoryHeight { get; set; } = 1;
    /// <summary>Только БД; в бой пока не входит.</summary>
    public int Quality { get; set; } = 100;
    /// <summary>Только БД; в бой пока не входит. Колонка <c>weapon_condition</c>.</summary>
    public int WeaponCondition { get; set; } = 100;
    /// <summary>Ослабленный штраф к p за дистанцию за пределами <see cref="Range"/> (кривая «снайпер»).</summary>
    public bool IsSniper { get; set; }
    public double Mass { get; set; }
    public string Caliber { get; set; } = "";
    /// <summary>In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int ArmorPierce { get; set; }
    /// <summary>In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int MagazineSize { get; set; }
    /// <summary>Reload AP cost. In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int ReloadApCost { get; set; }
    public string Category { get; set; } = "cold";
    /// <summary>Min character level. In DB, <c>-1</c> means N/A; combat uses 0 (no level gate).</summary>
    public int ReqLevel { get; set; } = 1;
    /// <summary>In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int ReqStrength { get; set; }
    /// <summary>In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int ReqEndurance { get; set; }
    /// <summary>In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int ReqAccuracy { get; set; }
    /// <summary>Владение по категории (ключ навыка).</summary>
    public string ReqMasteryCategory { get; set; } = "";
    /// <summary>Stat deltas. Exactly <c>-1</c> in DB means N/A (combat uses 0); other values (including negatives) are kept.</summary>
    public int StatEffectStrength { get; set; }
    public int StatEffectEndurance { get; set; }
    public int StatEffectAccuracy { get; set; }
    public string DamageType { get; set; } = "physical";
    /// <summary>Burst rounds per use. In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int BurstRounds { get; set; }
    /// <summary>Burst AP cost. In DB, <c>-1</c> means N/A; combat uses 0.</summary>
    public int BurstApCost { get; set; }
    /// <summary>Inventory grid width: 1 or 2 cells (server clamps).</summary>
    public int InventorySlotWidth { get; set; } = 1;
    /// <summary>Hand slots consumed by item in inventory grid: 0, 1 or 2.</summary>
    public int InventoryGrid { get; set; } = 1;
    /// <summary>Universal effect type (for example hp, ap).</summary>
    public string EffectType { get; set; } = "";
    /// <summary>Effect sign: positive or negative.</summary>
    public string EffectSign { get; set; } = "positive";
    /// <summary>Effect roll lower bound.</summary>
    public int EffectMin { get; set; }
    /// <summary>Effect roll upper bound.</summary>
    public int EffectMax { get; set; }
    /// <summary>Effect target: self or enemy.</summary>
    public string EffectTarget { get; set; } = "enemy";
    /// <summary>Common item flag from <c>items.is_equippable</c>.</summary>
    public bool IsEquippable { get; set; }
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
    /// <summary>Кучность 0…1 (выше — лучше). В БД колонка <c>spread_penalty</c>.</summary>
    public double Tightness { get; set; } = 1.0;
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
    /// <summary>1 or 2 inventory cells per instance.</summary>
    public int InventorySlotWidth { get; set; } = 1;
    /// <summary>Hand slots consumed by item in inventory grid: 0, 1 or 2.</summary>
    public int InventoryGrid { get; set; } = 1;
    public string EffectType { get; set; } = "";
    public string EffectSign { get; set; } = "positive";
    public int EffectMin { get; set; }
    public int EffectMax { get; set; }
    public string EffectTarget { get; set; } = "enemy";
    public bool IsEquippable { get; set; } = true;
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
    public int Healed { get; set; }
    public bool TargetDied { get; set; }
    /// <summary>Итоговая вероятность попадания (0…1) после дистанции, укрытия и меткости; null если броска не было (стена, промах валидации и т.п.).</summary>
    public double? HitProbability { get; set; }
    /// <summary>Результат броска по <see cref="HitProbability"/>; null если броска не было.</summary>
    public bool? HitSucceeded { get; set; }
    /// <summary>Hex distance shooter->target used in hit formula.</summary>
    public int? HitDebugDistance { get; set; }
    /// <summary>Base distance-only probability before cover/accuracy/spread.</summary>
    public double? HitDebugPDistance { get; set; }
    /// <summary>Tree cover factor in [0..1].</summary>
    public double? HitDebugTreeF { get; set; }
    /// <summary>Rock cover factor in [0..1].</summary>
    public double? HitDebugRockF { get; set; }
    /// <summary>Combined cover multiplier (treeF * rockF).</summary>
    public double? HitDebugCoverMul { get; set; }
    /// <summary>Accuracy additive bonus.</summary>
    public double? HitDebugAccBonus { get; set; }
    /// <summary>Weapon tightness T used in formula.</summary>
    public double? HitDebugWeaponTightness { get; set; }
    /// <summary>Raw spread from tightness: clamp(1 - T, 0..1).</summary>
    public double? HitDebugSpreadRaw { get; set; }
    /// <summary>Spread after combat clamp to 0.95.</summary>
    public double? HitDebugSpread { get; set; }
    /// <summary>Target posture used for rock-cover check.</summary>
    public string? HitDebugTargetPosture { get; set; }
    /// <summary>Whether any tree contributed to cover on this shot.</summary>
    public bool? HitDebugAnyTree { get; set; }
    /// <summary>Whether any rock contributed to cover on this shot.</summary>
    public bool? HitDebugAnyRock { get; set; }
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
    public int CurrentMagazineRounds { get; set; }
    /// <summary>Меткость: аддитивный бонус к p попадания (+2% за пункт после множителей дистанции и укрытия).</summary>
    public int Accuracy { get; set; } = 10;
    /// <summary>Кучность оружия <c>T</c> (0…1, выше — кучнее). В формуле попадания вычитается <c>clamp(1 − T, …)</c>. Колонка БД <c>spread_penalty</c> — историческое имя.</summary>
    public double WeaponTightness { get; set; } = 1.0;
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
    /// <summary>Поза юнита до симуляции этого раунда (для проигрывания журнала на клиенте без ложных Sit↔Stand).</summary>
    public string PostureAtRoundStart { get; set; } = "walk";
    public string CurrentPosture { get; set; } = "walk";
    public string WeaponCode { get; set; } = "fist";
    public int WeaponDamageMin { get; set; } = 1;
    public int WeaponDamage { get; set; } = 1;
    public int WeaponRange { get; set; } = 1;
    /// <summary>Стоимость атаки (ОД), фиксированная логикой боя.</summary>
    public int WeaponAttackApCost { get; set; } = 1;
    public int CurrentMagazineRounds { get; set; }
    public double WeaponTightness { get; set; } = 1.0;
    public int WeaponTrajectoryHeight { get; set; } = 1;
    public bool WeaponIsSniper { get; set; }
    public ExecutedBattleActionDto[]? ExecutedActions { get; set; }
    /// <summary>Player is in the flee channel (forced empty queue / 0 AP for the round).</summary>
    public bool IsEscaping { get; set; }
    /// <summary>Rounds remaining in the flee channel after this round resolves (0 when <see cref="HasFled"/>).</summary>
    public int EscapeRoundsRemaining { get; set; }
    /// <summary>Removed from the battle this round after completing flee.</summary>
    public bool HasFled { get; set; }
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
    /// <summary>Клетки, выведенные из игры этим сужением (клиент: падение гексов).</summary>
    public HexPositionDto[]? ZoneShrinkCells { get; set; }
    /// <summary>Активная зона после раунда (включительно).</summary>
    public int ActiveMinCol { get; set; }
    public int ActiveMaxCol { get; set; }
    public int ActiveMinRow { get; set; }
    public int ActiveMaxRow { get; set; }
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
    /// <summary>Current server round index (for resume after re-login).</summary>
    public int RoundIndex { get; set; }
    /// <summary>Дублирование спавна для Unity JsonUtility (массив объектов в JSON часто не парсится).</summary>
    public string[]? SpawnPlayerIds { get; set; }
    public int[]? SpawnCols { get; set; }
    public int[]? SpawnRows { get; set; }
    public int[]? SpawnCurrentAps { get; set; }
    public int[]? SpawnMaxAps { get; set; }
    public int[]? SpawnMaxHps { get; set; }
    public int[]? SpawnCurrentHps { get; set; }
    public string[]? SpawnCurrentPostures { get; set; }
    public string[]? SpawnWeaponCodes { get; set; }
    public int[]? SpawnWeaponDamages { get; set; }
    /// <summary>Параллельно <see cref="SpawnWeaponDamages"/> (макс.); мин. урон для отображения/логики клиента.</summary>
    public int[]? SpawnWeaponDamageMins { get; set; }
    public int[]? SpawnWeaponRanges { get; set; }
    public int[]? SpawnWeaponAttackApCosts { get; set; }
    public int[]? SpawnCurrentMagazineRounds { get; set; }
    public double[]? SpawnWeaponTightnesses { get; set; }
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
    public int ActiveMinCol { get; set; }
    public int ActiveMaxCol { get; set; }
    public int ActiveMinRow { get; set; }
    public int ActiveMaxRow { get; set; }
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
    public int CurrentHp { get; set; }
    public int MaxAp { get; set; }
    /// <summary>Equipped weapon code from <c>user_inventory_items</c> (<c>fist</c> if none).</summary>
    public string WeaponCode { get; set; } = "fist";
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
}

public class UserDebugHpRequest
{
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
}

/// <summary>Публичный срез прогресса. Характеристики меняются только через БД/админку, не игроком.</summary>
public class UserProgressProfileDto
{
    public string Username { get; set; } = "";
    public int Experience { get; set; }
    public int Level { get; set; }
    public int Strength { get; set; }
    public int Agility { get; set; }
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
    /// <summary>Primary cell of a multi-slot item: width (1 or 2). Continuation cells use 0.</summary>
    public int SlotSpan { get; set; }
    /// <summary>True when this stack is currently equipped (primary cell only).</summary>
    public bool Equipped { get; set; }
    /// <summary>Second cell of a 2-slot weapon.</summary>
    public bool Continuation { get; set; }
    /// <summary>True for countable stack items (for example ammo).</summary>
    public bool Stackable { get; set; }
    /// <summary>Stack amount for <see cref="Stackable"/> items.</summary>
    public int Quantity { get; set; }
    /// <summary>Rounds currently loaded in weapon chamber/magazine for this inventory item.</summary>
    public int ChamberRounds { get; set; }
    /// <summary>Whether this item can be equipped in hand.</summary>
    public bool IsEquippable { get; set; }
}

/// <summary>Row in <c>user_inventory_items</c> for admin GET/PUT.</summary>
public sealed class UserInventoryItemAdminDto
{
    public long Id { get; set; }
    public int StartSlot { get; set; }
    public string WeaponCode { get; set; } = "";
    public int SlotWidth { get; set; } = 1;
    public int ChamberRounds { get; set; }
    public bool IsEquipped { get; set; }
}

public sealed class AmmoTypeDto
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public string Caliber { get; set; } = "";
    public string Name { get; set; } = "";
    public double UnitWeight { get; set; }
    public int Quality { get; set; } = 100;
    public int Condition { get; set; } = 100;
    public string IconKey { get; set; } = "";
    public int InventoryGrid { get; set; } = 1;
}

public sealed class AmmoTypeUpsertRequest
{
    public string Caliber { get; set; } = "";
    public string Name { get; set; } = "";
    public double UnitWeight { get; set; }
    public int Quality { get; set; } = 100;
    public int Condition { get; set; } = 100;
    public string? IconKey { get; set; }
    public int InventoryGrid { get; set; } = 1;
}

public sealed class UserAmmoPackAdminDto
{
    public long Id { get; set; }
    public long AmmoTypeId { get; set; }
    public long ItemId { get; set; }
    public string Caliber { get; set; } = "";
    public string Name { get; set; } = "";
    public double UnitWeight { get; set; }
    public int Quality { get; set; } = 100;
    public int Condition { get; set; } = 100;
    public string IconKey { get; set; } = "";
    public int InventoryGrid { get; set; } = 1;
    public int StartSlot { get; set; }
    public int RoundsCount { get; set; }
    public int PacksCount { get; set; }
    public int TotalRounds { get; set; }
}

public sealed class UserItemAdminDto
{
    public string ItemType { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string IconKey { get; set; } = "";
    public int Quality { get; set; } = 100;
    public int Condition { get; set; } = 100;
    public double Mass { get; set; }
    public int InventoryGrid { get; set; } = 1;
    public int Quantity { get; set; }
    public int ChamberRounds { get; set; }
    public int StartSlot { get; set; } = -1;
    public bool IsEquipped { get; set; }
    public bool IsEquippable { get; set; }
    public bool IsStackable { get; set; }
    public int SlotWidth { get; set; }
}

public sealed class UserItemReplaceDto
{
    public string ItemType { get; set; } = "";
    public string Code { get; set; } = "";
    public int Quantity { get; set; }
    public int ChamberRounds { get; set; }
    public int StartSlot { get; set; } = -1;
    public bool IsEquipped { get; set; }
    public bool IsEquippable { get; set; }
}

public sealed class UserItemsReplaceHttpBody
{
    public List<UserItemReplaceDto> Items { get; set; } = new();
}
