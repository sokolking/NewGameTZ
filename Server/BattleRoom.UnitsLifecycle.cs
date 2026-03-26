using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
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
                : (DefaultPlayerMaxHp, DefaultPlayerMaxAp, DefaultWeaponCode, DefaultWeaponDamage, DefaultWeaponDamage, DefaultWeaponRange, GetWeaponAttackApCostFromDb(DefaultWeaponCode), 10, 0.0, 1, false);
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
                WeaponDamageMin = profile.Item4,
                WeaponDamage = profile.Item5,
                WeaponRange = profile.Item6,
                WeaponAttackApCost = Math.Max(1, profile.Item7),
                Accuracy = profile.Item8,
                WeaponSpreadPenalty = profile.Item9,
                WeaponTrajectoryHeight = profile.Item10,
                WeaponIsSniper = profile.Item11,
                Posture = PostureWalk
            };
        }

        // Серверный моб есть только в одиночном бою.
        if (IsSolo && Players.TryGetValue("P1", out var p1Pos))
        {
            int mobCol;
            int mobRow;
            int mobMaxHp = DefaultMobMaxHp;
            int mobCurHp = DefaultMobMaxHp;

            if (DebugSoloMobFiveHexNoChase1000Hp)
            {
                mobMaxHp = DebugSoloMobHp;
                mobCurHp = DebugSoloMobHp;
                if (!HexSpawn.TryFindHexAtExactDistance(
                        p1Pos.col,
                        p1Pos.row,
                        HexSpawn.DefaultGridWidth,
                        HexSpawn.DefaultGridLength,
                        DebugSoloMobHexDistanceFromPlayer,
                        out mobCol,
                        out mobRow))
                {
                    (mobCol, mobRow) = HexSpawn.FindOpponentSpawn(
                        p1Pos.col,
                        p1Pos.row,
                        HexSpawn.DefaultGridWidth,
                        HexSpawn.DefaultGridLength,
                        HexSpawn.MinSpawnHexDistance);
                }
            }
            else
            {
                (mobCol, mobRow) = HexSpawn.FindOpponentSpawn(
                    p1Pos.col,
                    p1Pos.row,
                    HexSpawn.DefaultGridWidth,
                    HexSpawn.DefaultGridLength,
                    HexSpawn.MinSpawnHexDistance);
            }

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
                    MaxHp = mobMaxHp,
                    CurrentHp = mobCurHp,
                    WeaponCode = DefaultWeaponCode,
                    WeaponDamageMin = DefaultWeaponDamage,
                    WeaponDamage = DefaultWeaponDamage,
                    WeaponRange = DefaultWeaponRange,
                    WeaponAttackApCost = GetWeaponAttackApCostFromDb(DefaultWeaponCode),
                    Accuracy = 10,
                    WeaponSpreadPenalty = 0,
                    WeaponTrajectoryHeight = 1,
                    WeaponIsSniper = false,
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
}
