using System;

/// <summary>
/// Модели данных для обмена клиент–сервер по плану ServerSyncPlan.
/// Сериализация в JSON (Unity JsonUtility или Newtonsoft) — использовать публичные поля или [SerializeField].
/// </summary>

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
    public HexPosition[] path;
    public int apSpentThisTurn;
    public int stepsTakenThisTurn;
}

/// <summary>Результат хода для одного игрока (часть TurnResult).</summary>
[Serializable]
public class PlayerTurnResult
{
    public string playerId;
    public bool accepted;
    public HexPosition finalPosition;
    public HexPosition[] actualPath;
    public int currentAp;
    public float penaltyFraction;
    public int apSpentThisTurn;
    public string rejectedReason;
}

/// <summary>Ответ сервера (Server → Client): TurnResult.</summary>
[Serializable]
public class TurnResultPayload
{
    public string battleId;
    public int roundIndex;
    public PlayerTurnResult[] results;
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
}
