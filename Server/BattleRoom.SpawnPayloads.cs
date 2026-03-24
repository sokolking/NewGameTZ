using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    public void FillSpawnArrays(out string[] ids, out int[] cols, out int[] rows, out int[] currentAps, out int[] maxHps, out int[] currentHps, out string[] currentPostures, out string[] weaponCodes, out int[] weaponDamages, out int[] weaponRanges, out int[] weaponAttackApCosts, out string[] spawnDisplayNames, out int[] spawnLevels)
    {
        EnsureUnitsInitialized();

        var items = new List<(string id, int col, int row, int currentAp, int maxHp, int currentHp, string posture, string wc, int wd, int wr, int wac, string displayName, int level)>();

        foreach (var playerId in ParticipantIds.Where(Players.ContainsKey))
        {
            string dn = PlayerDisplayNames.GetValueOrDefault(playerId, playerId);
            int lv = PlayerLevels.GetValueOrDefault(playerId, 1);
            if (PlayerToUnitId.TryGetValue(playerId, out var unitId) && Units.TryGetValue(unitId, out var unit))
            {
                items.Add((
                    playerId,
                    unit.Col,
                    unit.Row,
                    unit.CurrentAp,
                    unit.MaxHp,
                    unit.CurrentHp,
                    NormalizePosture(unit.Posture),
                    unit.WeaponCode ?? DefaultWeaponCode,
                    unit.WeaponDamage,
                    unit.WeaponRange,
                    Math.Max(1, unit.WeaponAttackApCost),
                    dn,
                    lv));
            }
            else
            {
                string wc = DefaultWeaponCode;
                int wd = DefaultWeaponDamage;
                int wr = DefaultWeaponRange;
                int wac = GetWeaponAttackApCostFromDb(DefaultWeaponCode);
                if (PlayerCombatProfiles.TryGetValue(playerId, out var prof))
                {
                    wc = prof.Item3;
                    wd = prof.Item4;
                    wr = prof.Item5;
                    wac = prof.Item6;
                }

                items.Add((playerId, Players[playerId].col, Players[playerId].row, MaxAp, DefaultPlayerMaxHp, DefaultPlayerMaxHp, PostureWalk, wc, wd, wr, wac, dn, lv));
            }
        }

        foreach (var unit in Units.Values.Where(u => u.UnitType == UnitType.Mob))
        {
            items.Add((
                unit.UnitId,
                unit.Col,
                unit.Row,
                unit.CurrentAp,
                unit.MaxHp,
                unit.CurrentHp,
                NormalizePosture(unit.Posture),
                unit.WeaponCode ?? DefaultWeaponCode,
                unit.WeaponDamage,
                unit.WeaponRange,
                Math.Max(1, unit.WeaponAttackApCost),
                unit.UnitId,
                1));
        }

        ids = items.Select(x => x.id).ToArray();
        cols = items.Select(x => x.col).ToArray();
        rows = items.Select(x => x.row).ToArray();
        currentAps = items.Select(x => x.currentAp).ToArray();
        maxHps = items.Select(x => x.maxHp).ToArray();
        currentHps = items.Select(x => x.currentHp).ToArray();
        currentPostures = items.Select(x => x.posture).ToArray();
        weaponCodes = items.Select(x => x.wc).ToArray();
        weaponDamages = items.Select(x => x.wd).ToArray();
        weaponRanges = items.Select(x => x.wr).ToArray();
        weaponAttackApCosts = items.Select(x => x.wac).ToArray();
        spawnDisplayNames = items.Select(x => x.displayName).ToArray();
        spawnLevels = items.Select(x => x.level).ToArray();
    }

    public BattleStartedPayloadDto BuildBattleStartedFor(string playerId)
    {
        EnsureUnitsInitialized();
        var players = Players.Select(p => new BattlePlayerInfoDto
        {
            PlayerId = p.Key,
            Col = p.Value.col,
            Row = p.Value.row
        }).ToArray();
        FillSpawnArrays(out var sid, out var sc, out var sr, out var sap, out var smh, out var sch, out var spos, out var swc, out var swd, out var swr, out var swac, out var sdn, out var slv);
        var sortedKeys = _obstacleTags.Keys.OrderBy(k => k.col).ThenBy(k => k.row).ToArray();
        var obstacleCols = sortedKeys.Select(k => k.col).ToArray();
        var obstacleRows = sortedKeys.Select(k => k.row).ToArray();
        var obstacleTags = sortedKeys.Select(k => _obstacleTags[k]).ToArray();
        var mapStateStart = new List<CellObject>();
        foreach (var kv in _obstacleTags.OrderBy(k => k.Key.col).ThenBy(k => k.Key.row))
        {
            if (!IsWallObstacleTag(kv.Value))
                continue;
            (int col, int row) wc = kv.Key;
            mapStateStart.Add(new CellObject
            {
                Hex = new HexPositionDto { Col = wc.col, Row = wc.row },
                State = kv.Value == "damaged_wall" ? CellObjectState.Damaged : CellObjectState.Full
            });
        }

        return new BattleStartedPayloadDto
        {
            BattleId = BattleId,
            PlayerId = playerId,
            Players = players,
            RoundDuration = RoundDuration,
            RoundDeadlineUtcMs = RoundDeadlineUtcMs,
            SpawnPlayerIds = sid,
            SpawnCols = sc,
            SpawnRows = sr,
            SpawnCurrentAps = sap,
            SpawnMaxHps = smh,
            SpawnCurrentHps = sch,
            SpawnCurrentPostures = spos,
            SpawnWeaponCodes = swc,
            SpawnWeaponDamages = swd,
            SpawnWeaponRanges = swr,
            SpawnWeaponAttackApCosts = swac,
            SpawnDisplayNames = sdn,
            SpawnLevels = slv,
            ObstacleCols = obstacleCols,
            ObstacleRows = obstacleRows,
            ObstacleTags = obstacleTags,
            MapState = mapStateStart.Count > 0 ? mapStateStart.ToArray() : System.Array.Empty<CellObject>()
        };
    }
}
