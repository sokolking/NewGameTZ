namespace BattleServer;

public static class BattleZoneShrinkDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Zone shrink</title>
  <style>
    body { margin: 0; background: #0f1117; color: #e8ecf4; font-family: Inter, sans-serif; }
    .wrap { padding: 16px; max-width: 640px; margin: 0 auto; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 16px; margin-top: 12px; }
    label { display: block; margin-top: 12px; color: #9aa4b2; font-size: 13px; }
    input[type="number"] { width: 100%; max-width: 200px; background: #11151d; border: 1px solid #2b3140; color: #e8ecf4; border-radius: 8px; padding: 8px; box-sizing: border-box; }
    .hint { font-size: 12px; color: #6b7280; margin-top: 4px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; margin-top: 16px; }
    button { background: #2a3f7a; border: 1px solid #4a6fd4; color: #e8ecf4; border-radius: 8px; padding: 10px 16px; cursor: pointer; }
    button.secondary { background: #11151d; border-color: #2b3140; }
    .status { color: #9aa4b2; font-size: 13px; margin-top: 12px; }
    h1 { font-size: 20px; margin: 0 0 8px; }
    h2 { font-size: 15px; margin: 0 0 8px; color: #c5cdd8; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="nav">
      <a href="/db">Battles</a>
      <a href="/users">Users</a>
      <a href="/weapons">Weapons</a>
      <a href="/ammo">Ammo</a>
      <a href="/obstacle-balance">Obstacle balance</a>
      <a href="/zone-shrink">Zone shrink</a>
    </div>
    <h1>battle_zone_shrink</h1>
    <p style="color:#9aa4b2;font-size:14px;margin:0;">Single row (id=1). Shrinking applies to new battles after save. Round numbers are 1-based (first resolved round = 1).</p>
    <div class="panel">
      <h2>Parameters</h2>
      <label for="shrinkStartRound">shrink_start_round (a)</label>
      <input id="shrinkStartRound" type="number" min="1" max="9999" />
      <div class="hint">First round (1-based) when shrink schedule starts.</div>

      <label for="horizontalShrinkInterval">horizontal_shrink_interval (b)</label>
      <input id="horizontalShrinkInterval" type="number" min="1" max="999" />
      <div class="hint">Every b rounds: remove columns from left and right.</div>

      <label for="horizontalShrinkAmount">horizontal_shrink_amount (c)</label>
      <input id="horizontalShrinkAmount" type="number" min="0" max="50" />
      <div class="hint">Columns removed per side each horizontal step.</div>

      <label for="verticalShrinkInterval">vertical_shrink_interval (d)</label>
      <input id="verticalShrinkInterval" type="number" min="1" max="999" />

      <label for="verticalShrinkAmount">vertical_shrink_amount (e)</label>
      <input id="verticalShrinkAmount" type="number" min="0" max="50" />
      <div class="hint">Rows removed per side each vertical step.</div>

      <label for="minWidth">min_width (f)</label>
      <input id="minWidth" type="number" min="1" max="25" />

      <label for="minHeight">min_height (g)</label>
      <input id="minHeight" type="number" min="1" max="40" />

      <div class="row">
        <button id="saveBtn" type="button">Save</button>
        <button id="reloadBtn" type="button" class="secondary">Reload</button>
      </div>
      <div id="status" class="status">loading…</div>
    </div>
  </div>
  <script>
    const statusEl = document.getElementById('status');
    function field(id) { return document.getElementById(id); }

    async function load() {
      statusEl.textContent = 'loading…';
      try {
        const resp = await fetch('/api/db/zone-shrink');
        if (!resp.ok) { statusEl.textContent = 'GET error: ' + resp.status; return; }
        const s = await resp.json();
        field('shrinkStartRound').value = s.shrinkStartRound;
        field('horizontalShrinkInterval').value = s.horizontalShrinkInterval;
        field('horizontalShrinkAmount').value = s.horizontalShrinkAmount;
        field('verticalShrinkInterval').value = s.verticalShrinkInterval;
        field('verticalShrinkAmount').value = s.verticalShrinkAmount;
        field('minWidth').value = s.minWidth;
        field('minHeight').value = s.minHeight;
        statusEl.textContent = 'loaded';
      } catch (e) {
        statusEl.textContent = 'error: ' + e;
      }
    }

    async function save() {
      const payload = {
        shrinkStartRound: Number(field('shrinkStartRound').value || 10),
        horizontalShrinkInterval: Number(field('horizontalShrinkInterval').value || 2),
        horizontalShrinkAmount: Number(field('horizontalShrinkAmount').value || 0),
        verticalShrinkInterval: Number(field('verticalShrinkInterval').value || 2),
        verticalShrinkAmount: Number(field('verticalShrinkAmount').value || 0),
        minWidth: Number(field('minWidth').value || 5),
        minHeight: Number(field('minHeight').value || 3)
      };
      statusEl.textContent = 'saving…';
      try {
        const resp = await fetch('/api/db/zone-shrink', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (!resp.ok) {
          const err = await resp.json().catch(() => ({}));
          statusEl.textContent = 'error: ' + (err.error || resp.status);
          return;
        }
        statusEl.textContent = 'saved (new battles pick up values)';
        await load();
      } catch (e) {
        statusEl.textContent = 'error: ' + e;
      }
    }

    document.getElementById('saveBtn').addEventListener('click', save);
    document.getElementById('reloadBtn').addEventListener('click', load);
    load();
  </script>
</body>
</html>
""";
}
