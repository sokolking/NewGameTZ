using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сессия боя: отправка хода на сервер и применение результата (по плану ServerSyncPlan).
/// Этап 1: только заглушки — формирование payload и логирование; ApplyTurnResult — заглушка.
/// </summary>
public class GameSession : MonoBehaviour
{
    [Header("Режим и идентификаторы")]
    [Tooltip("Включить отправку хода на сервер при завершении хода (пока — только логирование payload).")]
    [SerializeField] private bool _isOnlineMode;
    [SerializeField] private string _battleId = "battle-1";
    [SerializeField] private string _playerId = "P1";

    /// <summary>Включён ли онлайн-режим (отправка хода при завершении).</summary>
    public bool IsOnlineMode => _isOnlineMode;

    /// <summary>
    /// Собрать данные хода и отправить (этап 1 — только логируем payload).
    /// Вызывать до применения EndTurn у Player.
    /// </summary>
    public void SubmitTurnLocal(List<(int col, int row)> path, int apSpentThisTurn, int stepsTakenThisTurn, int roundIndex)
    {
        var payload = BuildSubmitPayload(path, apSpentThisTurn, stepsTakenThisTurn, roundIndex);
        string json = JsonUtility.ToJson(payload, prettyPrint: true);
        Debug.Log($"[GameSession] SubmitTurn (local stub):\n{json}");
    }

    /// <summary>Применить результат хода с сервера (этап 1 — заглушка, только лог).</summary>
    public void ApplyTurnResult(TurnResultPayload result)
    {
        if (result == null) return;
        Debug.Log($"[GameSession] ApplyTurnResult stub: battleId={result.battleId}, roundIndex={result.roundIndex}, players={result.results?.Length ?? 0}");
    }

    /// <summary>Применить старт боя после матчмейкинга (этап 1 — заглушка: сохраняем battleId и playerId для последующих SubmitTurn).</summary>
    public void ApplyBattleStarted(BattleStartedPayload payload)
    {
        if (payload == null) return;
        _battleId = payload.battleId ?? _battleId;
        _playerId = payload.playerId ?? _playerId;
        Debug.Log($"[GameSession] ApplyBattleStarted stub: battleId={_battleId}, playerId={_playerId}, players={payload.players?.Length ?? 0}");
    }

    private SubmitTurnPayload BuildSubmitPayload(List<(int col, int row)> path, int apSpentThisTurn, int stepsTakenThisTurn, int roundIndex)
    {
        var pathArr = new HexPosition[path?.Count ?? 0];
        if (path != null)
        {
            for (int i = 0; i < path.Count; i++)
                pathArr[i] = new HexPosition(path[i].col, path[i].row);
        }

        return new SubmitTurnPayload
        {
            battleId = _battleId,
            playerId = _playerId,
            roundIndex = roundIndex,
            path = pathArr,
            apSpentThisTurn = apSpentThisTurn,
            stepsTakenThisTurn = stepsTakenThisTurn
        };
    }
}
