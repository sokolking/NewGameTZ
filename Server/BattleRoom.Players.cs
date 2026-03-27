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

    public void SetPlayerCurrentHpOverride(string playerId, int currentHp)
    {
        if (string.IsNullOrWhiteSpace(playerId) || !Players.ContainsKey(playerId))
            return;
        PlayerCurrentHpOverrides[playerId] = Math.Max(0, currentHp);
    }

    /// <param name="weaponTightness">Кучность оружия 0…1 (выше — кучнее); в профиле хранится то же <c>T</c>.</param>
    public void SetPlayerCombatProfile(string playerId, int maxHp, int maxAp, string weaponCode, int weaponDamageMin, int weaponDamageMax, int weaponRange, int weaponAttackApCost, int accuracy, double weaponTightness = 1, int weaponTrajectoryHeight = 1, bool weaponIsSniper = false)
    {
        int dMin = Math.Max(0, weaponDamageMin);
        int dMax = Math.Max(0, weaponDamageMax);
        if (dMin > dMax)
            (dMin, dMax) = (dMax, dMin);
        double t = Math.Clamp(weaponTightness, 0.0, 1.0);
        PlayerCombatProfiles[playerId] = (
            Math.Max(1, maxHp),
            Math.Max(1, maxAp),
            string.IsNullOrWhiteSpace(weaponCode) ? DefaultWeaponCode : weaponCode,
            dMin,
            dMax,
            Math.Max(0, weaponRange),
            Math.Max(1, weaponAttackApCost),
            Math.Max(0, accuracy),
            t,
            Math.Clamp(weaponTrajectoryHeight, 0, 3),
            weaponIsSniper);
    }

    /// <summary>Смена оружия вне очереди хода (до отправки хода в текущем раунде). Статы берутся из БД оружия на сервере.</summary>
    public bool TryEquipWeapon(string playerId, string weaponCode, int weaponDamageMin, int weaponDamageMax, int weaponRange, int weaponAttackApCost, double weaponTightness, int weaponTrajectoryHeight, bool weaponIsSniper, out string? failureReason)
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
        int dMin = Math.Max(0, weaponDamageMin);
        int dMax = Math.Max(0, weaponDamageMax);
        if (dMin > dMax)
            (dMin, dMax) = (dMax, dMin);
        unit.WeaponDamageMin = dMin;
        unit.WeaponDamage = dMax;
        unit.WeaponRange = Math.Max(0, weaponRange);
        unit.WeaponAttackApCost = Math.Max(1, weaponAttackApCost);
        unit.CurrentMagazineRounds = GetWeaponMagazineSizeFromDb(code);
        unit.WeaponTightness = Math.Clamp(weaponTightness, 0.0, 1.0);
        unit.WeaponTrajectoryHeight = Math.Clamp(weaponTrajectoryHeight, 0, 3);
        unit.WeaponIsSniper = weaponIsSniper;
        Units[unitId] = unit;

        if (PlayerCombatProfiles.TryGetValue(playerId, out var prof))
            PlayerCombatProfiles[playerId] = (prof.Item1, prof.Item2, code, unit.WeaponDamageMin, unit.WeaponDamage, unit.WeaponRange, unit.WeaponAttackApCost, prof.Item7, unit.WeaponTightness, unit.WeaponTrajectoryHeight, unit.WeaponIsSniper);
        else
            PlayerCombatProfiles[playerId] = (DefaultPlayerMaxHp, DefaultPlayerMaxAp, code, unit.WeaponDamageMin, unit.WeaponDamage, unit.WeaponRange, unit.WeaponAttackApCost, 10, unit.WeaponTightness, unit.WeaponTrajectoryHeight, unit.WeaponIsSniper);

        return true;
    }
}
