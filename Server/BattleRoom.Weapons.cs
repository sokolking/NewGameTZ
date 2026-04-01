using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private int GetWeaponAttackApCostFromLegacyKey(string itemKey)
    {
        if (_weaponDb != null && _weaponDb.TryGetWeaponByKey(itemKey, out var w))
            return Math.Max(1, w.AttackApCost);
        return 1;
    }

    private int GetWeaponMagazineSizeFromLegacyKey(string itemKey)
    {
        if (_weaponDb != null && _weaponDb.TryGetWeaponByKey(itemKey, out var w))
            return Math.Max(0, w.MagazineSize);
        return 0;
    }

    private string GetWeaponCategoryFromLegacyKey(string itemKey)
    {
        if (_weaponDb != null && _weaponDb.TryGetWeaponByKey(itemKey, out var w))
            return (w.Category ?? string.Empty).Trim().ToLowerInvariant();
        return "";
    }

    private long GetWeaponItemIdFromLegacyKey(string itemKey)
    {
        if (_weaponDb != null && _weaponDb.TryGetWeaponByKey(itemKey, out var w))
            return w.Id;
        return 0;
    }

    private int GetWeaponAttackApCostFromDbByItemId(long weaponItemId)
    {
        if (weaponItemId > 0 && _weaponDb != null && _weaponDb.TryGetWeaponByItemId(weaponItemId, out var w))
            return Math.Max(1, w.AttackApCost);
        if (weaponItemId > 0 && _medicineDb != null && _medicineDb.TryGetMedicineByItemId(weaponItemId, out var m))
            return Math.Max(1, m.AttackApCost);
        return 1;
    }

    private int GetWeaponMagazineSizeFromDbByItemId(long weaponItemId)
    {
        if (weaponItemId > 0 && _weaponDb != null && _weaponDb.TryGetWeaponByItemId(weaponItemId, out var w))
            return Math.Max(0, w.MagazineSize);
        if (weaponItemId > 0 && _medicineDb != null && _medicineDb.TryGetMedicineByItemId(weaponItemId, out _))
            return 0;
        return 0;
    }

    private string GetWeaponCategoryFromDbByItemId(long weaponItemId)
    {
        if (weaponItemId > 0 && _weaponDb != null && _weaponDb.TryGetWeaponByItemId(weaponItemId, out var w))
            return (w.Category ?? string.Empty).Trim().ToLowerInvariant();
        if (weaponItemId > 0 && _medicineDb != null && _medicineDb.TryGetMedicineByItemId(weaponItemId, out _))
            return "medicine";
        return "";
    }
}
