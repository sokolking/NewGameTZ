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
    dialog#invEditor {
      background: var(--panel);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: 14px;
      padding: 0;
      max-width: min(420px, 96vw);
    }
    dialog#invEditor {
      max-width: min(560px, 96vw);
    }
    dialog#editUser::backdrop,
    dialog#invEditor::backdrop {
      background: rgba(0,0,0,.55);
    }
    dialog#editUser h3,
    dialog#invEditor h3 {
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

  <dialog id="invEditor">
    <h3>User inventory</h3>
    <p class="hint" style="margin:0 0 10px;font-size:12px;line-height:1.4;">
      Grid 12 cells (0–11). Each weapon uses <strong>inventory slot width</strong> from the weapons table (1 or 2).
      Include <code>fist</code>. Exactly <strong>one</strong> row must be equipped (in hands). No overlapping slots.
    </p>
    <div id="invRows" style="display:flex;flex-direction:column;gap:8px;max-height:320px;overflow:auto;"></div>
    <div class="edit-actions" style="margin-top:10px;">
      <button type="button" id="invAddRow">Add row</button>
    </div>
    <div class="edit-actions">
      <button type="button" id="invCancel">Cancel</button>
      <button type="button" id="invSave">Save inventory</button>
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
    const invDialog = document.getElementById('invEditor');
    const invRowsEl = document.getElementById('invRows');
    const invAddRowBtn = document.getElementById('invAddRow');
    const invCancelBtn = document.getElementById('invCancel');
    const invSaveBtn = document.getElementById('invSave');
    let users = [];
    let weaponList = [];
    let invUserId = null;

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
              <span>maxAp ${user.maxAp}</span>
              <span>weapon ${escapeHtml(user.weaponCode || '')}</span>
            </div>
          </div>
          <button type="button" class="edit-btn">Edit</button>
          <button type="button" class="edit-btn" data-act="inv">Inventory</button>
        `;
        row.querySelector('.edit-btn').addEventListener('click', () => openEdit(user));
        row.querySelector('[data-act="inv"]').addEventListener('click', () => openInventoryEditor(user));
        userListEl.appendChild(row);
      }
    }

    function weaponSlotWidth(code) {
      const w = weaponList.find(x => x.code === code);
      const n = w && w.inventorySlotWidth != null ? Number(w.inventorySlotWidth) : 1;
      return n >= 2 ? 2 : 1;
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

    function openInventoryEditor(user) {
      invUserId = user.id;
      loadInventoryForUser(user.id);
    }

    async function loadInventoryForUser(id) {
      setStatus('loading inventory…');
      const resp = await fetch('/api/db/users/' + id + '/inventory');
      const data = await resp.json().catch(() => ({}));
      if (!resp.ok) {
        setStatus('inventory load failed: ' + (data.error || resp.status));
        return;
      }
      const items = Array.isArray(data.items) ? data.items : [];
      renderInvRows(items.length ? items : [{ id: 0, startSlot: 0, weaponCode: 'fist', slotWidth: 1, isEquipped: true }]);
      invDialog.showModal();
      setStatus('inventory loaded');
    }

    function renderInvRows(items) {
      invRowsEl.innerHTML = '';
      for (const it of items) {
        const wrap = document.createElement('div');
        wrap.style.cssText = 'display:flex;flex-wrap:wrap;gap:8px;align-items:center;border:1px solid var(--border);border-radius:8px;padding:8px;';
        const start = document.createElement('input');
        start.type = 'number';
        start.min = 0;
        start.max = 11;
        start.value = String(it.startSlot ?? 0);
        start.title = 'start slot 0–11';
        start.style.width = '52px';
        const sel = document.createElement('select');
        for (const w of weaponList) {
          const opt = document.createElement('option');
          opt.value = w.code;
          opt.textContent = w.code + ' — ' + w.name;
          sel.appendChild(opt);
        }
        const wc = (it.weaponCode || 'fist').toLowerCase();
        if (![...sel.options].some(o => o.value === wc)) {
          const opt = document.createElement('option');
          opt.value = wc;
          opt.textContent = wc + ' (missing in weapons list)';
          sel.appendChild(opt);
        }
        sel.value = wc;
        const span = document.createElement('span');
        span.className = 'hint';
        span.style.fontSize = '11px';
        const updSpan = () => { span.textContent = 'uses ' + weaponSlotWidth(sel.value) + ' cell(s)'; };
        sel.addEventListener('change', updSpan);
        updSpan();
        const eq = document.createElement('label');
        eq.style.cssText = 'display:inline-flex;align-items:center;gap:4px;font-size:12px;';
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.checked = !!it.isEquipped;
        cb.addEventListener('change', () => {
          if (cb.checked) {
            invRowsEl.querySelectorAll('input[type=checkbox]').forEach(x => { if (x !== cb) x.checked = false; });
          }
        });
        eq.appendChild(cb);
        eq.appendChild(document.createTextNode(' equipped'));
        const rm = document.createElement('button');
        rm.type = 'button';
        rm.textContent = 'Remove';
        rm.addEventListener('click', () => wrap.remove());
        wrap.appendChild(document.createTextNode('slot '));
        wrap.appendChild(start);
        wrap.appendChild(sel);
        wrap.appendChild(span);
        wrap.appendChild(eq);
        wrap.appendChild(rm);
        invRowsEl.appendChild(wrap);
      }
    }

    invAddRowBtn.addEventListener('click', () => {
      const cur = collectInvRowsFromDom();
      const firstCode = weaponList.length ? weaponList[0].code : 'fist';
      cur.push({ startSlot: 0, weaponCode: firstCode, isEquipped: false });
      renderInvRows(cur);
    });

    function collectInvRowsFromDom() {
      const out = [];
      for (const wrap of invRowsEl.children) {
        const inputs = wrap.querySelectorAll('input');
        const start = wrap.querySelector('input[type=number]');
        const sel = wrap.querySelector('select');
        const cb = wrap.querySelector('input[type=checkbox]');
        if (!start || !sel || !cb) continue;
        out.push({
          startSlot: Number(start.value) || 0,
          weaponCode: sel.value,
          isEquipped: cb.checked
        });
      }
      return out;
    }

    invCancelBtn.addEventListener('click', () => invDialog.close());

    invSaveBtn.addEventListener('click', async () => {
      if (invUserId == null) return;
      const rows = collectInvRowsFromDom();
      const payload = { items: rows.map(r => ({
        startSlot: r.startSlot,
        weaponCode: r.weaponCode,
        slotWidth: 1,
        isEquipped: r.isEquipped
      })) };
      setStatus('saving inventory…');
      const resp = await fetch('/api/db/users/' + invUserId + '/inventory', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await resp.json().catch(() => ({}));
      if (!resp.ok) {
        setStatus('inventory save failed: ' + (data.error || resp.status));
        return;
      }
      invDialog.close();
      setStatus('inventory saved');
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
      const [usersResp, weaponsResp] = await Promise.all([
        fetch('/api/db/users?take=200'),
        fetch('/api/db/weapons?take=200')
      ]);
      users = await usersResp.json();
      weaponList = await weaponsResp.json();
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
