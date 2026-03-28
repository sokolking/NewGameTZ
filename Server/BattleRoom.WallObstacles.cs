using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    /// <summary>Полная стена при HP ≥ половины от max; повреждённая — при HP строго меньше половины (но &gt; 0). Сохраняет wall_low / wall.</summary>
    private void SetWallTagFromRemainingHp((int col, int row) wc, int hpRemaining, int wallMaxHp, bool isLowWall)
    {
        if (hpRemaining <= 0)
            return;
        int maxHp = Math.Max(1, wallMaxHp);
        bool belowHalf = hpRemaining * 2 < maxHp;
        string tag = isLowWall
            ? (belowHalf ? "damaged_wall_low" : "wall_low")
            : (belowHalf ? "damaged_wall" : "wall");
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
        bool isLowWall = oldTag == "wall_low" || oldTag == "damaged_wall_low";

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
        SetWallTagFromRemainingHp(wc, hp, bal.WallMaxHp, isLowWall);
        _obstacleTags.TryGetValue(wc, out var newTag);
        var newState = newTag == "damaged_wall" ? CellObjectState.Damaged : CellObjectState.Full;
        mapUpdates.Add(new MapUpdateDto { Tick = tick, Col = wc.col, Row = wc.row, NewState = newState });
    }
}
