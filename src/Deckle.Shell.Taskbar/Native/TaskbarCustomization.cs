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
    /// Brings the quick-settings group (network, volume, battery) to the same
    /// pitch as the tray icons, <see cref="TrayIconPitch"/> or 24 when that is not set.
    /// </summary>
    public bool TightenQuickSettings { get; init; }

    /// <summary>The clock's lines and styles; null leaves Explorer's clock alone.</summary>
    public ClockCustomization? Clock { get; init; }

    /// <summary>
    /// The Start menu; null leaves Windows' own. It lives in another process
    /// than the rest, so it has its own document (TaskbarDocumentBuilder.BuildStartMenu).
    /// </summary>
    public StartMenuCustomization? StartMenu { get; init; }

    /// <summary>Whether anything here lives in explorer, which is where the taskbar host is needed.</summary>
    public bool CustomizesExplorer =>
        Background != TaskbarBackground.Native || TrayIconPitch is not null || TightenQuickSettings || Clock is not null;

    /// <summary>Whether anything here lives in the Start menu process.</summary>
    public bool CustomizesStartMenu => StartMenu is { FullHeight: true };

    /// <summary>
    /// What the settings ask for on this taskbar. The compact tray is only
    /// carried to a vertical bar: its rules (a 24 px row, a tightened
    /// quick-settings stack) are written for the column layout, and on a
    /// horizontal bar the tray is laid out the other way.
    /// </summary>
    public static TaskbarCustomization From(TaskbarSettings settings, bool verticalBar) => new()
    {
        Background = settings.Background,
        TrayIconPitch = settings.CompactTray && verticalBar ? CompactTrayPitch : null,
        TightenQuickSettings = settings.CompactTray && verticalBar,
        Clock = settings.CompactClock ? ClockCustomization.DateAboveTime : null,
        StartMenu = settings.StartMenuFullHeight ? new StartMenuCustomization() : null,
    };

    /// <summary>The distance between two notification icons when the tray is compact.</summary>
    public const int CompactTrayPitch = 24;
}

/// <summary>
/// What Deckle wants the Start menu to look like.
/// </summary>
/// <param name="FullHeight">
/// The menu is as high as the screen it opens on (minus the margin Windows keeps
/// around it), at any scale. Windows caps the menu at 750 effective pixels.
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
/// How one of the clock's two TextBlocks is drawn. A null weight or color keeps
/// Explorer's own. Size is in effective pixels, weight a XAML FontWeight name
/// (Medium, SemiBold), color a XAML color (#C8C8C8).
///
/// There is no alignment: on a vertical bar Explorer already centers both blocks
/// (a visual state sets it), and a rule cannot hold or give back a value that a
/// visual state owns.
/// </summary>
internal sealed record ClockTextStyle(double FontSize, string? FontWeight = null, string? Color = null);

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
    /// BottomLine, TimeStyle, DateStyle, TextSpacing).
    /// </summary>
    public static ClockCustomization DateAboveTime { get; } = new(
        UpperLines: [ClockLine.Date("MMM."), ClockLine.Date("dd"), ClockLine.Date("ddd")],
        LowerLines: [ClockLine.Time("HH"), ClockLine.Time("mm")],
        UpperStyle: new ClockTextStyle(FontSize: 12, Color: "#C8C8C8"),
        LowerStyle: new ClockTextStyle(FontSize: 18, FontWeight: "Medium"),
        LineSpacing: 4);
}
