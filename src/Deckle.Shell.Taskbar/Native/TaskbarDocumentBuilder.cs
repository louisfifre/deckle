using System.Text;
using System.Text.Json;

namespace Deckle.Shell.Taskbar;

// One rule of the settings document: set `Property` to `Value` on the elements
// `Target` names. The wire shape and the target syntax are documented in
// native/Deckle.Shell.Taskbar.Native/xamldiag/rules/rule_document.h and selector.h.
// An optional rule targets elements that exist only now and then (the
// microphone-in-use icon): finding none is not a problem to tell on the page.
// It stays on Deckle's side; the engine treats every rule alike.
internal sealed record TaskbarRule(string Id, string Target, string Property, string Value, string? VisualState = null, bool Optional = false);

// Builds the settings document the injected engine applies, from a
// TaskbarCustomization. The rules live here, on the C# side, and not in the
// DLL: a value or a target changes with a release of Deckle and not with a
// rebuild of the native half, and the DLL stays a generic engine.
//
// Every target below comes from the visual tree of this machine's taskbar
// (Windows 11 build 26100, vertical bar, Small mode), as the engine dumps it,
// not from another tool's documentation. The rules and values follow what
// Louis had with Windhawk: the styler's SimplyTransparent theme, the
// notification icon spacing mod and the clock customization mod.
internal static class TaskbarDocumentBuilder
{
    // Taskbar.TaskbarFrame > Grid#RootGrid > Taskbar.TaskbarBackground > Grid > Rectangle#BackgroundFill
    private const string BackgroundFillTarget =
        "Taskbar.TaskbarFrame > Grid#RootGrid > Taskbar.TaskbarBackground > Grid > Rectangle#BackgroundFill";

    // The 1 px line along the bar's edge. Anywhere in the tree: the hover
    // flyouts have one of their own.
    private const string BackgroundStrokeTarget = "Rectangle#BackgroundStroke";

    private const string NotifyIcon =
        "SystemTray.NotificationAreaIcons > ItemsPresenter > StackPanel > ContentPresenter > SystemTray.NotifyIconView#NotifyItemIcon";

    // The two buttons of the tray's end: quick settings (network, volume) and the
    // clock. Each holds its icons in a StackPanel under its template.
    private const string QuickSettingsButton = "SystemTray.OmniButton#ControlCenterButton";
    private const string ClockButton = "SystemTray.OmniButton#NotificationCenterButton";
    private const string OmniButtonStack = " > Grid > ContentPresenter#ContentPresenter > ItemsPresenter > StackPanel";
    private const string QuickSettingsStack = QuickSettingsButton + OmniButtonStack;

    // The system icons between the tray and quick settings (microphone, camera
    // or location in use), one stack each, the same shape under both names.
    private const string SystemIconStackTail =
        " > Grid#Content > SystemTray.StackListView#IconStack > ItemsPresenter > StackPanel > ContentPresenter > SystemTray.IconView#SystemTrayIcon";

    private const string ClockStack =
        "SystemTray.DateTimeIconContent > Grid#ContainerGrid > StackPanel";

    // An app button's row; its height is the button's (Explorer sets it from
    // the bar's size, 32 in Small mode) and the column of buttons packs on it.
    private const string TaskButtonPanel = "Taskbar.TaskListButton > Grid#IconPanel";

    // Start and Task view: the same kind of button with its own panel, whose
    // Padding (4,7 for Start, 4,2 for Task view) frames a 16 px icon. Both are
    // named LaunchListButton on this build; the name keeps the rule off the other
    // experience buttons (widgets, search), whose content is not a 16 px icon.
    private const string ExperienceButtonPanel =
        "Taskbar.ExperienceToggleButton#LaunchListButton > Taskbar.TaskListButtonPanel#ExperienceToggleButtonRootPanel";

    public static IReadOnlyList<TaskbarRule> Rules(TaskbarCustomization customization)
    {
        var rules = new List<TaskbarRule>();

        if (customization.Background != TaskbarBackground.Native)
        {
            // Black is a solid fill; transparent shows the wallpaper. The edge
            // line goes in both: against black it would be a grey hairline.
            string fill = customization.Background == TaskbarBackground.Black ? "Black" : "Transparent";
            rules.Add(new("background.fill", BackgroundFillTarget, "Fill", fill));
            rules.Add(new("background.stroke", BackgroundStrokeTarget, "Fill", "Transparent"));
        }

        if (customization.TrayIconPitch is { } pitch)
        {
            // An icon's row is as high as its MinHeight (Explorer sets 32). The
            // MinWidth of 0 lets the icon fill the narrow column of a vertical bar.
            rules.Add(new("tray.icon.minHeight", NotifyIcon, "MinHeight", Number(pitch)));
            rules.Add(new("tray.icon.minWidth", NotifyIcon, "MinWidth", "0"));
        }

        if (customization.TightenQuickSettings)
        {
            // The group's icons sit in a StackPanel with 4 px between them and
            // 4 px of Padding above and below; each icon is given the pitch as
            // its height. The button's own Padding goes too, so its hover
            // highlight fits the icons it covers.
            int iconPitch = customization.TrayIconPitch ?? TaskbarCustomization.DefaultCompactPitch;
            rules.Add(new("quickSettings.spacing", QuickSettingsStack, "Spacing", "0"));
            rules.Add(new("quickSettings.stack.padding", QuickSettingsStack, "Padding", "0"));
            rules.Add(new("quickSettings.button.padding", QuickSettingsButton, "Padding", "0"));
            rules.Add(new("quickSettings.icon.height", QuickSettingsStack + " > ContentPresenter > SystemTray.IconView#SystemTrayIcon", "Height", Number(iconPitch)));

            // The microphone-in-use icon and its kin: Explorer gives them a 28 px
            // row. They come and go, so these two rules may find nothing.
            rules.Add(new("tray.mainStack.icon.minHeight", "SystemTray.Stack#MainStack" + SystemIconStackTail, "MinHeight", Number(iconPitch), Optional: true));
            rules.Add(new("tray.nonActivatableStack.icon.minHeight", "SystemTray.Stack#NonActivatableStack" + SystemIconStackTail, "MinHeight", Number(iconPitch), Optional: true));
        }

        if (customization.TaskButtonPitch is { } buttonPitch)
        {
            // Explorer writes an app button's height again when it lays the bar
            // out; the engine holds it. The column of buttons follows each
            // button's height, so the buttons pack at the pitch.
            rules.Add(new("taskButtons.app.height", TaskButtonPanel, "Height", Number(buttonPitch)));

            // Start and Task view: the same height, and a padding that leaves
            // their 16 px icon whole (Start's own 7 above and below would leave it
            // 10 px at a 24 px pitch). 4 on the sides is Explorer's own.
            double vertical = Math.Max(0, (buttonPitch - 20) / 2.0);
            rules.Add(new("taskButtons.experience.height", ExperienceButtonPanel, "Height", Number(buttonPitch)));
            rules.Add(new("taskButtons.experience.padding", ExperienceButtonPanel, "Padding", $"4,{Number(vertical)},4,{Number(vertical)}"));
        }

        if (customization.Clock is { } clock)
        {
            rules.Add(new("clock.spacing", ClockStack, "Spacing", Number(clock.LineSpacing)));
            AddClockStyle(rules, "upper", "TimeInnerTextBlock", clock.UpperStyle);
            AddClockStyle(rules, "lower", "DateInnerTextBlock", clock.LowerStyle);
        }

        if (customization.TightenClockButton)
        {
            // The clock's button keeps 4 px of Padding around its stack and 4 more
            // in the stack: they go, and 4 px above set the clock apart from the
            // icons over it.
            rules.Add(new("clock.button.padding", ClockButton, "Padding", "0"));
            rules.Add(new("clock.button.stack.padding", ClockButton + OmniButtonStack, "Padding", "0,4,0,0"));
        }

        return rules;
    }

    // The styles go on the TextBlocks as rules; the text itself is the clock behavior.
    private static void AddClockStyle(List<TaskbarRule> rules, string block, string textBlock, ClockTextStyle style)
    {
        string target = ClockStack + " > TextBlock#" + textBlock;
        rules.Add(new($"clock.{block}.fontSize", target, "FontSize", Number(style.FontSize)));
        if (style.FontWeight is { } weight) rules.Add(new($"clock.{block}.fontWeight", target, "FontWeight", weight));
        if (style.Color is { } color) rules.Add(new($"clock.{block}.color", target, "Foreground", color));
        if (style.LineHeight is { } lineHeight)
        {
            rules.Add(new($"clock.{block}.lineHeight", target, "LineHeight", Number(lineHeight)));
            rules.Add(new($"clock.{block}.lineStacking", target, "LineStackingStrategy", "BlockLineHeight"));
        }
    }

    // The section under the pinned apps: its header ("Recent" on 26100,
    // "Recommended" before) and its list, one Grid in StartHome's TopLevelHeader.
    // Collapsing it lets the All list move up. Measured live on 2026-11-10: the
    // value holds, no visual state takes it back.
    private const string StartRecommendedSection = "Grid#TopLevelSuggestionsRoot";

    /// <summary>The rules of the Start menu process, built from the Start menu part of a customization.</summary>
    public static IReadOnlyList<TaskbarRule> StartMenuRules(TaskbarCustomization customization)
    {
        var rules = new List<TaskbarRule>();
        if (customization.StartMenu is { HideRecommended: true })
            rules.Add(new("startMenu.recommended.visibility", StartRecommendedSection, "Visibility", "Collapsed"));
        return rules;
    }

    /// <summary>
    /// The settings document of the Start menu process, as one line of JSON. Its own
    /// document, for its own engine: the Start menu is not part of explorer. An empty
    /// customization gives an empty document, which restores everything.
    /// </summary>
    public static string BuildStartMenu(TaskbarCustomization customization)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteRules(writer, StartMenuRules(customization));

            writer.WriteStartObject("behaviors");
            if (customization.StartMenu is { FullHeight: true })
            {
                writer.WriteStartObject("startMenuHeight");
                writer.WriteBoolean("enabled", true);
                writer.WriteEndObject();
            }
            // The width is a behavior and not a rule: held to the Start window,
            // which only the engine can measure (it differs per screen, and a
            // portrait screen is narrower than most menus).
            if (customization.StartMenu?.Width is { } width)
            {
                writer.WriteStartObject("startMenuWidth");
                writer.WriteBoolean("enabled", true);
                writer.WriteBoolean("fill", width.IsFill);
                if (!width.IsFill) writer.WriteNumber("width", width.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // The bar's thickness is no element of the tree: Explorer asks this function
    // (static, one argument, the TaskbarSize, small regular or large) and sizes
    // the bar's window, its XAML and the work area from the answer. It answers
    // the thickness of a bar on any edge.
    public const string FrameSizeModule = "Taskbar.View.dll";
    public const string FrameSizeSymbol =
        "?GetFrameSize@TaskbarConfiguration@implementation@Taskbar@winrt@@SANW4TaskbarSize@Shell@UI@WindowsUdk@4@@Z";
    public const string BarThicknessId = "bar.thickness";

    /// <summary>
    /// The settings document as one line of JSON, ready for NativeCommand.Apply.
    /// <paramref name="frameSize"/> is where <see cref="FrameSizeSymbol"/> lives
    /// in the explorer running now; without it the thickness is left out.
    /// </summary>
    public static string Build(TaskbarCustomization customization, ExplorerFunction? frameSize = null)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteRules(writer, Rules(customization));

            if (customization.BarThickness is { } thickness && frameSize is { } function)
            {
                writer.WriteStartArray("constantReturns");
                writer.WriteStartObject();
                writer.WriteString("id", BarThicknessId);
                writer.WriteString("module", function.Module);
                writer.WriteNumber("timestamp", function.TimeDateStamp);
                writer.WriteNumber("imageSize", function.ImageSize);
                writer.WriteNumber("rva", function.Rva);
                writer.WriteString("original", Convert.ToHexString(function.Prologue));
                writer.WriteNumber("value", thickness);
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteStartObject("behaviors");
            if (customization.Clock is { } clock)
            {
                writer.WriteStartObject("clock");
                writer.WriteBoolean("enabled", true);
                writer.WriteString("locale", clock.Locale);
                WriteLines(writer, "upper", clock.UpperLines);
                WriteLines(writer, "lower", clock.LowerLines);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteRules(Utf8JsonWriter writer, IReadOnlyList<TaskbarRule> rules)
    {
        writer.WriteStartArray("rules");
        foreach (TaskbarRule rule in rules)
        {
            writer.WriteStartObject();
            writer.WriteString("id", rule.Id);
            writer.WriteString("target", rule.Target);
            writer.WriteString("property", rule.Property);
            writer.WriteString("value", rule.Value);
            if (rule.VisualState is { } state) writer.WriteString("visualState", state);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteLines(Utf8JsonWriter writer, string name, IReadOnlyList<ClockLine> lines)
    {
        writer.WriteStartArray(name);
        foreach (ClockLine line in lines)
        {
            writer.WriteStartObject();
            writer.WriteString(line.Kind == ClockLineKind.Date ? "date" : "time", line.Format);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    // XAML reads numbers with a point whatever the culture of the machine.
    private static string Number(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
