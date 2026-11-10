using System.Diagnostics;
using System.Globalization;
using Deckle.Shell.Taskbar;

namespace Deckle.Shell.Taskbar.Harness;

// Drives the same classes the app does, with no window: the settings service,
// its Changed event, TaskbarCustomizer, the engine hosts and the document. It is
// how the app's wiring is tried against the live explorer without a second
// Deckle. Run it with DECKLE_DATA_ROOT set to a folder of its own, so it reads
// and writes its own settings and never the resident app's.
//
//   Deckle.Shell.Taskbar.Harness customizer [--control <file>] [--stop-after <seconds>]
//
// Control file lines (the file is deleted once read):
//   set <background|compactTray|compactClock|startMenuFullHeight|showAllTrayIcons> <value>
//   status    prints the parts asked for, the problems, and who has the DLL loaded
//   stop      restores, unloads and exits
internal static class CustomizerCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? control = null;
        double? stopAfter = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--control": control = args[++i]; break;
                case "--stop-after": stopAfter = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }

        string? root = Environment.GetEnvironmentVariable("DECKLE_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("harness: customizer needs DECKLE_DATA_ROOT, so it never touches the resident app's settings");
            return 2;
        }

        TaskbarSettingsService settings = TaskbarSettingsService.Instance;
        TaskbarCustomizer customizer = TaskbarCustomizer.Instance;
        HarnessCommand.Print($"settings {settings.Path}");
        customizer.ProblemsChanged += () => HarnessCommand.Print("problems " + Describe(customizer));
        settings.Changed += () => customizer.Reconcile("setting");   // as App.Taskbar.cs does
        customizer.Reconcile("startup");

        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        var started = Stopwatch.StartNew();

        while (!stop.Task.IsCompleted)
        {
            await Task.Delay(200);
            if (stopAfter is { } limit && started.Elapsed.TotalSeconds >= limit) break;
            if (control is null || !File.Exists(control)) continue;

            string[] lines;
            try { lines = File.ReadAllLines(control); File.Delete(control); }
            catch (IOException) { continue; }

            foreach (string line in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (line == "stop") { stop.TrySetResult(); break; }
                if (line == "status") HarnessCommand.Print("status   " + Describe(customizer) + "  " + LoadedBy());
                else if (line.StartsWith("set ")) Set(settings, line[4..]);
                else HarnessCommand.Print("control  unknown command: " + line);
            }
        }

        HarnessCommand.Print("stop     restoring and unloading both engines");
        await customizer.ShutdownAsync();
        HarnessCommand.Print("stop     " + LoadedBy());
        return 0;
    }

    private static void Set(TaskbarSettingsService service, string text)
    {
        string[] parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
        TaskbarSettings current = service.Current;
        bool flag = parts.Length > 1 && bool.TryParse(parts[1], out bool parsed) && parsed;
        switch (parts[0])
        {
            case "background": current.Background = Enum.Parse<TaskbarBackground>(parts[1], ignoreCase: true); break;
            case "compactTray": current.CompactTray = flag; break;
            case "compactTaskButtons": current.CompactTaskButtons = flag; break;
            case "compactClock": current.CompactClock = flag; break;
            case "startMenuFullHeight": current.StartMenuFullHeight = flag; break;
            case "showAllTrayIcons": throw new ArgumentException("showAllTrayIcons belongs to the tray promoter, not driven here");
            default: throw new ArgumentException($"unknown setting {parts[0]}");
        }
        service.Save();
        HarnessCommand.Print($"set      {text} (saved; the store raises Changed after its delay)");
    }

    private static string Describe(TaskbarCustomizer customizer) =>
        customizer.Problems.Count == 0
            ? "none"
            : string.Join(", ", customizer.Problems.Select(pair => $"{pair.Key}={pair.Value}"));

    // Which of the two target processes have the DLL's versioned copy loaded.
    private static string LoadedBy()
    {
        var holders = new List<string>();
        foreach (string name in new[] { "explorer", "StartMenuExperienceHost" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    bool loaded = process.Modules.Cast<ProcessModule>().Any(module =>
                        module.ModuleName.Equals(VersionedNativeCopy.DllName, StringComparison.OrdinalIgnoreCase));
                    holders.Add($"{name}({process.Id})={(loaded ? "DLL loaded" : "no DLL")}");
                }
                catch (Exception) { holders.Add($"{name}({process.Id})=unreadable"); }
                finally { process.Dispose(); }
            }
        }
        return string.Join("  ", holders);
    }
}
