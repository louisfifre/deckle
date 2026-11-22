using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Deckle.Projects;

// Reads a project's script menu by asking it: `menu.ps1 -List` under the
// shell, hidden. Measured at about 1.25 s because it starts a PowerShell, so
// the window reads it on demand (a project page opening, "Pin an action...",
// the window coming back to the front on a page that had no menu), caches the
// answer per project for its life and never reads it for Home.
//
// The shell is started with a one-line command rather than `-File menu.ps1`:
// the command sets the console's output encoding to UTF-8 first. A redirected
// PowerShell writes in the machine's code page, which would turn an accented
// label into noise; reading it as UTF-8 needs the child to write UTF-8. The
// menu's own entry file (menu.ps1) is still what runs, with the arguments it
// would get from a person.
public sealed class ScriptMenuReader : IScriptMenuReader
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    // How many of the error's last lines the page can show.
    private const int LastLines = 8;

    private readonly string? _shell;
    private readonly TimeSpan _timeout;

    public ScriptMenuReader(string? shell, TimeSpan? timeout = null)
    {
        _shell = shell;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<ScriptMenuListing> ListAsync(string projectPath, CancellationToken cancellation)
    {
        string menu = Path.Combine(projectPath, "scripts", "menu.ps1");
        if (!File.Exists(menu)) return new ScriptMenuListing.NoMenu();
        if (_shell is null) return new ScriptMenuListing.NoShell();

        var clock = Stopwatch.StartNew();
        string script = string.Join("\n",
            "try { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false) } catch { }",
            $"& {RunWrapper.Quote(menu)} -List",
            "$code = $LASTEXITCODE; if ($null -eq $code) { $code = 0 }",
            "exit $code");

        var start = new ProcessStartInfo(_shell)
        {
            WorkingDirectory = projectPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(RunWrapper.Encode(script));

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("the shell did not start");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            DeckleProjectsSource.Log.ScriptMenuUnreadable("start-failed");
            return new ScriptMenuListing.Unreadable(ex.Message);
        }

        using (process)
        {
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
                cancellation.ThrowIfCancellationRequested();
                DeckleProjectsSource.Log.ScriptMenuUnreadable("timeout");
                return new ScriptMenuListing.Unreadable($"The menu did not answer within {_timeout.TotalSeconds:0} seconds.");
            }

            string text = await output.ConfigureAwait(false);
            string errors = await error.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                DeckleProjectsSource.Log.ScriptMenuUnreadable("exit-code");
                return new ScriptMenuListing.Unreadable(Tail(errors.Length > 0 ? errors : text));
            }

            IReadOnlyList<ScriptMenuEntry> entries = ScriptMenuParser.Parse(text);
            if (entries.Count == 0)
            {
                // Output that holds no row is a menu that does not speak -List
                // (an older one), not an empty menu.
                DeckleProjectsSource.Log.ScriptMenuUnreadable("no-rows");
                return new ScriptMenuListing.Unreadable(Tail(errors.Length > 0 ? errors : text));
            }

            DeckleProjectsSource.Log.ScriptMenuListed(entries.Count, clock.ElapsedMilliseconds);
            return new ScriptMenuListing.Listed(entries);
        }
    }

    // The last non-empty lines of an error, for the page.
    internal static string Tail(string text) =>
        string.Join(Environment.NewLine, text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0)
            .TakeLast(LastLines));

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
    }
}
