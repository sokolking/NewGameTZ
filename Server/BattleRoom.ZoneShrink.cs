using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private void EnsureActiveZoneInitialized()
    {
        if (_activeZoneInitialized)
            return;
        _zoneShrinkSettings = _zoneShrinkDb?.GetSettings() ?? BattleZoneShrinkRowDto.Defaults;
        _activeMinCol = 0;
        _activeMaxCol = HexSpawn.DefaultGridWidth - 1;
        _activeMinRow = 0;
        _activeMaxRow = HexSpawn.DefaultGridLength - 1;
        _activeZoneInitialized = true;
    }

    /// <summary>После симуляции раунда: сужение зоны по расписанию, удаление препятствий, перенос юнитов.</summary>
    /// <returns>Удалённые клетки или null, если сужения не было.</returns>
    private HexPositionDto[]? ApplyZoneShrinkIfNeeded(int resolvedRoundIndex, List<MapUpdateDto> mapUpdates, List<PlayerTurnResultDto> results)
    {
        EnsureActiveZoneInitialized();
        var cfg = _zoneShrinkSettings;
        int round1Based = resolvedRoundIndex + 1;
        if (round1Based < cfg.ShrinkStartRound)
            return null;

        bool doHorizontal = (round1Based - cfg.ShrinkStartRound) % cfg.HorizontalShrinkInterval == 0;
        bool doVertical = (round1Based - cfg.ShrinkStartRound) % cfg.VerticalShrinkInterval == 0;
        if (!doHorizontal && !doVertical)
            return null;

        int oldMinC = _activeMinCol, oldMaxC = _activeMaxCol, oldMinR = _activeMinRow, oldMaxR = _activeMaxRow;

        int newMinC = oldMinC, newMaxC = oldMaxC, newMinR = oldMinR, newMaxR = oldMaxR;

        GetPlayerZoneShrinkSideProtection(oldMinC, oldMaxC, oldMinR, oldMaxR,
            out bool protectHorizLeft, out bool protectHorizRight, out bool protectVertTop, out bool protectVertBottom);

        if (doHorizontal && cfg.HorizontalShrinkAmount > 0)
        {
            int w = newMaxC - newMinC + 1;
            int minW = Math.Max(1, cfg.MinWidth);
            int targetW = Math.Max(minW, w - 2 * cfg.HorizontalShrinkAmount);
            int removeTotal = w - targetW;
            int leftRemove = removeTotal / 2;
            int rightRemove = removeTotal - leftRemove;
            if (protectHorizLeft && protectHorizRight)
            {
                leftRemove = 0;
                rightRemove = 0;
            }
            else if (protectHorizLeft)
            {
                leftRemove = 0;
                rightRemove = removeTotal;
            }
            else if (protectHorizRight)
            {
                rightRemove = 0;
                leftRemove = removeTotal;
            }

            newMinC += leftRemove;
            newMaxC -= rightRemove;
        }

        if (doVertical && cfg.VerticalShrinkAmount > 0)
        {
            int h = newMaxR - newMinR + 1;
            int minH = Math.Max(1, cfg.MinHeight);
            int targetH = Math.Max(minH, h - 2 * cfg.VerticalShrinkAmount);
            int removeTotal = h - targetH;
            int topRemove = removeTotal / 2;
            int bottomRemove = removeTotal - topRemove;
            if (protectVertTop && protectVertBottom)
            {
                topRemove = 0;
                bottomRemove = 0;
            }
            else if (protectVertTop)
            {
                topRemove = 0;
                bottomRemove = removeTotal;
            }
            else if (protectVertBottom)
            {
                bottomRemove = 0;
                topRemove = removeTotal;
            }

            newMinR += topRemove;
            newMaxR -= bottomRemove;
        }

        if (newMinC == oldMinC && newMaxC == oldMaxC && newMinR == oldMinR && newMaxR == oldMaxR)
            return null;

        var removedCells = new List<(int col, int row)>();
        for (int c = oldMinC; c <= oldMaxC; c++)
        {
            for (int r = oldMinR; r <= oldMaxR; r++)
            {
                if (c < newMinC || c > newMaxC || r < newMinR || r > newMaxR)
                    removedCells.Add((c, r));
            }
        }

        foreach (var cell in removedCells)
        {
            if (_obstacleTags.TryGetValue(cell, out var tag) && IsWallObstacleTag(tag))
                mapUpdates.Add(new MapUpdateDto { Tick = ZoneShrinkMapTick, Col = cell.col, Row = cell.row, NewState = CellObjectState.None });
            _obstacleTags.Remove(cell);
            _wallHpRemaining.Remove(cell);
            _wallYawDegrees.Remove(cell);
        }

        _activeMinCol = newMinC;
        _activeMaxCol = newMaxC;
        _activeMinRow = newMinR;
        _activeMaxRow = newMaxR;

        var occupied = new HashSet<(int col, int row)>();
        foreach (var u in Units.Values)
        {
            if (u.CurrentHp > 0)
                occupied.Add((u.Col, u.Row));
        }

        foreach (var uid in Units.Keys.ToList())
        {
            if (!Units.TryGetValue(uid, out var unit) || unit.CurrentHp <= 0)
                continue;
            int uc = unit.Col, ur = unit.Row;
            if (uc >= newMinC && uc <= newMaxC && ur >= newMinR && ur <= newMaxR)
                continue;

            occupied.Remove((uc, ur));
            if (!TryFindNearestFreeHexInRectangle(uc, ur, newMinC, newMaxC, newMinR, newMaxR, occupied, out int nc, out int nr))
            {
                unit.CurrentHp = 0;
                Units[uid] = unit;
                continue;
            }

            unit.Col = nc;
            unit.Row = nr;
            Units[uid] = unit;
            occupied.Add((nc, nr));

            string? playerId = unit.UnitType == UnitType.Player
                ? PlayerToUnitId.FirstOrDefault(kv => kv.Value == uid).Key
                : null;
            if (playerId != null && Players.ContainsKey(playerId))
                Players[playerId] = (nc, nr);
            if (playerId != null && CurrentState.TryGetValue(playerId, out var st))
            {
                st.Col = nc;
                st.Row = nr;
                CurrentState[playerId] = st;
            }

            var dto = results.FirstOrDefault(r => r.UnitId == uid);
            if (dto != null)
            {
                dto.FinalPosition = new HexPositionDto { Col = nc, Row = nr };
                // Keep ActualPath as combat resolution only; client teleports to FinalPosition after turn animations, then applies zone shrink.
            }
        }

        var deadAfterShrink = Units.Where(kv => kv.Value.CurrentHp <= 0).Select(kv => kv.Key).ToList();
        foreach (var deadId in deadAfterShrink)
        {
            Units.Remove(deadId);
            UnitCommands.Remove(deadId);
            foreach (var kv in PlayerToUnitId.Where(kv => kv.Value == deadId).ToList())
                PlayerToUnitId.Remove(kv.Key);
            var dto = results.FirstOrDefault(r => r.UnitId == deadId);
            if (dto != null)
            {
                dto.IsDead = true;
                dto.CurrentHp = 0;
            }
        }

        return removedCells.Select(x => new HexPositionDto { Col = x.col, Row = x.row }).ToArray();
    }

    private bool TryFindNearestFreeHexInRectangle(
        int fromCol, int fromRow,
        int minC, int maxC, int minR, int maxR,
        HashSet<(int col, int row)> occupied,
        out int outCol, out int outRow)
    {
        outCol = outRow = 0;
        int bestD = int.MaxValue;
        bool found = false;
        for (int c = minC; c <= maxC; c++)
        {
            for (int r = minR; r <= maxR; r++)
            {
                if (occupied.Contains((c, r)))
                    continue;
                if (_obstacleTags.ContainsKey((c, r)))
                    continue;
                int d = HexSpawn.HexDistance(fromCol, fromRow, c, r);
                if (d < bestD)
                {
                    bestD = d;
                    outCol = c;
                    outRow = r;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// If a live player stands on an escape-border hex or on the inner edge of the active rectangle,
    /// do not shrink from that side (avoids shoving them while fleeing). Symmetric shrink is skipped on an axis when both sides are protected.
    /// </summary>
    private void GetPlayerZoneShrinkSideProtection(
        int oldMinC, int oldMaxC, int oldMinR, int oldMaxR,
        out bool protectHorizLeft, out bool protectHorizRight, out bool protectVertTop, out bool protectVertBottom)
    {
        protectHorizLeft = protectHorizRight = protectVertTop = protectVertBottom = false;
        foreach (var u in Units.Values)
        {
            if (u.CurrentHp <= 0 || u.UnitType != UnitType.Player)
                continue;
            int c = u.Col, r = u.Row;
            bool onEscape = IsEscapeBorderHex(c, r);
            bool inActive = IsInActiveZone(c, r);

            if (inActive && c == oldMinC || onEscape && c == oldMinC - 1)
                protectHorizLeft = true;
            if (inActive && c == oldMaxC || onEscape && c == oldMaxC + 1)
                protectHorizRight = true;
            if (inActive && r == oldMinR || onEscape && r == oldMinR - 1)
                protectVertTop = true;
            if (inActive && r == oldMaxR || onEscape && r == oldMaxR + 1)
                protectVertBottom = true;
        }
    }
}
