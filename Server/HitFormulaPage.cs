namespace BattleServer;

/// <summary>Static reference for hit probability (see <c>BattleRoom.CombatRules</c>, <c>BattleRoom.CloseRound</c>).</summary>
public static class HitFormulaPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="Cache-Control" content="no-store, max-age=0" />
  <title>Hit probability</title>
  <style>
    body { margin: 0; background: #0f1117; color: #e8ecf4; font-family: Inter, sans-serif; line-height: 1.45; }
    .wrap { padding: 16px; max-width: 880px; margin: 0 auto; box-sizing: border-box; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 14px 16px; margin-top: 12px; }
    h1 { font-size: 1.25rem; margin: 0 0 8px 0; font-weight: 600; }
    h2 { font-size: 1.05rem; margin: 18px 0 8px 0; color: #b8c4d9; font-weight: 600; }
    p, li { color: #c5ced9; font-size: 14px; margin: 8px 0; }
    code { background: #11151d; padding: 1px 5px; border-radius: 4px; font-size: 13px; }
    pre { background: #11151d; border: 1px solid #2b3140; border-radius: 8px; padding: 12px 14px; overflow-x: auto; font-size: 13px; color: #e0e6ef; }
    ul { margin: 6px 0 8px 0; padding-left: 20px; }
    .hint { color: #7f8ea3; font-size: 13px; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="nav">
      <a href="/db">Battles</a>
      <a href="/users">Users</a>
      <a href="/weapons">Weapons</a>
      <a href="/obstacle-balance">Obstacle balance</a>
      <a href="/hit_formula.html">Hit formula</a>
    </div>
    <div class="panel">
      <h1>Hit probability (server implementation)</h1>
      <p class="hint">Source: <code>BattleRoom.CombatRules.cs</code> (<code>CombineHitProbability</code>, <code>SpreadPenaltyFromTightness</code>), <code>BattleRoom.CloseRound.cs</code> (<code>GetBaseHitProbabilityFromRange</code>).</p>

      <h2>1. Distance factor <code>p<sub>distance</sub></code></h2>
      <p><code>weaponRange</code> = max(1, nominal weapon range in hexes). Let <code>d</code> = hex distance from shooter to target.</p>
      <ul>
        <li>If <code>d &le; 1</code>, <code>p<sub>distance</sub> = 1</code>.</li>
        <li>Else <code>dClamped = min(d, max(0, weaponRange))</code> and<br>
          <code>p<sub>distance</sub> = (weaponRange + 1 − dClamped) / weaponRange</code>, then clamp to [0, 1].</li>
        <li>Let <code>N = max(0, d − weaponRange)</code> (hexes beyond nominal range). If <code>N &gt; 0</code>, multiply <code>p<sub>distance</sub></code> by <code>0.5<sup>N</sup></code> for normal weapons, or <code>0.65<sup>N</sup></code> if the weapon is a sniper (<code>is_sniper</code>). Damage beyond range uses <code>0.5<sup>N</sup></code> regardless of sniper flag.</li>
      </ul>

      <h2>2. Cover multipliers</h2>
      <p>From obstacle balance (percent miss values):</p>
      <ul>
        <li><code>treeF</code> = <code>1 − tree_miss_percent/100</code> if any tree on the cover line, else <code>1</code> (floored at 0).</li>
        <li><code>rockF</code> = <code>1 − rock_miss_percent/100</code> if rock on the cover line and target is in hide, else <code>1</code> (floored at 0).</li>
        <li><code>coverMul = treeF × rockF</code>.</li>
      </ul>

      <h2>3. Accuracy bonus</h2>
      <p><code>accBonus = max(0, accuracy) × 0.02</code> (per point of accuracy).</p>

      <h2>4. Tightness (spread) penalty</h2>
      <p>Weapon <strong>tightness</strong> <code>T</code> in DB is in [0, 1]; higher is tighter (better). Column may still be named <code>spread_penalty</code> in the schema; semantics are tightness <code>T</code>. Unit state JSON uses <code>weaponTightness</code> (same <code>T</code>).</p>
      <pre>spreadRaw = clamp(1 − T, 0, 1)
spread    = clamp(spreadRaw, 0, 0.95)</pre>

      <h2>5. Combined hit probability</h2>
      <pre>p = p_distance × coverMul + accBonus − spread
p = clamp(p, 0, 1)</pre>
      <p class="hint">This matches the design note: multiplicative cover on distance, additive accuracy, then subtract spread derived from <code>(1 − T)</code>.</p>
    </div>
  </div>
</body>
</html>
""";
}
