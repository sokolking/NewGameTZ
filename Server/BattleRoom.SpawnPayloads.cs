using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    public void FillSpawnArrays(out string[] ids, out int[] cols, out int[] rows, out int[] currentAps, out int[] maxAps, out int[] maxHps, out int[] currentHps, out string[] currentPostures, out string[] weaponCodes, out int[] weaponDamageMins, out int[] weaponDamages, out int[] weaponRanges, out int[] weaponAttackApCosts, out int[] currentMagazineRounds, out double[] weaponTightnesses, out int[] weaponTrajectoryHeights, out bool[] weaponIsSnipers, out string[] spawnDisplayNames, out int[] spawnLevels)
    {
        EnsureUnitsInitialized();

        var items = new List<(string id, int col, int row, int currentAp, int maxAp, int maxHp, int currentHp, string posture, string wc, int wdm, int wd, int wr, int wac, int wmag, double wtn, int wth, bool wsn, string displayName, int level)>();

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
                    unit.MaxAp,
                    unit.MaxHp,
                    unit.CurrentHp,
                    NormalizePosture(unit.Posture),
                    unit.WeaponCode ?? DefaultWeaponCode,
                    unit.WeaponDamageMin,
                    unit.WeaponDamage,
                    unit.WeaponRange,
                    Math.Max(1, unit.WeaponAttackApCost),
                    Math.Max(0, unit.CurrentMagazineRounds),
                    unit.WeaponTightness,
                    unit.WeaponTrajectoryHeight,
                    unit.WeaponIsSniper,
                    dn,
                    lv));
            }
            else
            {
                string wc = DefaultWeaponCode;
                int wdm = DefaultWeaponDamage;
                int wd = DefaultWeaponDamage;
                int wr = DefaultWeaponRange;
                int wac = GetWeaponAttackApCostFromDb(DefaultWeaponCode);
                int wmag = GetWeaponMagazineSizeFromDb(wc);
                double wtn = 1.0;
                int wth = 1;
                bool wsn = false;
                int maxHp = DefaultPlayerMaxHp;
                int maxAp = DefaultPlayerMaxAp;
                if (PlayerCombatProfiles.TryGetValue(playerId, out var prof))
                {
                    wc = prof.Item3;
                    wdm = prof.Item4;
                    wd = prof.Item5;
                    wr = prof.Item6;
                    wac = prof.Item7;
                    wtn = prof.Item9;
                    wth = prof.Item10;
                    wsn = prof.Item11;
                    maxHp = prof.Item1;
                    maxAp = prof.Item2;
                }

                items.Add((playerId, Players[playerId].col, Players[playerId].row, maxAp, maxAp, maxHp, maxHp, PostureWalk, wc, wdm, wd, wr, wac, wmag, wtn, wth, wsn, dn, lv));
            }
        }

        foreach (var unit in Units.Values.Where(u => u.UnitType == UnitType.Mob))
        {
            items.Add((
                unit.UnitId,
                unit.Col,
                unit.Row,
                unit.CurrentAp,
                unit.MaxAp,
                unit.MaxHp,
                unit.CurrentHp,
                NormalizePosture(unit.Posture),
                unit.WeaponCode ?? DefaultWeaponCode,
                unit.WeaponDamageMin,
                unit.WeaponDamage,
                unit.WeaponRange,
                Math.Max(1, unit.WeaponAttackApCost),
                Math.Max(0, unit.CurrentMagazineRounds),
                unit.WeaponTightness,
                unit.WeaponTrajectoryHeight,
                unit.WeaponIsSniper,
                unit.UnitId,
                1));
        }

        ids = items.Select(x => x.id).ToArray();
        cols = items.Select(x => x.col).ToArray();
        rows = items.Select(x => x.row).ToArray();
        currentAps = items.Select(x => x.currentAp).ToArray();
        maxAps = items.Select(x => x.maxAp).ToArray();
        maxHps = items.Select(x => x.maxHp).ToArray();
        currentHps = items.Select(x => x.currentHp).ToArray();
        currentPostures = items.Select(x => x.posture).ToArray();
        weaponCodes = items.Select(x => x.wc).ToArray();
        weaponDamageMins = items.Select(x => x.wdm).ToArray();
        weaponDamages = items.Select(x => x.wd).ToArray();
        weaponRanges = items.Select(x => x.wr).ToArray();
        weaponAttackApCosts = items.Select(x => x.wac).ToArray();
        currentMagazineRounds = items.Select(x => x.wmag).ToArray();
        weaponTightnesses = items.Select(x => x.wtn).ToArray();
        weaponTrajectoryHeights = items.Select(x => x.wth).ToArray();
        weaponIsSnipers = items.Select(x => x.wsn).ToArray();
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
        FillSpawnArrays(out var sid, out var sc, out var sr, out var sap, out var smap, out var smh, out var sch, out var spos, out var swc, out var swdm, out var swd, out var swr, out var swac, out var swmag, out var swtn, out var swth, out var swsn, out var sdn, out var slv);
        var sortedKeys = _obstacleTags.Keys.OrderBy(k => k.col).ThenBy(k => k.row).ToArray();
        var obstacleCols = sortedKeys.Select(k => k.col).ToArray();
        var obstacleRows = sortedKeys.Select(k => k.row).ToArray();
        var obstacleTags = sortedKeys.Select(k => _obstacleTags[k]).ToArray();
        var obstacleWallYaws = sortedKeys.Select(k => _wallYawDegrees.TryGetValue(k, out var y) ? y : 0f).ToArray();
        var mapStateStart = new List<CellObject>();
        foreach (var kv in _obstacleTags.OrderBy(k => k.Key.col).ThenBy(k => k.Key.row))
        {
            if (!IsWallObstacleTag(kv.Value))
                continue;
            (int col, int row) wc = kv.Key;
            mapStateStart.Add(new CellObject
            {
                Hex = new HexPositionDto { Col = wc.col, Row = wc.row },
                State = kv.Value is "damaged_wall" or "damaged_wall_low" ? CellObjectState.Damaged : CellObjectState.Full
            });
        }

        EnsureActiveZoneInitialized();
        return new BattleStartedPayloadDto
        {
            BattleId = BattleId,
            PlayerId = playerId,
            Players = players,
            RoundDuration = RoundDuration,
            RoundDeadlineUtcMs = RoundDeadlineUtcMs,
            RoundIndex = RoundIndex,
            SpawnPlayerIds = sid,
            SpawnCols = sc,
            SpawnRows = sr,
            SpawnCurrentAps = sap,
            SpawnMaxAps = smap,
            SpawnMaxHps = smh,
            SpawnCurrentHps = sch,
            SpawnCurrentPostures = spos,
            SpawnWeaponCodes = swc,
            SpawnWeaponDamageMins = swdm,
            SpawnWeaponDamages = swd,
            SpawnWeaponRanges = swr,
            SpawnWeaponAttackApCosts = swac,
            SpawnCurrentMagazineRounds = swmag,
            SpawnWeaponTightnesses = swtn,
            SpawnWeaponTrajectoryHeights = swth,
            SpawnWeaponIsSnipers = swsn,
            SpawnDisplayNames = sdn,
            SpawnLevels = slv,
            ObstacleCols = obstacleCols,
            ObstacleRows = obstacleRows,
            ObstacleTags = obstacleTags,
            ObstacleWallYaws = obstacleWallYaws,
            MapState = mapStateStart.Count > 0 ? mapStateStart.ToArray() : System.Array.Empty<CellObject>(),
            ActiveMinCol = _activeMinCol,
            ActiveMaxCol = _activeMaxCol,
            ActiveMinRow = _activeMinRow,
            ActiveMaxRow = _activeMaxRow
        };
    }
}
