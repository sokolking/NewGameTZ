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
    .wrap { padding: 16px; box-sizing: border-box; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .layout { margin-top: 12px; display: grid; grid-template-columns: 250px 1fr; gap: 12px; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 12px; }
    .menu h3 { margin: 0 0 10px; font-size: 16px; }
    .menu button {
      width: 100%; text-align: left; margin: 3px 0; padding: 7px 10px;
      border: 1px solid #2b3140; border-radius: 6px; background: #11151d; color: #e8ecf4; cursor: pointer; font-size: 12px;
    }
    .menu button.active { border-color: #7aa2ff; background: #1b263d; }
    .menu .sub { margin-left: 12px; }
    .toolbar { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 10px; }
    input, button, select {
      background: #11151d; border: 1px solid #2b3140; color: #e8ecf4; border-radius: 6px; padding: 6px 8px; font-size: 12px;
    }
    button { cursor: pointer; }
    .status { color: #9aa4b2; font-size: 12px; }
    .hint { color: #7f8ea3; font-size: 12px; margin: 6px 0; }
    table { border-collapse: collapse; width: 100%; font-size: 11px; }
    th, td { border-bottom: 1px solid #2b3140; text-align: left; padding: 4px; white-space: nowrap; }
    th { position: sticky; top: 0; background: #171a22; }
    .scroll { overflow: auto; max-height: calc(100vh - 210px); }
    .hidden { display: none !important; }
    .wide { min-width: 130px; }
    .small { width: 70px; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="nav">
      <a href="/db">Battles</a>
      <a href="/users">Users</a>
      <a href="/items">Items</a>
      <a href="/obstacle-balance">Obstacle balance</a>
    </div>
    <div class="layout">
      <div class="panel menu">
        <h3>Items</h3>
        <button data-item-type="" data-category="" class="active">Items / All</button>
        <button data-item-type="weapon" data-category="">Weapons</button>
        <div class="sub">
          <button data-item-type="weapon" data-category="cold">Cold</button>
          <button data-item-type="weapon" data-category="light">Light</button>
          <button data-item-type="weapon" data-category="medium">Medium</button>
          <button data-item-type="weapon" data-category="heavy">Heavy</button>
          <button data-item-type="weapon" data-category="throwing">Throwing</button>
        </div>
        <button data-item-type="ammo" data-category="">Ammo</button>
        <button data-item-type="medicine" data-category="">Medicine</button>
      </div>
      <div class="panel">
        <div class="toolbar">
          <input id="search" class="wide" placeholder="Search by name/icon" />
          <button id="reloadBtn" type="button">Reload</button>
          <button id="addBtn" type="button">Add row</button>
          <button id="exportBtn" type="button">Export items</button>
          <label>Import <input id="importInput" type="file" accept="application/json,.json" /></label>
          <span id="status" class="status">loading...</span>
        </div>
        <div class="hint">Catalog editor for items with weapon extension fields. Unused fields are hidden by item type/category.</div>
        <div class="scroll">
          <table>
            <thead><tr id="head"></tr></thead>
            <tbody id="rows"></tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
  <script>
    const COLS = [
      { k: 'itemId', label: 'id', ro: true, t: 'num' },
      { k: 'itemType', label: 'type', t: 'sel', opts: ['weapon', 'ammo', 'medicine'] },
      { k: 'name', label: 'name', t: 'txt', cls: 'wide' },
      { k: 'iconKey', label: 'icon', t: 'txt' },
      { k: 'mass', label: 'mass', t: 'num' },
      { k: 'quality', label: 'qual', t: 'num' },
      { k: 'condition', label: 'cond', t: 'num' },
      { k: 'inventoryGrid', label: 'handGrid', t: 'num', cls: 'small' },
      { k: 'inventorySlotWidth', label: 'slotW', t: 'num', cls: 'small' },
      { k: 'isEquippable', label: 'equip', t: 'cb' },
      { k: 'category', label: 'category', t: 'sel', opts: [] },
      { k: 'reqLevel', label: 'reqLv', t: 'num', cls: 'small' },
      { k: 'reqStrength', label: 'reqStr', t: 'num', cls: 'small' },
      { k: 'reqEndurance', label: 'reqEnd', t: 'num', cls: 'small' },
      { k: 'reqAccuracy', label: 'reqAcc', t: 'num', cls: 'small' },
      { k: 'reqMasteryCategory', label: 'reqMastery', t: 'txt' },
      { k: 'damageMin', label: 'dmgMin', t: 'num' },
      { k: 'damageMax', label: 'dmgMax', t: 'num' },
      { k: 'range', label: 'range', t: 'num' },
      { k: 'ammoTypeId', label: 'ammoType', t: 'ammo-sel' },
      { k: 'magazineSize', label: 'mag', t: 'num' },
      { k: 'reloadApCost', label: 'reloadAp', t: 'num' },
      { k: 'attackApCost', label: 'AP', t: 'num' },
      { k: 'effectType', label: 'fxType', t: 'txt' },
      { k: 'effectSign', label: 'fxSign', t: 'txt' },
      { k: 'effectMin', label: 'fxMin', t: 'num', cls: 'small' },
      { k: 'effectMax', label: 'fxMax', t: 'num', cls: 'small' },
      { k: 'effectTarget', label: 'fxTarget', t: 'txt' },
      { k: '_save', label: '', t: 'btn' },
      { k: '_del', label: '', t: 'del' }
    ];

    const HIDE_FOR_MEDICINE = new Set(['damageMin','damageMax','range','ammoTypeId','magazineSize','reloadApCost']);
    const HIDE_FOR_AMMO = new Set(['category','damageMin','damageMax','range','attackApCost','ammoTypeId','magazineSize','reloadApCost','reqLevel','reqStrength','reqEndurance','reqAccuracy','reqMasteryCategory','effectType','effectSign','effectMin','effectMax','effectTarget','inventorySlotWidth']);
    const HIDE_FOR_COLD = new Set(['ammoTypeId','magazineSize','reloadApCost']);
    const EFFECT_KEYS = new Set(['effectType','effectSign','effectMin','effectMax','effectTarget']);
    const WEAPON_CATEGORY_OPTS = ['cold', 'light', 'medium', 'heavy', 'throwing'];
    const MEDICINE_CATEGORY_OPTS = ['medicine', 'medkit', 'bandage', 'stimulant', 'injectable', 'first_aid', 'antidote', 'other'];

    function categoryOptsForRow(row) {
      const t = String(row.itemType || '').toLowerCase();
      if (t === 'medicine') return MEDICINE_CATEGORY_OPTS;
      return WEAPON_CATEGORY_OPTS;
    }

    const rowsEl = document.getElementById('rows');
    const headEl = document.getElementById('head');
    const searchEl = document.getElementById('search');
    const statusEl = document.getElementById('status');
    const menuButtons = [...document.querySelectorAll('.menu button[data-item-type]')];
    let currentItemType = '';
    let currentCategory = '';
    let ammoItems = [];

    async function loadAmmoItems() {
      const resp = await fetch('/api/db/items/catalog?itemType=ammo&take=500', { cache: 'no-store' });
      ammoItems = await resp.json().catch(() => []);
    }

    function fieldHidden(k, row) {
      const t = String(row.itemType || '').toLowerCase();
      const c = String(row.category || '').toLowerCase();
      if (EFFECT_KEYS.has(k)) return t !== 'medicine';
      if (t === 'ammo' && HIDE_FOR_AMMO.has(k)) return true;
      if (t === 'medicine' && HIDE_FOR_MEDICINE.has(k)) return true;
      if (t === 'weapon' && c === 'cold' && HIDE_FOR_COLD.has(k)) return true;
      return false;
    }

    function buildHead() {
      headEl.innerHTML = '';
      for (const c of COLS) {
        const th = document.createElement('th');
        th.textContent = c.label;
        th.dataset.col = c.k;
        headEl.appendChild(th);
      }
    }

    function valueForInput(el, col) {
      if (col.t === 'cb') return !!el.checked;
      if (col.t === 'num' || col.t === 'ammo-sel') return Number.isFinite(Number(el.value)) ? Number(el.value) : 0;
      return (el.value || '').trim();
    }

    function collectRowAll(tr) {
      const o = {};
      tr.querySelectorAll('[data-field]').forEach(el => {
        const k = el.dataset.field;
        const col = COLS.find(x => x.k === k);
        if (!col) return;
        o[k] = valueForInput(el, col);
      });
      o.itemId = Number(tr.dataset.itemId || 0);
      return o;
    }

    /** Payload for save: only fields that apply to this row (visible cells — not sent if N/A for type). */
    function collectRow(tr) {
      const o = {};
      tr.querySelectorAll('td[data-col]').forEach(td => {
        if (td.classList.contains('hidden')) return;
        const el = td.querySelector('[data-field]');
        if (!el) return;
        const k = el.dataset.field;
        const col = COLS.find(x => x.k === k);
        if (!col) return;
        o[k] = valueForInput(el, col);
      });
      o.itemId = Number(tr.dataset.itemId || 0);
      return o;
    }

    function syncHeaderVisibility() {
      const rows = [...rowsEl.querySelectorAll('tr')];
      for (const c of COLS) {
        const th = headEl.querySelector('th[data-col="' + c.k + '"]');
        if (!th) continue;
        if (rows.length === 0) {
          th.classList.remove('hidden');
          continue;
        }
        let any = false;
        for (const tr of rows) {
          const data = collectRowAll(tr);
          if (!fieldHidden(c.k, data)) { any = true; break; }
        }
        th.classList.toggle('hidden', !any);
      }
    }

    function renderCell(row, col) {
      const td = document.createElement('td');
      td.dataset.col = col.k;
      if (col.t === 'btn') {
        const b = document.createElement('button');
        b.textContent = 'Save';
        b.onclick = () => saveRow(td.closest('tr'));
        td.appendChild(b);
      } else if (col.t === 'del') {
        const b = document.createElement('button');
        b.textContent = 'Delete';
        b.onclick = () => deleteRow(td.closest('tr'));
        td.appendChild(b);
      } else if (col.t === 'sel') {
        const s = document.createElement('select');
        s.dataset.field = col.k;
        const opts = (col.k === 'category') ? categoryOptsForRow(row) : col.opts;
        const cur = String(row[col.k] || '').trim();
        let matched = false;
        for (const v of opts) {
          const o = document.createElement('option');
          o.value = v; o.textContent = v;
          if (cur === v) { o.selected = true; matched = true; }
          s.appendChild(o);
        }
        if (cur && !matched) {
          const o = document.createElement('option');
          o.value = cur; o.textContent = cur + ' (custom)';
          o.selected = true;
          s.insertBefore(o, s.firstChild);
        }
        td.appendChild(s);
      } else if (col.t === 'ammo-sel') {
        const s = document.createElement('select');
        s.dataset.field = col.k;
        const none = document.createElement('option');
        none.value = '0'; none.textContent = '— none —';
        s.appendChild(none);
        for (const a of ammoItems) {
          const o = document.createElement('option');
          o.value = String(a.itemId || 0);
          o.textContent = a.name || ('item #' + a.itemId);
          s.appendChild(o);
        }
        s.value = String(row[col.k] || 0);
        td.appendChild(s);
      } else {
        const i = document.createElement('input');
        i.dataset.field = col.k;
        if (col.t === 'cb') {
          i.type = 'checkbox';
          i.checked = !!row[col.k];
        } else {
          i.type = col.t === 'num' ? 'number' : 'text';
          if (row[col.k] !== null && row[col.k] !== undefined) i.value = String(row[col.k]);
          if (col.cls) i.className = col.cls;
          if (col.ro) i.readOnly = true;
        }
        td.appendChild(i);
      }
      return td;
    }

    function applyVisibility(tr) {
      const data = collectRowAll(tr);
      for (const c of COLS) {
        const td = tr.querySelector('td[data-col="' + c.k + '"]');
        if (!td) continue;
        td.classList.toggle('hidden', fieldHidden(c.k, data));
      }
    }

    function refreshCategorySelect(tr) {
      const data = collectRowAll(tr);
      const td = tr.querySelector('td[data-col="category"]');
      if (!td) return;
      const opts = categoryOptsForRow(data);
      const cur = String(data.category || '').trim();
      td.innerHTML = '';
      const s = document.createElement('select');
      s.dataset.field = 'category';
      let matched = false;
      for (const v of opts) {
        const o = document.createElement('option');
        o.value = v; o.textContent = v;
        if (cur === v) { o.selected = true; matched = true; }
        s.appendChild(o);
      }
      if (cur && !matched) {
        const o = document.createElement('option');
        o.value = cur; o.textContent = cur + ' (custom)';
        o.selected = true;
        s.insertBefore(o, s.firstChild);
      }
      s.addEventListener('change', () => {
        applyVisibility(tr);
        syncHeaderVisibility();
      });
      td.appendChild(s);
    }

    function renderRow(r) {
      const tr = document.createElement('tr');
      tr.dataset.itemId = String(r.itemId || 0);
      COLS.forEach(c => tr.appendChild(renderCell(r, c)));
      tr.querySelector('[data-field="itemType"]')?.addEventListener('change', () => {
        refreshCategorySelect(tr);
        applyVisibility(tr);
        syncHeaderVisibility();
      });
      tr.querySelector('[data-field="category"]')?.addEventListener('change', () => {
        applyVisibility(tr);
        syncHeaderVisibility();
      });
      applyVisibility(tr);
      return tr;
    }

    async function load() {
      statusEl.textContent = 'loading...';
      const p = new URLSearchParams();
      p.set('take', '1000');
      if (currentItemType) p.set('itemType', currentItemType);
      if (currentCategory) p.set('weaponCategory', currentCategory);
      if (searchEl.value.trim()) p.set('q', searchEl.value.trim());
      const resp = await fetch('/api/db/items/catalog?' + p.toString(), { cache: 'no-store' });
      const list = await resp.json().catch(() => []);
      rowsEl.innerHTML = '';
      (Array.isArray(list) ? list : []).forEach(r => rowsEl.appendChild(renderRow(r)));
      syncHeaderVisibility();
      statusEl.textContent = 'loaded ' + rowsEl.children.length + ' rows';
    }

    async function saveRow(tr) {
      const payload = collectRow(tr);
      if (!payload.name) { statusEl.textContent = 'name required'; return; }
      const resp = await fetch('/api/db/items/catalog', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!resp.ok) {
        const e = await resp.json().catch(() => ({}));
        statusEl.textContent = 'save failed: ' + (e.error || resp.status);
        return;
      }
      await load();
      statusEl.textContent = 'saved';
    }

    async function deleteRow(tr) {
      const id = Number(tr.dataset.itemId || 0);
      if (!id) return;
      if (!confirm('Delete item #' + id + '?')) return;
      const resp = await fetch('/api/db/items/catalog/' + id, { method: 'DELETE' });
      if (!resp.ok) {
        const e = await resp.json().catch(() => ({}));
        statusEl.textContent = 'delete failed: ' + (e.error || resp.status);
        return;
      }
      await load();
      statusEl.textContent = 'deleted';
    }

    document.getElementById('reloadBtn').onclick = () => load();
    document.getElementById('addBtn').onclick = async () => {
      let nextId = 0;
      try {
        const resp = await fetch('/api/db/items/catalog/next-id', { cache: 'no-store' });
        const json = await resp.json().catch(() => ({}));
        nextId = Number(json?.itemId || 0);
      } catch {}
      const it = currentItemType || 'weapon';
      const defaultCat = it === 'medicine' ? 'medicine' : (currentCategory || 'cold');
      const row = {
        itemId: nextId > 0 ? nextId : 0, itemType: it, name: '', iconKey: '', mass: 0,
        quality: 100, condition: 100, inventoryGrid: 1, isEquippable: true,
        category: defaultCat, damageMin: 1, damageMax: 1, range: 1, attackApCost: 1
      };
      rowsEl.prepend(renderRow(row));
      syncHeaderVisibility();
      statusEl.textContent = 'new row added';
    };
    searchEl.oninput = () => load();
    document.getElementById('exportBtn').onclick = async () => {
      const resp = await fetch('/api/db/items/catalog/export', { cache: 'no-store' });
      if (!resp.ok) { statusEl.textContent = 'export failed'; return; }
      const text = await resp.text();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([text], { type: 'application/json;charset=utf-8' }));
      a.download = 'items-catalog-export.json';
      a.click();
      URL.revokeObjectURL(a.href);
      statusEl.textContent = 'exported';
    };
    document.getElementById('importInput').onchange = async (e) => {
      const f = e.target.files && e.target.files[0];
      if (!f) return;
      const text = await f.text();
      const resp = await fetch('/api/db/items/catalog/import', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: text });
      if (!resp.ok) {
        const er = await resp.json().catch(() => ({}));
        statusEl.textContent = 'import failed: ' + (er.error || resp.status);
        return;
      }
      e.target.value = '';
      await load();
      statusEl.textContent = 'import ok';
    };

    buildHead();
    (async () => {
      await loadAmmoItems();
      menuButtons.forEach(btn => {
        btn.onclick = () => {
          menuButtons.forEach(x => x.classList.remove('active'));
          btn.classList.add('active');
          currentItemType = (btn.dataset.itemType || '').toLowerCase();
          currentCategory = (btn.dataset.category || '').toLowerCase();
          load();
        };
      });
      load();
    })();
  </script>
</body>
</html>
""";
}
