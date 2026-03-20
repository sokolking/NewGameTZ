/// <summary>
/// Имена объектов UI по <c>Assets/Scenes/MainScene.unity</c>.
/// Прямые дочерние объекты корневого Canvas (fileID 1216319917): Front Content Maker, RoundWaitPanel, PauseMenuPanel, SkipDialogPanel (+ BlockOverlay при добавлении).
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
    public const string PauseMenuPanel = "PauseMenuPanel";
    public const string PauseButtonResume = "Button_Resume";
    public const string PauseButtonMainMenu = "Button_MainMenu";

    /// <summary>
    /// Полноэкранный слой под модальными панелями (raycast), синхронизируется с <see cref="UiBlockOverlaySync"/>.
    /// Добавь дочерний объект с таким именем к корневому Canvas боя.
    /// </summary>
    public const string BlockOverlay = "BlockOverlay";
}
