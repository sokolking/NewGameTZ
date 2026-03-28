namespace BattleServer;

public static class BattleObstacleBalanceDashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Obstacle balance</title>
  <style>
    body { margin: 0; background: #0f1117; color: #e8ecf4; font-family: Inter, sans-serif; }
    .wrap { padding: 16px; max-width: 640px; margin: 0 auto; }
    .nav a { color: #9fb8ff; margin-right: 10px; text-decoration: none; }
    .panel { background: #171a22; border: 1px solid #2b3140; border-radius: 10px; padding: 16px; margin-top: 12px; }
    label { display: block; margin-top: 12px; color: #9aa4b2; font-size: 13px; }
    input[type="number"] { width: 100%; max-width: 200px; background: #11151d; border: 1px solid #2b3140; color: #e8ecf4; border-radius: 8px; padding: 8px; box-sizing: border-box; }
    .hint { font-size: 12px; color: #6b7280; margin-top: 4px; }
    .row { display: flex; gap: 10px; flex-wrap: wrap; margin-top: 16px; }
    button { background: #2a3f7a; border: 1px solid #4a6fd4; color: #e8ecf4; border-radius: 8px; padding: 10px 16px; cursor: pointer; }
    button.secondary { background: #11151d; border-color: #2b3140; }
    .status { color: #9aa4b2; font-size: 13px; margin-top: 12px; }
    h1 { font-size: 20px; margin: 0 0 8px; }
    h2 { font-size: 15px; margin: 0 0 8px; color: #c5cdd8; }
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
    <h1>battle_obstacle_balance</h1>
    <p style="color:#9aa4b2;font-size:14px;margin:0;">Одна строка (id=1): HP стен, укрытия, число сегментов стен / камней / деревьев на карте.</p>
    <div class="panel">
      <h2>Параметры</h2>
      <label for="wallMaxHp">wall_max_hp</label>
      <input id="wallMaxHp" type="number" min="1" max="999" />
      <div class="hint">Макс. HP одной клетки стены (сервер: 1–999).</div>

      <label for="treeCoverMissPercent">tree_cover_miss_percent</label>
      <input id="treeCoverMissPercent" type="number" min="0" max="95" />
      <div class="hint">Штраф к попаданию, если цель за деревом (0–95).</div>

      <label for="rockCoverMissPercent">rock_cover_miss_percent</label>
      <input id="rockCoverMissPercent" type="number" min="0" max="95" />
      <div class="hint">Штраф к попаданию за камнем (sit/hide), 0–95.</div>

      <label for="wallSegmentsCount">wall_segments_count</label>
      <input id="wallSegmentsCount" type="number" min="1" max="50" />
      <div class="hint">Число сегментов стены при генерации (длина 2–3 гекса каждый).</div>

      <label for="rockCount">rock_count</label>
      <input id="rockCount" type="number" min="0" max="200" />

      <label for="treeCount">tree_count</label>
      <input id="treeCount" type="number" min="0" max="200" />

      <div class="row">
        <button id="saveBtn" type="button">Сохранить в БД</button>
        <button id="reloadBtn" type="button" class="secondary">Перезагрузить</button>
      </div>
      <div id="status" class="status">загрузка…</div>
    </div>
  </div>
  <script>
    const statusEl = document.getElementById('status');
    function field(id) { return document.getElementById(id); }

    async function load() {
      statusEl.textContent = 'загрузка…';
      try {
        const resp = await fetch('/api/db/obstacle-balance');
        if (!resp.ok) { statusEl.textContent = 'ошибка GET: ' + resp.status; return; }
        const b = await resp.json();
        field('wallMaxHp').value = b.wallMaxHp;
        field('treeCoverMissPercent').value = b.treeCoverMissPercent;
        field('rockCoverMissPercent').value = b.rockCoverMissPercent;
        field('wallSegmentsCount').value = b.wallSegmentsCount;
        field('rockCount').value = b.rockCount;
        field('treeCount').value = b.treeCount;
        statusEl.textContent = 'загружено';
      } catch (e) {
        statusEl.textContent = 'ошибка: ' + e;
      }
    }

    async function save() {
      const payload = {
        wallMaxHp: Number(field('wallMaxHp').value || 1),
        treeCoverMissPercent: Number(field('treeCoverMissPercent').value || 0),
        rockCoverMissPercent: Number(field('rockCoverMissPercent').value || 0),
        wallSegmentsCount: Number(field('wallSegmentsCount').value || 1),
        rockCount: Number(field('rockCount').value || 0),
        treeCount: Number(field('treeCount').value || 0)
      };
      statusEl.textContent = 'сохранение…';
      try {
        const resp = await fetch('/api/db/obstacle-balance', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (!resp.ok) {
          const err = await resp.json().catch(() => ({}));
          statusEl.textContent = 'ошибка: ' + (err.error || resp.status);
          return;
        }
        statusEl.textContent = 'сохранено (новые бои подхватят значения)';
        await load();
      } catch (e) {
        statusEl.textContent = 'ошибка: ' + e;
      }
    }

    document.getElementById('saveBtn').addEventListener('click', save);
    document.getElementById('reloadBtn').addEventListener('click', load);
    load();
  </script>
</body>
</html>
""";
}
