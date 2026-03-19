namespace BattleServer;

public static class BattleWeaponsDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Weapons</title>
  <style>
    body { margin: 0; background: #0f1117; color: #e8ecf4; font-family: Inter, sans-serif; }
    .wrap { padding: 16px; max-width: 980px; margin: 0 auto; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 12px; margin-top: 12px; }
    input, button { background: #11151d; border: 1px solid #2b3140; color: #e8ecf4; border-radius: 8px; padding: 8px; }
    button { cursor: pointer; }
    table { width: 100%; border-collapse: collapse; margin-top: 10px; }
    th, td { border-bottom: 1px solid #2b3140; text-align: left; padding: 8px; }
    .row { display: flex; gap: 8px; flex-wrap: wrap; }
    .status { color: #9aa4b2; font-size: 13px; margin-top: 8px; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="nav">
      <a href="/db">Battles</a>
      <a href="/users">Users</a>
      <a href="/weapons">Weapons</a>
    </div>
    <div class="panel">
      <h3>Create / Update Weapon</h3>
      <div class="row">
        <input id="code" placeholder="code (e.g. fist)" />
        <input id="name" placeholder="name" />
        <input id="damage" type="number" min="0" value="1" />
        <input id="range" type="number" min="0" value="1" />
        <button id="saveBtn">Save</button>
        <button id="reloadBtn">Reload</button>
      </div>
      <div id="status" class="status">loading...</div>
      <table>
        <thead><tr><th>id</th><th>code</th><th>name</th><th>damage</th><th>range</th></tr></thead>
        <tbody id="rows"></tbody>
      </table>
    </div>
  </div>
  <script>
    const rowsEl = document.getElementById('rows');
    const statusEl = document.getElementById('status');
    async function load() {
      statusEl.textContent = 'loading...';
      const resp = await fetch('/api/db/weapons?take=200');
      const list = await resp.json();
      rowsEl.innerHTML = '';
      for (const w of list) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>${w.id}</td><td>${w.code}</td><td>${w.name}</td><td>${w.damage}</td><td>${w.range}</td>`;
        rowsEl.appendChild(tr);
      }
      statusEl.textContent = `loaded ${list.length} weapons`;
    }
    document.getElementById('reloadBtn').addEventListener('click', () => load());
    document.getElementById('saveBtn').addEventListener('click', async () => {
      const payload = {
        code: document.getElementById('code').value,
        name: document.getElementById('name').value,
        damage: Number(document.getElementById('damage').value || 0),
        range: Number(document.getElementById('range').value || 0)
      };
      const resp = await fetch('/api/db/weapons', {
        method: 'POST',
        headers: {'Content-Type':'application/json'},
        body: JSON.stringify(payload)
      });
      if (!resp.ok) {
        statusEl.textContent = 'save failed';
        return;
      }
      statusEl.textContent = 'saved';
      await load();
    });
    load();
  </script>
</body>
</html>
""";
}
