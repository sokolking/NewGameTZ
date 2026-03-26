using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
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
                    MaxAp = DefaultPlayerMaxAp,
                    CurrentAp = GetUnitRoundStartAp(UnitType.Player, 0f),
                    PenaltyFraction = 0f,
                    MaxHp = DefaultPlayerMaxHp,
                    CurrentHp = DefaultPlayerMaxHp,
                    WeaponCode = DefaultWeaponCode,
                    WeaponDamageMin = DefaultWeaponDamage,
                    WeaponDamage = DefaultWeaponDamage,
                    WeaponRange = DefaultWeaponRange,
                    WeaponAttackApCost = GetWeaponAttackApCostFromDb(DefaultWeaponCode),
                    Accuracy = 10,
                    WeaponTightness = 1.0,
                    WeaponTrajectoryHeight = 1,
                    WeaponIsSniper = false,
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
}
