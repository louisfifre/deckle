using System.Text;
using System.Text.Json;

namespace Deckle.Shell.Taskbar;

// One rule of the settings document: set `Property` to `Value` on the elements
// `Target` names. The wire shape and the target syntax are documented in
// native/Deckle.Shell.Taskbar.Native/xamldiag/rules/rule_document.h and selector.h.
internal sealed record TaskbarRule(string Id, string Target, string Property, string Value, string? VisualState = null);

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

    private const string QuickSettingsStack =
        "SystemTray.OmniButton#ControlCenterButton > Grid > ContentPresenter#ContentPresenter > ItemsPresenter > StackPanel";

    private const string ClockStack =
        "SystemTray.DateTimeIconContent > Grid#ContainerGrid > StackPanel";

    private const int DefaultTrayPitch = 24;

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
            // The group's icons sit in a StackPanel with 4 px between them;
            // each icon is given the pitch as its height.
            int iconPitch = customization.TrayIconPitch ?? DefaultTrayPitch;
            rules.Add(new("quickSettings.spacing", QuickSettingsStack, "Spacing", "0"));
            rules.Add(new("quickSettings.icon.height", QuickSettingsStack + " > ContentPresenter > SystemTray.IconView#SystemTrayIcon", "Height", Number(iconPitch)));
        }

        if (customization.Clock is { } clock)
        {
            rules.Add(new("clock.spacing", ClockStack, "Spacing", Number(clock.LineSpacing)));
            AddClockStyle(rules, "upper", "TimeInnerTextBlock", clock.UpperStyle);
            AddClockStyle(rules, "lower", "DateInnerTextBlock", clock.LowerStyle);
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
            writer.WriteStartArray("rules");
            writer.WriteEndArray();

            writer.WriteStartObject("behaviors");
            if (customization.StartMenu is { FullHeight: true })
            {
                writer.WriteStartObject("startMenuHeight");
                writer.WriteBoolean("enabled", true);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The settings document as one line of JSON, ready for NativeCommand.Apply.</summary>
    public static string Build(TaskbarCustomization customization)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("rules");
            foreach (TaskbarRule rule in Rules(customization))
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
