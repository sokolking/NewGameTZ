/// <summary>
/// Локальные статы оружия (дублируют строки в БД сервера) — для офлайна и подсказки клиенту.
/// </summary>
public static class WeaponCatalog
{
    public const string FistCode = "fist";
    public const string StoneCode = "stone";

    public static void GetStats(string code, out string normalizedCode, out int damage, out int range)
    {
        if (string.Equals(code, StoneCode, System.StringComparison.OrdinalIgnoreCase))
        {
            normalizedCode = StoneCode;
            damage = 3;
            range = 2;
            return;
        }

        normalizedCode = FistCode;
        damage = 1;
        range = 1;
    }

    /// <summary>Стоимость смены оружия в очереди хода (фиксированная, совпадает с сервером).</summary>
    public const int EquipWeaponSwapApCost = 2;
}
