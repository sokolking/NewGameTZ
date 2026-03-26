namespace BattleServer;

public static class BattleAmmoDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="Cache-Control" content="no-store, max-age=0" />
  <title>Ammo</title>
  <style>
    body { margin: 0; background: #0f1117; color: #e8ecf4; font-family: Inter, sans-serif; }
    .wrap { padding: 16px; max-width: 100%; margin: 0 auto; box-sizing: border-box; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 12px; margin-top: 12px; }
    input, button { background: #11151d; border: 1px solid #2b3140; color: #e8ecf4; border-radius: 6px; padding: 4px 6px; font-size: 12px; }
    button { cursor: pointer; padding: 8px 12px; font-size: 13px; }
    table { border-collapse: collapse; margin-top: 10px; font-size: 12px; width: 100%; }
    th, td { border-bottom: 1px solid #2b3140; text-align: left; padding: 6px; vertical-align: middle; white-space: nowrap; }
    th { position: sticky; top: 0; background: #171a22; z-index: 1; font-weight: 600; color: #b8c4d9; }
    .scroll { overflow-x: auto; max-width: 100%; }
    .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-top: 8px; }
    .status { color: #9aa4b2; font-size: 13px; margin-top: 8px; }
    .hint { color: #7f8ea3; font-size: 12px; margin-top: 6px; line-height: 1.4; }
    h3 { margin: 0 0 8px 0; }
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
    </div>
    <div class="panel">
      <h3>Create / update ammo type</h3>
      <p class="hint">Rows are upserted by <code>caliber</code> using <code>POST /api/db/ammo</code>.</p>
      <div class="row">
        <input id="c_caliber" placeholder="caliber" />
        <input id="c_unitWeight" type="number" step="0.01" min="0" placeholder="unitWeight" />
        <input id="c_iconKey" placeholder="iconKey" />
        <button id="createSaveBtn" type="button">Save new</button>
        <button id="reloadBtn" type="button">Reload table</button>
      </div>
    </div>
    <div class="panel">
      <h3>Ammo types</h3>
      <div id="status" class="status">loading...</div>
      <div class="scroll">
        <table>
          <thead>
            <tr>
              <th>id</th>
              <th>caliber</th>
              <th>unitWeight</th>
              <th>iconKey</th>
              <th></th>
            </tr>
          </thead>
          <tbody id="rows"></tbody>
        </table>
      </div>
    </div>
  </div>
  <script>
    const rowsEl = document.getElementById('rows');
    const statusEl = document.getElementById('status');

    function numOr(v, d) {
      const n = Number(v);
      return Number.isFinite(n) ? n : d;
    }

    async function postAmmo(payload) {
      return fetch('/api/db/ammo', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
    }

    function rowFromAmmo(a) {
      const tr = document.createElement('tr');
      tr.dataset.caliber = a.caliber || '';
      tr.innerHTML = `
        <td>${a.id ?? ''}</td>
        <td><input data-field="caliber" type="text" value="${(a.caliber || '').replaceAll('"', '&quot;')}" /></td>
        <td><input data-field="unitWeight" type="number" step="0.01" min="0" value="${a.unitWeight ?? 0}" /></td>
        <td><input data-field="iconKey" type="text" value="${(a.iconKey || '').replaceAll('"', '&quot;')}" /></td>
        <td><button type="button" data-act="save">Save</button></td>
      `;
      tr.querySelector('[data-act="save"]').addEventListener('click', async () => {
        const caliber = (tr.querySelector('[data-field="caliber"]').value || '').trim();
        const unitWeight = Math.max(0, numOr(tr.querySelector('[data-field="unitWeight"]').value, 0));
        const iconKey = (tr.querySelector('[data-field="iconKey"]').value || '').trim();
        if (!caliber) {
          statusEl.textContent = 'caliber is required';
          return;
        }
        statusEl.textContent = 'saving ' + caliber + '...';
        const resp = await postAmmo({ caliber, unitWeight, iconKey });
        if (!resp.ok) {
          const err = await resp.json().catch(() => ({}));
          statusEl.textContent = 'save failed: ' + (err.error || resp.status);
          return;
        }
        statusEl.textContent = 'saved: ' + caliber;
        await load();
      });
      return tr;
    }

    async function load() {
      statusEl.textContent = 'loading...';
      const resp = await fetch('/api/db/ammo?take=500', { cache: 'no-store' });
      const list = await resp.json().catch(() => []);
      rowsEl.innerHTML = '';
      for (const a of list)
        rowsEl.appendChild(rowFromAmmo(a));
      statusEl.textContent = 'loaded ' + list.length + ' ammo types';
    }

    document.getElementById('reloadBtn').addEventListener('click', () => load());
    document.getElementById('createSaveBtn').addEventListener('click', async () => {
      const caliber = (document.getElementById('c_caliber').value || '').trim();
      const unitWeight = Math.max(0, numOr(document.getElementById('c_unitWeight').value, 0));
      const iconKey = (document.getElementById('c_iconKey').value || '').trim();
      if (!caliber) {
        statusEl.textContent = 'caliber is required';
        return;
      }
      statusEl.textContent = 'saving ' + caliber + '...';
      const resp = await postAmmo({ caliber, unitWeight, iconKey });
      if (!resp.ok) {
        const err = await resp.json().catch(() => ({}));
        statusEl.textContent = 'save failed: ' + (err.error || resp.status);
        return;
      }
      document.getElementById('c_caliber').value = '';
      document.getElementById('c_iconKey').value = '';
      statusEl.textContent = 'saved: ' + caliber;
      await load();
    });

    load();
  </script>
</body>
</html>
""";
}
