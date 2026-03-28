using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    private UserInventorySlotPayload[] _slots = new UserInventorySlotPayload[12];
    private readonly Dictionary<string, WeaponDbRowPayload> _weaponRowsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UserAmmoPackPayload> _ammoByCaliber = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _localAmmoRoundsByCaliber = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _localMagazineRoundsByWeapon = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _serverChamberRoundsByWeapon = new(StringComparer.OrdinalIgnoreCase);
    private string _ammoWeaponCodeApplied = "";
    private int _ammoMagazineCapacity;
    private int _ammoReserveRounds;
    private ActiveItemAmmoDonutSlider _ammoDonutSlider;
    private StripedDonutIndicator _ammoStripedIndicator;
    private BattleServerConnection _serverConnection;
    private int _lastDisplayedAttackApCost = int.MinValue;
    private int _lastAmmoActionCount = -1;
    private int _lastAmmoAp = int.MinValue;
    private int _lastKnownReloadApCost = 1;
    private string _lastAmmoLogLine;

    private void Awake()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        _serverConnection = FindFirstObjectByType<BattleServerConnection>();
        ResolveHierarchyIfNeeded();
        ClearActiveWeaponPlaceholder();
        TryResolveAmmoUi();
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
    }

    private void Update()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
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
        SyncLocalMagazineFromCurrentWeaponState();
        ApplySlotsToCells();
    }

    /// <summary>
    /// OnEnable срабатывает до Start у <see cref="BattleServerConnection"/>, поэтому первый запрос инвентаря
    /// мог уйти на localhost из инспектора. После идентификации боя URL уже верный — перезагружаем слоты.
    /// </summary>
    private void OnBattleIdentifiedForInventory(string battleId, string playerId, string serverUrl)
    {
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
            // JsonUtility не десериализует массив слотов с weaponId:null и другими нюансами System.Text.Json.
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
                if (s == null || s.continuation || string.IsNullOrWhiteSpace(s.weaponCode))
                    continue;
                string code = WeaponCatalog.NormalizeWeaponCode(s.weaponCode);
                if (string.IsNullOrEmpty(code))
                    continue;
                if (_serverChamberRoundsByWeapon.ContainsKey(code))
                    continue;
                _serverChamberRoundsByWeapon[code] = Mathf.Max(0, s.chamberRounds);
                AmmoLog($"[InventorySync] slot weapon={code}, slotIndex={s.slotIndex}, chamberRounds={s.chamberRounds}, equipped={s.equipped}, stackable={s.stackable}");
            }

            ApplySlotsToCells();
            yield return LoadAmmoAndWeaponsCoroutine(baseUrl);
            OnPlayerEquippedWeaponChanged();
        }
    }

    public void ReloadInventoryFromServer()
    {
        StartCoroutine(LoadInventoryFromServerCoroutine());
    }

    private IEnumerator LoadAmmoAndWeaponsCoroutine(string baseUrl)
    {
        string weaponsText = null;
        string weaponsErr = null;

        string weaponsUrl = $"{baseUrl}/api/db/weapons?take=500";

        yield return HttpSimple.GetString(weaponsUrl, b => weaponsText = b, e => weaponsErr = e);

        _weaponRowsByCode.Clear();
        if (string.IsNullOrEmpty(weaponsErr) && !string.IsNullOrEmpty(weaponsText))
        {
            WeaponDbRowPayload[] rows = null;
            try { rows = JsonConvert.DeserializeObject<WeaponDbRowPayload[]>(weaponsText); } catch { }
            if (rows != null)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    var r = rows[i];
                    if (r == null || string.IsNullOrWhiteSpace(r.code))
                        continue;
                    _weaponRowsByCode[r.code.Trim().ToLowerInvariant()] = r;
                }
            }
        }

        _ammoByCaliber.Clear();
        _localAmmoRoundsByCaliber.Clear();
        _localMagazineRoundsByWeapon.Clear();
        RebuildAmmoCacheFromSlots();
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
            string caliber = (slot.weaponCode ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(caliber))
                continue;
            int rounds = Mathf.Max(0, slot.quantity);
            if (_localAmmoRoundsByCaliber.ContainsKey(caliber))
                _localAmmoRoundsByCaliber[caliber] += rounds;
            else
                _localAmmoRoundsByCaliber[caliber] = rounds;
        }

        foreach (var kv in _localAmmoRoundsByCaliber)
        {
            _ammoByCaliber[kv.Key] = new UserAmmoPackPayload
            {
                caliber = kv.Key,
                roundsCount = kv.Value,
                totalRounds = kv.Value
            };
        }
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
            bool showCount = s.stackable && !s.continuation && displayQty > 0;
            SetCellCountVisible(i, showCount, showCount ? FormatStackCount(displayQty) : "");
            if (_cellButtons[i] != null)
                _cellButtons[i].interactable = !s.continuation && s.isEquippable && !string.IsNullOrWhiteSpace(s.weaponCode);
        }
    }

    private int GetDisplayedItemQuantity(UserInventorySlotPayload slot)
    {
        if (slot == null || !slot.stackable)
            return 0;

        int serverQty = Mathf.Max(0, slot.quantity);
        string slotCode = (slot.weaponCode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slotCode))
            return serverQty;
        if (_localAmmoRoundsByCaliber.TryGetValue(slotCode, out int localQty))
            return Mathf.Max(0, localQty);
        return serverQty;
    }

    private void SyncLocalAmmoFromCurrentWeaponState()
    {
        if (_player == null)
            return;
        string weaponCode = WeaponCatalog.NormalizeWeaponCode(_player.WeaponCode);
        if (!_weaponRowsByCode.TryGetValue(weaponCode, out var w) || w == null)
            return;
        string cal = (w.caliber ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(cal))
            return;
        _localAmmoRoundsByCaliber[cal] = Mathf.Max(0, _player.ReserveAmmoRounds);
    }

    private void SyncLocalMagazineFromCurrentWeaponState()
    {
        if (_player == null)
            return;
        string weaponCode = WeaponCatalog.NormalizeWeaponCode(_player.WeaponCode);
        if (string.IsNullOrEmpty(weaponCode))
            return;
        int cap = Mathf.Max(0, _player.MagazineCapacity);
        if (cap <= 0)
            return;
        _localMagazineRoundsByWeapon[weaponCode] = Mathf.Clamp(_player.CurrentMagazineRounds, 0, cap);
    }

    private static string ResolveItemIconKey(UserInventorySlotPayload slot)
    {
        if (slot == null)
            return null;
        if (!string.IsNullOrWhiteSpace(slot.iconKey))
            return slot.iconKey.Trim();
        if (!string.IsNullOrWhiteSpace(slot.weaponCode))
            return slot.weaponCode.Trim();
        if (!string.IsNullOrWhiteSpace(slot.weaponName))
            return slot.weaponName.Trim();
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
        string code = _player != null ? _player.WeaponCode : WeaponCatalog.DefaultWeaponCode;
        int attackApCost = GetAttackApCostForCurrentWeaponDisplay(code);
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
    private int GetAttackApCostForCurrentWeaponDisplay(string weaponCode)
    {
        if (string.IsNullOrWhiteSpace(weaponCode))
            weaponCode = WeaponCatalog.DefaultWeaponCode;
        string norm = WeaponCatalog.NormalizeWeaponCode(weaponCode);
        if (_weaponRowsByCode.TryGetValue(norm, out var w) && w != null && w.attackApCost > 0)
            return w.attackApCost;
        for (int i = 0; i < 12; i++)
        {
            var s = _slots[i];
            if (s == null || string.IsNullOrWhiteSpace(s.weaponCode))
                continue;
            if (!string.Equals(s.weaponCode, weaponCode, StringComparison.OrdinalIgnoreCase))
                continue;
            if (s.attackApCost > 0)
                return s.attackApCost;
            break;
        }
        if (_player != null && string.Equals(_player.WeaponCode, weaponCode, StringComparison.OrdinalIgnoreCase))
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

        string code = _player.WeaponCode;
        string iconKey = null;
        for (int i = 0; i < 12; i++)
        {
            var slot = _slots[i];
            if (slot == null || string.IsNullOrWhiteSpace(slot.weaponCode))
                continue;
            if (!string.Equals(slot.weaponCode, code, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(slot.iconKey))
                iconKey = slot.iconKey.Trim();
            break;
        }

        Sprite sp = !string.IsNullOrEmpty(iconKey)
            ? WeaponIconHelper.LoadInventoryIcon(iconKey)
            : WeaponIconHelper.LoadEquippedWeaponIcon(code);
        SetImageIcon(_activeWeaponImage, sp, ActiveIconSize);
    }

    private void RefreshActiveItemAmmoUi(bool forceResetOnWeaponChange = false)
    {
        TryResolveAmmoUi();
        if (_activeItemAmmoDonutImage == null || _activeItemAmmoText == null)
            return;
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        string weaponCode = _player != null ? WeaponCatalog.NormalizeWeaponCode(_player.WeaponCode) : WeaponCatalog.DefaultWeaponCode;
        string previousWeaponCode = _ammoWeaponCodeApplied;
        bool weaponChanged = !string.Equals(_ammoWeaponCodeApplied, weaponCode, StringComparison.OrdinalIgnoreCase);
        if (weaponChanged && _player != null && !string.IsNullOrEmpty(previousWeaponCode) && _player.MagazineCapacity > 0)
        {
            _localMagazineRoundsByWeapon[previousWeaponCode] = Mathf.Clamp(_player.CurrentMagazineRounds, 0, _player.MagazineCapacity);
            AmmoLog($"[WeaponSwitch] save prev weapon={previousWeaponCode}, mag={_player.CurrentMagazineRounds}/{_player.MagazineCapacity}");
        }
        if (weaponChanged)
            _ammoWeaponCodeApplied = weaponCode;

        if (!_weaponRowsByCode.TryGetValue(weaponCode, out var w))
        {
            SetAmmoUiVisible(false);
            return;
        }

        int mag = Mathf.Max(0, w.magazineSize);
        string cal = (w.caliber ?? string.Empty).Trim().ToLowerInvariant();
        bool isMedicine = string.Equals((w.category ?? string.Empty).Trim(), "medicine", StringComparison.OrdinalIgnoreCase);
        if (isMedicine)
        {
            // Self-use items should hide both ammo donut and ammo count.
            SetAmmoUiVisible(false);
            return;
        }
        bool weaponUsesAmmo = mag > 0 && !string.IsNullOrEmpty(cal);
        if (!weaponUsesAmmo)
        {
            SetAmmoUiVisible(false);
            return;
        }

        _ammoMagazineCapacity = mag;
        if (!string.IsNullOrEmpty(cal) && _ammoByCaliber.TryGetValue(cal, out var ammo))
        {
            int serverReserve = Mathf.Max(0, ammo.roundsCount > 0 ? ammo.roundsCount : ammo.totalRounds);
            bool hasLocal = _localAmmoRoundsByCaliber.TryGetValue(cal, out int localReserve);
            int reserve = hasLocal ? Mathf.Max(0, localReserve) : serverReserve;
            _localAmmoRoundsByCaliber[cal] = reserve;
            _ammoReserveRounds = reserve;
            // Do not overwrite local in-turn ammo on every frame.
            // Push into Player only on weapon switch / explicit server refresh, or when local value not initialized yet.
            if (_player != null && (weaponChanged || forceResetOnWeaponChange || !hasLocal))
                _player.SetReserveAmmoRounds(reserve, notify: false);
        }
        int currentRounds = _ammoMagazineCapacity;
        string roundsSource = "default_mag_capacity";
        if (forceResetOnWeaponChange)
        {
            if (_serverChamberRoundsByWeapon.TryGetValue(weaponCode, out int srv))
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
        else if (_localMagazineRoundsByWeapon.TryGetValue(weaponCode, out int savedRounds))
        {
            currentRounds = savedRounds;
            roundsSource = "local_mag_cache";
        }
        else if (_serverChamberRoundsByWeapon.TryGetValue(weaponCode, out int serverChamber))
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
        _localMagazineRoundsByWeapon[weaponCode] = currentRounds;
        AmmoLog($"[AmmoUi] weapon={weaponCode}, weaponChanged={weaponChanged}, source={roundsSource}, currentRounds={currentRounds}, magCap={_ammoMagazineCapacity}, forceReset={forceResetOnWeaponChange}");

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
        string weaponCode = _player != null ? WeaponCatalog.NormalizeWeaponCode(_player.WeaponCode) : WeaponCatalog.DefaultWeaponCode;
        if (_weaponRowsByCode.TryGetValue(weaponCode, out var w) && w != null)
            return Mathf.Max(1, w.reloadApCost);
        return Mathf.Max(1, _lastKnownReloadApCost);
    }

    public int GetCurrentActiveItemUseApCost()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        string weaponCode = _player != null ? WeaponCatalog.NormalizeWeaponCode(_player.WeaponCode) : WeaponCatalog.DefaultWeaponCode;
        if (_weaponRowsByCode.TryGetValue(weaponCode, out var w) && w != null)
            return Mathf.Max(1, w.attackApCost);
        return 1;
    }

    public bool IsActiveItemMedicine()
    {
        if (_player == null)
            _player = FindFirstObjectByType<Player>();
        string weaponCode = _player != null ? WeaponCatalog.NormalizeWeaponCode(_player.WeaponCode) : WeaponCatalog.DefaultWeaponCode;
        if (!_weaponRowsByCode.TryGetValue(weaponCode, out var w) || w == null)
            return false;
        return string.Equals((w.category ?? string.Empty).Trim(), "medicine", StringComparison.OrdinalIgnoreCase);
    }

    private void OnAmmoDonutValueChanged(float value01)
    {
        if (_ammoMagazineCapacity <= 0)
            return;
        int shotsUsed = Mathf.Clamp(Mathf.RoundToInt(value01 * _ammoMagazineCapacity), 0, _ammoMagazineCapacity);
        int currentRounds = Mathf.Max(0, _ammoMagazineCapacity - shotsUsed);
        _player?.SetMagazineState(_ammoMagazineCapacity, currentRounds, notify: false);
        if (!string.IsNullOrEmpty(_ammoWeaponCodeApplied))
            _localMagazineRoundsByWeapon[_ammoWeaponCodeApplied] = currentRounds;
        AmmoLog($"[AmmoDonut] weapon={_ammoWeaponCodeApplied}, value01={value01:0.###}, currentRounds={currentRounds}, magCap={_ammoMagazineCapacity}");
        float fill = _ammoMagazineCapacity > 0 ? (float)shotsUsed / _ammoMagazineCapacity : 0f;
        if (_activeItemAmmoText != null && _weaponRowsByCode.TryGetValue(_ammoWeaponCodeApplied, out var row))
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
        if (s == null || string.IsNullOrWhiteSpace(s.weaponCode))
            return;
        if (s.continuation)
            return;
        if (!s.isEquippable)
            return;
        if (_player != null && s.equipped &&
            string.Equals(s.weaponCode, _player.WeaponCode, StringComparison.OrdinalIgnoreCase))
            return;

        var session = GameSession.Active != null
            ? GameSession.Active
            : FindFirstObjectByType<GameSession>();
        int atk = s.attackApCost > 0 ? s.attackApCost : 1;
        session?.RequestEquipWeapon(s.weaponCode, atk, s.damage, s.range);
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

    /// <summary>Панель текущего оружия: пустой код → <see cref="WeaponCatalog.DefaultWeaponCode"/>.</summary>
    public static Sprite LoadEquippedWeaponIcon(string weaponCode)
    {
        return WeaponCatalog.LoadSpriteForEquippedWeaponPanel(weaponCode);
    }
}
