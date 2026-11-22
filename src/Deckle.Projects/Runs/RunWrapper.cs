using System.Text;

namespace Deckle.Projects;

// What to run for one action, as the facts the wrapper needs.
//
// RunFolder is the folder Deckle watches: the wrapper writes its own process id
// in started.txt there and the exit code in result.txt.
public sealed record RunWrapperInput(
    string Title,
    string RunFolder,
    string Shell,
    string MenuScript,
    string ActionId,
    string? Variant);

// The small PowerShell script that runs inside the terminal around an action,
// so Deckle can follow it to its end.
//
// Why a wrapper (settled by experiment, see the build plan, section 2.0):
// wt.exe hands the command to a Windows Terminal process and returns at once,
// so its exit code and lifetime say nothing about the command. A script that
// writes its own process id first, runs the action, then writes the exit code
// to a file gives Deckle the two facts it needs without owning the process.
// The exit code is read from that file, never from the process: the code of a
// process Deckle did not start came back empty in the probe.
//
// Windows Terminal's default closeOnExit closes the tab of a shell that exits
// with 0, which would hide the output of a successful build the moment it ends.
// So the wrapper, after writing the result, prints one line and waits for a key
// (in a try/catch, so a redirected input does not hang), then exits 0.
//
// Every value inserted goes through Quote. The script travels as
// -EncodedCommand (base64 of UTF-16), because wt.exe reads ';' as a command
// separator and reparses quotes; the encoded form crosses it untouched. The
// script stays Windows PowerShell 5.1-safe: no .Parent, no ??.
public static class RunWrapper
{
    public const string StartedFile = "started.txt";
    public const string ResultFile = "result.txt";
    private const string ResultTemporaryFile = "result.tmp";

    public static string Build(RunWrapperInput input)
    {
        string started = Quote(Path.Combine(input.RunFolder, StartedFile));
        string temporary = Quote(Path.Combine(input.RunFolder, ResultTemporaryFile));
        string result = Quote(Path.Combine(input.RunFolder, ResultFile));
        string run = $"& {Quote(input.Shell)} -NoProfile -ExecutionPolicy Bypass -File {Quote(input.MenuScript)} -Run {Quote(input.ActionId)}";
        if (input.Variant is not null) run += $" -Variant {Quote(input.Variant)}";

        return string.Join("\n",
            $"$Host.UI.RawUI.WindowTitle = {Quote(input.Title)}",
            $"[IO.File]::WriteAllText({started}, [string]$PID)",
            run,
            "$code = $LASTEXITCODE; if ($null -eq $code) { $code = 0 }",
            // Written then moved, so Deckle never reads half a number.
            $"[IO.File]::WriteAllText({temporary}, [string]$code); Move-Item {temporary} {result} -Force",
            "Write-Host ''; Write-Host \"Finished with exit code $code. Press any key to close this window.\"",
            "try { [void][Console]::ReadKey($true) } catch { }",
            "exit 0");
    }

    // A script that only opens the menu in a terminal: not an action, nothing
    // is followed.
    public static string BuildMenu(string shell, string menuScript) =>
        $"& {Quote(shell)} -NoProfile -ExecutionPolicy Bypass -File {Quote(menuScript)}";

    // A PowerShell single-quoted string. Inside one, only a quote ends the
    // string and a doubled quote is a literal one. PowerShell reads the typographic
    // single quotes (U+2018, U+2019, U+201A, U+201B) as quotes too, so a project
    // called "Louis's" with a curly apostrophe would end the string early unless
    // those are doubled as well.
    public static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('\'');
        foreach (char c in value)
        {
            text.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') text.Append(c);
        }
        return text.Append('\'').ToString();
    }

    // The form -EncodedCommand takes: UTF-16 little endian, base64.
    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
