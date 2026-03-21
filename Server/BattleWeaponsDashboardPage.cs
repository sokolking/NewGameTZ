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
    th, td { border-bottom: 1px solid #2b3140; text-align: left; padding: 8px; vertical-align: middle; }
    table input { width: 100%; min-width: 0; box-sizing: border-box; max-width: 220px; }
    table input[type="number"] { max-width: 88px; }
    .col-id { width: 56px; }
    .col-code { max-width: 100px; }
    .col-actions { white-space: nowrap; width: 96px; }
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
        <input id="iconKey" placeholder="icon_key" title="sprite key Resources/WeaponIcons/{iconKey}" />
        <input id="attackApCost" type="number" min="1" value="1" title="attack AP cost (default 1; fist 3, stone 7)" />
        <button id="saveBtn">Save</button>
        <button id="reloadBtn">Reload</button>
      </div>
      <div id="status" class="status">loading...</div>
      <table>
        <thead><tr><th class="col-id">id</th><th class="col-code">code</th><th>name</th><th>damage</th><th>range</th><th>icon</th><th>attack ОД</th><th class="col-actions"></th></tr></thead>
        <tbody id="rows"></tbody>
      </table>
    </div>
  </div>
  <script>
    const rowsEl = document.getElementById('rows');
    const statusEl = document.getElementById('status');

    function inputEl(type, value, dataField, opts) {
      const i = document.createElement('input');
      if (type) i.type = type;
      if (dataField) i.setAttribute('data-field', dataField);
      i.value = value != null ? String(value) : '';
      if (opts) {
        if (opts.readOnly) i.readOnly = true;
        if (opts.min != null) i.min = opts.min;
        if (opts.placeholder) i.placeholder = opts.placeholder;
        if (opts.title) i.title = opts.title;
      }
      return i;
    }

    async function postWeapon(payload) {
      return fetch('/api/db/weapons', {
        method: 'POST',
        headers: {'Content-Type':'application/json'},
        body: JSON.stringify(payload)
      });
    }

    async function saveRow(btn) {
      const tr = btn.closest('tr');
      const code = tr.dataset.code;
      const payload = {
        code,
        name: tr.querySelector('[data-field="name"]').value,
        damage: Number(tr.querySelector('[data-field="damage"]').value || 0),
        range: Number(tr.querySelector('[data-field="range"]').value || 0),
        iconKey: (tr.querySelector('[data-field="iconKey"]').value || '').trim() || null,
        attackApCost: Number(tr.querySelector('[data-field="attackApCost"]').value || 1)
      };
      statusEl.textContent = 'saving ' + code + '...';
      const resp = await postWeapon(payload);
      if (!resp.ok) {
        statusEl.textContent = 'save failed: ' + code;
        return;
      }
      statusEl.textContent = 'saved row: ' + code;
      await load();
    }

    async function load() {
      statusEl.textContent = 'loading...';
      const resp = await fetch('/api/db/weapons?take=200');
      const list = await resp.json();
      rowsEl.innerHTML = '';
      for (const w of list) {
        const tr = document.createElement('tr');
        tr.dataset.code = w.code;

        const tdId = document.createElement('td');
        tdId.className = 'col-id';
        tdId.appendChild(inputEl('text', w.id, null, { readOnly: true }));

        const tdCode = document.createElement('td');
        tdCode.className = 'col-code';
        tdCode.appendChild(inputEl('text', w.code, null, { readOnly: true }));

        const tdName = document.createElement('td');
        tdName.appendChild(inputEl('text', w.name, 'name', { placeholder: 'name' }));

        const tdDmg = document.createElement('td');
        tdDmg.appendChild(inputEl('number', w.damage, 'damage', { min: 0 }));

        const tdRng = document.createElement('td');
        tdRng.appendChild(inputEl('number', w.range, 'range', { min: 0 }));

        const tdIcon = document.createElement('td');
        tdIcon.appendChild(inputEl('text', w.iconKey ?? '', 'iconKey', { placeholder: 'icon', title: 'Resources/WeaponIcons/{iconKey}' }));

        const tdAp = document.createElement('td');
        tdAp.appendChild(inputEl('number', w.attackApCost ?? 1, 'attackApCost', { min: 1, title: 'attack AP cost' }));

        const tdAct = document.createElement('td');
        tdAct.className = 'col-actions';
        const saveBtn = document.createElement('button');
        saveBtn.type = 'button';
        saveBtn.textContent = 'Save';
        saveBtn.addEventListener('click', () => saveRow(saveBtn));
        tdAct.appendChild(saveBtn);

        tr.appendChild(tdId);
        tr.appendChild(tdCode);
        tr.appendChild(tdName);
        tr.appendChild(tdDmg);
        tr.appendChild(tdRng);
        tr.appendChild(tdIcon);
        tr.appendChild(tdAp);
        tr.appendChild(tdAct);
        rowsEl.appendChild(tr);
      }
      statusEl.textContent = `loaded ${list.length} weapons (edit cells, Save per row)`;
    }

    document.getElementById('reloadBtn').addEventListener('click', () => load());
    document.getElementById('saveBtn').addEventListener('click', async () => {
      const payload = {
        code: document.getElementById('code').value,
        name: document.getElementById('name').value,
        damage: Number(document.getElementById('damage').value || 0),
        range: Number(document.getElementById('range').value || 0),
        iconKey: (document.getElementById('iconKey').value || '').trim() || null,
        attackApCost: Number(document.getElementById('attackApCost').value || 1)
      };
      const resp = await postWeapon(payload);
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
