namespace BattleServer;

public static class BattleItemsDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="Cache-Control" content="no-store, max-age=0" />
  <title>Items</title>
  <style>
    body { margin: 0; background: #0f1117; color: #e8ecf4; font-family: Inter, sans-serif; }
    .wrap { padding: 16px; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 12px; margin-top: 12px; }
    .tabs { display: flex; gap: 8px; flex-wrap: wrap; }
    .tab-btn {
      background: #11151d; border: 1px solid #2b3140; color: #e8ecf4;
      border-radius: 6px; padding: 7px 10px; cursor: pointer; font-size: 13px;
    }
    .tab-btn.active { border-color: #7aa2ff; background: #1b263d; }
    .subtabs { margin-top: 10px; display: flex; gap: 8px; flex-wrap: wrap; }
    iframe {
      margin-top: 12px; width: 100%; height: calc(100vh - 220px);
      border: 1px solid #2b3140; border-radius: 8px; background: #0f1117;
    }
    .hint { color: #9aa4b2; font-size: 12px; margin-top: 8px; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="nav">
      <a href="/db">Battles</a>
      <a href="/users">Users</a>
      <a href="/weapons">Items</a>
      <a href="/obstacle-balance">Obstacle balance</a>
    </div>
    <div class="panel">
      <div class="tabs">
        <button id="tabWeapons" class="tab-btn active" type="button">Weapons</button>
        <button id="tabAmmo" class="tab-btn" type="button">Ammo</button>
      </div>
      <div id="weaponSubtabs" class="subtabs">
        <button class="tab-btn active" data-category="" type="button">All</button>
        <button class="tab-btn" data-category="cold" type="button">Cold</button>
        <button class="tab-btn" data-category="light" type="button">Light</button>
        <button class="tab-btn" data-category="medium" type="button">Medium</button>
        <button class="tab-btn" data-category="heavy" type="button">Heavy</button>
        <button class="tab-btn" data-category="throwing" type="button">Throwing</button>
        <button class="tab-btn" data-category="medicine" type="button">Medicine</button>
      </div>
      <div class="hint">Unified Items view: weapons and ammo are now managed as item-based entities.</div>
      <iframe id="contentFrame" src="/weapons-table"></iframe>
    </div>
  </div>
  <script>
    const tabWeapons = document.getElementById('tabWeapons');
    const tabAmmo = document.getElementById('tabAmmo');
    const weaponSubtabs = document.getElementById('weaponSubtabs');
    const frame = document.getElementById('contentFrame');
    let mode = 'weapons';
    let category = '';

    function refresh() {
      tabWeapons.classList.toggle('active', mode === 'weapons');
      tabAmmo.classList.toggle('active', mode === 'ammo');
      weaponSubtabs.style.display = mode === 'weapons' ? '' : 'none';
      if (mode === 'ammo') {
        frame.src = '/ammo-table';
      } else {
        const q = category ? ('?category=' + encodeURIComponent(category)) : '';
        frame.src = '/weapons-table' + q;
      }
    }

    tabWeapons.addEventListener('click', () => { mode = 'weapons'; refresh(); });
    tabAmmo.addEventListener('click', () => { mode = 'ammo'; refresh(); });

    weaponSubtabs.querySelectorAll('[data-category]').forEach(btn => {
      btn.addEventListener('click', () => {
        category = btn.dataset.category || '';
        weaponSubtabs.querySelectorAll('[data-category]').forEach(x => x.classList.remove('active'));
        btn.classList.add('active');
        refresh();
      });
    });

    refresh();
  </script>
</body>
</html>
""";
}
