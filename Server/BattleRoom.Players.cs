using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    public void AddPlayer(string playerId, int col, int row)
    {
        Players[playerId] = (col, row);
        if (!ParticipantIds.Contains(playerId))
            ParticipantIds.Add(playerId);
    }

    public void SetPlayerDisplayInfo(string playerId, string displayName, int level)
    {
        if (string.IsNullOrWhiteSpace(playerId) || !Players.ContainsKey(playerId))
            return;
        PlayerDisplayNames[playerId] = string.IsNullOrWhiteSpace(displayName) ? playerId : displayName.Trim();
        PlayerLevels[playerId] = Math.Max(1, level);
    }

    public void SetPlayerCombatProfile(string playerId, int maxHp, int maxAp, string weaponCode, int weaponDamage, int weaponRange, int weaponAttackApCost, int accuracy, double weaponSpreadPenalty = 0, int weaponTrajectoryHeight = 1)
    {
        PlayerCombatProfiles[playerId] = (
            Math.Max(1, maxHp),
            Math.Max(1, maxAp),
            string.IsNullOrWhiteSpace(weaponCode) ? DefaultWeaponCode : weaponCode,
            Math.Max(0, weaponDamage),
            Math.Max(0, weaponRange),
            Math.Max(1, weaponAttackApCost),
            Math.Max(0, accuracy),
            Math.Clamp(weaponSpreadPenalty, 0.0, 1.0),
            Math.Clamp(weaponTrajectoryHeight, 0, 3));
    }

    /// <summary>Смена оружия вне очереди хода (до отправки хода в текущем раунде). Статы берутся из БД оружия на сервере.</summary>
    public bool TryEquipWeapon(string playerId, string weaponCode, int weaponDamage, int weaponRange, int weaponAttackApCost, double weaponSpreadPenalty, int weaponTrajectoryHeight, out string? failureReason)
    {
        failureReason = null;
        EnsureUnitsInitialized();
        if (string.IsNullOrWhiteSpace(playerId) || !Players.ContainsKey(playerId))
        {
            failureReason = "player_not_in_battle";
            return false;
        }

        if (Submissions.ContainsKey(playerId))
        {
            failureReason = "already_submitted";
            return false;
        }

        if (!PlayerToUnitId.TryGetValue(playerId, out var unitId) || !Units.TryGetValue(unitId, out var unit))
        {
            failureReason = "no_unit";
            return false;
        }

        if (unit.UnitType != UnitType.Player)
        {
            failureReason = "not_player_unit";
            return false;
        }

        string code = string.IsNullOrWhiteSpace(weaponCode) ? DefaultWeaponCode : weaponCode.Trim().ToLowerInvariant();
        unit.WeaponCode = code;
        unit.WeaponDamage = Math.Max(0, weaponDamage);
        unit.WeaponRange = Math.Max(0, weaponRange);
        unit.WeaponAttackApCost = Math.Max(1, weaponAttackApCost);
        unit.WeaponSpreadPenalty = Math.Clamp(weaponSpreadPenalty, 0.0, 1.0);
        unit.WeaponTrajectoryHeight = Math.Clamp(weaponTrajectoryHeight, 0, 3);
        Units[unitId] = unit;

        if (PlayerCombatProfiles.TryGetValue(playerId, out var prof))
            PlayerCombatProfiles[playerId] = (prof.Item1, prof.Item2, code, unit.WeaponDamage, unit.WeaponRange, unit.WeaponAttackApCost, prof.Item7, unit.WeaponSpreadPenalty, unit.WeaponTrajectoryHeight);
        else
            PlayerCombatProfiles[playerId] = (DefaultPlayerMaxHp, MaxAp, code, unit.WeaponDamage, unit.WeaponRange, unit.WeaponAttackApCost, 10, unit.WeaponSpreadPenalty, unit.WeaponTrajectoryHeight);

        return true;
    }
}
