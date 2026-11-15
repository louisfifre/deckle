using System.Diagnostics;

namespace Deckle.Projects;

// Makes the sample world's repositories with the git command line. The identity
// is passed with -c and signing is off, so no developer's own configuration
// reaches a sample repository, and the commit date is fixed, so two runs make
// the same history. When git is not on the machine the sample says so and its
// projects are plain folders.
internal static class SampleGit
{
    private const string FixedDate = "2026-11-09T18:00:00+00:00";

    public static bool IsAvailable { get; } = ProbeAvailability();

    // Runs git in a folder; false when it fails or cannot start.
    public static bool Run(string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (string argument in (string[])["-c", "user.name=Sample", "-c", "user.email=sample@example.invalid",
                     "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false", "-c", "init.defaultBranch=main"])
            start.ArgumentList.Add(argument);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_AUTHOR_DATE"] = FixedDate;
        start.Environment["GIT_COMMITTER_DATE"] = FixedDate;
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        try
        {
            using Process process = Process.Start(start)!;
            process.StandardInput.Close();
            // Both pipes are drained so a chatty git never blocks on a full one.
            process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();
            return process.WaitForExit(TimeSpan.FromSeconds(30)) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    // A repository with everything in the folder committed, then the tags.
    public static void InitializeAndCommit(string folder, params string[] tags)
    {
        if (!IsAvailable) return;
        Run(folder, "init", "-q");
        Run(folder, "add", "-A");
        Run(folder, "commit", "-q", "-m", "Initial sample commit");
        foreach (string tag in tags) Run(folder, "tag", tag);
    }

    private static bool ProbeAvailability()
    {
        var start = new ProcessStartInfo("git", "--version")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            using Process process = Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
