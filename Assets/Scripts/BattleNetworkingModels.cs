using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// Модели данных для обмена клиент–сервер по плану ServerSyncPlan.
/// Сериализация в JSON (Unity JsonUtility или Newtonsoft) — использовать публичные поля или [SerializeField].
/// </summary>

/// <summary>Общие настройки Newtonsoft для пушей боя и загрузки хода по HTTP.</summary>
public static class HopeBattleJson
{
    public static readonly JsonSerializerSettings DeserializeSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore
    };
}

[Serializable]
public class HexPosition
{
    public int col;
    public int row;

    public HexPosition() { }

    public HexPosition(int col, int row)
    {
        this.col = col;
        this.row = row;
    }
}

/// <summary>Отправка хода (Client → Server): SubmitTurn.</summary>
[Serializable]
public class SubmitTurnPayload
{
    public string battleId;
    public string playerId;
    public int roundIndex;
    /// <summary>Client-local rounds currently loaded in magazine for active weapon.</summary>
    public int currentMagazineRounds;
    public BattleQueuedAction[] actions;
}

[Serializable]
public class BattleQueuedAction
{
    public string actionType;
    public HexPosition targetPosition;
    public string targetUnitId;
    /// <summary>Server <c>body_parts.id</c>; 0 = unspecified.</summary>
    public int bodyPart;
    public string posture;
    /// <summary>Для ChangePosture: поза до смены (клиент, отмена последнего действия).</summary>
    public string previousPosture;
    /// <summary>Для EquipWeapon: код оружия.</summary>
    public string weaponCode;
    /// <summary>Для отмены EquipWeapon на клиенте.</summary>
    public string previousWeaponCode;
    public int previousWeaponAttackApCost;
    /// <summary>Для отмены EquipWeapon: статы до смены (не отправляются на сервер).</summary>
    public int previousWeaponDamage;
    public int previousWeaponRange;
    public int weaponAttackApCost;
    /// <summary>Client-only helper for local undo/ui state.</summary>
    public int previousMagazineRounds;
    public int cost;
}

[Serializable]
public class BattleExecutedAction
{
    public string unitId;
    public string actionType;
    public int tick;
    public bool succeeded;
    public string failureReason;
    public HexPosition fromPosition;
    public HexPosition toPosition;
    public string targetUnitId;
    /// <summary>Server <c>body_parts.id</c>; 0 = unspecified.</summary>
    public int bodyPart;
    public string posture;
    public int damage;
    public bool targetDied;
    /// <summary>Сервер: итоговая вероятность попадания (0…1); null если броска не было (например, только стена).</summary>
    [JsonProperty("hitProbability")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitProbability;
    /// <summary>Сервер: результат броска; null если броска не было.</summary>
    [JsonProperty("hitSucceeded")]
    [JsonConverter(typeof(NullableBoolNewtonsoftConverter))]
    public bool? hitSucceeded;
    [JsonProperty("hitDebugDistance")]
    public int? hitDebugDistance;
    [JsonProperty("hitDebugPDistance")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugPDistance;
    [JsonProperty("hitDebugTreeF")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugTreeF;
    [JsonProperty("hitDebugRockF")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugRockF;
    [JsonProperty("hitDebugCoverMul")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugCoverMul;
    [JsonProperty("hitDebugAccBonus")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugAccBonus;
    [JsonProperty("hitDebugWeaponTightness")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugWeaponTightness;
    [JsonProperty("hitDebugSpreadRaw")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugSpreadRaw;
    [JsonProperty("hitDebugSpread")]
    [JsonConverter(typeof(NullableDoubleNewtonsoftConverter))]
    public double? hitDebugSpread;
    [JsonProperty("hitDebugTargetPosture")]
    public string hitDebugTargetPosture;
    [JsonProperty("hitDebugAnyTree")]
    [JsonConverter(typeof(NullableBoolNewtonsoftConverter))]
    public bool? hitDebugAnyTree;
    [JsonProperty("hitDebugAnyRock")]
    [JsonConverter(typeof(NullableBoolNewtonsoftConverter))]
    public bool? hitDebugAnyRock;
}

/// <summary>Результат хода для одного игрока (часть TurnResult).</summary>
[Serializable]
public class PlayerTurnResult
{
    public string unitId;
    /// <summary>Сервер (JsonStringEnumConverter): "Player" | "Mob". Свойство + JsonConverter надёжнее поля для Newtonsoft.</summary>
    [JsonConverter(typeof(UnitTypeIntOrStringNewtonsoftConverter))]
    public int unitType { get; set; } // 0 = Player, 1 = Mob
    public string playerId;
    public bool accepted;
    public HexPosition finalPosition;
    public HexPosition[] actualPath;
    public int currentAp;
    public float penaltyFraction;
    public int apSpentThisTurn;
    public string rejectedReason;
    public int maxHp;
    public int currentHp;
    public bool isDead;
    public string attackTargetUnitId;
    public int damageDealt;
    public string currentPosture;
    /// <summary>Состояние оружия после раунда (сервер).</summary>
    public string weaponCode;
    public int weaponDamageMin;
    public int weaponDamage;
    public int weaponRange;
    /// <summary>Стоимость атаки (ОД), weapons.attack_ap_cost.</summary>
    public int weaponAttackApCost;
    /// <summary>Rounds currently loaded in magazine after round resolve.</summary>
    public int currentMagazineRounds;
    public BattleExecutedAction[] executedActions;
}

/// <summary>Клиент → сервер: ход по WebSocket.</summary>
[Serializable]
public class WsClientSubmitTurn
{
    public string type;
    public string battleId;
    public string playerId;
    public int roundIndex;
    public int currentMagazineRounds;
    public BattleQueuedAction[] actions;
}

/// <summary>Сервер → клиент: квитанция на submitTurn.</summary>
[Serializable]
public class WsSubmitAckMsg
{
    public string type;
    public bool ok;
    public string error;
    public int expectedRound;
}

/// <summary>Пуш по WebSocket после закрытия раунда (turnResult + новый roundIndex + UTC deadline следующего раунда).</summary>
[Serializable]
public class BattleRoundWsPush
{
    public string type;
    public TurnResultPayload turnResult;
    public int roundIndex;
    public long roundDeadlineUtcMs;
    public string[] turnHistoryIds;
    public int currentTurnPointer;
}

[Serializable]
public class BattleTurnResponsePayload
{
    public string turnId;
    public TurnResultPayload turnResult;
}

/// <summary>Состояние клетки препятствия (сервер: имя enum в JSON, см. <see cref="BattleMapUpdate.newState"/>).</summary>
public enum CellObjectState
{
    None = 0,
    Full = 1,
    Damaged = 2
}

/// <summary>Одно изменение карты за тик (сервер MapUpdateDto, camelCase). newState: сервер отдаёт имя enum (JsonStringEnumConverter).</summary>
[Serializable]
public class BattleMapUpdate
{
    public int tick;
    public int col;
    public int row;
    [JsonConverter(typeof(StringEnumConverter))]
    public CellObjectState newState;
}

/// <summary>Ответ сервера (Server → Client): TurnResult.</summary>
[Serializable]
public class TurnResultPayload
{
    public string battleId;
    public int roundIndex;
    public PlayerTurnResult[] results;
    /// <summary>Сервер: allSubmitted | timerExpired</summary>
    public string roundResolveReason;
    public bool battleFinished;
    /// <summary>Смена состояния стен/препятствий за раунд (урон, разрушение).</summary>
    public BattleMapUpdate[] mapUpdates;
}

/// <summary>Старт раунда (Server → Client): RoundStarted — один раз в начале раунда.</summary>
[Serializable]
public class RoundStartedPayload
{
    public string battleId;
    public int roundIndex;
    public float roundDuration;
}

/// <summary>Участник боя с начальной позицией (часть BattleStarted).</summary>
[Serializable]
public class BattlePlayerInfo
{
    public string playerId;
    public int col;
    public int row;
}

/// <summary>Старт боя после матчмейкинга (Server → Client): BattleStarted. Рассылается каждому участнику при наборе очереди (например, 2 игрока).</summary>
[Serializable]
public class BattleStartedPayload
{
    public string battleId;
    public string playerId;
    public BattlePlayerInfo[] players;
    public float roundDuration;
    public long roundDeadlineUtcMs;
    /// <summary>Спавн для JsonUtility (параллельные массивы): локальный playerId и все прочие entity ids, включая мобов.</summary>
    public string[] spawnPlayerIds;
    public int[] spawnCols;
    public int[] spawnRows;
    public int[] spawnCurrentAps;
    public int[] spawnMaxAps;
    public int[] spawnMaxHps;
    public int[] spawnCurrentHps;
    public string[] spawnCurrentPostures;
    public string[] spawnWeaponCodes;
    public int[] spawnWeaponDamageMins;
    public int[] spawnWeaponDamages;
    public int[] spawnWeaponRanges;
    public int[] spawnWeaponAttackApCosts;
    public int[] spawnCurrentMagazineRounds;
    /// <summary>Ник для планки над головой (параллельно spawnPlayerIds).</summary>
    public string[] spawnDisplayNames;
    /// <summary>Уровень персонажа (параллельно spawnPlayerIds).</summary>
    public int[] spawnLevels;
    public int[] obstacleCols;
    public int[] obstacleRows;
    /// <summary>wall | tree | rock — параллельно obstacleCols/Rows.</summary>
    public string[] obstacleTags;
    /// <summary>Yaw стен (градусы вокруг Y), параллельно obstacleCols/Rows.</summary>
    public float[] obstacleWallYaws;
}

/// <summary>POST /api/battle/.../equip-weapon (клиент).</summary>
/// <summary>Ответ POST /api/db/user/inventory.</summary>
[Serializable]
public class UserInventorySlotsPayload
{
    public UserInventorySlotPayload[] slots;
}

[Serializable]
public class UserAmmoPackPayload
{
    public long id;
    public long itemId;
    public long ammoTypeId;
    public string caliber;
    public string name;
    public double unitWeight;
    public int quality;
    public int condition;
    public string iconKey;
    public int inventoryGrid;
    public int roundsCount;
    public int packsCount;
    public int totalRounds;
}

[Serializable]
public class WeaponDbRowPayload
{
    public long id;
    public string code;
    public string name;
    public string caliber;
    public int magazineSize;
    public int reloadApCost;
    public int inventoryGrid;
}

[Serializable]
public class UserInventorySlotPayload
{
    public int slotIndex;
    /// <summary>Сервер может отдавать null для пустой ячейки — JsonUtility это ломает; для парсинга используйте Newtonsoft.</summary>
    public long? weaponId;
    public string weaponCode;
    public string weaponName;
    public int damage;
    public int range;
    public string iconKey;
    /// <summary>Стоимость атаки этим оружием (ОД), из weapons.attack_ap_cost.</summary>
    public int attackApCost;
    /// <summary>Primary cell: span 1 or 2; continuation cells use 0.</summary>
    public int slotSpan;
    /// <summary>Currently equipped (in hands); primary cell only.</summary>
    public bool equipped;
    /// <summary>Second cell of a 2-slot weapon; not clickable.</summary>
    public bool continuation;
    public bool stackable;
    public int quantity;
    public int chamberRounds;
}

/// <summary>Newtonsoft + Unity: надёжный разбор <c>double?</c> из Integer/Float/String.</summary>
public sealed class NullableDoubleNewtonsoftConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) =>
        objectType == typeof(double?) || objectType == typeof(double);

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
            case JsonToken.Undefined:
                return null;
            case JsonToken.Float:
            case JsonToken.Integer:
                return Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
            case JsonToken.String:
                if (reader.Value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
                return null;
            default:
                return null;
        }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        if (value is double d)
            writer.WriteValue(d);
        else
            writer.WriteNull();
    }
}

/// <summary>Newtonsoft + Unity: надёжный разбор <c>bool?</c> из Bool/Integer/String.</summary>
public sealed class NullableBoolNewtonsoftConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) =>
        objectType == typeof(bool?) || objectType == typeof(bool);

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
            case JsonToken.Undefined:
                return null;
            case JsonToken.Boolean:
                return (bool)reader.Value;
            case JsonToken.Integer:
                return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture) != 0;
            case JsonToken.String:
                if (reader.Value is string s)
                {
                    if (bool.TryParse(s, out bool b))
                        return b;
                    if (s == "1") return true;
                    if (s == "0") return false;
                }

                return null;
            default:
                return null;
        }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        if (value is bool b)
            writer.WriteValue(b);
        else
            writer.WriteNull();
    }
}

/// <summary>Newtonsoft: сервер отдаёт <c>unitType</c> как "Player"/"Mob", не как 0/1.</summary>
public sealed class UnitTypeIntOrStringNewtonsoftConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(int);

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Integer)
            return Convert.ToInt32(reader.Value);
        if (reader.TokenType == JsonToken.String)
        {
            var s = (reader.Value as string)?.Trim();
            if (string.IsNullOrEmpty(s)) return 0;
            if (string.Equals(s, "Mob", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(s, "Player", StringComparison.OrdinalIgnoreCase)) return 0;
            if (int.TryParse(s, out var i)) return i;
        }
        if (reader.TokenType == JsonToken.Null)
            return 0;
        return 0;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }
}
