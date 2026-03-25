namespace BattleServer;

public static class BattleWeaponsDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="Cache-Control" content="no-store, max-age=0" />
  <title>Weapons</title>
  <style>
    body { margin: 0; background: #0f1117; color: #e8ecf4; font-family: Inter, sans-serif; }
    .wrap { padding: 16px; max-width: 100%; margin: 0 auto; box-sizing: border-box; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 12px; margin-top: 12px; }
    input, button, select { background: #11151d; border: 1px solid #2b3140; color: #e8ecf4; border-radius: 6px; padding: 4px 6px; font-size: 11px; }
    button { cursor: pointer; padding: 8px 12px; font-size: 13px; }
    table { border-collapse: collapse; margin-top: 10px; font-size: 11px; }
    th, td { border-bottom: 1px solid #2b3140; text-align: left; padding: 4px 5px; vertical-align: middle; white-space: nowrap; }
    th { position: sticky; top: 0; background: #171a22; z-index: 1; font-weight: 600; color: #b8c4d9; }
    table input[type="text"], table input[type="number"] { min-width: 52px; max-width: 96px; width: 100%; box-sizing: border-box; }
    table input.w-wide { max-width: 120px; }
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
      <a href="/obstacle-balance">Obstacle balance</a>
    </div>
    <div class="panel">
      <h3>Create / update weapon</h3>
      <p class="hint">PostgreSQL via <code>POST /api/db/weapons</code>. Legacy field <code>damage</code> maps from <code>damageMin</code>/<code>damageMax</code> when max is unset. Restart the server to apply <code>EnsureCreated</code> migrations.</p>
      <div class="row" id="createBar">
        <input id="c_code" placeholder="code" />
        <input id="c_name" placeholder="name" />
        <button id="createSaveBtn" type="button">Save new</button>
        <button id="reloadBtn" type="button">Reload table</button>
      </div>
      <div class="row" id="createFields" style="max-height:220px;overflow-y:auto;padding:4px;border:1px solid #2b3140;border-radius:8px;margin-top:8px;"></div>
    </div>
    <div class="panel">
      <h3>Table (horizontal scroll)</h3>
      <div id="status" class="status">loading...</div>
      <div class="scroll">
        <table>
          <thead><tr id="theadRow"></tr></thead>
          <tbody id="rows"></tbody>
        </table>
      </div>
    </div>
  </div>
  <script>
    const rowsEl = document.getElementById('rows');
    const theadRow = document.getElementById('theadRow');
    const statusEl = document.getElementById('status');
    const createFields = document.getElementById('createFields');

    const COLS = [
      { k: 'id', label: 'id', ro: true, type: 'text' },
      { k: 'code', label: 'code', ro: true, type: 'text' },
      { k: 'name', label: 'name', type: 'text', wide: true },
      { k: 'damageMin', label: 'dmg↓', title: 'Damage min' },
      { k: 'damageMax', label: 'dmg↑', title: 'Damage max' },
      { k: 'damageType', label: 'dmg type', title: 'Damage type', type: 'text', wide: true },
      { k: 'range', label: 'range', title: 'Range (hexes)' },
      { k: 'attackApCost', label: 'AP shot', title: 'Single shot AP cost' },
      { k: 'burstRounds', label: 'burst N', title: 'Burst: rounds per use' },
      { k: 'burstApCost', label: 'burst AP', title: 'Burst: AP cost' },
      { k: 'spreadPenalty', label: 'spread', title: 'Spread penalty 0..1', step: 0.01, float: true },
      { k: 'trajectoryHeight', label: 'traj', title: 'Trajectory height 0..3' },
      { k: 'quality', label: 'qual', title: 'Quality' },
      { k: 'weaponCondition', label: 'cond', title: 'Condition' },
      { k: 'mass', label: 'mass', title: 'Mass', float: true, step: 0.01 },
      { k: 'caliber', label: 'cal', type: 'text', wide: true },
      { k: 'armorPierce', label: 'AP', title: 'Armor pierce' },
      { k: 'magazineSize', label: 'mag', title: 'Magazine size' },
      { k: 'reloadApCost', label: 'reload', title: 'Reload AP cost' },
      { k: 'category', label: 'cat', title: 'Weapon category', type: 'text', wide: true },
      { k: 'reqLevel', label: 'req Lv', title: 'Required level' },
      { k: 'reqStrength', label: 'req Str', title: 'Required strength' },
      { k: 'reqEndurance', label: 'req End', title: 'Required endurance' },
      { k: 'reqAccuracy', label: 'req Acc', title: 'Required accuracy' },
      { k: 'reqMasteryCategory', label: 'mast.', title: 'Mastery category key', type: 'text', wide: true },
      { k: 'statEffectStrength', label: 'ΔStr', title: 'Stat effect strength' },
      { k: 'statEffectEndurance', label: 'ΔEnd', title: 'Stat effect endurance' },
      { k: 'statEffectAccuracy', label: 'ΔAcc', title: 'Stat effect accuracy' },
      { k: 'isSniper', label: 'snp', title: 'Sniper range curve (p)', type: 'cb' },
      { k: 'iconKey', label: 'icon', title: 'icon_key', type: 'text' },
      { k: '_save', label: '', type: 'btn' }
    ];

    function numOr(v, d) {
      const n = parseInt(v, 10);
      return Number.isFinite(n) ? n : d;
    }
    function floatOr(v, d) {
      const n = parseFloat(v);
      return Number.isFinite(n) ? n : d;
    }

    function thFor(c) {
      const th = document.createElement('th');
      th.textContent = c.label;
      if (c.title) th.title = c.title;
      return th;
    }

    function inputFor(c, value, readOnly) {
      if (c.type === 'cb') {
        const td = document.createElement('td');
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.setAttribute('data-field', c.k);
        cb.checked = !!value;
        cb.title = c.title || '';
        td.appendChild(cb);
        return td;
      }
      if (c.type === 'btn') {
        const td = document.createElement('td');
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = 'Save';
        b.addEventListener('click', () => saveRow(b.closest('tr')));
        td.appendChild(b);
        return td;
      }
      const td = document.createElement('td');
      const i = document.createElement('input');
      i.type = c.type === 'text' ? 'text' : 'number';
      if (i.type === 'number') {
        if (c.step != null) i.step = String(c.step);
        if (c.float) i.step = i.step || 'any';
      }
      i.setAttribute('data-field', c.k);
      if (readOnly || c.ro) i.readOnly = true;
      if (c.wide) i.className = 'w-wide';
      if (value != null && value !== '') i.value = String(value);
      i.title = c.title || '';
      td.appendChild(i);
      return td;
    }

    COLS.forEach(c => theadRow.appendChild(thFor(c)));

    function defaultForKey(k) {
      if (k === 'quality' || k === 'weaponCondition') return 100;
      if (k === 'attackApCost') return 1;
      if (k === 'trajectoryHeight') return 1;
      if (k === 'reqLevel') return 1;
      if (k === 'damageMin' || k === 'damageMax') return 1;
      return 0;
    }

    function buildCreatePanel() {
      createFields.innerHTML = '';
      const frag = document.createDocumentFragment();
      COLS.filter(c => !c.ro && c.k !== '_save').forEach(c => {
        const lab = document.createElement('label');
        lab.style.marginRight = '10px';
        lab.style.display = 'inline-flex';
        lab.style.alignItems = 'center';
        lab.style.gap = '4px';
        const span = document.createElement('span');
        span.textContent = (c.title || c.label) + ':';
        span.style.color = '#7f8ea3';
        lab.appendChild(span);
        if (c.type === 'cb') {
          const cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.setAttribute('data-field', c.k);
          lab.appendChild(cb);
        } else {
          const i = document.createElement('input');
          i.type = c.type === 'text' ? 'text' : 'number';
          if (i.type === 'number') {
            if (c.step != null) i.step = String(c.step);
            if (c.float) i.step = i.step || 'any';
          }
          i.setAttribute('data-field', c.k);
          i.placeholder = c.k;
          i.style.width = c.wide ? '100px' : '64px';
          lab.appendChild(i);
        }
        frag.appendChild(lab);
      });
      createFields.appendChild(frag);
    }

    buildCreatePanel();

    function collectWeaponFields(root) {
      const o = {};
      root.querySelectorAll('[data-field]').forEach(el => {
        const k = el.getAttribute('data-field');
        if (el.type === 'checkbox') o[k] = !!el.checked;
        else if (el.type === 'number') {
          if (k === 'spreadPenalty' || k === 'mass') o[k] = floatOr(el.value, 0);
          else o[k] = numOr(el.value, 0);
        }
        else o[k] = (el.value || '').trim();
      });
      return o;
    }

    function weaponPayloadFromRow(tr) {
      const code = tr.dataset.code;
      const base = collectWeaponFields(tr);
      base.code = code;
      return base;
    }

    async function postWeapon(payload) {
      return fetch('/api/db/weapons', {
        method: 'POST',
        headers: {'Content-Type':'application/json'},
        body: JSON.stringify(payload)
      });
    }

    async function saveRow(tr) {
      const payload = weaponPayloadFromRow(tr);
      statusEl.textContent = 'saving ' + payload.code + '...';
      const resp = await postWeapon(payload);
      if (!resp.ok) {
        statusEl.textContent = 'save failed: ' + payload.code;
        return;
      }
      statusEl.textContent = 'saved: ' + payload.code;
      await load();
    }

    function rowFromWeapon(w) {
      const tr = document.createElement('tr');
      tr.dataset.code = w.code;
      COLS.forEach(c => {
        if (c.k === '_save') {
          tr.appendChild(inputFor(c, null, false));
          return;
        }
        let val = w[c.k];
        if (val === undefined || val === null) val = defaultForKey(c.k);
        tr.appendChild(inputFor(c, val, !!c.ro));
      });
      return tr;
    }

    async function load() {
      statusEl.textContent = 'loading...';
      const resp = await fetch('/api/db/weapons?take=200', { cache: 'no-store' });
      const list = await resp.json();
      rowsEl.innerHTML = '';
      for (const w of list) rowsEl.appendChild(rowFromWeapon(w));
      statusEl.textContent = `loaded ${list.length} weapons`;
    }

    document.getElementById('reloadBtn').addEventListener('click', () => load());

    document.getElementById('createSaveBtn').addEventListener('click', async () => {
      const code = (document.getElementById('c_code').value || '').trim();
      const name = (document.getElementById('c_name').value || '').trim();
      if (!code || !name) {
        statusEl.textContent = 'code and name required';
        return;
      }
      const payload = collectWeaponFields(createFields);
      payload.code = code;
      payload.name = name;
      statusEl.textContent = 'saving new ' + code + '...';
      const resp = await postWeapon(payload);
      if (!resp.ok) {
        statusEl.textContent = 'save failed';
        return;
      }
      document.getElementById('c_code').value = '';
      document.getElementById('c_name').value = '';
      statusEl.textContent = 'saved new: ' + code;
      await load();
    });

    load();
  </script>
</body>
</html>
""";
}
