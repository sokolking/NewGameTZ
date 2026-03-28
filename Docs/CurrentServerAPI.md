# Текущий серверный API и модель боя (факт на сегодня) 🧩

Этот документ фиксирует **как сервер и клиент реально работают сейчас**: вход в бой — **HTTP** (join, poll), сам бой — **WebSocket** (ход и результат раунда).

---

## 1. Эндпоинты сервера

Сервер — ASP.NET Core Web API (`BattleServer`).

### 1.1. POST `/api/battle/join`

**Назначение:** встать в очередь, сразу начать бой 1v1 или создать одиночный бой (1 игрок + серверный моб).

**Тело запроса:**

```json
{ "startCol": 0, "startRow": 0, "solo": false }
```

- `startCol`, `startRow` — желаемая стартовая клетка игрока (ограничивается размерами поля).
- `solo`:
  - `false` — обычный матчмейкинг 1v1 (через очередь).
  - `true` — одиночный бой: создаётся сразу бой, в котором участвует только `P1` и серверный моб.

**Ответ (общая форма):**

```jsonc
// Если первый игрок в очереди (solo=false) — ждёт второго
{
  "battleId": "abcd1234",
  "playerId": "P1",
  "status": "waiting"
}

// Если второй игрок (solo=false) — бой сразу начинается
{
  "battleId": "abcd1234",
  "playerId": "P2",
  "status": "battle",
  "battleStarted": {
    "battleId": "abcd1234",
    "playerId": "P2",
    "players": [
      { "playerId": "P1", "col": 0, "row": 0 },
      { "playerId": "P2", "col": 24, "row": 39 }
    ],
    "roundDuration": 100.0,
    "spawnPlayerIds": [ "P1", "P2" ],
    "spawnCols": [ 0, 24 ],
    "spawnRows": [ 0, 39 ]
  }
}

// Если solo=true — одиночный бой сразу начинается, без очереди
{
  "battleId": "solo1234",
  "playerId": "P1",
  "status": "battle",
  "battleStarted": {
    "battleId": "solo1234",
    "playerId": "P1",
    "players": [
      { "playerId": "P1", "col": 0, "row": 0 }
    ],
    "roundDuration": 100.0,
    "spawnPlayerIds": [ "P1" ],
    "spawnCols": [ 0 ],
    "spawnRows": [ 0 ]
  }
}
```

`battleStarted` используется клиентом для расстановки юнитов и первого раунда.  
В одиночном бою второй «игрок» — это серверный моб, который присутствует только в результатах ходов (`TurnResult`), а не в списке `players`.

---

### 1.2. GET `/api/battle/{battleId}/poll?playerId=P1`

**Назначение:** первый игрок опрашивает сервер, пока не придёт второй.

**Ответ:**

```jsonc
// Пока ждем второго игрока
{ "status": "waiting" }

// Второй присоединился — бой начался
{
  "status": "battle",
  "battleStarted": {
    "battleId": "abcd1234",
    "playerId": "P1",
    "players": [ ... ],
    "roundDuration": 100.0,
    "spawnPlayerIds": [ ... ],
    "spawnCols": [ ... ],
    "spawnRows": [ ... ]
  },
  "roundIndex": 0,
  "roundDuration": 100.0
}
```

---

### 1.3. WebSocket `/ws/battle` (весь бой после join)

**Подключение:** `ws://<host>/ws/battle?battleId=<id>&playerId=<id>`.

**Клиент → сервер (текст JSON):**

| `type` | Назначение |
|--------|------------|
| `submitTurn` | Ход: поля как у бывшего POST submit (`battleId`, `playerId`, `roundIndex`, `path`, `apSpentThisTurn`, `stepsTakenThisTurn`). `battleId` на сервере сверяется с комнатой сокета. |
| `leave` | Выход из боя (опционально `playerId`; иначе из query). |

**Сервер → клиент:**

| `type` | Назначение |
|--------|------------|
| `submitAck` | `{ "ok": true }` или `{ "ok": false, "error": "...", "expectedRound": n }` |
| `leaveAck` | `{ "ok": true }` |
| `roundResolved` | Итог раунда: `turnResult`, `roundIndex` (следующий), `roundDeadlineUtcMs` |

При **отключении** сокета сервер вызывает тот же сценарий, что и при выходе игрока (`PlayerLeft`).

**Во время боя POST не используется** (кроме `POST /leave` с главного меню при отмене очереди, где нет WebSocket).

`GET /api/battle/{id}` — только срез для отладки, без `turnResult`.

**Логи WebSocket (префикс `[BattleWS]`):** сервер пишет в консоль подключение (`accept begin`, `registry add`), входящие кадры от клиента, отключение, рассылку (`broadcast`, `send ok` / `send fail`). Клиент Unity — в консоль редактора: подключение, каждое входящее сообщение (размер + превью JSON), применение / дубликат. Флаг **`Log Sockets`** на `BattleSignalRConnection` отключает клиентские логи.

---

### 1.4. GET `/api/battle/{battleId}`

**Назначение:** срез состояния (раунд, таймер, участники, спавны). **`turnResult` всегда null** — итог раунда только по WebSocket.

**Ответ (`BattleStateResponse`):**

```jsonc
{
  "roundIndex": 1,          // текущий раунд (уже следующий, если прошлый только что завершился)
  "roundDuration": 100.0,
  "roundTimeLeft": 27.2,
  "roundDeadlineUtcMs": 1773858123456,

  "turnResult": {           // может быть null, если раунд ещё не завершился
    "battleId": "abcd1234",
    "roundIndex": 0,       // индекс ЗАВЕРШЁННОГО раунда
    "roundResolveReason": "allSubmitted" // или "timerExpired"
    "results": [
      {
        "playerId": "P1",
        "accepted": true,
        "finalPosition": { "col": 2, "row": 0 },
        "actualPath": [ ... ],
        "currentAp": 86,
        "penaltyFraction": 0.0,
        "apSpentThisTurn": 14,
        "rejectedReason": null
      },
      {
        "playerId": "P2",
        "accepted": false,
        "finalPosition": { "col": 1, "row": 1 },
        "actualPath": [ ... ],
        "currentAp": 80,
        "penaltyFraction": 0.05,
        "apSpentThisTurn": 20,
        "rejectedReason": "Target hex (2,0) already occupied"
      }
    ]
  },

  "participants": [
    {
      "playerId": "P1",
      "hasSubmitted": false,      // для ТЕКУЩЕГО раунда
      "endedTurnEarly": false
    },
    {
      "playerId": "P2",
      "hasSubmitted": false,
      "endedTurnEarly": false
    }
  ],
  "allSubmittedThisRound": false,

  "spawnPlayerIds": [ "P1", "P2" ],
  "spawnCols": [ 0, 24 ],
  "spawnRows": [ 0, 39 ]
}
```

`roundDeadlineUtcMs` — абсолютный UTC timestamp окончания текущего раунда. Клиент должен вычислять остаток времени как `deadline - now`, а не заново запускать локальные `100` секунд после показа анимаций.

Замечание: `participants` и `allSubmittedThisRound` относятся **к текущему раунду** (`roundIndex`), а `turnResult` — к **предыдущему завершённому раунду**.

После закрытия раунда сервер увеличивает `RoundIndex`, очищает `Submissions` и `EndedTurnEarlyThisRound`, поэтому при следующем `GET` для нового раунда оба флага для игроков снова `false`.

---

### 1.5. POST `/api/battle/{battleId}/leave?playerId=P1`

**Назначение:** игрок вышел (закрыл клиент / сцену); убрать его из очереди / закрыть бой.

**Ответ:**

```json
{ "left": true }
```

Или ошибка:

```json
{ "error": "Battle not found or player not in battle" }
```

---

## 2. Внутренняя модель боя на сервере (`BattleRoom`)

Класс `BattleRoom` отвечает за **состояние одного боя**.

### 2.1. Основные поля

- `BattleId: string`
- `RoundIndex: int`
- `RoundTimeLeft: float`
- `RoundInProgress: bool`
- `Players: Dictionary<string playerId, (int col, int row)>`
- `CurrentState: Dictionary<string playerId, PlayerBattleState>`
- `Submissions: Dictionary<string playerId, SubmitTurnPayloadDto>`
- `SubmissionOrder: List<string playerId>` — порядок отправки `SubmitTurn` в этом раунде.
- `EndedTurnEarlyThisRound: Dictionary<string playerId, bool>`
- `LastTurnResult: TurnResultPayloadDto?`

`PlayerBattleState`:

```csharp
public class PlayerBattleState
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int CurrentAp { get; set; }
    public float PenaltyFraction { get; set; }
}
```

### 2.2. Старт первого раунда

```csharp
public void StartFirstRound()
{
    RoundIndex = 0;
    RoundTimeLeft = RoundDuration; // 100f
    RoundInProgress = true;
    Submissions.Clear();
    SubmissionOrder.Clear();
    EndedTurnEarlyThisRound.Clear();
    CurrentState.Clear();
    foreach (var kv in Players)
        CurrentState[kv.Key] = new PlayerBattleState {
            Col = kv.Value.col,
            Row = kv.Value.row,
            CurrentAp = MaxAp,           // 100
            PenaltyFraction = 0f
        };
}
```

---

### 2.3. Приём хода (`SubmitTurn`)

```csharp
public bool SubmitTurn(SubmitTurnPayloadDto payload)
{
    if (payload.RoundIndex != RoundIndex) return false;
    if (!Players.ContainsKey(payload.PlayerId)) return false;
    if (Submissions.ContainsKey(payload.PlayerId)) return false; // дубликат

    Submissions[payload.PlayerId] = payload;
    SubmissionOrder.Add(payload.PlayerId);
    if (RoundTimeLeft > 0.01f)
        EndedTurnEarlyThisRound[payload.PlayerId] = true;

    return Submissions.Count >= Players.Count;
}
```

- Возвращает `true`, когда оба игрока прислали ход в текущем раунде.
- Поля `hasSubmitted` / `endedTurnEarly` в `participants` строятся на основе `Submissions` / `EndedTurnEarlyThisRound`.

---

### 2.4. Тик таймера раунда

```csharp
public void Tick(float deltaSeconds)
{
    if (!RoundInProgress) return;
    RoundTimeLeft -= deltaSeconds;
    if (RoundTimeLeft <= 0)
    {
        RoundTimeLeft = 0;
        CloseRound(fromTimer: true);
    }
}
```

Таймер тикает раз в 0.2 сек из `Program.cs`:

```csharp
var timer = new System.Timers.Timer(200);
timer.Elapsed += (_, _) => store.Tick(0.2f);
timer.Start();
```

---

### 2.5. Закрытие раунда (`CloseRound`)

Главная задача — **пошаговая симуляция движения** обоих игроков по путям с учётом приоритета по `SubmissionOrder`, пересчёт ОД и штрафов и формирование `TurnResult`.

Ключевые моменты:

- Если игрок **не прислал** путь — он считается стоящим на месте (путь длиной 1 точка — текущая клетка).
- На каждом шаге симуляции:
  - Обработка игроков в порядке `SubmissionOrder` (кто раньше отправил ход, тот раньше выбирает клетку).
  - Если целевая клетка уже занята — игрок остаётся на месте, `accepted = false`, записывается причина.
- После симуляции:
  - Считаются потраченные ОД по фактическому пути (`actualPath`).
  - Обновляется штраф и высчитывается новый пул ОД на **следующий** раунд (как в офлайновом `Player.EndTurn`).

Финал:

```csharp
LastTurnResult = new TurnResultPayloadDto
{
    BattleId = BattleId,
    RoundIndex = RoundIndex,   // завершённый раунд
    Results = results.ToArray(),
    RoundResolveReason = resolveReason // "allSubmitted" или "timerExpired"
};

RoundIndex++;
RoundTimeLeft = RoundDuration;
Submissions.Clear();
SubmissionOrder.Clear();
EndedTurnEarlyThisRound.Clear();
RoundInProgress = true;
```

---

## 3. Клиентский код (Unity)

### 3.1. Модели обмена (`BattleNetworkingModels.cs`)

Зеркальные структуры под JSON сервера:

- `HexPosition`
- `SubmitTurnPayload`
- `PlayerTurnResult`
- `TurnResultPayload`
- `BattlePlayerInfo`
- `BattleStartedPayload`

Пример `TurnResultPayload`:

```csharp
[Serializable]
public class TurnResultPayload
{
    public string battleId;
    public int roundIndex;
    public PlayerTurnResult[] results;
    public string roundResolveReason; // allSubmitted | timerExpired
}
```

---

### 3.2. BattleServerConnection (HTTP: join и poll)

Отвечает за:

- `Join` / `JoinOrCreate` через `POST /api/battle/join`.
- Ожидание второго игрока через `/poll`.
- `GET /api/battle/{id}` клиентом в бою **не опрашивается** (конец боя — по **404** на submit или по сообщению).
- Отправку `SubmitTurn` (`POST …/submit`) и обработку ошибок/таймаутов.

**Результат раунда** клиент получает только по **WebSocket** (`BattleSignalRConnection`).

---

### 3.3. GameSession (игровая сцена)

Основные задачи:

- При `BattleStarted`:
  - создать локального `Player` и `RemoteBattleUnitView` для оппонента;
  - выставить начальные позиции из сервера;
  - синхронизировать первый раунд (`ApplyRoundState`).
- При `TurnResult` (из сокета):
  - обновить позицию, ОД и штрафы для обоих юнитов;
  - запустить параллельную анимацию по `actualPath`.
- Взаимодействовать с `ActionPointsUI` (блокировка ввода, показ «ожидание сервера», лог сообщений).

Клиентский код **должен считать сервер истинным источником состояния** и не переигрывать решения сервера.

---

## 4. Ограничения текущей реализации

1. **Юниты игроков + серверный моб.**  
   - Для PvP в комнате по‑прежнему участвуют только игроки `P1`/`P2` как управляемые юниты.  
   - В одиночном бою сервер создаёт дополнительный юнит `MOB_1` (`unitType = Mob`), который ходит по простому ИИ на сервере и шлётся в `TurnResult` как обычный участник (но без отдельного `playerId` у клиента).

2. **Часть логики ОД и штрафов есть и на клиенте, и на сервере.**  
   Для честного PvP в будущем всю боевую математику нужно держать строго на сервере.

3. **Транспорт:** **HTTP** — только join, poll до старта и при необходимости `POST /leave` с меню; **WebSocket** — отправка хода (`submitTurn`) и пуш результата раунда.  
   GET состояния без `turnResult` (отладка).

4. **`participants` отражают только текущий раунд.**  
   После перехода к следующему раунду все флаги `hasSubmitted`/`endedTurnEarly` снова `false`.

Этот документ фиксирует именно текущее поведение. Все дальнейшие изменения («всё на сервере», серверные мобы/ИИ, новые DTO) должны ссылаться на это как на отправную точку. 

