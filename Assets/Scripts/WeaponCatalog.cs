using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Соглашения по оружию на клиенте: код и icon_key приходят с сервера (БД).
/// Статы урона/дальности здесь не задаются — только из инвентаря или TurnResult.
/// Спрайты: <see cref="LoadSpriteFromWeaponIconsFolder"/> → Resources/WeaponIcons/{ключ}.
/// </summary>
public static class WeaponCatalog
{
    /// <summary>Код оружия по умолчанию, если строка пустая (как на сервере DefaultWeaponCode).</summary>
    public const string DefaultWeaponCode = "fist";

    /// <summary>Стоимость смены оружия в очереди хода (совпадает с сервером).</summary>
    public const int EquipWeaponSwapApCost = 2;

    /// <summary>Нормализованный код из БД (нижний регистр); пусто → <see cref="DefaultWeaponCode"/>.</summary>
    public static string NormalizeWeaponCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return DefaultWeaponCode;
        return code.Trim().ToLowerInvariant();
    }

    /// <summary>Server <c>weapons.category = cold</c> (and similar melee). Extend when adding new cold weapons.</summary>
    private static readonly HashSet<string> ColdWeaponCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fist",
        "knife"
    };

    /// <summary>Pistol/revolver codes — same locomotion clips as <c>light</c> category from DB.</summary>
    public static bool IsPistolStyleWeapon(string code)
    {
        string n = NormalizeWeaponCode(code);
        return n == "gun" || n == "revolver";
    }

    /// <summary>
    /// Idle/walk/run/sit rifle clips on <see cref="PlayerCharacterAnimator"/> when server reports <c>weapons.category = medium</c>.
    /// Takes precedence over <see cref="UsesPistolLocomotionClips"/> (e.g. same code must not mix sets).
    /// </summary>
    public static bool UsesRifleLocomotionClips(string weaponCode, string weaponCategoryOrEmpty)
    {
        if (!string.IsNullOrWhiteSpace(weaponCategoryOrEmpty)
            && string.Equals(weaponCategoryOrEmpty.Trim(), "medium", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Idle/walk/run/sit pistol clips on <see cref="PlayerCharacterAnimator"/> (user_T_model slots).
    /// True for legacy pistol codes or when server reports <c>weapons.category = light</c>.
    /// </summary>
    public static bool UsesPistolLocomotionClips(string weaponCode, string weaponCategoryOrEmpty)
    {
        if (UsesRifleLocomotionClips(weaponCode, weaponCategoryOrEmpty))
            return false;
        if (IsPistolStyleWeapon(weaponCode))
            return true;
        if (!string.IsNullOrWhiteSpace(weaponCategoryOrEmpty)
            && string.Equals(weaponCategoryOrEmpty.Trim(), "light", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>Cold/melee stance idle and melee attack clips (standing). Mutually exclusive with pistol locomotion.</summary>
    public static bool IsColdWeapon(string code)
    {
        if (IsPistolStyleWeapon(code))
            return false;
        return ColdWeaponCodes.Contains(NormalizeWeaponCode(code));
    }

    /// <summary>Загрузка спрайта из Resources/WeaponIcons/{iconKeyOrCode}. Ключ — code или icon_key из БД.</summary>
    public static Sprite LoadSpriteFromWeaponIconsFolder(string iconKeyOrCode)
    {
        if (string.IsNullOrWhiteSpace(iconKeyOrCode))
            return null;
        string k = iconKeyOrCode.Trim().ToLowerInvariant();
        string path = $"WeaponIcons/{k}";
        var s = Resources.Load<Sprite>(path);
        if (s != null)
            return s;
        var tex = Resources.Load<Texture2D>(path);
        if (tex == null)
            return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>Панель активного оружия: пустой код трактуется как <see cref="DefaultWeaponCode"/>.</summary>
    public static Sprite LoadSpriteForEquippedWeaponPanel(string weaponCodeOrEmpty)
    {
        string key = string.IsNullOrWhiteSpace(weaponCodeOrEmpty) ? DefaultWeaponCode : weaponCodeOrEmpty.Trim().ToLowerInvariant();
        return LoadSpriteFromWeaponIconsFolder(key);
    }
}
