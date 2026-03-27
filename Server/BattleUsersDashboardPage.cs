namespace BattleServer;

public static class BattleUsersDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Users Browser</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #0f1117;
      --panel: #171a22;
      --panel-alt: #11151d;
      --border: #2b3140;
      --text: #e8ecf4;
      --muted: #9aa4b2;
      --accent: #7aa2ff;
      --accent-soft: rgba(122,162,255,.12);
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: var(--bg);
      color: var(--text);
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
    }
    .topbar {
      position: sticky;
      top: 0;
      z-index: 20;
      background: rgba(15,17,23,.92);
      backdrop-filter: blur(10px);
      border-bottom: 1px solid var(--border);
      padding: 14px 18px;
    }
    .title {
      margin: 0 0 10px;
      font-size: 20px;
    }
    .controls {
      display: flex;
      gap: 10px;
      flex-wrap: wrap;
      align-items: center;
    }
    .nav {
      display: inline-flex;
      gap: 8px;
      margin-right: 8px;
    }
    .nav a,
    button {
      background: var(--panel);
      border: 1px solid var(--border);
      color: var(--text);
      border-radius: 10px;
      padding: 9px 12px;
      cursor: pointer;
      text-decoration: none;
    }
    .nav a.active {
      border-color: var(--accent);
      background: var(--accent-soft);
    }
    input[type="search"] {
      background: var(--panel);
      border: 1px solid var(--border);
      color: var(--text);
      border-radius: 10px;
      padding: 9px 12px;
      min-width: 260px;
    }
    .status {
      margin-left: auto;
      color: var(--muted);
      font-size: 13px;
    }
    .layout {
      padding: 14px 18px 20px;
      min-height: calc(100vh - 90px);
    }
    .card {
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 14px;
      overflow: hidden;
      min-height: 220px;
      display: flex;
      flex-direction: column;
    }
    .card h2 {
      margin: 0;
      padding: 14px 14px 10px;
      font-size: 15px;
      border-bottom: 1px solid var(--border);
    }
    .list {
      overflow: auto;
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 12px;
    }
    .row {
      border: 1px solid var(--border);
      background: var(--panel-alt);
      border-radius: 12px;
      padding: 10px 12px;
    }
    .primary {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 12px;
      font-weight: 700;
      margin-bottom: 6px;
      word-break: break-all;
    }
    .meta {
      color: var(--muted);
      font-size: 12px;
      display: flex;
      gap: 10px;
      flex-wrap: wrap;
    }
    .empty {
      color: var(--muted);
      padding: 16px 14px;
      font-size: 13px;
    }
    .row-user {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;
    }
    .row-main {
      flex: 1;
      min-width: 0;
    }
    .edit-btn {
      flex-shrink: 0;
      font-size: 12px;
      padding: 8px 12px;
    }
    dialog#editUser,
    dialog#invEditor,
    dialog#ammoEditor {
      background: var(--panel);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: 14px;
      padding: 0;
      max-width: min(420px, 96vw);
    }
    dialog#invEditor,
    dialog#ammoEditor {
      max-width: min(560px, 96vw);
    }
    dialog#editUser::backdrop,
    dialog#invEditor::backdrop,
    dialog#ammoEditor::backdrop {
      background: rgba(0,0,0,.55);
    }
    dialog#editUser h3,
    dialog#invEditor h3,
    dialog#ammoEditor h3 {
      margin: 0;
      padding: 14px 16px;
      font-size: 15px;
      border-bottom: 1px solid var(--border);
    }
    .edit-form {
      padding: 14px 16px 16px;
      display: flex;
      flex-direction: column;
      gap: 10px;
    }
    .edit-form label {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 12px;
      color: var(--muted);
    }
    .edit-form input,
    .edit-form select {
      background: var(--panel-alt);
      border: 1px solid var(--border);
      color: var(--text);
      border-radius: 8px;
      padding: 8px 10px;
      font-size: 13px;
    }
    .edit-actions {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
      margin-top: 4px;
    }
  </style>
</head>
<body>
  <div class="topbar">
    <h1 class="title">Users Browser</h1>
    <div class="controls">
      <div class="nav">
        <a href="/db">Battles</a>
        <a href="/users" class="active">Users</a>
        <a href="/weapons">Weapons</a>
        <a href="/ammo">Ammo</a>
        <a href="/obstacle-balance">Obstacle balance</a>
      </div>
      <input id="userSearch" type="search" placeholder="Search username">
      <button id="refreshBtn" type="button">Refresh</button>
      <div id="status" class="status">loading...</div>
    </div>
  </div>

  <div class="layout">
    <section class="card">
      <h2>Users</h2>
      <div id="userList" class="list"></div>
    </section>
  </div>

  <dialog id="editUser">
    <h3>Edit user</h3>
    <form class="edit-form" id="editForm">
      <input type="hidden" id="editId" />
      <label>Username
        <input id="editUsername" type="text" autocomplete="off" required />
      </label>
      <label>New password <span style="font-weight:400">(leave empty to keep)</span>
        <input id="editPassword" type="password" autocomplete="new-password" />
      </label>
      <label>Experience
        <input id="editExperience" type="number" min="0" step="1" required />
      </label>
      <label>Strength
        <input id="editStrength" type="number" min="0" step="1" required />
      </label>
      <label>Endurance
        <input id="editEndurance" type="number" min="0" step="1" required />
      </label>
      <label>Accuracy
        <input id="editAccuracy" type="number" min="0" step="1" required />
      </label>
      <p class="hint" style="margin:0;font-size:12px;">Equipped weapon and bag: use <strong>Inventory</strong> on the user row.</p>
      <div class="edit-actions">
        <button type="button" id="editCancel">Cancel</button>
        <button type="submit" id="editSave">Save</button>
      </div>
    </form>
  </dialog>

  <dialog id="itemsEditor">
    <h3>User items</h3>
    <p class="hint" style="margin:0 0 10px;font-size:12px;line-height:1.4;">
      Single list of entities: <strong>weapon</strong> and <strong>ammo</strong>. Ammo is stackable (<code>quantity</code>), weapon is non-stackable.
      For weapons: keep exactly one equipped and include <code>fist</code>. Weapon slot uses inventory grid 0..11.
    </p>
    <div id="itemsRows" style="display:flex;flex-direction:column;gap:8px;max-height:360px;overflow:auto;"></div>
    <div class="edit-actions" style="margin-top:10px;">
      <button type="button" id="itemsAddRow">Add row</button>
    </div>
    <div class="edit-actions">
      <button type="button" id="itemsCancel">Cancel</button>
      <button type="button" id="itemsSave">Save items</button>
    </div>
  </dialog>

  <script>
    const userListEl = document.getElementById('userList');
    const userSearchEl = document.getElementById('userSearch');
    const refreshBtnEl = document.getElementById('refreshBtn');
    const statusEl = document.getElementById('status');
    const editDialog = document.getElementById('editUser');
    const editForm = document.getElementById('editForm');
    const editIdEl = document.getElementById('editId');
    const editUsernameEl = document.getElementById('editUsername');
    const editPasswordEl = document.getElementById('editPassword');
    const editExperienceEl = document.getElementById('editExperience');
    const editStrengthEl = document.getElementById('editStrength');
    const editEnduranceEl = document.getElementById('editEndurance');
    const editAccuracyEl = document.getElementById('editAccuracy');
    const editCancelBtn = document.getElementById('editCancel');
    const itemsDialog = document.getElementById('itemsEditor');
    const itemsRowsEl = document.getElementById('itemsRows');
    const itemsAddRowBtn = document.getElementById('itemsAddRow');
    const itemsCancelBtn = document.getElementById('itemsCancel');
    const itemsSaveBtn = document.getElementById('itemsSave');
    let users = [];
    let weaponList = [];
    let ammoTypeList = [];
    let medicineWeaponList = [];
    let itemsUserId = null;

    function setStatus(text) {
      statusEl.textContent = text;
    }

    function escapeHtml(text) {
      return text
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
    }

    function renderUsers() {
      const query = userSearchEl.value.trim().toLowerCase();
      userListEl.innerHTML = '';
      const filtered = users.filter(u => !query || u.username.toLowerCase().includes(query));
      if (!filtered.length) {
        userListEl.innerHTML = '<div class="empty">No users found.</div>';
        return;
      }

      for (const user of filtered) {
        const row = document.createElement('div');
        row.className = 'row row-user';
        row.innerHTML = `
          <div class="row-main">
            <div class="primary">${escapeHtml(user.username)}</div>
            <div class="meta">
              <span>id ${user.id}</span>
              <span>password ${escapeHtml(user.password)}</span>
              <span>exp ${user.experience}</span>
              <span>lvl ${user.level}</span>
              <span>str ${user.strength}</span>
              <span>end ${user.endurance}</span>
              <span>acc ${user.accuracy}</span>
              <span>maxHp ${user.maxHp}</span>
              <span>curHp ${user.currentHp}</span>
              <span>maxAp ${user.maxAp}</span>
              <span>weapon ${escapeHtml(user.weaponCode || '')}</span>
            </div>
          </div>
          <button type="button" class="edit-btn">Edit</button>
          <button type="button" class="edit-btn" data-act="hp">Debug HP</button>
          <button type="button" class="edit-btn" data-act="items">Items</button>
        `;
        row.querySelector('.edit-btn').addEventListener('click', () => openEdit(user));
        row.querySelector('[data-act="hp"]').addEventListener('click', () => debugSetHp(user));
        row.querySelector('[data-act="items"]').addEventListener('click', () => openItemsEditor(user));
        userListEl.appendChild(row);
      }
    }

    async function debugSetHp(user) {
      const maxInput = prompt('Set max HP:', String(Number(user.maxHp || 1)));
      if (maxInput == null) return;
      const maxHp = Math.max(1, Number(maxInput) || 1);
      const baseCurrent = user.currentHp != null ? Number(user.currentHp) : Number(user.maxHp || 1);
      const curInput = prompt('Set current HP:', String(Math.min(maxHp, baseCurrent)));
      if (curInput == null) return;
      const currentHp = Math.max(0, Math.min(maxHp, Number(curInput) || 0));
      setStatus('updating max/current HP for user ' + user.id + '...');
      const resp = await fetch('/api/db/users/' + user.id + '/debug-hp', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ maxHp, currentHp })
      });
      const data = await resp.json().catch(() => ({}));
      if (!resp.ok) {
        setStatus('debug HP update failed: ' + (data.error || resp.status));
        return;
      }
      setStatus('max/current HP updated');
      await loadUsers();
    }

    function weaponSlotWidth(code) {
      const w = weaponList.find(x => x.code === code);
      const n = w && w.inventorySlotWidth != null ? Number(w.inventorySlotWidth) : 1;
      return n >= 2 ? 2 : 1;
    }

    function weaponMagazineSize(code) {
      const w = weaponList.find(x => x.code === code);
      return w && w.magazineSize != null ? Math.max(0, Number(w.magazineSize) || 0) : 0;
    }

    function weaponHasCaliber(code) {
      const w = weaponList.find(x => x.code === code);
      return !!(w && String(w.caliber || '').trim());
    }

    function openEdit(user) {
      editIdEl.value = String(user.id);
      editUsernameEl.value = user.username;
      editPasswordEl.value = '';
      editExperienceEl.value = user.experience;
      editStrengthEl.value = user.strength;
      editEnduranceEl.value = user.endurance;
      editAccuracyEl.value = user.accuracy;
      editDialog.showModal();
    }

    function openItemsEditor(user) {
      itemsUserId = user.id;
      loadItemsForUser(user.id);
    }

    function addWeaponOptions(sel, code) {
      for (const w of weaponList) {
        const opt = document.createElement('option');
        opt.value = w.code;
        opt.textContent = w.code + ' - ' + w.name;
        sel.appendChild(opt);
      }
      const wc = (code || 'fist').toLowerCase();
      if (![...sel.options].some(o => o.value === wc)) {
        const opt = document.createElement('option');
        opt.value = wc;
        opt.textContent = wc + ' (missing in weapons list)';
        sel.appendChild(opt);
      }
      sel.value = wc;
    }

    function addAmmoOptions(sel, caliber) {
      for (const a of ammoTypeList) {
        const opt = document.createElement('option');
        opt.value = a.caliber;
        opt.textContent = a.caliber;
        sel.appendChild(opt);
      }
      const cal = (caliber || '').trim();
      if (cal && ![...sel.options].some(o => o.value === cal)) {
        const opt = document.createElement('option');
        opt.value = cal;
        opt.textContent = cal + ' (missing in ammo list)';
        sel.appendChild(opt);
      }
      if (sel.options.length && !cal) sel.value = sel.options[0].value;
      else sel.value = cal;
    }

    function renderItemsRows(items) {
      itemsRowsEl.innerHTML = '';
      for (const it of items) {
        const row = document.createElement('div');
        row.style.cssText = 'display:flex;flex-wrap:wrap;gap:8px;align-items:center;border:1px solid var(--border);border-radius:8px;padding:8px;';

        const type = document.createElement('select');
        type.innerHTML = '<option value="weapon">weapon</option><option value="ammo">ammo</option><option value="medicine">medicine</option>';
        {
          const t = (it.itemType || 'weapon').toLowerCase();
          type.value = (t === 'ammo' || t === 'medicine') ? t : 'weapon';
        }

        const code = document.createElement('select');
        const slot = document.createElement('input');
        slot.type = 'number'; slot.min = 0; slot.max = 11; slot.step = 1; slot.style.width = '58px';
        const qty = document.createElement('input');
        qty.type = 'number'; qty.min = 0; qty.step = 1; qty.style.width = '92px';
        const chamber = document.createElement('input');
        chamber.type = 'number'; chamber.min = 0; chamber.step = 1; chamber.style.width = '82px';
        const eqLab = document.createElement('label');
        eqLab.style.cssText = 'display:inline-flex;align-items:center;gap:4px;font-size:12px;';
        const eq = document.createElement('input');
        eq.type = 'checkbox';
        eqLab.appendChild(eq);
        eqLab.appendChild(document.createTextNode('equipped'));
        const info = document.createElement('span');
        info.className = 'hint';
        info.style.fontSize = '11px';
        const rm = document.createElement('button');
        rm.type = 'button';
        rm.textContent = 'Remove';
        rm.addEventListener('click', () => row.remove());

        function syncUi() {
          code.innerHTML = '';
          if (type.value === 'weapon') {
            addWeaponOptions(code, it.code || 'fist');
            slot.disabled = false;
            qty.disabled = true;
            qty.value = '1';
            const magSize = weaponMagazineSize(code.value);
            const hasCal = weaponHasCaliber(code.value);
            chamber.disabled = !(hasCal && magSize > 0);
            chamber.max = String(Math.max(0, magSize));
            if (chamber.disabled) chamber.value = '0';
            else chamber.value = String(Math.min(Math.max(0, Number(it.chamberRounds || chamber.value || 0)), magSize));
            eqLab.style.display = '';
            const weaponDef = weaponList.find(x => x.code === code.value);
            const hand = weaponDef && weaponDef.inventoryGrid != null ? Number(weaponDef.inventoryGrid) : 1;
            info.textContent = 'uses ' + weaponSlotWidth(code.value) + ' cell(s), hand ' + hand + ', mag ' + magSize;
          } else if (type.value === 'ammo') {
            addAmmoOptions(code, it.code || '');
            slot.disabled = false;
            qty.disabled = false;
            chamber.disabled = true;
            chamber.value = '0';
            eq.checked = false;
            eqLab.style.display = 'none';
            const ammoDef = ammoTypeList.find(x => x.caliber === code.value);
            const hand = ammoDef && ammoDef.inventoryGrid != null ? Number(ammoDef.inventoryGrid) : 1;
            info.textContent = 'stackable item, 1 cell, hand ' + hand;
          } else {
            code.innerHTML = '';
            for (const w of medicineWeaponList) {
              const opt = document.createElement('option');
              opt.value = w.code;
              opt.textContent = w.code + ' - ' + w.name;
              code.appendChild(opt);
            }
            const medCode = (it.code || '').trim().toLowerCase();
            if (medCode && ![...code.options].some(o => o.value === medCode)) {
              const opt = document.createElement('option');
              opt.value = medCode;
              opt.textContent = medCode + ' (missing in medicine list)';
              code.appendChild(opt);
            }
            if (code.options.length && !medCode) code.value = code.options[0].value;
            else code.value = medCode;
            slot.disabled = false;
            qty.disabled = false;
            chamber.disabled = true;
            chamber.value = '0';
            eq.checked = false;
            eqLab.style.display = 'none';
            const medDef = medicineWeaponList.find(x => x.code === code.value);
            const hand = medDef && medDef.inventoryGrid != null ? Number(medDef.inventoryGrid) : 1;
            info.textContent = 'medicine stack, hand ' + hand;
          }
        }

        slot.value = String(it.startSlot != null ? it.startSlot : 0);
        qty.value = String(Math.max(0, Number(it.quantity != null ? it.quantity : 1)));
        chamber.value = String(Math.max(0, Number(it.chamberRounds != null ? it.chamberRounds : 0)));
        eq.checked = !!it.isEquipped;
        eq.addEventListener('change', () => {
          if (!eq.checked || type.value !== 'weapon') return;
          itemsRowsEl.querySelectorAll('div input[type="checkbox"]').forEach(x => { if (x !== eq) x.checked = false; });
        });
        type.addEventListener('change', syncUi);
        code.addEventListener('change', () => {
          if (type.value === 'weapon') {
            const magSize = weaponMagazineSize(code.value);
            const hasCal = weaponHasCaliber(code.value);
            chamber.disabled = !(hasCal && magSize > 0);
            chamber.max = String(Math.max(0, magSize));
            if (chamber.disabled) chamber.value = '0';
            else chamber.value = String(Math.min(Math.max(0, Number(chamber.value || 0)), magSize));
            const weaponDef = weaponList.find(x => x.code === code.value);
            const hand = weaponDef && weaponDef.inventoryGrid != null ? Number(weaponDef.inventoryGrid) : 1;
            info.textContent = 'uses ' + weaponSlotWidth(code.value) + ' cell(s), hand ' + hand + ', mag ' + magSize;
          } else if (type.value === 'ammo') {
            const ammoDef = ammoTypeList.find(x => x.caliber === code.value);
            const hand = ammoDef && ammoDef.inventoryGrid != null ? Number(ammoDef.inventoryGrid) : 1;
            info.textContent = 'stackable item, 1 cell, hand ' + hand;
          } else {
            const medDef = medicineWeaponList.find(x => x.code === code.value);
            const hand = medDef && medDef.inventoryGrid != null ? Number(medDef.inventoryGrid) : 1;
            info.textContent = 'medicine stack, hand ' + hand;
          }
        });
        syncUi();

        row.appendChild(document.createTextNode('type'));
        row.appendChild(type);
        row.appendChild(document.createTextNode('code'));
        row.appendChild(code);
        row.appendChild(document.createTextNode('slot'));
        row.appendChild(slot);
        row.appendChild(document.createTextNode('qty'));
        row.appendChild(qty);
        row.appendChild(document.createTextNode('chamber'));
        row.appendChild(chamber);
        row.appendChild(eqLab);
        row.appendChild(info);
        row.appendChild(rm);
        itemsRowsEl.appendChild(row);
      }
    }

    function collectItemsRowsFromDom() {
      const out = [];
      for (const row of itemsRowsEl.children) {
        const selects = row.querySelectorAll('select');
        const inputs = row.querySelectorAll('input');
        const typeEl = selects[0];
        const codeEl = selects[1];
        const slotEl = inputs[0];
        const qtyEl = inputs[1];
        const chamberEl = inputs[2];
        const eqEl = inputs[3];
        if (!typeEl || !codeEl || !slotEl || !qtyEl || !chamberEl || !eqEl) continue;
        let outType = typeEl.value;
        if (outType === 'medicine')
          outType = 'medicine';
        out.push({
          itemType: outType,
          code: (codeEl.value || '').trim(),
          quantity: Math.max(0, Number(qtyEl.value || 0)),
          chamberRounds: Math.max(0, Number(chamberEl.value || 0)),
          startSlot: Number(slotEl.value || -1),
          isEquipped: !!eqEl.checked
        });
      }
      return out;
    }

    async function loadItemsForUser(id) {
      setStatus('loading items...');
      const resp = await fetch('/api/db/users/' + id + '/items');
      const data = await resp.json().catch(() => ({}));
      if (!resp.ok) {
        setStatus('items load failed: ' + (data.error || resp.status));
        return;
      }
      const items = Array.isArray(data.items) ? data.items : [];
      const defaults = items.length ? items : [{ itemType: 'weapon', code: 'fist', quantity: 1, startSlot: 0, isEquipped: true }];
      renderItemsRows(defaults);
      itemsDialog.showModal();
      setStatus('items loaded');
    }

    itemsAddRowBtn.addEventListener('click', () => {
      const cur = collectItemsRowsFromDom();
      cur.push({ itemType: 'weapon', code: 'fist', quantity: 1, startSlot: 0, isEquipped: false });
      renderItemsRows(cur);
    });

    itemsCancelBtn.addEventListener('click', () => itemsDialog.close());

    itemsSaveBtn.addEventListener('click', async () => {
      if (itemsUserId == null) return;
      const payload = { items: collectItemsRowsFromDom() };
      setStatus('saving items...');
      const resp = await fetch('/api/db/users/' + itemsUserId + '/items', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await resp.json().catch(() => ({}));
      if (!resp.ok) {
        setStatus('items save failed: ' + (data.error || resp.status));
        return;
      }
      itemsDialog.close();
      setStatus('items saved');
      await loadUsers();
    });

    editCancelBtn.addEventListener('click', () => editDialog.close());

    editForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const id = Number(editIdEl.value);
      const payload = {
        id,
        username: editUsernameEl.value.trim(),
        experience: Number(editExperienceEl.value),
        strength: Number(editStrengthEl.value),
        endurance: Number(editEnduranceEl.value),
        accuracy: Number(editAccuracyEl.value),
        maxHp: 1,
        maxAp: 1
      };
      const pw = editPasswordEl.value;
      if (pw && pw.trim())
        payload.password = pw;

      setStatus('saving user ' + id + '...');
      const resp = await fetch('/api/db/users', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await resp.json().catch(() => ({}));
      if (!resp.ok) {
        setStatus('save failed: ' + (data.error || resp.status));
        return;
      }
      editDialog.close();
      setStatus('saved user ' + id);
      await loadUsers();
    });

    async function loadUsers() {
      setStatus('loading users...');
      const [usersResp, weaponsResp, ammoResp] = await Promise.all([
        fetch('/api/db/users?take=200'),
        fetch('/api/db/weapons?take=200'),
        fetch('/api/db/ammo?take=300')
      ]);
      users = await usersResp.json();
      weaponList = await weaponsResp.json();
      ammoTypeList = await ammoResp.json();
      medicineWeaponList = (Array.isArray(weaponList) ? weaponList : [])
        .filter(w => String(w?.category || '').toLowerCase() === 'medicine');
      renderUsers();
      setStatus(`loaded ${users.length} users`);
    }

    userSearchEl.addEventListener('input', renderUsers);
    refreshBtnEl.addEventListener('click', () => {
      loadUsers().catch(err => {
        console.error(err);
        setStatus('failed to load users');
      });
    });

    loadUsers().catch(err => {
      console.error(err);
      setStatus('failed to load users');
    });
  </script>
</body>
</html>
""";
}
