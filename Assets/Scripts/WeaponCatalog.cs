using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Соглашения по оружию на клиенте: icon_key приходит с сервера (БД).
/// Статы урона/дальности здесь не задаются — только из инвентаря или TurnResult.
/// Спрайты: <see cref="LoadSpriteFromWeaponIconsFolder"/> → Resources/WeaponIcons/{ключ}.
/// </summary>
public static class WeaponCatalog
{
    /// <summary>Стоимость смены оружия в очереди хода (совпадает с сервером).</summary>
    public const int EquipWeaponSwapApCost = 2;

    /// <summary>Загрузка спрайта из Resources/WeaponIcons/{iconKeyOrItem}. Ключ — item key или icon_key из БД.</summary>
    public static Sprite LoadSpriteFromWeaponIconsFolder(string iconKeyOrItem)
    {
        if (string.IsNullOrWhiteSpace(iconKeyOrItem))
            return null;
        string k = iconKeyOrItem.Trim().ToLowerInvariant();
        string path = $"WeaponIcons/{k}";
        var s = Resources.Load<Sprite>(path);
        if (s != null)
            return s;
        var tex = Resources.Load<Texture2D>(path);
        if (tex == null)
            return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

}
