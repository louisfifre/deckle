using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Deckle.Projects;

// What one run of git left: its exit code and its output, or the reason it
// could not be asked at all.
public enum GitFailure
{
    None,

    // git is not on the PATH (the process could not start).
    NotFound,

    // It did not answer within the time limit and was killed.
    TimedOut,

    // Git refuses a folder owned by another account (exit 128 and a message
    // saying so). The project reads as "not readable", never as an error.
    DubiousOwnership,
}

public sealed record GitResult(int ExitCode, string Output, string Error, GitFailure Failure)
{
    public bool Succeeded => Failure == GitFailure.None && ExitCode == 0;

    // "fatal: not a git repository": the folder is fine, it is just not one.
    public bool IsNotARepository =>
        Failure == GitFailure.None && ExitCode != 0
        && Error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);
}

// The seam the readouts stand on: tests of the reading rules give it canned
// answers, and the integration tests give it the real command.
public interface IGit
{
    Task<GitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellation);
}

// Git is the `git` command line, run as a hidden process.
//
// Why not a library: the spec's rule is "everything on screen is found without
// a model", the project's own scripts already shell out to git, and a library
// would add a package for what three commands answer.
//
// Every call is `git -C <dir> -c core.quotepath=off ...` with
// GIT_OPTIONAL_LOCKS=0, so reading the status never takes the index lock and
// never disturbs the person's own git command running in another terminal.
// LC_ALL=C keeps git's messages in English, the one text this class reads
// ("dubious ownership"); GIT_TERMINAL_PROMPT=0 makes a command that would ask
// for a password fail instead of waiting.
public sealed class GitCommand : IGit
{
    // A command that has not answered by then is killed: a project on a
    // network drive must not hold a page open.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _timeout;

    public GitCommand(TimeSpan? timeout = null) => _timeout = timeout ?? DefaultTimeout;

    public async Task<GitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=off");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C";

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new GitResult(-1, "", "", GitFailure.NotFound);
        }

        using (process)
        {
            // Nothing is ever typed to git.
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                // The person leaving the page is not a timeout.
                cancellation.ThrowIfCancellationRequested();
                return new GitResult(-1, "", "", GitFailure.TimedOut);
            }

            string text = await output.ConfigureAwait(false);
            string errors = await error.ConfigureAwait(false);
            if (process.ExitCode == 128 && errors.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase))
                return new GitResult(process.ExitCode, text, errors, GitFailure.DubiousOwnership);
            return new GitResult(process.ExitCode, text, errors, GitFailure.None);
        }
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
    }
}
