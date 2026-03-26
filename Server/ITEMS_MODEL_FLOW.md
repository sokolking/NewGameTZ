# Item-Centric Model: Как это теперь работает

Этот документ описывает текущую item-центричную архитектуру после рефактора.

## 1) Основная идея

- Любая сущность инвентаря — это `item`.
- `weapon` и `ammo` — подтипы item.
- Источник истины:
  - экземпляры оружия и `chamberRounds` -> `user_inventory_items`
  - запас патронов -> `user_ammo_packs.rounds_count`
  - общие атрибуты предмета -> `items`

## 2) Модель БД

### `items`

Общие поля:

- `name`
- `mass`
- `quality`
- `condition`
- `icon_key`
- `type` (`weapon` или `ammo`)
- `inventorygrid` (`0 | 1 | 2`)

### Связи

- `weapons.item_id -> items.id`
- `ammo_types.item_id -> items.id`

Поля, специфичные для оружия/патронов, остаются в `weapons` и `ammo_types`, общие данные — в `items`.

### Важные миграции

- `rounds_per_pack` удален из `ammo_types`.
- Запас патронов теперь представлен напрямую через `rounds_count`.

## 3) API (актуальное состояние)

### Клиентский auth/user endpoint

- `POST /api/db/user/items`
  - вход: `username/password`
  - выход: `{ slots }`
  - используется и для проверки логина, и для загрузки инвентаря.

### Админские endpoint'ы пользователей

- `GET /api/db/users/{userId}/items`
- `PUT /api/db/users/{userId}/items`

Legacy split routes (`/inventory`, `/ammo`) удалены.

### Справочные endpoint'ы

- `GET /api/db/weapons`
- `POST /api/db/weapons`
- `GET /api/db/ammo`
- `POST /api/db/ammo`

## 4) Поведение админ-страниц

### `/weapons`

- Поддерживает `inventoryGrid`.
- Для `category = cold` значение `range` предустанавливается в `1` при сохранении в админке (в данных, без хардкода в боевой логике).

### `/ammo`

Редактируются общие item-поля:

- `name`
- `unitWeight` (используется как масса для ammo)
- `quality`
- `condition`
- `iconKey`
- `inventoryGrid`
- `caliber`

### `/users`

Единый список `items`:

- `weapon` (не стакуемый, с `chamberRounds`, `isEquipped`)
- `ammo` (стакуемый, с `quantity`)

Валидация при сохранении:

- ровно одно экипированное оружие
- обязательно наличие `fist`
- нет пересечений слотов
- корректный `startSlot` и корректная ширина оружия

## 5) Клиентский флоу (Inventory + Ammo)

### Загрузка

- `InventoryUI` запрашивает `slots` через `POST /api/db/user/items`.
- Ammo-стэки собираются напрямую из `slots` (`stackable + quantity`), без отдельного запроса `/user/ammo`.

### Отображение

- `InventoryCellCount` показывается только для stackable-предметов.
- Формат для больших значений:
  - `1067 -> 1k`
  - `1248 -> 1.2k`
  - `9478 -> 9.4k`

### Видимость Ammo UI

Ammo donut/text отображается только если по данным оружия:

- `magazineSize > 0`
- `caliber` не пустой

Иначе UI скрывается.

## 6) Флоу раунда и авторитетность сервера

### Во время раунда (клиент)

- Клиент делает локальный прогноз ammo/chamber для мгновенного UI-отклика.

### При закрытии раунда (сервер)

- `ActionAttack` расходует только `chamberRounds`.
- `ActionReload` заряжает только в пределах доступного резерва (`rounds_count`) и вычисляет фактически загруженное `loaded`.
- В конце раунда:
  - из `user_ammo_packs.rounds_count` вычитается фактически загруженное `loaded`
  - `chamberRounds` оружия сохраняется в БД

### Начало следующего раунда

- Клиент получает серверное состояние и перезаписывает локальный прогноз.

## 7) Логика Equip и chamber

- `EquipWeapon` читает состояние патронника из БД (`TryGetUserWeaponChamberRounds`) для выбранного оружия.
- Оружие больше не заполняется автоматически до полного магазина при экипировке.

## 8) Что удалено как legacy

- Логика `rounds_per_pack`.
- Админские routes:
  - `/api/db/users/{id}/inventory`
  - `/api/db/users/{id}/ammo`
- Auth routes:
  - `/api/db/user/inventory`
  - `/api/db/user/ammo`
- Legacy split DTO/методы, использовавшиеся только старым inventory/ammo admin флоу.

## 9) Чеклист smoke-проверки

1. В `/users` настроить items (оружие + стак патронов).
2. Запустить бой, сделать несколько выстрелов.
3. Перезарядиться до отправки хода:
   - локальный `InventoryCellCount` должен обновиться сразу.
4. Завершить ход:
   - сервер обновляет резерв и патронник в БД.
5. Новый раунд:
   - клиентское состояние совпадает с серверным.