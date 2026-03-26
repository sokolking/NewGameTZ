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
    table input[type="text"], table input[type="number"], table select { min-width: 52px; max-width: 96px; width: 100%; box-sizing: border-box; }
    table input.w-wide, table select.w-wide { max-width: 140px; }
    .scroll { overflow-x: auto; max-width: 100%; }
    .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-top: 8px; }
    .status { color: #9aa4b2; font-size: 13px; margin-top: 8px; }
    .hint { color: #7f8ea3; font-size: 12px; margin-top: 6px; line-height: 1.4; }
    h3 { margin: 0 0 8px 0; }
    .param-help-pop { position: fixed; z-index: 9999; max-width: 340px; padding: 10px 12px; background: #1e2430; border: 1px solid #4a5568; border-radius: 8px; box-shadow: 0 8px 24px rgba(0,0,0,.45); font-size: 12px; line-height: 1.45; color: #e8ecf4; white-space: pre-wrap; display: none; pointer-events: none; }
    .param-help-pop.visible { display: block; }
    [data-param-key] { cursor: context-menu; }
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
      <p class="hint">Right-click any parameter (table header, cell, or create form label) for a short explanation in Russian. Row <strong>Save</strong> uses <code>POST /api/db/weapons</code>. <strong>Save table to DB</strong> calls <code>PUT /api/db/weapons/replace</code> (truncates <code>weapons</code> and inserts all visible rows — the set must include <code>fist</code>). <strong>Download SQL</strong> / import: <code>GET/POST /api/db/weapons/sql-export|sql-import</code>. Damage type and category use dropdowns from distinct DB values (current cell value kept if not in list). Numeric <strong>-1</strong> in most combat columns means <em>not applicable</em> (stored as-is; battle code substitutes safe defaults — e.g. melee range 1, spread 0). For stat-effect columns, only exactly <strong>-1</strong> is N/A (other negatives can be real penalties).</p>
      <div class="row" id="createBar">
        <input id="c_code" placeholder="code" />
        <input id="c_name" placeholder="name" />
        <button id="createSaveBtn" type="button">Save new</button>
        <button id="reloadBtn" type="button">Reload table</button>
        <button id="saveAllBtn" type="button">Save table to DB</button>
        <button id="exportSqlBtn" type="button">Download SQL</button>
        <label class="hint" style="display:inline-flex;align-items:center;gap:6px;">Import SQL
          <input id="importSqlInput" type="file" accept=".sql,text/plain,.txt" style="max-width:200px;" />
        </label>
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

    const WEAPON_CATEGORY_LABELS = {
      cold: 'Холодное',
      light: 'Легкое',
      medium: 'Среднее',
      heavy: 'Тяжелое',
      throwing: 'Метательное',
      medicine: 'Медицина'
    };

    const COLS = [
      { k: 'id', label: 'id', ro: true, type: 'text' },
      { k: 'code', label: 'code', ro: true, type: 'text' },
      { k: 'name', label: 'name', type: 'text', wide: true },
      { k: 'damageMin', label: 'dmg↓', title: 'Damage min' },
      { k: 'damageMax', label: 'dmg↑', title: 'Damage max' },
      { k: 'damageType', label: 'dmg type', title: 'Damage type', type: 'selectMeta', metaList: 'damageTypes', wide: true },
      { k: 'range', label: 'range', title: 'Range (hexes)' },
      { k: 'inventorySlotWidth', label: 'inv W', title: 'Inventory grid width: 1 or 2 cells' },
      { k: 'attackApCost', label: 'AP shot', title: 'Single shot AP cost' },
      { k: 'burstRounds', label: 'burst N', title: 'Burst: rounds per use' },
      { k: 'burstApCost', label: 'burst AP', title: 'Burst: AP cost' },
      { k: 'tightness', label: 'tight', title: 'Tightness 0..1 (higher = tighter grouping, better hit chance)', step: 0.01, float: true },
      { k: 'trajectoryHeight', label: 'traj', title: 'Trajectory height 0..3' },
      { k: 'quality', label: 'qual', title: 'Quality' },
      { k: 'weaponCondition', label: 'cond', title: 'Condition' },
      { k: 'mass', label: 'mass', title: 'Mass', float: true, step: 0.01 },
      { k: 'caliber', label: 'cal', type: 'text', wide: true },
      { k: 'armorPierce', label: 'AP', title: 'Armor pierce' },
      { k: 'magazineSize', label: 'mag', title: 'Magazine size' },
      { k: 'reloadApCost', label: 'reload', title: 'Reload AP cost' },
      { k: 'category', label: 'cat', title: 'Weapon category', type: 'selectMeta', metaList: 'categories', wide: true, optionLabels: WEAPON_CATEGORY_LABELS },
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
      { k: '_del', label: '', type: 'del' },
      { k: '_save', label: '', type: 'btn' }
    ];

    const PARAM_HELP_RU = {
      id: 'Внутренний числовой id строки в таблице weapons (PostgreSQL).',
      code: 'Уникальный код оружия: на него ссылаются клиент, экипировка и API. Менять у уже существующего оружия рискованно.',
      name: 'Название для интерфейса и отладки.',
      damageMin: 'Минимальный урон за одно попадание (в паре с максимумом задаёт разброс).',
      damageMax: 'Максимальный урон за одно попадание.',
      damageType: 'Тип урона (например physical). Задел под сопротивления и правила; не всё может уже учитываться в бою.',
      range: 'Дальность атаки в гексах.',
      inventorySlotWidth: 'Сколько ячеек сетки инвентаря занимает предмет по ширине: 1 или 2.',
      attackApCost: 'Сколько очков действия (ОД) стоит один выстрел или удар.',
      burstRounds: 'Сколько выстрелов в одной очереди.',
      burstApCost: 'Сколько ОД стоит выстрел очередью целиком.',
      tightness: 'Кучность оружия от 0 до 1: чем выше — тем кучнее стрельба и тем выше шанс попадания (из итоговой вероятности вычитается 1 − кучность).',
      trajectoryHeight: 'Уровень «высоты» траектории для линии огня через стены (0…3). Чем больше число, тем реже преграда на пути блокирует выстрел.',
      quality: 'Показатель качества предмета (торговля, износ и связанные правила — по дизайну игры).',
      weaponCondition: 'Состояние / износ оружия.',
      mass: 'Масса (число; конкретная единица — по дизайну: перегруз, требования и т.д.).',
      caliber: 'Обозначение калибра (текст) для отображения и описания.',
      armorPierce: 'Пробитие брони — задел под снижение защиты цели; в текущей версии сервера может не участвовать в расчёте боя.',
      magazineSize: 'Патронов в магазине до перезарядки.',
      reloadApCost: 'Очки действия на перезарядку.',
      category: 'Категория оружия (холодное, лёгкое и т.д.): мастерство, UI и связанные правила.',
      reqLevel: 'Минимальный уровень персонажа для использования.',
      reqStrength: 'Требуемая сила для экипировки.',
      reqEndurance: 'Требуемая выносливость.',
      reqAccuracy: 'Требуемая меткость.',
      reqMasteryCategory: 'Ключ категории мастерства, к которой относится оружие.',
      statEffectStrength: 'Бонус или штраф к силе при экипировке. Для колонок stat-effect только −1 означает «не применимо».',
      statEffectEndurance: 'Бонус или штраф к выносливости при экипировке. Только −1 = «не применимо».',
      statEffectAccuracy: 'Бонус или штраф к меткости при экипировке. Только −1 = «не применимо».',
      isSniper: 'Если включено, для шанса попадания используется отдельная (снайперская) зависимость от дистанции.',
      iconKey: 'Имя ключа иконки на клиенте.',
      _del: 'Удалить это оружие из базы (после подтверждения в диалоге).',
      _save: 'Сохранить только эту строку на сервер (POST одной записи), не перезаписывая всю таблицу.'
    };

    let metaDamageTypes = [];
    let metaCategories = [];

    async function refreshMeta() {
      const resp = await fetch('/api/db/weapons/meta', { cache: 'no-store' });
      const m = await resp.json();
      metaDamageTypes = Array.isArray(m.damageTypes) ? m.damageTypes : [];
      metaCategories = Array.isArray(m.categories) ? m.categories : [];
    }

    function optionsForMeta(metaList, current) {
      const base = metaList === 'damageTypes' ? metaDamageTypes : metaCategories;
      const set = new Set(base.map(String));
      if (current != null && String(current) !== '') set.add(String(current));
      return [...set].sort((a, b) => a.localeCompare(b));
    }

    function fillSelect(sel, metaList, current, optionLabels) {
      sel.innerHTML = '';
      for (const v of optionsForMeta(metaList, current)) {
        const o = document.createElement('option');
        o.value = v;
        o.textContent = (optionLabels && optionLabels[v]) ? optionLabels[v] : v;
        if (String(current) === v) o.selected = true;
        sel.appendChild(o);
      }
    }

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
      th.dataset.paramKey = c.k;
      if (c.title) th.title = c.title;
      return th;
    }

    function inputFor(c, value, readOnly) {
      if (c.type === 'selectMeta') {
        const td = document.createElement('td');
        td.dataset.paramKey = c.k;
        const sel = document.createElement('select');
        sel.setAttribute('data-field', c.k);
        if (readOnly || c.ro) sel.disabled = true;
        if (c.wide) sel.className = 'w-wide';
        sel.title = c.title || '';
        fillSelect(sel, c.metaList, value != null && value !== '' ? value : (c.k === 'damageType' ? 'physical' : 'cold'));
        td.appendChild(sel);
        return td;
      }
      if (c.type === 'del') {
        const td = document.createElement('td');
        td.dataset.paramKey = c.k;
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = 'Delete';
        b.addEventListener('click', (ev) => deleteRow(ev.currentTarget.closest('tr')));
        td.appendChild(b);
        return td;
      }
      if (c.type === 'cb') {
        const td = document.createElement('td');
        td.dataset.paramKey = c.k;
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
        td.dataset.paramKey = c.k;
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = 'Save';
        b.addEventListener('click', () => saveRow(b.closest('tr')));
        td.appendChild(b);
        return td;
      }
      const td = document.createElement('td');
      td.dataset.paramKey = c.k;
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
      if (k === 'inventorySlotWidth') return 1;
      if (k === 'tightness') return 1;
      return 0;
    }

    function buildCreatePanel() {
      createFields.innerHTML = '';
      const frag = document.createDocumentFragment();
      COLS.filter(c => !c.ro && c.k !== '_save' && c.k !== '_del').forEach(c => {
        const lab = document.createElement('label');
        lab.dataset.paramKey = c.k;
        lab.style.marginRight = '10px';
        lab.style.display = 'inline-flex';
        lab.style.alignItems = 'center';
        lab.style.gap = '4px';
        const span = document.createElement('span');
        span.textContent = (c.title || c.label) + ':';
        span.style.color = '#7f8ea3';
        span.dataset.paramKey = c.k;
        lab.appendChild(span);
        if (c.type === 'cb') {
          const cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.setAttribute('data-field', c.k);
          cb.dataset.paramKey = c.k;
          if (c.title) cb.title = c.title;
          lab.appendChild(cb);
        } else if (c.type === 'selectMeta') {
          const sel = document.createElement('select');
          sel.setAttribute('data-field', c.k);
          sel.dataset.paramKey = c.k;
          if (c.title) sel.title = c.title;
          const def = c.k === 'damageType' ? 'physical' : 'cold';
          fillSelect(sel, c.metaList, def, c.optionLabels);
          sel.style.width = c.wide ? '100px' : '72px';
          lab.appendChild(sel);
        } else {
          const i = document.createElement('input');
          i.type = c.type === 'text' ? 'text' : 'number';
          if (i.type === 'number') {
            if (c.step != null) i.step = String(c.step);
            if (c.float) i.step = i.step || 'any';
          }
          i.setAttribute('data-field', c.k);
          i.dataset.paramKey = c.k;
          if (c.title) i.title = c.title;
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
        if (el.tagName === 'SELECT') o[k] = (el.value || '').trim();
        else if (el.type === 'checkbox') o[k] = !!el.checked;
        else if (el.type === 'number') {
          if (k === 'tightness' || k === 'mass') o[k] = floatOr(el.value, 0);
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

    async function deleteRow(tr) {
      if (!tr || !tr.dataset.code) return;
      const code = tr.dataset.code;
      if (!confirm('Delete weapon "' + code + '"?')) return;
      statusEl.textContent = 'deleting ' + code + '...';
      const resp = await fetch('/api/db/weapons/' + encodeURIComponent(code), { method: 'DELETE' });
      if (!resp.ok) {
        const err = await resp.json().catch(() => ({}));
        statusEl.textContent = 'delete failed: ' + (err.error || resp.status);
        return;
      }
      statusEl.textContent = 'deleted: ' + code;
      await load();
    }

    async function saveAllTable() {
      const trs = [...rowsEl.querySelectorAll('tr')];
      const payloads = trs.map(tr => weaponPayloadFromRow(tr));
      if (!payloads.some(p => String(p.code || '').toLowerCase() === 'fist')) {
        statusEl.textContent = 'Cannot replace: table must include weapon code "fist".';
        return;
      }
      statusEl.textContent = 'replacing DB with ' + payloads.length + ' rows...';
      const resp = await fetch('/api/db/weapons/replace', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payloads)
      });
      if (!resp.ok) {
        const err = await resp.json().catch(() => ({}));
        statusEl.textContent = 'replace failed: ' + (err.error || resp.status);
        return;
      }
      statusEl.textContent = 'table saved (' + payloads.length + ' weapons)';
      await load();
    }

    async function exportSql() {
      statusEl.textContent = 'downloading SQL...';
      const resp = await fetch('/api/db/weapons/sql-export', { cache: 'no-store' });
      if (!resp.ok) {
        statusEl.textContent = 'export failed: ' + resp.status;
        return;
      }
      const text = await resp.text();
      const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = 'weapons-export.sql';
      a.click();
      URL.revokeObjectURL(a.href);
      statusEl.textContent = 'SQL downloaded';
    }

    function rowFromWeapon(w) {
      const tr = document.createElement('tr');
      tr.dataset.code = w.code;
      COLS.forEach(c => {
        if (c.k === '_save') {
          tr.appendChild(inputFor(c, null, false));
          return;
        }
        if (c.k === '_del') {
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
      await refreshMeta();
      buildCreatePanel();
      const resp = await fetch('/api/db/weapons?take=500', { cache: 'no-store' });
      const list = await resp.json();
      rowsEl.innerHTML = '';
      for (const w of list) rowsEl.appendChild(rowFromWeapon(w));
      statusEl.textContent = 'loaded ' + list.length + ' weapons';
    }

    document.getElementById('reloadBtn').addEventListener('click', () => load());
    document.getElementById('saveAllBtn').addEventListener('click', () => saveAllTable());
    document.getElementById('exportSqlBtn').addEventListener('click', () => exportSql());
    document.getElementById('importSqlInput').addEventListener('change', async (e) => {
      const input = e.target;
      const f = input.files && input.files[0];
      if (!f) return;
      const text = await f.text();
      statusEl.textContent = 'importing SQL...';
      const resp = await fetch('/api/db/weapons/sql-import', {
        method: 'POST',
        headers: { 'Content-Type': 'text/plain;charset=utf-8' },
        body: text
      });
      input.value = '';
      if (!resp.ok) {
        const err = await resp.json().catch(() => ({}));
        statusEl.textContent = 'import failed: ' + (err.error || resp.status);
        return;
      }
      statusEl.textContent = 'SQL import OK';
      await load();
    });

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

    (function initParamHelpRu() {
      let pop = null;
      function hidePop() {
        if (!pop) return;
        pop.classList.remove('visible');
        pop.style.display = 'none';
      }
      function showPop(clientX, clientY, text) {
        if (!pop) {
          pop = document.createElement('div');
          pop.className = 'param-help-pop';
          pop.setAttribute('role', 'tooltip');
          document.body.appendChild(pop);
        }
        pop.textContent = text;
        pop.style.display = 'block';
        pop.classList.add('visible');
        const pad = 10;
        requestAnimationFrame(() => {
          const r = pop.getBoundingClientRect();
          const vw = window.innerWidth;
          const vh = window.innerHeight;
          let left = clientX + pad;
          let top = clientY + pad;
          if (left + r.width > vw - 8) left = Math.max(8, vw - r.width - 8);
          if (top + r.height > vh - 8) top = Math.max(8, clientY - r.height - pad);
          pop.style.left = left + 'px';
          pop.style.top = top + 'px';
        });
      }
      document.addEventListener('contextmenu', (e) => {
        const host = e.target.closest('[data-param-key]');
        if (!host) return;
        const text = PARAM_HELP_RU[host.dataset.paramKey];
        if (!text) return;
        e.preventDefault();
        showPop(e.clientX, e.clientY, text);
      }, true);
      document.addEventListener('mousedown', (e) => {
        if (e.button === 0) hidePop();
      }, true);
      document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') hidePop();
      });
      window.addEventListener('scroll', hidePop, true);
      window.addEventListener('resize', hidePop);
    })();

    load();
  </script>
</body>
</html>
""";
}
