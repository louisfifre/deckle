namespace Deckle.Projects;

// Finds the shell that runs the script menu and the wrapper around an action.
//
// pwsh (PowerShell 7) first: the menu is written for it. Without it,
// powershell.exe (Windows PowerShell 5.1): the menu's entry file, menu.ps1, is
// written for 5.1 and says "install PowerShell 7" itself, so a machine with
// only 5.1 still gets that sentence in the terminal instead of a silent
// failure. The wrapper stays 5.1-safe for the same reason (no .Parent, no ??).
// Without either, an action cannot start and the page says so.
public static class ShellLocator
{
    // The shell's full path, or null when neither is installed. The directories
    // are a parameter so a test can point it at a folder it made.
    public static string? Find(IEnumerable<string>? pathDirectories = null)
    {
        string[] directories = [.. pathDirectories ?? Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? []];

        return FindIn(directories, "pwsh.exe") ?? FindIn(directories, "powershell.exe");
    }

    private static string? FindIn(string[] directories, string file)
    {
        foreach (string directory in directories)
        {
            try
            {
                string candidate = Path.Combine(directory.Trim().Trim('"'), file);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A PATH entry with a character a path cannot hold.
            }
        }
        return null;
    }
}
