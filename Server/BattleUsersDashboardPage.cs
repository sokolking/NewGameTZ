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

  <script>
    const userListEl = document.getElementById('userList');
    const userSearchEl = document.getElementById('userSearch');
    const refreshBtnEl = document.getElementById('refreshBtn');
    const statusEl = document.getElementById('status');
    let users = [];

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
        row.className = 'row';
        row.innerHTML = `
          <div class="primary">${escapeHtml(user.username)}</div>
          <div class="meta">
            <span>id ${user.id}</span>
            <span>password ${escapeHtml(user.password)}</span>
            <span>maxHp ${user.maxHp}</span>
            <span>maxAp ${user.maxAp}</span>
            <span>weapon ${escapeHtml(user.weaponCode || '')}</span>
          </div>
        `;
        userListEl.appendChild(row);
      }
    }

    async function loadUsers() {
      setStatus('loading users...');
      const resp = await fetch('/api/db/users?take=200');
      users = await resp.json();
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
