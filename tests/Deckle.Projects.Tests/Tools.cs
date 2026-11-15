using System.Diagnostics;

namespace Deckle.Projects.Tests;

// Which tools the machine running the tests has. A test that needs one says so
// with `SkipUnless`, so a machine without it reports a skip with the reason
// rather than a failure that is not the code's.
internal static class Tools
{
    public static bool GitPresent { get; } = OnPath("git.exe");

    public static bool PwshPresent { get; } = OnPath("pwsh.exe");

    private static bool OnPath(string file) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir =>
            {
                try { return File.Exists(Path.Combine(dir.Trim('"'), file)); }
                catch (ArgumentException) { return false; }
            });

    // Runs git in a folder with the identity of a test and no signing, so a
    // developer's own configuration never reaches a scratch repository.
    public static string Git(string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in (string[])["-c", "user.name=Sample", "-c", "user.email=sample@example.invalid",
                     "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false", "-c", "init.defaultBranch=main"])
            start.ArgumentList.Add(a);
        foreach (string a in arguments) start.ArgumentList.Add(a);

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {error}");
        return output;
    }

    // A repository with one commit, so tags and a status have something to
    // stand on.
    public static string Repository(ScratchFolder scratch, string name)
    {
        string folder = scratch.Folder(name);
        Git(folder, "init", "-q");
        File.WriteAllText(Path.Combine(folder, "README.md"), "sample\n");
        Git(folder, "add", "README.md");
        Git(folder, "commit", "-q", "-m", "first");
        return folder;
    }
}
