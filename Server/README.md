# Battle Server (Этап 2)

Минимальный сервер боя: **HTTP** (join, poll, отмена очереди) и **WebSocket** (ход и результат раунда).

## Запуск

Требуется .NET 8 SDK.

Путь к проекту с **пробелами** (например `My project (1)`) — обязательно в **кавычках**:

```bash
cd "/Users/ВАШ_ЛОГИН/My project (1)/Server"
TMPDIR=~/tmp dotnet run
```

Либо без `cd` (подставьте свой полный путь к `.csproj`):

```bash
mkdir -p ~/tmp
TMPDIR=~/tmp dotnet run --project "/Users/ВАШ_ЛОГИН/My project (1)/Server/BattleServer.csproj"
```

Сервер слушает **http://localhost:5000**.

**Не путать:** команды `cd` и `dotnet run` — отдельно (не существует `dotnet runcd`).

### Если при `dotnet run` ошибка доступа к `/var/folders/.../T/MSBuildTemp`

На macOS папка системного temp иногда недоступна MSBuild. Всегда задавайте свой каталог:

```bash
mkdir -p ~/tmp
export TMPDIR=~/tmp
dotnet run
```

Или одной строкой: `TMPDIR=~/tmp dotnet run`

## API

- **POST /api/battle/join** — встать в очередь или начать бой. Тело: `{ "startCol": 0, "startRow": 0 }`. Ответ: `{ "battleId", "playerId", "status": "waiting"|"battle", "battleStarted"? }`. Если пришёл второй игрок — оба получают `status: "battle"` и `battleStarted`.
- **GET /api/battle/{battleId}/poll?playerId=P1** — для первого игрока: опрос до появления второго. Ответ: `{ "status": "waiting"|"battle", "battleStarted"? }`.
- **WebSocket `/ws/battle`** — ход и события боя. Клиент шлёт `submitTurn`, сервер отвечает `submitAck`, а после закрытия раунда пушит `roundResolved` с `turnResult`, `roundIndex` и `roundDeadlineUtcMs`.
- **GET /api/battle/{battleId}** — состояние раунда для отладки. Ответ: `roundIndex`, `roundDuration`, `roundTimeLeft`, `roundDeadlineUtcMs`, `turnResult?`, `participants`, `allSubmittedThisRound`. Для UI таймера ориентируйтесь на `roundDeadlineUtcMs`, а не на перезапуск локальных `30` секунд.
- **POST /api/battle/{battleId}/leave?playerId=P1** — игрок вышел (закрыл клиент или сцену). Если ждал в очереди один — комната удаляется из очереди. Если бой уже идёт — комната удаляется, таймер раунда больше не тикает; второй клиент при опросе получит 404.

## Стартовые позиции

Первый игрок в матче — в запрошенной клетке (ограничено полем **25×40**). Второй — в клетке **не ближе 10 гексов** (та же метрика, что в клиенте odd-r), выбирается **максимально далёкая** от первого, чтобы не спавниться рядом.

## Unity

В сцене: объект с `BattleServerConnection` (URL сервера), ссылка на `GameSession`. У `GameSession` включить Is Online Mode и указать ссылку на `BattleServerConnection`. При старте игры вызывается Join; при завершении хода клиент шлёт `submitTurn` по WebSocket; итог раунда приходит по `roundResolved`.
