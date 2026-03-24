using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private static bool IsWallObstacleTag(string? tag) =>
        tag == "wall" || tag == "damaged_wall";

    /// <summary>Полная стена при HP ≥ половины от max; повреждённая (damaged_wall) — при HP строго меньше половины (но &gt; 0).</summary>
    private void SetWallTagFromRemainingHp((int col, int row) wc, int hpRemaining, int wallMaxHp)
    {
        if (hpRemaining <= 0)
            return;
        int maxHp = Math.Max(1, wallMaxHp);
        bool belowHalf = hpRemaining * 2 < maxHp;
        string tag = belowHalf ? "damaged_wall" : "wall";
        _obstacleTags[wc] = tag;
    }

    /// <summary>Урон по стене; каждое попадание добавляет запись в mapUpdates (клиент: VFX пули и состояние стены).</summary>
    private void ApplyWallDamageAndRecord(
        int tick,
        (int col, int row) wc,
        int rawDamage,
        BattleObstacleBalanceRowDto bal,
        List<MapUpdateDto> mapUpdates)
    {
        if (!_obstacleTags.TryGetValue(wc, out var oldTag) || !IsWallObstacleTag(oldTag))
            return;

        int hp = _wallHpRemaining.GetValueOrDefault(wc, bal.WallMaxHp);
        hp -= Math.Max(0, rawDamage);

        if (hp <= 0)
        {
            _obstacleTags.Remove(wc);
            _wallHpRemaining.Remove(wc);
            _wallYawDegrees.Remove(wc);
            mapUpdates.Add(new MapUpdateDto { Tick = tick, Col = wc.col, Row = wc.row, NewState = CellObjectState.None });
            return;
        }

        _wallHpRemaining[wc] = hp;
        SetWallTagFromRemainingHp(wc, hp, bal.WallMaxHp);
        _obstacleTags.TryGetValue(wc, out var newTag);
        var newState = newTag == "damaged_wall" ? CellObjectState.Damaged : CellObjectState.Full;
        mapUpdates.Add(new MapUpdateDto { Tick = tick, Col = wc.col, Row = wc.row, NewState = newState });
    }
}
