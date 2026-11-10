namespace Deckle.Shell.Taskbar.Harness;

internal sealed class HarnessOptions
{
    public string? DllPath { get; private init; }
    public string? Root { get; private init; }
    public string? SendFile { get; private init; }
    public string? TreeFile { get; private init; }
    public double TreeAfter { get; private init; } = 6;
    public double? StopAfter { get; private init; }
    public string? ControlFile { get; private init; }
    public bool Visual { get; private init; }
    public TaskbarTarget Target { get; private init; } = TaskbarTarget.Explorer;
    public string? DocumentOut { get; private init; }
    public TaskbarCustomization Customization { get; private init; } = new();
    public string? DumpFile { get; private init; }
    public double DumpAfter { get; private init; } = 6;

    public static HarnessOptions Parse(string[] args)
    {
        string? dll = null, root = null, send = null, tree = null, control = null;
        double treeAfter = 6;
        double? stopAfter = null;
        bool visual = false;
        TaskbarTarget target = TaskbarTarget.Explorer;
        string? documentOut = null, dump = null;
        double dumpAfter = 6;
        var customization = new TaskbarCustomization();

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--dll": dll = Next(); break;
                case "--root": root = Next(); break;
                case "--send": send = Next(); break;
                case "--tree": tree = Next(); break;
                case "--tree-after": treeAfter = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--stop-after": stopAfter = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--control": control = Next(); break;
                case "--visual": visual = true; break;
                case "--target":
                    target = Next().ToLowerInvariant() switch
                    {
                        "explorer" => TaskbarTarget.Explorer,
                        "startmenu" => TaskbarTarget.StartMenu,
                        var other => throw new ArgumentException($"unknown target {other} (explorer or startmenu)"),
                    };
                    break;
                case "--start-menu-full-height":
                    customization = customization with { StartMenu = (customization.StartMenu ?? new()) with { FullHeight = true } };
                    break;
                case "--hide-start-recommended":
                    customization = customization with { StartMenu = (customization.StartMenu ?? new()) with { HideRecommended = true } };
                    break;
                case "--start-menu-width":
                    string width = Next();
                    customization = customization with
                    {
                        StartMenu = (customization.StartMenu ?? new()) with
                        {
                            Width = width == "fill"
                                ? StartMenuWidthRequest.Fill
                                : StartMenuWidthRequest.Of(int.Parse(width, System.Globalization.CultureInfo.InvariantCulture)),
                        },
                    };
                    break;
                case "--doc-out": documentOut = Next(); break;
                case "--bg":
                    customization = customization with { Background = Enum.Parse<TaskbarBackground>(Next(), ignoreCase: true) };
                    break;
                case "--bar-thickness":
                    customization = customization with { BarThickness = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) };
                    break;
                case "--tray-pitch":
                    customization = customization with { TrayIconPitch = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) };
                    break;
                case "--tighten-quick-settings": customization = customization with { TightenQuickSettings = true }; break;
                case "--clock-date-above-time": customization = customization with { Clock = ClockCustomization.DateAboveTime }; break;
                case "--tighten-clock-button": customization = customization with { TightenClockButton = true }; break;
                case "--task-button-pitch":
                    customization = customization with { TaskButtonPitch = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) };
                    break;
                case "--dump": dump = Next(); break;
                case "--dump-after": dumpAfter = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }

        return new HarnessOptions
        {
            DllPath = dll, Root = root, SendFile = send, TreeFile = tree, TreeAfter = treeAfter,
            StopAfter = stopAfter, ControlFile = control, Visual = visual, Target = target,
            DocumentOut = documentOut, Customization = customization, DumpFile = dump, DumpAfter = dumpAfter,
        };
    }
}

