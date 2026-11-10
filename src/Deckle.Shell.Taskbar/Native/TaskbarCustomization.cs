namespace Deckle.Shell.Taskbar;

// What Deckle wants the taskbar to look like, in the terms of a person and not
// of XAML: the input of TaskbarDocumentBuilder, which turns it into the rules
// and behaviors the injected engine applies. From maps the settings page onto it.
internal sealed record TaskbarCustomization
{
    /// <summary>The bar's background. Native leaves Explorer's acrylic alone.</summary>
    public TaskbarBackground Background { get; init; } = TaskbarBackground.Native;

    /// <summary>
    /// The distance, in pixels, between two notification icons on a vertical bar;
    /// null leaves Explorer's 32. The rules assume a vertical bar: on a horizontal
    /// one the tray is laid out the other way and these rules would be wrong.
    /// </summary>
    public int? TrayIconPitch { get; init; }

    /// <summary>
    /// Brings the quick-settings group (network, volume, battery) and the system
    /// icons beside the tray (microphone or location in use) to the same pitch as
    /// the tray icons, <see cref="TrayIconPitch"/> or 24 when that is not set.
    /// </summary>
    public bool TightenQuickSettings { get; init; }

    /// <summary>
    /// The height of an app button, of Start and of Task view on a vertical bar;
    /// null leaves Explorer's (32 for an app, 43 for Start in Small mode).
    /// </summary>
    public int? TaskButtonPitch { get; init; }

    /// <summary>The clock's lines and styles; null leaves Explorer's clock alone.</summary>
    public ClockCustomization? Clock { get; init; }

    /// <summary>
    /// Takes away the empty band the clock's button keeps above and below the
    /// stacked lines, leaving a small gap above it to set the clock apart from
    /// the icons. Vertical bar only.
    /// </summary>
    public bool TightenClockButton { get; init; }

    /// <summary>
    /// The Start menu; null leaves Windows' own. It lives in another process
    /// than the rest, so it has its own document (TaskbarDocumentBuilder.BuildStartMenu).
    /// </summary>
    public StartMenuCustomization? StartMenu { get; init; }

    /// <summary>Whether anything here lives in explorer, which is where the taskbar host is needed.</summary>
    public bool CustomizesExplorer =>
        Background != TaskbarBackground.Native || TrayIconPitch is not null || TightenQuickSettings
        || TaskButtonPitch is not null || Clock is not null || TightenClockButton;

    /// <summary>Whether anything here lives in the Start menu process.</summary>
    public bool CustomizesStartMenu => StartMenu is { FullHeight: true };

    /// <summary>
    /// What the settings ask for on this taskbar. The compact tray, the compact
    /// app buttons and the clock button's tightening are only carried to a
    /// vertical bar: their rules (a 24 px row, a tightened stack) are written for
    /// the column layout, and on a horizontal bar the items run the other way.
    /// </summary>
    public static TaskbarCustomization From(TaskbarSettings settings, bool verticalBar) => new()
    {
        Background = settings.Background,
        TrayIconPitch = settings.CompactTray && verticalBar ? CompactPitch : null,
        TightenQuickSettings = settings.CompactTray && verticalBar,
        TaskButtonPitch = settings.CompactTaskButtons && verticalBar ? CompactPitch : null,
        Clock = settings.CompactClock ? ClockCustomization.DateAboveTime : null,
        TightenClockButton = settings.CompactClock && verticalBar,
        StartMenu = settings.StartMenuFullHeight ? new StartMenuCustomization() : null,
    };

    /// <summary>
    /// The pitch of everything compact: a tray icon, a quick-settings icon, an app
    /// button. One value, so the column reads as one rhythm from top to bottom.
    /// </summary>
    public const int CompactPitch = 24;
}

/// <summary>
/// What Deckle wants the Start menu to look like.
/// </summary>
/// <param name="FullHeight">
/// The menu is as high as the screen it opens on, with the same gap above and
/// below it, at any scale. Windows caps the menu at 750 effective pixels.
/// </param>
internal sealed record StartMenuCustomization(bool FullHeight = true);

internal enum ClockLineKind
{
    Date,
    Time,
}

/// <summary>One line of the clock: a date or time picture as Windows reads it ("MMM.", "dd", "ddd", "HH", "mm").</summary>
internal readonly record struct ClockLine(ClockLineKind Kind, string Format)
{
    public static ClockLine Date(string format) => new(ClockLineKind.Date, format);
    public static ClockLine Time(string format) => new(ClockLineKind.Time, format);
}

/// <summary>
/// How one of the clock's two TextBlocks is drawn. A null weight, color or line
/// height keeps Explorer's own. Size and line height are in effective pixels,
/// weight a XAML FontWeight name (Medium, SemiBold), color a XAML color (#C8C8C8).
/// A line height is held exactly (BlockLineHeight): the font's own leading would
/// leave about a third of the size between two stacked lines.
///
/// There is no alignment: on a vertical bar Explorer already centers both blocks
/// (a visual state sets it), and a rule cannot hold or give back a value that a
/// visual state owns.
/// </summary>
internal sealed record ClockTextStyle(double FontSize, string? FontWeight = null, string? Color = null, double? LineHeight = null);

/// <summary>
/// The clock as stacked lines. The taskbar clock is two TextBlocks, the upper one
/// first; each shows a number of lines Deckle formats itself, in <see cref="Locale"/>
/// and not in the user's.
/// </summary>
internal sealed record ClockCustomization(
    IReadOnlyList<ClockLine> UpperLines,
    IReadOnlyList<ClockLine> LowerLines,
    ClockTextStyle UpperStyle,
    ClockTextStyle LowerStyle,
    int LineSpacing,
    string Locale = "en-US")
{
    /// <summary>
    /// The clock Louis had with Windhawk's taskbar-clock-customization: "MMM." "dd"
    /// "ddd" small and grey above "HH" "mm" large and medium weight, 4 px between
    /// the two blocks, all in English. Values from that mod's settings (TopLine,
    /// BottomLine, TimeStyle, DateStyle, TextSpacing). The line heights, a little
    /// above each size, close the stack up (81 px tall instead of 100 with the
    /// font's own leading, measured on the live bar on 2026-11-10).
    /// </summary>
    public static ClockCustomization DateAboveTime { get; } = new(
        UpperLines: [ClockLine.Date("MMM."), ClockLine.Date("dd"), ClockLine.Date("ddd")],
        LowerLines: [ClockLine.Time("HH"), ClockLine.Time("mm")],
        UpperStyle: new ClockTextStyle(FontSize: 12, Color: "#C8C8C8", LineHeight: 13),
        LowerStyle: new ClockTextStyle(FontSize: 18, FontWeight: "Medium", LineHeight: 19),
        LineSpacing: 4);
}
