# Логика боя на сервере: новые и изменённые правила

Документ описывает изменения в симуляции боя, БД оружия, JSON API и связанном клиентском поведении. Обычный цикл хода (движение, ОД, очередь действий) здесь не дублируется.

---

## 1. Стрельба за пределы номинальной дальности оружия

**Было:** при расстоянии до цели (или до гекса прицела) больше `WeaponRange` атака отклонялась (например, `Attack target out of range`).

**Стало:**

- Выстрел **разрешён** на любую дистанцию в пределах карты и прочих проверок (враг, живой юнит и т.д.).
- **Сырой урон** перед множителем дальности — **случайное целое** от **`WeaponDamageMin`** до **`WeaponDamage`** включительно (из БД `damage_min` / `damage_max`).
- **Урон** после попадания: множитель **0.5^N**, где **N = max(0, расстояние_до_фактического_попадания − WeaponRange)**. Каждый «лишний» гекс относительно номинальной дальности **половинит** урон; итог округляется вниз до целого.
- То же правило для **урона по стене:** расстояние считается от стрелка до **клетки стены**, в которую попал выстрел.
- **Базовая вероятность попадания по дистанции** (`GetBaseHitProbabilityFromRange`) остаётся «линейной» внутри номинальной дальности; для гексов **за** `WeaponRange` к этому множителю дополнительно умножается **0.5^N**, иначе вероятность обнулялась бы и дальние выстрелы не попадали бы.

**Файлы:** `BattleRoom.CloseRound.cs` — `GetHexesBeyondWeaponRange`, `ApplyOverRangeDamage`, `GetBaseHitProbabilityFromRange`.

---

## 2. Флаг снайпера (`is_sniper`)

- В PostgreSQL колонка **`weapons.is_sniper`** (BOOLEAN, по умолчанию false).
- На юните: **`UnitStateDto.WeaponIsSniper`**, в JSON спавна — **`spawnWeaponIsSnipers`**, в итоге раунда — в **`PlayerTurnResultDto`**.
- **Только для вероятности попадания:** за пределами номинальной дальности к множителю **p** применяется **0.65^N** вместо **0.5^N** (N — число лишних гексов). **Урон** по-прежнему **0.5^N**.
- Редактирование: дашборд `/weapons`, колонка **snp** (чекбокс) или поле **`isSniper`** в `POST /api/db/weapons`.

---

## 3. Формула попадания (п. 5.15)

Реализовано в **`CombineHitProbability`** (`BattleRoom.CombatRules.cs`):

1. **p_дистанция** — результат `GetBaseHitProbabilityFromRange` (уже с штрафом 0.5 за каждый гекс за пределами дальности).
2. **Множители укрытия** (произведение):
   - есть **дерево** на линии укрытия → множитель `(1 - TreeCoverMissPercent/100)`;
   - есть **камень** на линии и цель в позе **sit** или **hide** → множитель `(1 - RockCoverMissPercent/100)`.
3. **Формула:**  
   `p = p_дистанция × treeFactor × rockFactor + бонус_меткости − штраф_кучности`
4. **Бонус меткости:** **+0.02 за пункт** `Accuracy` (как раньше по ощущению: +2% за пункт).
5. **Штраф кучности:** поле **`WeaponSpreadPenalty`** (из БД `spread_penalty`), в коде ограничение **0…0.95**, вычитается из `p`.
6. Итог **clamp в [0, 1]**.

Используется и для **выстрела по гексу** (Ctrl-прицел), и для **атаки по юниту**.

**Зафиксировано в коде (XML):** `BattleRoom.CombatRules.cs` — пункты 2.5, 2.6, 5.15, 7.20 в summary класса.

---

## 4. Новые поля оружия на юните и в JSON

### `UnitStateDto` / `PlayerTurnResultDto`

| Поле | Тип | Назначение |
|------|-----|------------|
| `WeaponSpreadPenalty` | `double` | Штраф кучности в формуле попадания (0…1 в данных, в комбинировании clamp до 0.95). |
| `WeaponTrajectoryHeight` | `int` | «Высота траектории» для правил ЛС со стенами (0 низкая, 1 обычная, 2 высокая; при экипировке clamp 0…3). |
| `WeaponIsSniper` | `bool` | Снайперская кривая **p** за пределами `WeaponRange` (см. §2). |

Источник: таблица **`weapons`** (`spread_penalty`, `trajectory_height`, `is_sniper`, `damage_min`/`damage_max`, …), подстановка при **join**, **equip** и хранение в **`PlayerCombatProfiles`** (кортеж из **11** полей: min/max урон, дальность, ОД атаки, меткость, кучность, траектория, снайпер).

---

## 5. Линия выстрела и стены (п. 7.20)

**Файл:** `BattleRoom.LineOfFire.cs`

- Теги стен: **`wall`**, **`damaged_wall`**, **`wall_low`**, **`damaged_wall_low`**.
- **Высота преграды по ЛС:** полная стена → **2**, низкая → **1**.
- **`CellBlocksLineOfFire(tag, weaponTrajectoryHeight)`:** блокирует луч, если это стена **и** `высота_стены >= WeaponTrajectoryHeight`.

Примеры:

- Траектория **2**, стена **wall_low** (1): **не** блокирует (1 ≥ 2 ложно).
- Траектория **2**, стена **wall** (2): блокирует (2 ≥ 2).
- Траектория **1**, **wall_low**: блокирует (1 ≥ 1).

В **`BattleRoom.CloseRound.cs`** поиск первой стены на луче использует **`CellBlocksLineOfFire`**, а не просто «любой wall-тег».

В **`BattleRoom.WallObstacles.cs`** при изменении HP стены тег сохраняет признак низкой стены (`wall_low` ↔ `damaged_wall_low`).

**Генерация карты:** по умолчанию по-прежнему ставятся только **`wall`** (высота 2); поведение как раньше, пока не появятся **`wall_low`**.

---

## 6. База данных PostgreSQL

- Убрано **`DROP TABLE weapons`** из `BattlePostgresDatabase.EnsureCreated`, чтобы не уничтожать таблицу при каждом запуске.
- Таблица **`weapons`** создаётся с полями:
  - `spread_penalty` (REAL),
  - `trajectory_height` (INT),
  - `is_sniper` (BOOLEAN) — влияет на **p** за пределами дальности;
  - `quality`, `weapon_condition` (INT) — **только хранение контента**, в расчёты боя **не входят** (п. 3.10).
- Сид строки **`fist`** с дефолтами (в т.ч. spread 0, trajectory 1).

**Код:** `BattlePostgresDatabase.cs`, `BattleWeaponDatabase.cs`, DTO **`BattleWeaponBrowseRowDto`** в `Models/BattleModels.cs`.

**API:** `WeaponUpsertRequest` и `UpsertWeapon` принимают новые поля; ответ **equip-weapon** включает `weaponSpreadPenalty`, `weaponTrajectoryHeight`.

---

## 7. JSON: спавн и состояние боя

- **`BattleStartedPayloadDto`:**  
  `SpawnWeaponSpreadPenalties` (`double[]`),  
  `SpawnWeaponTrajectoryHeights` (`int[]`),  
  `SpawnWeaponIsSnipers` (`bool[]`) — параллельно остальным spawn-массивам.
- **`FillSpawnArrays`** заполняет их из `UnitStateDto` (игроки и мобы).
- **`BattleStateResponse`** (GET `/api/battle/{battleId}`): те же массивы для отладки/клиента.
- **`PlayerTurnResultDto`:** в конце раунда отдаются `WeaponSpreadPenalty`, `WeaponTrajectoryHeight`, `WeaponIsSniper`.

---

## 8. Join, экипировка, мобы, фолбэки

- **`BattleRoomStore.JoinOrCreate`:** в конец добавлены `weaponSpreadPenalty`, `weaponTrajectoryHeight`, `weaponIsSniper` → **`SetPlayerCombatProfile`**.
- **`Program.cs`:** при join передаются значения из строки оружия в БД (сейчас типично `fist`).
- **`TryEquipWeapon`:** расширенная сигнатура; обновляет юнит и **11-полевой** профиль (min/max урон, снайпер и др.).
- **`CloseRound`:** действие **`EquipWeapon`** подтягивает из БД также spread, trajectory и **`IsSniper`**.
- **Моб** (`BattleRoom.UnitsLifecycle.cs`): явно задаются `Accuracy`, `WeaponSpreadPenalty = 0`, `WeaponTrajectoryHeight = 1`, `WeaponIsSniper = false`.
- **Фолбэк-юнит** в **`SubmitTurn`:** те же поля, чтобы не оставлять неинициализированное состояние.

---

## 9. Прочие правки сборки / моделей

- Восстановлен и расширен **`BattleWeaponBrowseRowDto`**.
- **`TryGetCombatProfile`:** актуальная сигнатура без `weapon_code` у пользователя; оружие при входе в бой берётся из БД (например, `fist`).

---

## 10. Клиент Unity (точечно)

- **`GameSession`:** снята блокировка Ctrl-выстрела «гекс вне дальности» (кроме вырожденного `weaponRange <= 0`).
- Разворот к цели и **VFX пули** идут до **реальной** цели, без обрезки по `weaponRange` (штрафы остаются на сервере).
- Поля `spawnWeaponSpreadPenalties` / `spawnWeaponTrajectoryHeights` в **`BattleStartedPayload`** на клиенте можно добавить отдельно; сервер уже отдаёт массивы в JSON.

---

## 11. Дизайн без полной реализации в коде

В **`BattleRoom.CombatRules.cs`** задокументировано:

- **2.5** — метательное/гранаты: то же действие **`Attack`** и тот же гексовый прицел.
- **2.6** — три броска слота силуэта **с возвращением** (один слот может повториться).

**Симуляция гранаты** (три броска по частям тела) в коде **пока не реализована** — только зафиксированные правила в комментариях.

---

## Карта файлов (ориентир)

| Область | Файлы |
|---------|--------|
| Дистанция, урон, вызов комбинирования p | `BattleRoom.CloseRound.cs` |
| Формула 5.15, дизайн-комментарии | `BattleRoom.CombatRules.cs` |
| ЛС и теги стен | `BattleRoom.LineOfFire.cs`, `BattleRoom.WallObstacles.cs` |
| Профили, equip | `BattleRoom.Players.cs`, `BattleRoomStore.cs`, `Program.cs` |
| Юниты, спавн | `BattleRoom.UnitsLifecycle.cs`, `BattleRoom.SpawnPayloads.cs`, `BattleRoom.SubmitTurn.cs` |
| DTO | `Models/BattleModels.cs` |
| БД | `BattlePostgresDatabase.cs`, `BattleWeaponDatabase.cs` |
| Клиент | `Assets/Scripts/GameSession.cs` |

---

*Документ отражает состояние логики на момент добавления этого README.*
