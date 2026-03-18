namespace BattleServer;

public static class BattleDbDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Battle DB Browser</title>
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
      --ok: #6dd3a0;
      --warn: #ffcc66;
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
    input[type="search"] {
      background: var(--panel);
      border: 1px solid var(--border);
      color: var(--text);
      border-radius: 10px;
      padding: 9px 12px;
      min-width: 260px;
    }
    button {
      background: var(--panel);
      border: 1px solid var(--border);
      color: var(--text);
      border-radius: 10px;
      padding: 9px 12px;
      cursor: pointer;
    }
    button:hover { border-color: #46506a; }
    .status {
      margin-left: auto;
      color: var(--muted);
      font-size: 13px;
    }
    .layout {
      display: grid;
      grid-template-columns: 360px 420px 1fr;
      gap: 14px;
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
      cursor: pointer;
    }
    .row:hover { border-color: #46506a; }
    .row.active {
      border-color: var(--accent);
      background: var(--accent-soft);
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
    .badge {
      display: inline-flex;
      align-items: center;
      padding: 2px 8px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      border: 1px solid transparent;
    }
    .badge-allSubmitted { color: var(--ok); border-color: rgba(109,211,160,.3); background: rgba(109,211,160,.08); }
    .badge-timerExpired { color: var(--warn); border-color: rgba(255,204,102,.3); background: rgba(255,204,102,.08); }
    .detail {
      padding: 14px;
      overflow: auto;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .detail pre {
      margin: 0;
      white-space: pre-wrap;
      word-break: break-word;
      background: var(--panel-alt);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 12px;
      font-size: 12px;
      line-height: 1.5;
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
    }
    .empty {
      color: var(--muted);
      padding: 16px 14px;
      font-size: 13px;
    }
    @media (max-width: 1200px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
  </style>
</head>
<body>
  <div class="topbar">
    <h1 class="title">Battle Database Browser</h1>
    <div class="controls">
      <input id="battleSearch" type="search" placeholder="Search battleId">
      <input id="turnSearch" type="search" placeholder="Search turnId / resolve reason">
      <button id="refreshBtn" type="button">Refresh</button>
      <div id="status" class="status">loading...</div>
    </div>
  </div>

  <div class="layout">
    <section class="card">
      <h2>Battles</h2>
      <div id="battleList" class="list"></div>
    </section>

    <section class="card">
      <h2>Turns</h2>
      <div id="turnList" class="list"></div>
    </section>

    <section class="card">
      <h2>Turn Detail</h2>
      <div id="turnDetail" class="detail">
        <div class="empty">Select a turn to inspect the stored JSON payload.</div>
      </div>
    </section>
  </div>

  <script>
    const battleListEl = document.getElementById('battleList');
    const turnListEl = document.getElementById('turnList');
    const turnDetailEl = document.getElementById('turnDetail');
    const battleSearchEl = document.getElementById('battleSearch');
    const turnSearchEl = document.getElementById('turnSearch');
    const refreshBtnEl = document.getElementById('refreshBtn');
    const statusEl = document.getElementById('status');

    let battles = [];
    let turns = [];
    let selectedBattleId = null;
    let selectedTurnId = null;

    function fmtUtc(value) {
      return new Date(value).toLocaleString();
    }

    function setStatus(text) {
      statusEl.textContent = text;
    }

    function renderBattles() {
      const query = battleSearchEl.value.trim().toLowerCase();
      battleListEl.innerHTML = '';
      const filtered = battles.filter(b => !query || b.battleId.toLowerCase().includes(query));
      if (!filtered.length) {
        battleListEl.innerHTML = '<div class="empty">No battles found.</div>';
        return;
      }

      for (const battle of filtered) {
        const row = document.createElement('div');
        row.className = 'row' + (battle.battleId === selectedBattleId ? ' active' : '');
        row.innerHTML = `
          <div class="primary">${battle.battleId}</div>
          <div class="meta">
            <span>created ${fmtUtc(battle.createdUtc)}</span>
            <span>${battle.turnCount} turns</span>
            <span>latest ${battle.latestTurnId ?? 'none'}</span>
          </div>
        `;
        row.addEventListener('click', () => selectBattle(battle.battleId));
        battleListEl.appendChild(row);
      }
    }

    function renderTurns() {
      const query = turnSearchEl.value.trim().toLowerCase();
      turnListEl.innerHTML = '';
      const filtered = turns.filter(t => {
        if (!query) return true;
        return t.turnId.toLowerCase().includes(query) || t.roundResolveReason.toLowerCase().includes(query);
      });

      if (!selectedBattleId) {
        turnListEl.innerHTML = '<div class="empty">Select a battle first.</div>';
        return;
      }
      if (!filtered.length) {
        turnListEl.innerHTML = '<div class="empty">No turns found for this battle.</div>';
        return;
      }

      for (const turn of filtered) {
        const row = document.createElement('div');
        row.className = 'row' + (turn.turnId === selectedTurnId ? ' active' : '');
        row.innerHTML = `
          <div class="primary">${turn.turnId}</div>
          <div class="meta">
            <span>turn #${turn.turnIndex + 1}</span>
            <span>round ${turn.roundIndex}</span>
            <span class="badge badge-${turn.roundResolveReason || 'allSubmitted'}">${turn.roundResolveReason || 'unknown'}</span>
            <span>${fmtUtc(turn.createdUtc)}</span>
          </div>
        `;
        row.addEventListener('click', () => selectTurn(turn.turnId));
        turnListEl.appendChild(row);
      }
    }

    function renderTurnDetail(detail) {
      if (!detail) {
        turnDetailEl.innerHTML = '<div class="empty">Select a turn to inspect the stored JSON payload.</div>';
        return;
      }

      turnDetailEl.innerHTML = `
        <div class="meta">
          <span><strong>battle</strong> ${detail.battleId}</span>
          <span><strong>turn</strong> #${detail.turnIndex + 1}</span>
          <span><strong>saved</strong> ${fmtUtc(detail.createdUtc)}</span>
        </div>
        <pre>${escapeHtml(JSON.stringify(detail.turnResult, null, 2))}</pre>
        <pre>${escapeHtml(detail.rawJson)}</pre>
      `;
    }

    function escapeHtml(text) {
      return text
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
    }

    async function loadBattles() {
      setStatus('loading battles...');
      const resp = await fetch('/api/db/battles?take=200');
      battles = await resp.json();
      renderBattles();
      setStatus(`loaded ${battles.length} battles`);
      if (!selectedBattleId && battles.length)
        await selectBattle(battles[0].battleId);
    }

    async function selectBattle(battleId) {
      selectedBattleId = battleId;
      selectedTurnId = null;
      renderBattles();
      renderTurnDetail(null);
      setStatus(`loading turns for ${battleId}...`);
      const resp = await fetch(`/api/db/battles/${encodeURIComponent(battleId)}/turns?take=500`);
      turns = await resp.json();
      renderTurns();
      setStatus(`loaded ${turns.length} turns for ${battleId}`);
      if (turns.length)
        await selectTurn(turns[0].turnId);
    }

    async function selectTurn(turnId) {
      selectedTurnId = turnId;
      renderTurns();
      setStatus(`loading ${turnId}...`);
      const resp = await fetch(`/api/db/turns/${encodeURIComponent(turnId)}`);
      const detail = await resp.json();
      renderTurnDetail(detail);
      setStatus(`showing ${turnId}`);
    }

    battleSearchEl.addEventListener('input', renderBattles);
    turnSearchEl.addEventListener('input', renderTurns);
    refreshBtnEl.addEventListener('click', () => {
      turns = [];
      selectedBattleId = null;
      selectedTurnId = null;
      renderTurns();
      renderTurnDetail(null);
      loadBattles().catch(err => {
        console.error(err);
        setStatus('failed to load data');
      });
    });

    loadBattles().catch(err => {
      console.error(err);
      setStatus('failed to load data');
    });
  </script>
</body>
</html>
""";
}
