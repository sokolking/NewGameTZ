using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private int GetWeaponAttackApCostFromDb(string weaponCode)
    {
        if (_weaponDb != null && _weaponDb.TryGetWeaponByCode(weaponCode, out var w))
            return Math.Max(1, w.AttackApCost);
        return 1;
    }

    private int GetWeaponMagazineSizeFromDb(string weaponCode)
    {
        if (_weaponDb != null && _weaponDb.TryGetWeaponByCode(weaponCode, out var w))
            return Math.Max(0, w.MagazineSize);
        return 0;
    }

    private string GetWeaponCategoryFromDb(string weaponCode)
    {
        if (_weaponDb != null && _weaponDb.TryGetWeaponByCode(weaponCode, out var w))
            return (w.Category ?? string.Empty).Trim().ToLowerInvariant();
        return "";
    }
}
