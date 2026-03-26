/// <summary>
/// Имена объектов UI по <c>Assets/Scenes/MainScene.unity</c>.
/// Прямые дочерние объекты корневого Canvas (fileID 1216319917): Front Content Maker, RoundWaitPanel, SkipDialogPanel (+ BlockOverlay при добавлении).
/// При переименовании в редакторе обновляй здесь — автопоиск в <see cref="ActionPointsUI"/> опирается на эти строки.
/// </summary>
public static class UiHierarchyNames
{
    public const string FrontContentMaker = "Front Content Maker";

    public const string MiniMapPanel = "MiniMapPanel";
    public const string MiniMapStatsPanel = "MiniMapStatsPanel";
    public const string MiniMapTimeText = "MiniMapTimeText";
    public const string MiniMapApText = "MiniMapApText";

    public const string TurnTrackerText = "TurnTrackerText";
    public const string TurnTrackerPrevButton = "TurnTrackerPrevButton";
    public const string TurnTrackerNextButton = "TurnTrackerNextButton";

    public const string WalkButton = "walkButton";
    public const string WalkButtonPascal = "WalkButton";
    public const string RunButton = "runButton";
    public const string RunButtonPascal = "RunButton";
    public const string SitButton = "sitButton";
    public const string SitButtonPascal = "SitButton";
    public const string HideButton = "hideButton";
    public const string HideButtonPascal = "HideButton";
    public const string SkipButton = "skipButton";
    public const string SkipButtonPascal = "SkipButton";
    public const string StepBackButton = "StepBackButton";
    public const string StepBackButtonCamel = "stepBackButton";

    public const string WalkBg = "walkBG";
    public const string WalkBgPascal = "WalkBG";
    public const string RunBg = "runBG";
    public const string RunBgPascal = "RunBG";
    public const string SitBg = "sitBG";
    public const string SitBgPascal = "SitBG";
    public const string HideBg = "hideBG";
    public const string HideBgPascal = "HideBG";
    public const string SkipBg = "skipBG";
    public const string SkipBgPascal = "SkipBG";

    /// <summary>Кнопка «Закончить ход» (в сцене с пробелом).</summary>
    public const string EndTurnButton = "EndTurn Button";
    public const string EndTurnButtonCompact = "EndTurnButton";

    /// <summary>Анимация движения при планировании хода (вкл — как сейчас, выкл — телепорт).</summary>
    public const string ToggleShowAnimation = "ToggleShowAnimation";

    public const string LoggerText = "LoggerText";
    public const string LogTextLegacy = "LogText";
    public const string LoggerUp = "LoggerUp";
    public const string LoggerUpButton = "LoggerUpButton";
    public const string LoggerDown = "LoggerDown";
    public const string LoggerDownButton = "LoggerDownButton";

    public const string SkipDialogPanel = "SkipDialogPanel";
    public const string SkipDialogQuestionText = "SkipDialogQuestionText";
    public const string SkipDialogInput = "SkipDialogInput";
    public const string SkipDialogOkButton = "SkipDialogOkButton";
    public const string SkipDialogCancelButton = "SkipDialogCancelButton";

    public const string SkipGlobalNameShort = "Skip";
    public const string SkipGlobalNameBtn = "BtnSkip";

    public const string RoundWaitPanel = "RoundWaitPanel";

    /// <summary>Панель «доступна новая версия» на LoginScene / MainMenu (<see cref="ClientUpdateGate"/>).</summary>
    public const string ClientUpdatePanel = "ClientUpdatePanel";
    public const string ClientUpdateMessageText = "ClientUpdateMessageText";
    public const string ClientUpdateStatusText = "ClientUpdateStatusText";
    public const string ClientUpdateDownloadButton = "ClientUpdateDownloadButton";
    public const string ClientUpdateProgressSlider = "ClientUpdateProgressSlider";

    /// <summary>
    /// Полноэкранный слой под модальными панелями (raycast), синхронизируется с <see cref="UiBlockOverlaySync"/>.
    /// Добавь дочерний объект с таким именем к корневому Canvas боя.
    /// </summary>
    public const string BlockOverlay = "BlockOverlay";

    /// <summary>Корень панели инвентаря (12 ячеек).</summary>
    public const string Inventory = "Inventory";
    /// <summary>Опечатка в иерархии — поддерживаем оба имени.</summary>
    public const string IntentoryTypo = "Intentory";

    /// <summary>Canvas-обёртка для иконки активного оружия.</summary>
    public const string ActiveItemPanel = "ActiveItemPanel";
    /// <summary>Дочерний объект с Image (внутри <see cref="ActiveItemPanel"/>).</summary>
    public const string ActiveItem = "ActiveItem";
    /// <summary>Radial ammo donut around <see cref="ActiveItem"/>.</summary>
    public const string ActiveItemAmmoDonut = "ActiveItemAmmoDonut";
    /// <summary>Ammo text under/near donut.</summary>
    public const string ActiveItemAmmoText = "ActiveItemAmmoText";
    /// <summary>Attack AP cost label; scene GameObject is still named with legacy typo <c>ItemAtionPointsCost</c>.</summary>
    public const string ItemActionPointsCost = "ItemAtionPointsCost";
    public const string InventoryCellImage = "InventoryCellImage";

    /// <summary>Ячейки: InventoryCell1 … InventoryCell12.</summary>
    public static string InventoryCellName(int index1Based) => $"InventoryCell{index1Based}";

    /// <summary>Переключение режима планирование/просмотр и подпись текущего режима.</summary>
    public const string ModeButton = "ModeButton";
}
