using Deckle.Catalog;
using Deckle.Localization;

namespace Deckle.Shell.Taskbar;

// The page's cards. Headers and descriptions resolve from this module's .resw as
// "<LabelKey>.Header" / ".Description"; a choice's options as "<OptionKey>.Content".
// Every default reads from a fresh TaskbarSettings, the single source the reset
// writes back.
//
// Three groups, each its own section: the bar itself, its tray, the Start menu.
// There is no module switch: every card is its own switch, and all of them are
// off (or Explorer's own) by default.
public partial class TaskbarViewModel
{
    private const string ProblemNotAppliedKey = "TaskbarPage_ProblemNotApplied";
    private const string ProblemContestedKey = "TaskbarPage_ProblemContested";
    private const string ProblemErrorKey = "TaskbarPage_ProblemError";
    private const string HorizontalBarKey = "TaskbarPage_HorizontalBarNoEffect";

    public IReadOnlyList<SettingDescriptor> BarManifest =>
    [
        // A dropdown: three short names, and seeing them all at once adds nothing
        // to the choice (the app theme card is built the same way).
        Setting.Choice<TaskbarBackground>(
            "TaskbarPage_BackgroundCard",
            () => Background,
            value => Background = value,
            [
                (TaskbarBackground.Native, "TaskbarPage_BackgroundDefault"),
                (TaskbarBackground.Transparent, "TaskbarPage_BackgroundTransparent"),
                (TaskbarBackground.Black, "TaskbarPage_BackgroundBlack"),
            ],
            defaultValue: () => new TaskbarSettings().Background,
            advisory: () => ProblemAdvisory(TaskbarPart.Background)),
        // A switch whose fold holds its value, like the compact app buttons: the
        // thickness means nothing while the bar keeps Explorer's own. It holds
        // on any edge, so it has no horizontal-bar line.
        Setting.Group(
            "TaskbarPage_BarThicknessCard",
            () => CustomBarThickness,
            value => CustomBarThickness = value,
            [
                Pitch("TaskbarPage_BarThicknessValueCard",
                    () => BarThickness, value => BarThickness = value,
                    TaskbarCustomization.BarThicknessRange, () => new TaskbarSettings().BarThickness),
            ],
            defaultValue: () => new TaskbarSettings().CustomBarThickness,
            advisory: () => ProblemAdvisory(TaskbarPart.BarThickness)),
        // A switch whose fold holds its one value: the pitch is meaningless while
        // the buttons keep Explorer's own, so it shows only once the switch is on.
        Setting.Group(
            "TaskbarPage_CompactTaskButtonsCard",
            () => CompactTaskButtons,
            value => CompactTaskButtons = value,
            [
                Pitch("TaskbarPage_TaskButtonPitchCard",
                    () => TaskButtonPitch, value => TaskButtonPitch = value,
                    TaskbarCustomization.TaskButtonPitchRange, () => new TaskbarSettings().TaskButtonPitch),
            ],
            defaultValue: () => new TaskbarSettings().CompactTaskButtons,
            advisory: CompactTaskButtonsAdvisory),
        Setting.Toggle(
            "TaskbarPage_CompactClockCard",
            () => CompactClock,
            value => CompactClock = value,
            defaultValue: () => new TaskbarSettings().CompactClock,
            advisory: () => ProblemAdvisory(TaskbarPart.CompactClock)),
        // A switch that unfolds the box the thumbnails grow to fit: two numbers,
        // each a field with a slider (a size is judged by eye). The engine's word
        // hangs under the group, like the other switches of this page.
        Setting.Group(
            "TaskbarPage_LargerThumbnailsCard",
            () => LargerThumbnails,
            value => LargerThumbnails = value,
            [
                Setting.Magnitude(
                    "TaskbarPage_ThumbnailMaxWidthCard",
                    () => ThumbnailMaxWidth,
                    value => ThumbnailMaxWidth = value,
                    new MagnitudeArgs(ThumbnailMinimumWidth, ThumbnailMaximumWidth, Unit: "px", Integer: true),
                    defaultValue: () => new TaskbarSettings().ThumbnailMaxWidth),
                Setting.Magnitude(
                    "TaskbarPage_ThumbnailMaxHeightCard",
                    () => ThumbnailMaxHeight,
                    value => ThumbnailMaxHeight = value,
                    new MagnitudeArgs(ThumbnailMinimumHeight, ThumbnailMaximumHeight, Unit: "px", Integer: true),
                    defaultValue: () => new TaskbarSettings().ThumbnailMaxHeight),
            ],
            defaultValue: () => new TaskbarSettings().LargerThumbnails,
            advisory: () => ProblemAdvisory(TaskbarPart.Thumbnails)),
    ];

    // The box's bounds, in effective pixels. The low ends are Explorer's own
    // size (a box below it would shrink the thumbnails); the high ends a third
    // of a 4K screen's width and two thirds of a 1080p one's height.
    internal const double ThumbnailMinimumWidth = 200;
    internal const double ThumbnailMaximumWidth = 1280;
    internal const double ThumbnailMinimumHeight = 110;
    internal const double ThumbnailMaximumHeight = 720;

    public IReadOnlyList<SettingDescriptor> TrayManifest =>
    [
        Setting.Toggle(
            "TaskbarPage_ShowAllTrayIconsCard",
            () => ShowAllTrayIcons,
            value => ShowAllTrayIcons = value,
            defaultValue: () => new TaskbarSettings().ShowAllTrayIcons,
            advisory: TrayAdvisory),
        Setting.Group(
            "TaskbarPage_CompactTrayCard",
            () => CompactTray,
            value => CompactTray = value,
            [
                Pitch("TaskbarPage_TrayIconPitchCard",
                    () => TrayIconPitch, value => TrayIconPitch = value,
                    TaskbarCustomization.TrayPitchRange, () => new TaskbarSettings().TrayIconPitch),
            ],
            defaultValue: () => new TaskbarSettings().CompactTray,
            advisory: CompactTrayAdvisory),
    ];

    // A size in effective pixels, typed or swept: whole pixels only (a half
    // pixel would blur the icons), so the field shows no decimals.
    private static SettingDescriptor Pitch(
        string labelKey, Func<int> get, Action<int> set, PixelRange range, Func<int> defaultValue) =>
        Setting.Magnitude(
            labelKey,
            () => get(),
            value => set(range.Clamp((int)Math.Round(value))),
            new MagnitudeArgs(range.Minimum, range.Maximum, Unit: "px", Integer: true),
            defaultValue: () => defaultValue());

    public IReadOnlyList<SettingDescriptor> StartManifest =>
    [
        Setting.Toggle(
            "TaskbarPage_StartMenuFullHeightCard",
            () => StartMenuFullHeight,
            value => StartMenuFullHeight = value,
            defaultValue: () => new TaskbarSettings().StartMenuFullHeight,
            advisory: () => ProblemAdvisory(TaskbarPart.StartMenuHeight)),
        // A choice that decides whether a value exists: the width field shows
        // under Custom only, and the engine holds any width to the screen.
        Setting.Mode<StartMenuWidth>(
            "TaskbarPage_StartMenuWidthCard",
            () => StartMenuWidth,
            value => StartMenuWidth = value,
            [
                (StartMenuWidth.Native, "TaskbarPage_StartMenuWidthDefault"),
                (StartMenuWidth.Fill, "TaskbarPage_StartMenuWidthFill"),
                (StartMenuWidth.Custom, "TaskbarPage_StartMenuWidthCustom"),
            ],
            [
                Setting.Magnitude(
                    "TaskbarPage_StartMenuCustomWidthCard",
                    () => StartMenuCustomWidth,
                    value => StartMenuCustomWidth = TaskbarCustomization.StartMenuWidthRange.Clamp((int)Math.Round(value)),
                    new MagnitudeArgs(
                        TaskbarCustomization.StartMenuWidthRange.Minimum,
                        TaskbarCustomization.StartMenuWidthRange.Maximum,
                        Unit: "px", Integer: true),
                    visibleWhen: () => StartMenuWidth == StartMenuWidth.Custom,
                    defaultValue: () => new TaskbarSettings().StartMenuCustomWidth),
            ],
            defaultValue: () => new TaskbarSettings().StartMenuWidth,
            advisory: () => ProblemAdvisory(TaskbarPart.StartMenuWidth)),
        // Sits with the Start menu because it changes what the Windows key does to
        // Start. It asks nothing of the engine, so it has no advisory.
        Setting.Toggle(
            "TaskbarPage_WindowsKeyShowsTaskbarCard",
            () => WindowsKeyShowsTaskbar,
            value => WindowsKeyShowsTaskbar = value,
            defaultValue: () => new TaskbarSettings().WindowsKeyShowsTaskbar),
    ];

    // The promoter's last pass, said only when it did not go through. While
    // the switch is on the pass is the one that keeps the icons on the bar;
    // once it is off, the one that gives them back.
    private string? TrayAdvisory() =>
        TrayMessageKey is { } key ? Loc.GetFromOptional(TaskbarSettingsModule.ResourceLibrary, key) : null;

    // The .resw key of that line, or null when there is nothing to say.
    internal string? TrayMessageKey
    {
        get
        {
            if (_tray.LastOutcome is null or TrayOutcome.Applied) return null;
            return ShowAllTrayIcons ? "TaskbarPage_TrayFailedOn" : "TaskbarPage_TrayFailedOff";
        }
    }

    // The compact tray says why it does nothing on a horizontal bar, else what
    // the engine said about it. Nothing is said while the switch is off.
    private string? CompactTrayAdvisory() =>
        CompactTrayMessageKey is { } key ? Loc.GetFromOptional(TaskbarSettingsModule.ResourceLibrary, key) : null;

    internal string? CompactTrayMessageKey
    {
        get
        {
            if (!CompactTray) return null;
            return _isBarVertical() ? ProblemKey(TaskbarPart.CompactTray) : HorizontalBarKey;
        }
    }

    // The compact app buttons speak the same way, for the same reason.
    private string? CompactTaskButtonsAdvisory() =>
        CompactTaskButtonsMessageKey is { } key ? Loc.GetFromOptional(TaskbarSettingsModule.ResourceLibrary, key) : null;

    internal string? CompactTaskButtonsMessageKey
    {
        get
        {
            if (!CompactTaskButtons) return null;
            return _isBarVertical() ? ProblemKey(TaskbarPart.CompactTaskButtons) : HorizontalBarKey;
        }
    }

    private string? ProblemAdvisory(TaskbarPart part) =>
        ProblemKey(part) is { } key ? Loc.GetFromOptional(TaskbarSettingsModule.ResourceLibrary, key) : null;

    // The line for a part the engine could not put in effect; null when it is
    // fine, or when the person has it off (a status can lag a toggle).
    internal string? ProblemKey(TaskbarPart part)
    {
        bool on = part switch
        {
            TaskbarPart.Background => Background != TaskbarBackground.Native,
            TaskbarPart.BarThickness => CustomBarThickness,
            TaskbarPart.CompactTray => CompactTray,
            TaskbarPart.CompactTaskButtons => CompactTaskButtons,
            TaskbarPart.CompactClock => CompactClock,
            TaskbarPart.Thumbnails => LargerThumbnails,
            TaskbarPart.StartMenuHeight => StartMenuFullHeight,
            TaskbarPart.StartMenuWidth => StartMenuWidth != StartMenuWidth.Native,
            _ => false,
        };
        if (!on || !_customizer.Problems.TryGetValue(part, out TaskbarProblem problem)) return null;

        return problem switch
        {
            TaskbarProblem.NotApplied => ProblemNotAppliedKey,
            TaskbarProblem.Contested => ProblemContestedKey,
            _ => ProblemErrorKey,
        };
    }
}
