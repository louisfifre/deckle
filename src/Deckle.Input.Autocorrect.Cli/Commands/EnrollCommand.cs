using Deckle.Input.Autocorrect;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// Manages the enrollment list — the activation gate. An app never enrolled is
// never corrected, so this is how a surface opts in. Process names are stored
// without extension, deduped case-insensitively, and persisted immediately.
internal static class EnrollCommand
{
    public static int Run(CliArgs args)
    {
        if (args.Positional.Count == 0)
        {
            Console.Error.WriteLine("Usage: enroll list | add <process> | remove <process>");
            return 1;
        }

        var settings = AutocorrectSettingsService.Instance;
        var current = settings.Current;
        string action = args.Positional[0].ToLowerInvariant();

        switch (action)
        {
            case "list":
                break;

            case "add":
                if (args.Positional.Count < 2) { return Usage(); }
                AddDistinct(current.EnrolledProcesses, args.Positional[1]);
                settings.Save();
                settings.Flush();
                break;

            case "remove":
                if (args.Positional.Count < 2) { return Usage(); }
                current.EnrolledProcesses.RemoveAll(p =>
                    string.Equals(p, args.Positional[1], StringComparison.OrdinalIgnoreCase));
                settings.Save();
                settings.Flush();
                break;

            default:
                return Usage();
        }

        PrintList(current.EnrolledProcesses);
        return 0;
    }

    // Adds a process only when no case-insensitive match already exists.
    private static void AddDistinct(List<string> list, string process)
    {
        foreach (string p in list)
            if (string.Equals(p, process, StringComparison.OrdinalIgnoreCase))
                return;
        list.Add(process);
    }

    private static void PrintList(IReadOnlyList<string> processes)
    {
        Console.WriteLine("Enrolled processes:");
        if (processes.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }
        foreach (string p in processes)
            Console.WriteLine($"  {p}");
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: enroll list | add <process> | remove <process>");
        return 1;
    }
}
