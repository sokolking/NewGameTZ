using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Загрузка инвентаря из БД, иконки 32×32 в ячейках, активное оружие 64×64 в ActiveItem (внутри Canvas ActiveItemPanel).
/// Клик по ячейке — смена оружия (2 ОД); стоимость атаки — «ОД:X» из БД и <see cref="Player.WeaponAttackApCost"/>.
/// </summary>
public sealed class InventoryUI : MonoBehaviour
{
    private const float CellIconSize = 32f;
    private const float ActiveIconSize = 64f;

    [SerializeField] private Player _player;
    [SerializeField] private Transform _inventoryRoot;
    [SerializeField] private Image[] _cellImages = new Image[12];
    [SerializeField] private Button[] _cellButtons = new Button[12];
    [SerializeField] private TextMeshProUGUI[] _cellCountTmps = new TextMeshProUGUI[12];
    [SerializeField] private Text[] _cellCountLegacies = new Text[12];
    [SerializeField] private Image _activeWeaponImage;
    [Tooltip("Text for AP:X attack cost with current weapon. Wire object named ItemAtionPointsCost in scene (legacy typo).")]
    [SerializeField] private TextMeshProUGUI _itemActionPointsCostTmp;
    [SerializeField] private Text _itemActionPointsCostLegacy;
    [Header("Active item ammo donut")]
    [SerializeField] private Graphic _activeItemAmmoDonutImage;
    [SerializeField] private Text _activeItemAmmoText;
    [Header("Debug")]
    [SerializeField] private bool _debugAmmoLogs = false;
    [Header("Item card hover")]
    [SerializeField] private ItemCardView _itemCardView;
    [SerializeField] private Vector2 _itemCardCursorOffset = new Vector2(0f, 10f);

    private UserInventorySlotPayload[] _slots = new UserInventorySlotPayload[12];
    private readonly Dictionary<long, WeaponDbRowPayload> _weaponRowsById = new();
    /// <summary>Reserve rounds keyed by ammo item id (<c>items.id</c>).</summary>
    private readonly Dictionary<long, UserAmmoPackPayload> _ammoByAmmoTypeId = new();
    private readonly Dictionary<long, int> _localAmmoRoundsByAmmoTypeId = new();
    /// <summary>Medicine uses left per <c>items.id</c>; updated from <see cref="Player.MedicineInventoryRounds"/> while medicine is equipped.</summary>
    private readonly Dictionary<long, int> _localMedicineRoundsByItemId = new();
    private readonly Dictionary<long, int> _localMagazineRoundsByWeapon = new();
    private readonly Dictionary<long, int> _serverChamberRoundsByWeapon = new();
    private long _ammoWeaponItemIdApplied;
    private int _ammoMagazineCapacity;
    private int _ammoReserveRounds;
    private ActiveItemAmmoDonutSlider _ammoDonutSlider;
    private StripedDonutIndicator _ammoStripedIndicator;
    private BattleServerConnection _serverConnection;
    private int _lastDisplayedAttackApCost = int.MinValue;
    private int _lastAmmoActionCount = -1;
    private int _lastAmmoAp = int.MinValue;
    private float _findPlayerCooldown;
    private int _lastKnownReloadApCost = 1;
    private string _lastAmmoLogLine;
    private bool _itemCardVisible;
    private int _hoveredSlotIndex = -1;
    readonly List<RaycastResult> _hoverRaycastBuffer = new();

    private void Awake()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        _serverConnection = FindFirstObjectByType<BattleServerConnection>();
        ResolveHierarchyIfNeeded();
        ClearActiveWeaponPlaceholder();
        TryResolveAmmoUi();
        EnsureItemCardReference();
        HideItemCard();
    }

    private void OnEnable()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        if (_player != null)
            _player.OnEquippedWeaponChanged += OnPlayerEquippedWeaponChanged;
        BattleSessionStateHooks.OnBattleIdentified += OnBattleIdentifiedForInventory;
        // Сразу убираем белый квадрат по умолчанию (Image без спрайта) и подставляем иконку, если Player уже есть.
        OnPlayerEquippedWeaponChanged();
        StartCoroutine(InventoryOnEnableCoroutine());
    }

    /// <summary>
    /// Профайлер: один вход вместо двух <see cref="StartCoroutine"/>; внутри параллельно
    /// <see cref="LoadInventoryFromServerCoroutine"/> (HTTP) и <see cref="RefreshActiveWeaponAfterLayoutCoroutine"/> (2 кадра после layout).
    /// </summary>
    private IEnumerator InventoryOnEnableCoroutine()
    {
        Coroutine load = StartCoroutine(LoadInventoryFromServerCoroutine());
        Coroutine layout = StartCoroutine(RefreshActiveWeaponAfterLayoutCoroutine());
        yield return load;
        yield return layout;
    }

    private void OnDisable()
    {
        BattleSessionStateHooks.OnBattleIdentified -= OnBattleIdentifiedForInventory;
        if (_player != null)
            _player.OnEquippedWeaponChanged -= OnPlayerEquippedWeaponChanged;
        HideItemCard();
    }

    private void Update()
    {
        if (_player == null)
        {
            _findPlayerCooldown -= Time.deltaTime;
            if (_findPlayerCooldown > 0f) return;
            _findPlayerCooldown = 1f;
            _player = FindFirstObjectByType<Player>();
            if (_player != null)
                _player.OnEquippedWeaponChanged += OnPlayerEquippedWeaponChanged;
        }
        if (_player == null)
            return;

        var actions = _player.GetTurnActionsCopy();
        int actionCount = actions != null ? actions.Length : 0;
        int ap = _player.CurrentAp;
        if (actionCount == _lastAmmoActionCount && ap == _lastAmmoAp)
            return;
        _lastAmmoActionCount = actionCount;
        _lastAmmoAp = ap;
        RefreshActiveItemAmmoUi();
        SyncLocalAmmoFromCurrentWeaponState();
        SyncLocalMedicineFromCurrentWeaponState();
        SyncLocalMagazineFromCurrentWeaponState();
        ApplySlotsToCells();
        if (_itemCardVisible && !IsPointerOverInventoryCell())
            HideItemCard();
        UpdateItemCardPosition();
    }

    /// <summary>
    /// OnEnable срабатывает до Start у <see cref="BattleServerConnection"/>, поэтому первый запрос инвентаря
    /// мог уйти на localhost из инспектора. После идентификации боя URL уже верный — перезагружаем слоты.
    /// </summary>
    private void OnBattleIdentifiedForInventory(string battleId, string playerId, string serverUrl)
    {
        if (GameSession.Active != null && GameSession.Active.IsSpectatorMode)
            return;
        if (!string.IsNullOrEmpty(serverUrl))
            StartCoroutine(LoadInventoryFromServerCoroutine());
    }

    /// <summary>Базовый URL API для /api/db/user/items: сначала URL сессии (уже известен до Start), затем продакшен из меню, затем компонент соединения.</summary>
    private static string ResolveInventoryApiBaseUrl(BattleServerConnection connection)
    {
        if (!string.IsNullOrEmpty(BattleSessionState.ServerUrl))
            return BattleSessionState.ServerUrl.TrimEnd('/');
        if (!string.IsNullOrEmpty(BattleServerRuntime.CurrentBaseUrl))
            return BattleServerRuntime.CurrentBaseUrl.TrimEnd('/');
        if (connection != null && !string.IsNullOrEmpty(connection.ServerUrl))
            return connection.ServerUrl.TrimEnd('/');
        return "";
    }

    private void OnPlayerEquippedWeaponChanged()
    {
        _lastDisplayedAttackApCost = int.MinValue;
        RefreshActiveWeaponIcon();
        RefreshItemActionPointsCostText();
        RefreshActiveItemAmmoUi();
    }

    private void ResolveHierarchyIfNeeded()
    {
        if (_inventoryRoot == null)
        {
            var go = GameObject.Find(UiHierarchyNames.Inventory)
                     ?? GameObject.Find(UiHierarchyNames.IntentoryTypo);
            if (go != null)
                _inventoryRoot = go.transform;
        }

        if (_inventoryRoot != null)
        {
            for (int i = 0; i < 12; i++)
            {
                int n = i + 1;
                bool needResolve = _cellImages[i] == null || _cellButtons[i] == null || (_cellCountTmps[i] == null && _cellCountLegacies[i] == null);

                if (needResolve)
                {
                    Transform cell = _inventoryRoot.Find(UiHierarchyNames.InventoryCellName(n))
                                     ?? FindChildRecursive(_inventoryRoot, UiHierarchyNames.InventoryCellName(n));
                    if (cell == null)
                        continue;

                    if (_cellButtons[i] == null)
                        _cellButtons[i] = cell.GetComponent<Button>() ?? cell.gameObject.AddComponent<Button>();
                    if (_cellImages[i] == null)
                    {
                        var imgTr = cell.Find(UiHierarchyNames.InventoryCellImage)
                                    ?? FindChildRecursive(cell, UiHierarchyNames.InventoryCellImage);
                        if (imgTr != null)
                            _cellImages[i] = imgTr.GetComponent<Image>();
                    }
                    if (_cellCountTmps[i] == null && _cellCountLegacies[i] == null)
                    {
                        var countTr = cell.Find(UiHierarchyNames.InventoryCellCount)
                                      ?? FindChildRecursive(cell, UiHierarchyNames.InventoryCellCount);
                        if (countTr != null)
                        {
                            _cellCountTmps[i] = countTr.GetComponent<TextMeshProUGUI>() ?? countTr.GetComponentInChildren<TextMeshProUGUI>(true);
                            if (_cellCountTmps[i] == null)
                                _cellCountLegacies[i] = countTr.GetComponent<Text>() ?? countTr.GetComponentInChildren<Text>(true);
                        }
                    }
                }

                // Всегда вешаем клик: раньше при заполненных из инспектора Image+Button делали continue и слушатели не добавлялись.
                int slot = i;
                if (_cellButtons[i] != null)
                {
                    _cellButtons[i].onClick.RemoveAllListeners();
                    _cellButtons[i].onClick.AddListener(() => OnCellClicked(slot));
                    var hover = _cellButtons[i].GetComponent<InventoryCellHoverRelay>();
                    if (hover == null)
                        hover = _cellButtons[i].gameObject.AddComponent<InventoryCellHoverRelay>();
                    hover.Owner = this;
                    hover.SlotIndex = slot;
                }
            }
        }

        ValidateActiveWeaponImageReference();
    }

    /// <summary>
    /// Иконка должна быть на дочернем <see cref="UiHierarchyNames.ActiveItem"/>, не на Canvas <see cref="UiHierarchyNames.ActiveItemPanel"/>.
    /// Если в инспекторе перетянули Image с панели — сбрасываем и ищем заново.
    /// </summary>
    private void ValidateActiveWeaponImageReference()
    {
        if (_activeWeaponImage != null)
        {
            var go = _activeWeaponImage.gameObject;
            if (string.Equals(go.name, UiHierarchyNames.ActiveItemPanel, StringComparison.Ordinal))
            {
                _activeWeaponImage = null;
            }
            else
            {
                var panelGo = GameObject.Find(UiHierarchyNames.ActiveItemPanel);
                if (panelGo != null && go.transform.IsChildOf(panelGo.transform))
                {
                    if (!string.Equals(go.name, UiHierarchyNames.ActiveItem, StringComparison.Ordinal))
                        _activeWeaponImage = null;
                }
            }
        }

        if (_activeWeaponImage == null)
            TryResolveActiveWeaponImage();
        TryResolveAmmoUi();
    }

    /// <summary>Новая иерархия: ActiveItemPanel (Canvas) → ActiveItem (Image). Старый вариант: один объект ActiveItem с Image.</summary>
    private void TryResolveActiveWeaponImage()
    {
        var panelGo = GameObject.Find(UiHierarchyNames.ActiveItemPanel);
        if (panelGo != null)
        {
            Transform tr = panelGo.transform.Find(UiHierarchyNames.ActiveItem)
                           ?? FindChildRecursive(panelGo.transform, UiHierarchyNames.ActiveItem);
            if (tr != null)
            {
                _activeWeaponImage = tr.GetComponent<Image>();
                return;
            }
        }

        var legacy = GameObject.Find(UiHierarchyNames.ActiveItem);
        if (legacy != null)
            _activeWeaponImage = legacy.GetComponent<Image>();
    }

    private void TryResolveAmmoUi()
    {
        var panelGo = GameObject.Find(UiHierarchyNames.ActiveItemPanel);
        if (panelGo == null)
            return;

        if (_activeItemAmmoDonutImage == null)
        {
            Transform donutTr = panelGo.transform.Find(UiHierarchyNames.ActiveItemAmmoDonut)
                               ?? FindChildRecursive(panelGo.transform, UiHierarchyNames.ActiveItemAmmoDonut);
            if (donutTr == null)
                return;
            _activeItemAmmoDonutImage = donutTr.GetComponent<Graphic>();
        }

        if (_activeItemAmmoDonutImage != null)
        {
            _ammoDonutSlider = _activeItemAmmoDonutImage.GetComponent<ActiveItemAmmoDonutSlider>();
            if (_ammoDonutSlider == null)
                _ammoDonutSlider = _activeItemAmmoDonutImage.gameObject.AddComponent<ActiveItemAmmoDonutSlider>();
            _ammoStripedIndicator = _activeItemAmmoDonutImage.GetComponent<StripedDonutIndicator>();
            _ammoDonutSlider.OnValueChanged -= OnAmmoDonutValueChanged;
            _ammoDonutSlider.OnValueChanged += OnAmmoDonutValueChanged;
        }

        if (_activeItemAmmoText == null)
        {
            Transform txtTr = panelGo.transform.Find(UiHierarchyNames.ActiveItemAmmoText)
                             ?? FindChildRecursive(panelGo.transform, UiHierarchyNames.ActiveItemAmmoText);
            if (txtTr == null)
                return;
            _activeItemAmmoText = txtTr.GetComponent<Text>();
        }
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == name)
                return c;
            var d = FindChildRecursive(c, name);
            if (d != null)
                return d;
        }
        return null;
    }

    /// <summary>GET слотов с <c>/api/db/user/items</c>, заполнение ячеек; при ошибке — локальный fallback.</summary>
    private IEnumerator LoadInventoryFromServerCoroutine()
    {
        ResolveHierarchyIfNeeded();

        string token = BattleSessionState.AccessToken;
        if (_serverConnection == null)
            _serverConnection = FindFirstObjectByType<BattleServerConnection>();
        string baseUrl = ResolveInventoryApiBaseUrl(_serverConnection);

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(baseUrl))
        {
            FillFallbackLocalIcons();
            OnPlayerEquippedWeaponChanged();
            yield break;
        }

        string url = $"{baseUrl}/api/db/user/items";
        string responseText = null;
        string err = null;
        yield return HttpSimple.GetStringWithAuth(url, token, b => responseText = b, e => err = e);

        if (err != null)
        {
            FillFallbackLocalIcons();
            OnPlayerEquippedWeaponChanged();
            yield break;
        }

        if (string.IsNullOrEmpty(responseText))
        {
            FillFallbackLocalIcons();
            OnPlayerEquippedWeaponChanged();
            yield break;
        }

        {
            // JsonUtility хуже переносит nullable/optional поля слотов; используем Newtonsoft.
            UserInventorySlotsPayload parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<UserInventorySlotsPayload>(responseText);
            }
            catch
            {
                parsed = null;
            }
            if (parsed?.slots == null || parsed.slots.Length == 0)
            {
                FillFallbackLocalIcons();
                RefreshActiveWeaponIcon();
                yield break;
            }

            for (int i = 0; i < 12; i++)
                _slots[i] = new UserInventorySlotPayload { slotIndex = i };

            foreach (var s in parsed.slots)
            {
                if (s.slotIndex >= 0 && s.slotIndex < 12)
                    _slots[s.slotIndex] = s;
            }

            _serverChamberRoundsByWeapon.Clear();
            foreach (var s in _slots)
            {
                if (s == null || s.continuation)
                    continue;
                long weaponItemId = s.itemId ?? 0;
                if (weaponItemId <= 0)
                    continue;
                if (_serverChamberRoundsByWeapon.ContainsKey(weaponItemId))
                    continue;
                _serverChamberRoundsByWeapon[weaponItemId] = Mathf.Max(0, s.chamberRounds);
                AmmoLog($"[InventorySync] slot weaponItemId={weaponItemId}, slotIndex={s.slotIndex}, chamberRounds={s.chamberRounds}, equipped={s.equipped}, stackable={s.stackable}");
            }

            ApplySlotsToCells();
            yield return LoadAmmoAndWeaponsCoroutine(baseUrl);
            OnPlayerEquippedWeaponChanged();
        }
    }

    public void ReloadInventoryFromServer()
    {
        if (GameSession.Active != null && GameSession.Active.IsSpectatorMode)
            return;
        StartCoroutine(LoadInventoryFromServerCoroutine());
    }

    private IEnumerator LoadAmmoAndWeaponsCoroutine(string baseUrl)
    {
        string weaponsText = null;
        string weaponsErr = null;

        string weaponsUrl = $"{baseUrl}/api/db/weapons?take=2000";
        string medicineText = null;
        string medicineErr = null;
        string medicineUrl = $"{baseUrl}/api/db/medicine?take=2000";

        yield return HttpSimple.GetString(weaponsUrl, b => weaponsText = b, e => weaponsErr = e);
        yield return HttpSimple.GetString(medicineUrl, b => medicineText = b, e => medicineErr = e);

        _weaponRowsById.Clear();
        if (string.IsNullOrEmpty(weaponsErr) && !string.IsNullOrEmpty(weaponsText))
        {
            WeaponDbRowPayload[] rows = null;
            try { rows = JsonConvert.DeserializeObject<WeaponDbRowPayload[]>(weaponsText, HopeBattleJson.DeserializeSettings); } catch { }
            if (rows != null)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    var r = rows[i];
                    if (r == null || r.id <= 0)
                        continue;
                    _weaponRowsById[r.id] = r;
                }
            }
        }
        if (string.IsNullOrEmpty(medicineErr) && !string.IsNullOrEmpty(medicineText))
        {
            WeaponDbRowPayload[] medRows = null;
            try { medRows = JsonConvert.DeserializeObject<WeaponDbRowPayload[]>(medicineText, HopeBattleJson.DeserializeSettings); } catch { }
            if (medRows != null)
            {
                for (int i = 0; i < medRows.Length; i++)
                {
                    var r = medRows[i];
                    if (r == null || r.id <= 0)
                        continue;
                    r.itemType = "medicine";
                    r.damageMin = 0;
                    r.damageMax = 0;
                    r.range = 0;
                    r.magazineSize = 0;
                    r.reloadApCost = 0;
                    r.category = "";
                    _weaponRowsById[r.id] = r;
                }
            }
        }

        _ammoByAmmoTypeId.Clear();
        _localAmmoRoundsByAmmoTypeId.Clear();
        _localMagazineRoundsByWeapon.Clear();
        _localMedicineRoundsByItemId.Clear();
        RebuildAmmoCacheFromSlots();
        RebuildMedicineCacheFromSlots();
        RefreshActiveItemAmmoUi(forceResetOnWeaponChange: true);
        ApplySlotsToCells();
    }

    private void RebuildAmmoCacheFromSlots()
    {
        if (_slots == null || _slots.Length == 0)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (slot == null || !slot.stackable || slot.continuation)
                continue;
            long ammoKey = ResolveAmmoStackKey(slot);
            if (ammoKey <= 0)
                continue;
            int rounds = GetServerSlotRounds(slot);
            if (_localAmmoRoundsByAmmoTypeId.ContainsKey(ammoKey))
                _localAmmoRoundsByAmmoTypeId[ammoKey] += rounds;
            else
                _localAmmoRoundsByAmmoTypeId[ammoKey] = rounds;
        }

        foreach (var kv in _localAmmoRoundsByAmmoTypeId)
        {
            _ammoByAmmoTypeId[kv.Key] = new UserAmmoPackPayload
            {
                ammoTypeId = kv.Key,
                roundsCount = kv.Value,
                totalRounds = kv.Value
            };
        }
    }

    private void RebuildMedicineCacheFromSlots()
    {
        if (_slots == null || _slots.Length == 0)
            return;
        _localMedicineRoundsByItemId.Clear();
        for (int i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (slot == null || slot.continuation || !IsMedicineInventorySlot(slot))
                continue;
            long itemId = slot.itemId ?? 0;
            if (itemId <= 0)
                continue;
            int r = GetServerSlotRounds(slot);
            if (_localMedicineRoundsByItemId.ContainsKey(itemId))
                _localMedicineRoundsByItemId[itemId] += r;
            else
                _localMedicineRoundsByItemId[itemId] = r;
        }
    }

    private int GetMedicineRoundsFromSlotsForItemId(long itemId)
    {
        if (itemId <= 0)
            return 0;
        int sum = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            var s = _slots[i];
            if (s == null || s.continuation)
                continue;
            if ((s.itemId ?? 0) != itemId)
                continue;
            if (!IsMedicineInventorySlot(s))
                continue;
            sum += GetServerSlotRounds(s);
        }
        return sum;
    }

    /// <summary>Stackable ammo rows: prefer server <see cref="UserInventorySlotPayload.ammoTypeId"/>, else <see cref="UserInventorySlotPayload.itemId"/>.</summary>
    private static long ResolveAmmoStackKey(UserInventorySlotPayload slot)
    {
        if (slot == null)
            return 0;
        long aid = slot.ammoTypeId ?? 0;
        if (aid > 0)
            return aid;
        long iid = slot.itemId ?? 0;
        return slot.stackable && iid > 0 ? iid : 0;
    }

    private void FillFallbackLocalIcons()
    {
        for (int i = 0; i < 12; i++)
            _slots[i] = new UserInventorySlotPayload { slotIndex = i };
        ApplySlotsToCells();
    }

    private void ApplySlotsToCells()
    {
        for (int i = 0; i < 12; i++)
        {
            if (_cellButtons[i] != null)
                _cellButtons[i].interactable = false;
            if (_cellImages[i] == null)
                continue;
            var s = _slots[i];
            if (s == null)
            {
                SetImageIcon(_cellImages[i], null, CellIconSize);
                if (_cellImages[i] != null)
                    _cellImages[i].color = Color.white;
                SetCellCountVisible(i, false, "");
                continue;
            }

            string key = ResolveItemIconKey(s);

            if (string.IsNullOrWhiteSpace(key))
            {
                SetImageIcon(_cellImages[i], null, CellIconSize);
                if (_cellImages[i] != null)
                    _cellImages[i].color = Color.white;
                SetCellCountVisible(i, false, "");
                continue;
            }

            var sp = WeaponIconHelper.LoadInventoryIcon(key);
            SetImageIcon(_cellImages[i], sp, CellIconSize);
            if (_cellImages[i] != null)
            {
                bool dim = s.continuation || !s.isEquippable;
                _cellImages[i].color = dim ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            }
            int displayQty = GetDisplayedItemQuantity(s);
            bool showCount = !s.continuation && displayQty > 0
                && (s.stackable || IsMedicineInventorySlot(s));
            SetCellCountVisible(i, showCount, showCount ? FormatStackCount(displayQty) : "");
            if (_cellButtons[i] != null)
                _cellButtons[i].interactable = !s.continuation && s.isEquippable && (s.itemId ?? 0) > 0;
        }
    }

    /// <summary>Stack count from API: <c>rounds</c> (<c>user_inventory_items.rounds</c>), then legacy <c>quantity</c>.</summary>
    private static int GetServerSlotRounds(UserInventorySlotPayload slot)
    {
        if (slot == null)
            return 0;
        int r = Mathf.Max(0, slot.rounds);
        int q = Mathf.Max(0, slot.quantity);
        // Medicine: prefer max(rounds, quantity) so a mistaken weapon slot (quantity=1) still shows DB rounds.
        if (IsMedicineInventorySlot(slot))
            return Mathf.Max(r, q);
        if (r > 0)
            return r;
        return q;
    }

    private int GetDisplayedItemQuantity(UserInventorySlotPayload slot)
    {
        if (slot == null)
            return 0;

        int serverRounds = GetServerSlotRounds(slot);

        if (!slot.stackable)
        {
            if (IsMedicineInventorySlot(slot))
            {
                long itemId = slot.itemId ?? 0;
                if (itemId > 0 && _localMedicineRoundsByItemId.TryGetValue(itemId, out int localMed))
                    return Mathf.Max(0, localMed);
                return serverRounds;
            }
            return 0;
        }

        long ammoKey = ResolveAmmoStackKey(slot);
        if (ammoKey <= 0)
            return serverRounds;
        if (_localAmmoRoundsByAmmoTypeId.TryGetValue(ammoKey, out int localQty))
            return Mathf.Max(0, localQty);
        return serverRounds;
    }

    private static bool IsMedicineInventorySlot(UserInventorySlotPayload slot)
    {
        if (slot == null)
            return false;
        return string.Equals((slot.itemType ?? "").Trim(), "medicine", StringComparison.OrdinalIgnoreCase);
    }

    private void SyncLocalAmmoFromCurrentWeaponState()
    {
        if (_player == null)
            return;
        long weaponItemId = _player.WeaponItemId;
        if (weaponItemId <= 0 || !_weaponRowsById.TryGetValue(weaponItemId, out var w) || w == null)
            return;
        long ammoId = w.ammoTypeId ?? 0;
        if (ammoId <= 0)
            return;
        _localAmmoRoundsByAmmoTypeId[ammoId] = Mathf.Max(0, _player.ReserveAmmoRounds);
    }

    private void SyncLocalMedicineFromCurrentWeaponState()
    {
        if (_player == null)
            return;
        long weaponItemId = _player.WeaponItemId;
        if (weaponItemId <= 0 || !_weaponRowsById.TryGetValue(weaponItemId, out var w) || w == null)
            return;
        if (!IsWeaponRowMedicine(w))
            return;
        _localMedicineRoundsByItemId[weaponItemId] = Mathf.Max(0, _player.MedicineInventoryRounds);
    }

    private void SyncLocalMagazineFromCurrentWeaponState()
    {
        if (_player == null)
            return;
        long weaponItemId = _player.WeaponItemId;
        if (weaponItemId <= 0)
            return;
        int cap = Mathf.Max(0, _player.MagazineCapacity);
        if (cap <= 0)
            return;
        _localMagazineRoundsByWeapon[weaponItemId] = Mathf.Clamp(_player.CurrentMagazineRounds, 0, cap);
    }

    private static string ResolveItemIconKey(UserInventorySlotPayload slot)
    {
        if (slot == null)
            return null;
        if (!string.IsNullOrWhiteSpace(slot.iconKey))
            return slot.iconKey.Trim();
        if (!string.IsNullOrWhiteSpace(slot.itemName))
            return slot.itemName.Trim();
        return null;
    }

    private static string FormatStackCount(int value)
    {
        int n = Math.Max(0, value);
        if (n <= 999)
            return n.ToString();

        float k = n / 1000f;
        if (k < 10f)
        {
            // Truncate to 1 decimal: 1.248 -> 1.2k, 9.478 -> 9.4k
            float oneDecimal = Mathf.Floor(k * 10f) / 10f;
            // Keep integer form for exact thousands: 1.0k -> 1k
            if (Mathf.Approximately(oneDecimal, Mathf.Floor(oneDecimal)))
                return $"{Mathf.FloorToInt(oneDecimal)}k";
            return $"{oneDecimal:0.0}k";
        }

        return $"{Mathf.FloorToInt(k)}k";
    }

    private void SetCellCountVisible(int index, bool visible, string text)
    {
        if (index < 0 || index >= 12)
            return;
        var tmp = _cellCountTmps[index];
        if (tmp != null)
        {
            tmp.text = visible ? text : "";
            tmp.enabled = visible;
            if (tmp.gameObject.activeSelf != visible)
                tmp.gameObject.SetActive(visible);
        }
        var legacy = _cellCountLegacies[index];
        if (legacy != null)
        {
            legacy.text = visible ? text : "";
            legacy.enabled = visible;
            if (legacy.gameObject.activeSelf != visible)
                legacy.gameObject.SetActive(visible);
        }
    }

    private void TryResolveItemActionPointsCostText()
    {
        if (_itemActionPointsCostTmp != null || _itemActionPointsCostLegacy != null)
            return;
        GameObject go = GameObject.Find(UiHierarchyNames.ItemActionPointsCost);
        if (go == null)
        {
            var panel = GameObject.Find(UiHierarchyNames.ActiveItemPanel);
            if (panel != null)
            {
                var tr = panel.transform.Find(UiHierarchyNames.ItemActionPointsCost)
                         ?? FindChildRecursive(panel.transform, UiHierarchyNames.ItemActionPointsCost);
                if (tr != null)
                    go = tr.gameObject;
            }
        }
        if (go == null)
            return;
        _itemActionPointsCostTmp = go.GetComponent<TextMeshProUGUI>() ?? go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (_itemActionPointsCostTmp == null)
            _itemActionPointsCostLegacy = go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>(true);
    }

    private void RefreshItemActionPointsCostText()
    {
        TryResolveItemActionPointsCostText();
        if (_itemActionPointsCostTmp == null && _itemActionPointsCostLegacy == null)
            return;
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        long weaponItemId = _player != null ? _player.WeaponItemId : 0;
        int attackApCost = IsActiveItemMedicine()
            ? GetCurrentActiveItemUseApCost()
            : GetAttackApCostForCurrentWeaponDisplay(weaponItemId);
        if (attackApCost == _lastDisplayedAttackApCost)
            return;
        _lastDisplayedAttackApCost = attackApCost;
        string s = Loc.Tf("ui.ap_colon", attackApCost);
        if (_itemActionPointsCostTmp != null)
            _itemActionPointsCostTmp.text = s;
        if (_itemActionPointsCostLegacy != null)
            _itemActionPointsCostLegacy.text = s;
    }

    /// <summary>Стоимость атаки (не смены оружия): слот инвентаря из БД или синхронизированное значение у игрока.</summary>
    private int GetAttackApCostForCurrentWeaponDisplay(long weaponItemId)
    {
        if (weaponItemId > 0 && _weaponRowsById.TryGetValue(weaponItemId, out var w) && w != null && w.attackApCost > 0)
            return w.attackApCost;
        for (int i = 0; i < 12; i++)
        {
            var s = _slots[i];
            if (s == null)
                continue;
            long slotWeaponItemId = s.itemId ?? 0;
            if (slotWeaponItemId <= 0 || slotWeaponItemId != weaponItemId)
                continue;
            if (s.useApCost > 0)
                return s.useApCost;
            break;
        }
        if (_player != null && _player.WeaponItemId == weaponItemId)
            return _player.WeaponAttackApCost;
        return 1;
    }

    private void RefreshActiveWeaponIcon()
    {
        ResolveHierarchyIfNeeded();
        ValidateActiveWeaponImageReference();
        if (_activeWeaponImage == null)
            return;

        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        if (_player == null)
        {
            ClearActiveWeaponPlaceholder();
            return;
        }

        long weaponItemId = _player.WeaponItemId;
        string iconKey = null;
        for (int i = 0; i < 12; i++)
        {
            var slot = _slots[i];
            if (slot == null)
                continue;
            long slotWeaponItemId = slot.itemId ?? 0;
            if (slotWeaponItemId <= 0 || slotWeaponItemId != weaponItemId)
                continue;
            if (!string.IsNullOrWhiteSpace(slot.iconKey))
                iconKey = slot.iconKey.Trim();
            break;
        }

        Sprite sp = !string.IsNullOrEmpty(iconKey) ? WeaponIconHelper.LoadInventoryIcon(iconKey) : null;
        SetImageIcon(_activeWeaponImage, sp, ActiveIconSize);
    }

    private void RefreshActiveItemAmmoUi(bool forceResetOnWeaponChange = false)
    {
        TryResolveAmmoUi();
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        long weaponItemId = _player != null ? _player.WeaponItemId : 0;
        long previousWeaponItemId = _ammoWeaponItemIdApplied;
        bool weaponChanged = previousWeaponItemId != weaponItemId;
        if (weaponChanged && _player != null && previousWeaponItemId > 0 && _player.MagazineCapacity > 0)
        {
            _localMagazineRoundsByWeapon[previousWeaponItemId] = Mathf.Clamp(_player.CurrentMagazineRounds, 0, _player.MagazineCapacity);
            AmmoLog($"[WeaponSwitch] save prev weaponItemId={previousWeaponItemId}, mag={_player.CurrentMagazineRounds}/{_player.MagazineCapacity}");
        }
        if (weaponChanged)
            _ammoWeaponItemIdApplied = weaponItemId;

        // Planning: medicine uses left in hand (like ammo reserve). Sync even when ammo donut UI is not assigned.
        if (weaponItemId > 0 && _weaponRowsById.TryGetValue(weaponItemId, out var wMed) && IsWeaponRowMedicine(wMed))
        {
            int serverMed = GetMedicineRoundsFromSlotsForItemId(weaponItemId);
            bool hasLocal = _localMedicineRoundsByItemId.TryGetValue(weaponItemId, out int localMed);
            if (forceResetOnWeaponChange)
            {
                // After GET /api/db/user/items — slots are authoritative; do not keep stale planning counts.
                int medQty = Mathf.Max(0, serverMed);
                _localMedicineRoundsByItemId[weaponItemId] = medQty;
                _player?.SetMedicineInventoryRounds(medQty, notify: false);
            }
            else if (weaponChanged || !hasLocal)
            {
                int medQty = hasLocal ? Mathf.Max(0, localMed) : serverMed;
                _localMedicineRoundsByItemId[weaponItemId] = medQty;
                _player?.SetMedicineInventoryRounds(medQty, notify: false);
            }
        }

        if (_activeItemAmmoDonutImage == null || _activeItemAmmoText == null)
            return;

        if (weaponItemId <= 0 || !_weaponRowsById.TryGetValue(weaponItemId, out var w))
        {
            SetAmmoUiVisible(false);
            return;
        }

        int mag = Mathf.Max(0, w.magazineSize);
        long ammoTypeId = w.ammoTypeId ?? 0;
        bool isMedicine = IsWeaponRowMedicine(w);
        if (isMedicine)
        {
            // Self-use items should hide both ammo donut and ammo count.
            SetAmmoUiVisible(false);
            return;
        }
        bool weaponUsesAmmo = mag > 0 && ammoTypeId > 0;
        if (!weaponUsesAmmo)
        {
            SetAmmoUiVisible(false);
            return;
        }

        _ammoMagazineCapacity = mag;
        if (ammoTypeId > 0 && _ammoByAmmoTypeId.TryGetValue(ammoTypeId, out var ammo))
        {
            int serverReserve = Mathf.Max(0, ammo.roundsCount > 0 ? ammo.roundsCount : ammo.totalRounds);
            bool hasLocal = _localAmmoRoundsByAmmoTypeId.TryGetValue(ammoTypeId, out int localReserve);
            int reserve = hasLocal ? Mathf.Max(0, localReserve) : serverReserve;
            _localAmmoRoundsByAmmoTypeId[ammoTypeId] = reserve;
            _ammoReserveRounds = reserve;
            // Do not overwrite local in-turn ammo on every frame.
            // Push into Player only on weapon switch / explicit server refresh, or when local value not initialized yet.
            if (_player != null && (weaponChanged || forceResetOnWeaponChange || !hasLocal))
                _player.SetReserveAmmoRounds(reserve, notify: false);
        }
        else if (ammoTypeId > 0)
        {
            // No stack in inventory for this ammo item id — reserve is 0 unless we already tracked local state.
            bool hasLocal = _localAmmoRoundsByAmmoTypeId.TryGetValue(ammoTypeId, out int localReserve);
            int reserve = hasLocal ? Mathf.Max(0, localReserve) : 0;
            _ammoReserveRounds = reserve;
            if (_player != null && (weaponChanged || forceResetOnWeaponChange || !hasLocal))
                _player.SetReserveAmmoRounds(reserve, notify: false);
        }
        int currentRounds = _ammoMagazineCapacity;
        string roundsSource = "default_mag_capacity";
        if (forceResetOnWeaponChange)
        {
            if (_serverChamberRoundsByWeapon.TryGetValue(weaponItemId, out int srv))
            {
                currentRounds = srv;
                roundsSource = "server_slot_chamber_force";
            }
            else if (_player != null)
            {
                currentRounds = _player.CurrentMagazineRounds;
                roundsSource = "player_current_force_fallback";
            }
        }
        else if (!weaponChanged && _player != null)
        {
            // While staying on the same weapon, Player state is the freshest (shot/reload just updated it).
            currentRounds = _player.CurrentMagazineRounds;
            roundsSource = "player_current_same_weapon";
        }
        else if (_localMagazineRoundsByWeapon.TryGetValue(weaponItemId, out int savedRounds))
        {
            currentRounds = savedRounds;
            roundsSource = "local_mag_cache";
        }
        else if (_serverChamberRoundsByWeapon.TryGetValue(weaponItemId, out int serverChamber))
        {
            currentRounds = serverChamber;
            roundsSource = "server_slot_chamber";
        }
        else if (!forceResetOnWeaponChange && _player != null && _player.MagazineCapacity == _ammoMagazineCapacity)
        {
            currentRounds = _player.CurrentMagazineRounds;
            roundsSource = "player_current_same_capacity";
        }
        currentRounds = Mathf.Clamp(currentRounds, 0, _ammoMagazineCapacity);
        _player?.SetMagazineState(_ammoMagazineCapacity, currentRounds, notify: false);
        if (weaponItemId > 0)
            _localMagazineRoundsByWeapon[weaponItemId] = currentRounds;
        AmmoLog($"[AmmoUi] weaponItemId={weaponItemId}, weaponChanged={weaponChanged}, source={roundsSource}, currentRounds={currentRounds}, magCap={_ammoMagazineCapacity}, forceReset={forceResetOnWeaponChange}");

        int spent = Mathf.Max(0, _ammoMagazineCapacity - currentRounds);
        float fill = _ammoMagazineCapacity > 0 ? (float)spent / _ammoMagazineCapacity : 0f;
        if (_ammoDonutSlider != null)
            _ammoDonutSlider.SetValue01(fill, notify: false);
        if (_ammoStripedIndicator != null)
        {
            _ammoStripedIndicator.SetStripeCount(_ammoMagazineCapacity);
            _ammoStripedIndicator.SetValue01(fill);
        }
        int reloadApCost = Mathf.Max(1, w.reloadApCost);
        _lastKnownReloadApCost = reloadApCost;
        _activeItemAmmoText.text = $"{reloadApCost} AP";
        SetAmmoUiVisible(true);
    }

    public int GetCurrentWeaponReloadApCost()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        long weaponItemId = _player != null ? _player.WeaponItemId : 0;
        if (weaponItemId > 0 && _weaponRowsById.TryGetValue(weaponItemId, out var w) && w != null)
            return Mathf.Max(1, w.reloadApCost);
        return Mathf.Max(1, _lastKnownReloadApCost);
    }

    public int GetCurrentActiveItemUseApCost()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        long weaponItemId = _player != null ? _player.WeaponItemId : 0;
        if (weaponItemId > 0 && _weaponRowsById.TryGetValue(weaponItemId, out var w) && w != null)
            return Mathf.Max(1, w.attackApCost);
        return 1;
    }

    public bool IsActiveItemMedicine()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        long weaponItemId = _player != null ? _player.WeaponItemId : 0;
        if (weaponItemId <= 0)
            return false;
        if (_player != null
            && string.Equals((_player.WeaponCategory ?? string.Empty).Trim(), "medicine", StringComparison.OrdinalIgnoreCase))
            return true;
        if (_weaponRowsById.TryGetValue(weaponItemId, out var w) && w != null)
            return IsWeaponRowMedicine(w);
        if (_slots != null)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i];
                if (s == null || s.continuation)
                    continue;
                if ((s.itemId ?? 0) != weaponItemId)
                    continue;
                return string.Equals((s.itemType ?? string.Empty).Trim(), "medicine", StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    private static bool IsWeaponRowMedicine(WeaponDbRowPayload row)
    {
        if (row == null)
            return false;
        if (string.Equals((row.category ?? string.Empty).Trim(), "medicine", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals((row.itemType ?? string.Empty).Trim(), "medicine", StringComparison.OrdinalIgnoreCase);
    }

    private void OnAmmoDonutValueChanged(float value01)
    {
        if (_ammoMagazineCapacity <= 0)
            return;
        int shotsUsed = Mathf.Clamp(Mathf.RoundToInt(value01 * _ammoMagazineCapacity), 0, _ammoMagazineCapacity);
        int currentRounds = Mathf.Max(0, _ammoMagazineCapacity - shotsUsed);
        _player?.SetMagazineState(_ammoMagazineCapacity, currentRounds, notify: false);
        if (_ammoWeaponItemIdApplied > 0)
            _localMagazineRoundsByWeapon[_ammoWeaponItemIdApplied] = currentRounds;
        AmmoLog($"[AmmoDonut] weaponItemId={_ammoWeaponItemIdApplied}, value01={value01:0.###}, currentRounds={currentRounds}, magCap={_ammoMagazineCapacity}");
        float fill = _ammoMagazineCapacity > 0 ? (float)shotsUsed / _ammoMagazineCapacity : 0f;
        if (_activeItemAmmoText != null && _weaponRowsById.TryGetValue(_ammoWeaponItemIdApplied, out var row))
            _activeItemAmmoText.text = $"{Mathf.Max(1, row.reloadApCost)} AP";
    }

    private void AmmoLog(string message)
    {
        if (!_debugAmmoLogs)
            return;
        if (string.Equals(_lastAmmoLogLine, message, StringComparison.Ordinal))
            return;
        _lastAmmoLogLine = message;
        Debug.Log("[InventoryUI] " + message);
    }

    private void SetAmmoUiVisible(bool visible)
    {
        if (_activeItemAmmoDonutImage != null)
            _activeItemAmmoDonutImage.enabled = visible;
        if (_activeItemAmmoText != null)
            _activeItemAmmoText.enabled = visible;
    }

    


    /// <summary>Убирает «белый квадрат» Unity UI Image без спрайта (до загрузки иконки).</summary>
    private void ClearActiveWeaponPlaceholder()
    {
        ValidateActiveWeaponImageReference();
        if (_activeWeaponImage == null)
            return;
        SetImageIcon(_activeWeaponImage, null, ActiveIconSize);
    }

    /// <summary>Два кадра после enable — RectTransform/Canvas успевают пересчитаться; убирает «белый квадрат» у активного оружия.</summary>
    private IEnumerator RefreshActiveWeaponAfterLayoutCoroutine()
    {
        yield return null;
        RefreshActiveWeaponIcon();
        yield return null;
        RefreshActiveWeaponIcon();
    }

    private static void SetImageIcon(Image img, Sprite sprite, float sizePx)
    {
        if (img == null)
            return;
        img.sprite = sprite;
        img.enabled = sprite != null;
        if (sprite == null)
            img.color = new Color(1f, 1f, 1f, 0f);
        else
            img.color = Color.white;
        var rt = img.rectTransform;
        rt.sizeDelta = new Vector2(sizePx, sizePx);
    }

    void EnsureItemCardReference()
    {
        if (_itemCardView != null)
            return;
        Canvas canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            Transform tr = canvas.transform.Find("ItemCard") ?? FindChildRecursive(canvas.transform, "ItemCard");
            if (tr != null)
            {
                _itemCardView = tr.GetComponent<ItemCardView>();
                if (_itemCardView == null)
                    _itemCardView = tr.gameObject.AddComponent<ItemCardView>();
            }
        }

        if (_itemCardView == null)
            _itemCardView = FindFirstObjectByType<ItemCardView>();

        if (_itemCardView != null)
        {
            DisableLegacyProfileLoaderOnItemCard(_itemCardView.gameObject);
            DisableItemCardRaycasts(_itemCardView.gameObject);
        }
    }

    static void DisableLegacyProfileLoaderOnItemCard(GameObject card)
    {
        if (card == null)
            return;
        var profile = card.GetComponent<PlayerProfileCardController>();
        if (profile != null)
            profile.enabled = false;
    }

    static void DisableItemCardRaycasts(GameObject card)
    {
        if (card == null)
            return;
        var graphics = card.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = false;
    }

    public void OnInventoryCellHoverEnter(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length)
            return;
        var slot = _slots[slotIndex];
        if (slot == null || (slot.itemId ?? 0) <= 0)
            return;
        if (slot.continuation)
            return;

        EnsureItemCardReference();
        if (_itemCardView == null)
            return;

        _hoveredSlotIndex = slotIndex;
        RenderItemCard(slot);
        _itemCardView.SetVisible(true);
        _itemCardVisible = true;
        UpdateItemCardPosition();
    }

    public void OnInventoryCellHoverExit(int slotIndex)
    {
        if (_hoveredSlotIndex == slotIndex)
            HideItemCard();
    }

    void HideItemCard()
    {
        _hoveredSlotIndex = -1;
        _itemCardVisible = false;
        if (_itemCardView == null)
            return;
        _itemCardView.Clear();
        _itemCardView.SetVisible(false);
    }

    void UpdateItemCardPosition()
    {
        if (!_itemCardVisible || _itemCardView == null)
            return;
        var rt = _itemCardView.transform as RectTransform;
        if (rt == null)
            return;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        var canvas = _itemCardView.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        var rootRt = canvas.transform as RectTransform;
        if (rootRt == null)
            return;

        Camera eventCam = null;
        if (canvas.renderMode == RenderMode.ScreenSpaceCamera || canvas.renderMode == RenderMode.WorldSpace)
            eventCam = canvas.worldCamera;

        Vector2 screen = GetHoveredCellAnchorScreen(eventCam) + _itemCardCursorOffset;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, screen, eventCam, out var localPos))
            rt.anchoredPosition = localPos;
    }

    Vector2 GetHoveredCellAnchorScreen(Camera eventCam)
    {
        if (_hoveredSlotIndex < 0 || _hoveredSlotIndex >= _cellButtons.Length)
            return GetMouseScreenPosition();

        RectTransform cellRt = null;
        var btn = _cellButtons[_hoveredSlotIndex];
        if (btn != null)
            cellRt = btn.transform as RectTransform;
        if (cellRt == null && _hoveredSlotIndex < _cellImages.Length && _cellImages[_hoveredSlotIndex] != null)
            cellRt = _cellImages[_hoveredSlotIndex].transform as RectTransform;
        if (cellRt == null)
            return GetMouseScreenPosition();

        var corners = new Vector3[4];
        cellRt.GetWorldCorners(corners);
        Vector2 topCenterWorld = (corners[1] + corners[2]) * 0.5f;
        return RectTransformUtility.WorldToScreenPoint(eventCam, topCenterWorld);
    }

    static Vector2 GetMouseScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
        return Vector2.zero;
    }

    bool IsPointerOverInventoryCell()
    {
        if (EventSystem.current == null)
            return false;

        var eventData = new PointerEventData(EventSystem.current)
        {
            position = GetMouseScreenPosition()
        };
        _hoverRaycastBuffer.Clear();
        EventSystem.current.RaycastAll(eventData, _hoverRaycastBuffer);
        for (int i = 0; i < _hoverRaycastBuffer.Count; i++)
        {
            var go = _hoverRaycastBuffer[i].gameObject;
            if (go == null)
                continue;
            if (go.GetComponentInParent<InventoryCellHoverRelay>() != null)
                return true;
        }

        return false;
    }

    void RenderItemCard(UserInventorySlotPayload slot)
    {
        if (_itemCardView == null || slot == null)
            return;

        _itemCardView.Clear();

        string rowType = !string.IsNullOrWhiteSpace(slot.itemType)
            ? slot.itemType.Trim().ToLowerInvariant()
            : (slot.stackable ? "ammo" : "weapon");
        WeaponDbRowPayload weapon = null;
        long slotItemId = slot.itemId ?? 0;
        if (slotItemId > 0 && _weaponRowsById.TryGetValue(slotItemId, out var row) && row != null)
        {
            weapon = row;
            if (!string.IsNullOrWhiteSpace(row.itemType))
                rowType = row.itemType.Trim().ToLowerInvariant();
            else if (IsWeaponRowMedicine(row))
                rowType = "medicine";
        }

        string displayName = !string.IsNullOrWhiteSpace(slot.itemName) ? slot.itemName.Trim() : "";
        if (string.IsNullOrWhiteSpace(displayName) && weapon != null && !string.IsNullOrWhiteSpace(weapon.name))
            displayName = weapon.name.Trim();
        if (string.IsNullOrWhiteSpace(displayName) && slotItemId > 0)
            displayName = "#" + slotItemId;
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = Loc.T("itemcard.fallback_item_name");
        _itemCardView.AddItemStatShort(displayName);

        if (rowType == "weapon")
        {
            int range = weapon != null ? weapon.range : slot.range;
            int useAp = weapon != null ? weapon.attackApCost : slot.useApCost;
            int damageMin = weapon != null ? weapon.damageMin : Mathf.Max(0, slot.damageMin);
            int damageMax = weapon != null ? weapon.damageMax : Mathf.Max(damageMin, slot.damageMax);
            int reload = weapon != null ? weapon.reloadApCost : slot.reloadApCost;
            string ammoLabel = weapon != null
                ? (weapon.ammoName ?? weapon.caliber ?? "").Trim()
                : "";
            if (string.IsNullOrEmpty(ammoLabel))
                ammoLabel = (slot.caliber ?? "").Trim();
            int mag = weapon != null ? weapon.magazineSize : slot.magazineSize;
            bool isCold = weapon != null
                && string.Equals((weapon.category ?? "").Trim(), "cold", StringComparison.OrdinalIgnoreCase);

            _itemCardView.AddItemStat(Loc.T("itemcard.stat.range"), range.ToString());
            _itemCardView.AddItemStat(Loc.T("itemcard.stat.damage"), $"{damageMin}...{damageMax}");
            _itemCardView.AddItemStat(Loc.T("itemcard.stat.use_ap"), Mathf.Max(0, useAp).ToString());
            if (!isCold)
            {
                _itemCardView.AddItemStat(Loc.T("itemcard.stat.reload"), Mathf.Max(0, reload).ToString());
                if (!string.IsNullOrEmpty(ammoLabel))
                    _itemCardView.AddItemStat(Loc.T("itemcard.stat.ammo"), ammoLabel);
                _itemCardView.AddItemStat(Loc.T("itemcard.stat.magazine_size"), Mathf.Max(0, mag).ToString());
            }
            return;
        }

        if (rowType == "medicine")
        {
            int useAp = weapon != null ? weapon.attackApCost : slot.useApCost;
            string effectType = weapon != null ? (weapon.effectType ?? "") : "";
            int effectMin = weapon != null ? weapon.effectMin : 0;
            int effectMax = weapon != null ? weapon.effectMax : 0;
            if (effectMin > effectMax)
                (effectMin, effectMax) = (effectMax, effectMin);
            if (string.IsNullOrWhiteSpace(effectType))
                effectType = Loc.T("itemcard.effect_type.hp");

            _itemCardView.AddItemStat(Loc.T("itemcard.stat.use_ap"), Mathf.Max(0, useAp).ToString());
            _itemCardView.AddItemStatShort(Loc.T("itemcard.section.effects"));
            _itemCardView.AddItemStat(Loc.T("itemcard.stat.effect_type"), effectType);
            _itemCardView.AddItemStat(Loc.T("itemcard.stat.effect"), $"{effectMin}...{effectMax}");
            int medQty = GetDisplayedItemQuantity(slot);
            if (medQty > 0)
                _itemCardView.AddItemStat(Loc.T("itemcard.stat.quantity"), medQty.ToString());
            return;
        }

        if (weapon != null)
        {
            string stackAmmo = (weapon.ammoName ?? weapon.caliber ?? "").Trim();
            if (!string.IsNullOrEmpty(stackAmmo))
                _itemCardView.AddItemStat(Loc.T("itemcard.stat.ammo"), stackAmmo);
        }
        _itemCardView.AddItemStat(Loc.T("itemcard.stat.quantity"), GetDisplayedItemQuantity(slot).ToString());
    }

    private void OnCellClicked(int slotIndex)
    {
        if (GameplayMapInputBlock.IsBlocked)
            return;
        if (GameSession.Active != null && GameSession.Active.BlockPlayerInput)
            return;
        if (_player == null || _player.IsDead || _player.IsHidden)
            return;
        if (slotIndex < 0 || slotIndex >= _slots.Length)
            return;

        var s = _slots[slotIndex];
        if (s == null || (s.itemId ?? 0) <= 0)
            return;
        if (s.continuation)
            return;
        if (!s.isEquippable)
            return;
        long selectedItemId = s.itemId ?? 0;
        if (_player != null && s.equipped && selectedItemId > 0 && selectedItemId == _player.WeaponItemId)
            return;

        var session = GameSession.Active != null
            ? GameSession.Active
            : FindFirstObjectByType<GameSession>();
        int atk = s.useApCost > 0 ? s.useApCost : 1;
        string categoryFromDb = null;
        if (selectedItemId > 0 && _weaponRowsById.TryGetValue(selectedItemId, out var wRow) && wRow != null && !string.IsNullOrWhiteSpace(wRow.category))
            categoryFromDb = wRow.category.Trim();
        else if (string.Equals((s.itemType ?? "").Trim(), "medicine", StringComparison.OrdinalIgnoreCase))
            categoryFromDb = "medicine";
        session?.RequestEquipWeapon(selectedItemId, atk, s.damageMax, s.range, categoryFromDb);
    }
}

/// <summary>Обёртка над <see cref="WeaponCatalog"/> для загрузки иконок из Resources по ключу из БД.</summary>
public static class WeaponIconHelper
{
    /// <summary>Иконка для ячейки инвентаря: пустой ключ или отсутствующий файл → null (пустая ячейка, без подстановки кулака).</summary>
    public static Sprite LoadInventoryIcon(string iconKeyOrCode)
    {
        return WeaponCatalog.LoadSpriteFromWeaponIconsFolder(iconKeyOrCode);
    }

}
