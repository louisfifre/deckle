namespace Deckle.Shell.Taskbar;

// What Deckle wants the taskbar to look like, in the terms of a person and not
// of XAML: the input of TaskbarDocumentBuilder, which turns it into the rules
// and behaviors the injected engine applies. From maps the settings page onto it.
internal sealed record TaskbarCustomization
{
    /// <summary>The bar's background. Native leaves Explorer's acrylic alone.</summary>
    public TaskbarBackground Background { get; init; } = TaskbarBackground.Native;

    /// <summary>
    /// The bar's thickness in effective pixels, on any edge; null leaves
    /// Explorer's. Not a XAML rule: the engine makes the function Explorer asks
    /// for it return this value (TaskbarDocumentBuilder.FrameSizeSymbol).
    /// </summary>
    public int? BarThickness { get; init; }

    /// <summary>
    /// The distance, in pixels, between two notification icons on a vertical bar;
    /// null leaves Explorer's 32. The rules assume a vertical bar: on a horizontal
    /// one the tray is laid out the other way and these rules would be wrong.
    /// </summary>
    public int? TrayIconPitch { get; init; }

    /// <summary>
    /// Brings the quick-settings group (network, volume, battery) and the system
    /// icons beside the tray (microphone or location in use) to the same pitch as
    /// the tray icons, <see cref="TrayIconPitch"/> or <see cref="DefaultCompactPitch"/>
    /// when that is not set.
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

    /// <summary>The box the hover thumbnails grow to fit; null leaves Explorer's size.</summary>
    public ThumbnailBox? Thumbnails { get; init; }

    /// <summary>
    /// The Start menu; null leaves Windows' own. It lives in another process
    /// than the rest, so it has its own document (TaskbarDocumentBuilder.BuildStartMenu).
    /// </summary>
    public StartMenuCustomization? StartMenu { get; init; }

    /// <summary>Whether anything here lives in explorer, which is where the taskbar host is needed.</summary>
    public bool CustomizesExplorer =>
        Background != TaskbarBackground.Native || BarThickness is not null || TrayIconPitch is not null || TightenQuickSettings
        || TaskButtonPitch is not null || Clock is not null || TightenClockButton || Thumbnails is not null;

    /// <summary>Whether anything here lives in the Start menu process.</summary>
    public bool CustomizesStartMenu => StartMenu is { IsEmpty: false };

    /// <summary>
    /// What the settings ask for on this taskbar. The compact tray, the compact
    /// app buttons and the clock button's tightening are only carried to a
    /// vertical bar: their rules (a row of the pitch, a tightened stack) are
    /// written for the column layout, and on a horizontal bar the items run the
    /// other way. A pitch read from the file is held to its range: a value
    /// written by hand cannot squash an icon to nothing.
    /// </summary>
    public static TaskbarCustomization From(TaskbarSettings settings, bool verticalBar) => new()
    {
        Background = settings.Background,
        BarThickness = settings.CustomBarThickness ? BarThicknessRange.Clamp(settings.BarThickness) : null,
        TrayIconPitch = settings.CompactTray && verticalBar ? TrayPitchRange.Clamp(settings.TrayIconPitch) : null,
        TightenQuickSettings = settings.CompactTray && verticalBar,
        TaskButtonPitch = settings.CompactTaskButtons && verticalBar ? TaskButtonPitchRange.Clamp(settings.TaskButtonPitch) : null,
        Clock = settings.CompactClock ? ClockCustomization.DateAboveTime : null,
        TightenClockButton = settings.CompactClock && verticalBar,
        Thumbnails = settings.LargerThumbnails ? new ThumbnailBox(settings.ThumbnailMaxWidth, settings.ThumbnailMaxHeight) : null,
        StartMenu = StartMenuFrom(settings),
    };

    // Null when the Start menu is left to Windows. A custom width read from the
    // file is held to the page's range, like a pitch.
    private static StartMenuCustomization? StartMenuFrom(TaskbarSettings settings)
    {
        var startMenu = new StartMenuCustomization
        {
            FullHeight = settings.StartMenuFullHeight,
            HideRecommended = settings.HideStartMenuRecommended,
            Width = settings.StartMenuWidth switch
            {
                StartMenuWidth.Fill => StartMenuWidthRequest.Fill,
                StartMenuWidth.Custom => StartMenuWidthRequest.Of(StartMenuWidthRange.Clamp(settings.StartMenuCustomWidth)),
                _ => null,
            },
        };
        return startMenu.IsEmpty ? null : startMenu;
    }

    /// <summary>
    /// The thickness the page starts at, two thirds of Explorer's 48: a 16 px
    /// icon with 8 px on each side.
    /// </summary>
    public const int DefaultBarThickness = 32;

    /// <summary>
    /// A thickness: from 24, a 16 px icon with 4 px on each side, to 72, the
    /// height of a Large-mode bar at the bottom of the screen.
    /// </summary>
    public static PixelRange BarThicknessRange { get; } = new(24, 72);

    /// <summary>
    /// The custom width the page starts at: narrower than Windows' own 834, so
    /// choosing a width shows at once what it does.
    /// </summary>
    public const int DefaultStartMenuWidth = 640;

    /// <summary>
    /// A custom Start menu width: from 320, below which the pinned grid keeps
    /// one or two columns, to 1920, a full HD screen's width (wider is what Fill
    /// is for). The span gives the slider a grain of 50. Whatever the value, the
    /// engine holds the menu to the screen it opens on.
    /// </summary>
    public static PixelRange StartMenuWidthRange { get; } = new(320, 1920);

    /// <summary>
    /// The pitch everything compact starts at: a tray icon, a quick-settings icon,
    /// an app button. One value, so the column reads as one rhythm from top to
    /// bottom until the person sets the two apart.
    /// </summary>
    public const int DefaultCompactPitch = 24;

    /// <summary>
    /// A tray icon's pitch: from 16, the icon itself with no room around it, to
    /// 40, beyond Explorer's own 32.
    /// </summary>
    public static PixelRange TrayPitchRange { get; } = new(16, 40);

    /// <summary>
    /// An app button's height: from 20, the 16 px icon with 2 px above and below
    /// (Start's padding goes to 0 there), to 48, the height of a Large-mode button.
    /// </summary>
    public static PixelRange TaskButtonPitchRange { get; } = new(20, 48);
}

/// <summary>The bounds of a size in effective pixels: what the page offers, and what a value read from the file is held to.</summary>
internal readonly record struct PixelRange(int Minimum, int Maximum)
{
    public int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);
}

/// <summary>What Deckle wants the Start menu to look like.</summary>
internal sealed record StartMenuCustomization
{
    /// <summary>
    /// The menu is as high as the screen it opens on, with the same gap above and
    /// below it, at any scale. Windows caps the menu at 750 effective pixels.
    /// </summary>
    public bool FullHeight { get; init; }

    /// <summary>The menu's width; null leaves Windows' own.</summary>
    public StartMenuWidthRequest? Width { get; init; }

    /// <summary>The section of recent files and suggestions under the pinned apps is taken out.</summary>
    public bool HideRecommended { get; init; }

    /// <summary>Whether this asks nothing of the Start menu.</summary>
    public bool IsEmpty => !FullHeight && Width is null && !HideRecommended;
}

/// <summary>
/// A Start menu width: all the screen allows (Fill), or a value in effective
/// pixels, the panel the person sees. The engine holds either to the screen the
/// menu opens on, which only it can measure.
/// </summary>
internal readonly record struct StartMenuWidthRequest(bool IsFill, int Value)
{
    public static StartMenuWidthRequest Fill { get; } = new(true, 0);

    public static StartMenuWidthRequest Of(int value) => new(false, value);
}

/// <summary>
/// The box, in effective pixels, a hover thumbnail grows to fit. Each thumbnail
/// keeps its window's ratio: a wide window meets <paramref name="MaxWidth"/>, a
/// tall one <paramref name="MaxHeight"/>. Either orientation of the bar.
/// </summary>
internal sealed record ThumbnailBox(double MaxWidth, double MaxHeight);

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
