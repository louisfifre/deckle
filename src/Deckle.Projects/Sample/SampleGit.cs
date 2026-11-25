using System.Diagnostics;

namespace Deckle.Projects;

// Makes the sample world's repositories with the git command line. Nothing of
// the machine's git setup reaches a sample repository: the global and system
// configuration files are not read, the identity is the sample's own and set
// in the environment (which wins over any -c), signing is off and no hook runs.
// The commit date is fixed, so two runs make the same history. When git is not
// on the machine the sample says so and its projects are plain folders.
//
// Why the configuration is shut out, not just overridden: the maintainer's
// global configuration holds a commit-msg hook that refuses any author but him
// (hook.<name>.event, read by git 2.5x). The sample's commit was refused, its
// tags then had no commit to point at, and every sample repository came out
// with no commit, no tag and all its files uncommitted. The self-check's P1 then
// failed on "the highest tag as the version", from a terminal without the
// maintainer's GIT_AUTHOR_* variables, and passed from one that had them.
internal static class SampleGit
{
    private const string FixedDate = "2026-11-09T18:00:00+00:00";
    private const string SampleName = "Sample";
    private const string SampleEmail = "sample@example.invalid";

    public static bool IsAvailable { get; } = ProbeAvailability();

    // What one git command left: whether it succeeded and what it wrote on its
    // error stream, so a failure can say why.
    internal readonly record struct Outcome(bool Succeeded, string Error);

    // Runs git in a folder; false when it fails or cannot start.
    public static bool Run(string folder, params string[] arguments) => Execute(folder, arguments).Succeeded;

    private static Outcome Execute(string folder, string[] arguments)
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
        foreach (string argument in (string[])["-c", $"user.name={SampleName}", "-c", $"user.email={SampleEmail}",
                     "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false", "-c", "init.defaultBranch=main"])
            start.ArgumentList.Add(argument);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        Isolate(start.Environment);

        try
        {
            using Process process = Process.Start(start)!;
            process.StandardInput.Close();
            // Both pipes are drained so a chatty git never blocks on a full one.
            process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return new Outcome(false, "git did not answer within 30 s");
            }
            return new Outcome(process.ExitCode == 0, error.Result.Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new Outcome(false, ex.Message);
        }
    }

    // The environment of a sample's git command. GIT_CONFIG_GLOBAL on the null
    // device and GIT_CONFIG_NOSYSTEM leave only the repository's own file and
    // the -c values: no hook, alias, template or identity of the machine. The
    // identity variables are set because a caller's GIT_AUTHOR_* would win over
    // user.name, and the sample's history must be the same from any terminal.
    internal static void Isolate(IDictionary<string, string?> environment)
    {
        // A caller inside a git command (a hook that runs the self-check) has
        // these set: they would aim the sample's commands at that repository.
        foreach (string inherited in (string[])["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR",
                     "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT"])
            environment.Remove(inherited);
        environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_AUTHOR_NAME"] = SampleName;
        environment["GIT_AUTHOR_EMAIL"] = SampleEmail;
        environment["GIT_COMMITTER_NAME"] = SampleName;
        environment["GIT_COMMITTER_EMAIL"] = SampleEmail;
        environment["GIT_AUTHOR_DATE"] = FixedDate;
        environment["GIT_COMMITTER_DATE"] = FixedDate;
        environment["GIT_TERMINAL_PROMPT"] = "0";
    }

    // A repository with everything in the folder committed, then the tags.
    // Throws when a step fails, with git's own words: a sample without its
    // commit or its tags is not the world the walk's checks describe, and the
    // run must say so where it happened instead of failing later on a page.
    public static void InitializeAndCommit(string folder, params string[] tags)
    {
        if (!IsAvailable) return;
        Require(folder, "init", "-q");
        Require(folder, "add", "-A");
        Require(folder, "commit", "-q", "--no-verify", "-m", "Initial sample commit");
        foreach (string tag in tags) Require(folder, "tag", tag);
    }

    private static void Require(string folder, params string[] arguments)
    {
        Outcome outcome = Execute(folder, arguments);
        if (!outcome.Succeeded)
            throw new InvalidOperationException(
                $"The sample repository {Path.GetFileName(folder)} could not be made: git {arguments[0]} failed"
                + (outcome.Error.Length > 0 ? $" ({OneLine(outcome.Error)})." : "."));
    }

    // git's error, or a hook's, on one line and short enough for a report.
    private static string OneLine(string text)
    {
        string flat = string.Join(' ', text.Split((char[])['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 300 ? flat : flat[..300] + "…";
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
