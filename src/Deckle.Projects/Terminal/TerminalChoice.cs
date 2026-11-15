using System.Text.Json;

namespace Deckle.Projects;

// The terminal the whole app uses to run actions, open the script menu and
// serve the "Terminal" link: one choice for the app (spec, Behaviours).
public enum TerminalChoice
{
    // The default, and the one the spec names.
    WindowsTerminal,

    // The system's own console host.
    WindowsDefault,
}

// Where the choice is kept: terminal.json under the module's data folder. An
// absent or unreadable file means Windows Terminal. The settings page that
// edits it is not built in this version: there is no settings surface for the
// module yet, and a page for a choice between two terminals is a decision for
// the maintainer (deckle-settings-ux).
public static class TerminalChoiceFile
{
    public const string FileName = "terminal.json";

    private sealed class Stored
    {
        public string Terminal { get; set; } = "";
    }

    public static TerminalChoice Read(string moduleDirectory)
    {
        try
        {
            string path = Path.Combine(moduleDirectory, FileName);
            if (!File.Exists(path)) return TerminalChoice.WindowsTerminal;
            Stored? stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            return Enum.TryParse(stored?.Terminal, ignoreCase: false, out TerminalChoice choice) && Enum.IsDefined(choice)
                ? choice
                : TerminalChoice.WindowsTerminal;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return TerminalChoice.WindowsTerminal;
        }
    }

    public static void Write(string moduleDirectory, TerminalChoice choice)
    {
        Directory.CreateDirectory(moduleDirectory);
        string path = Path.Combine(moduleDirectory, FileName);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Stored { Terminal = choice.ToString() }));
        File.Move(temporary, path, overwrite: true);
    }
}
