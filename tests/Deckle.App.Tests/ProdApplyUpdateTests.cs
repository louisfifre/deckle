using System.Diagnostics;
using Xunit;

namespace Deckle.App.Tests;

// The update helper of the prod copy, run the way ProdUpdate runs it:
// under Windows PowerShell 5.1, on a temporary prod copy. Its last step starts
// current\Deckle.exe, so a copy of rundll32.exe, which exits at once and opens
// no window when given no arguments, stands in for the app.
[Trait("Category", "integration")]
public sealed class ProdApplyUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Deckle-Prod-" + Guid.NewGuid().ToString("N")[..8]);

    public ProdApplyUpdateTests()
    {
        foreach (string folder in new[] { "current", "next" })
        {
            Directory.CreateDirectory(Path.Combine(_root, folder));
            File.Copy(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), Path.Combine(_root, folder, "Deckle.exe"));
            File.WriteAllText(Path.Combine(_root, folder, "build.txt"), folder);
        }
        File.Copy(HelperSource(), Path.Combine(_root, ProdUpdate.ApplyScriptName));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void The_staged_build_takes_the_place_of_current_and_the_replaced_one_is_kept()
    {
        RunHelper();

        Assert.Equal("next", Build("current"));
        Assert.Equal("current", Build("previous"));
        Assert.False(Directory.Exists(Path.Combine(_root, "next")));
        Assert.Contains("apply completed", Log());
    }

    [Fact]
    public void An_older_previous_gives_way_to_the_build_being_replaced()
    {
        Directory.CreateDirectory(Path.Combine(_root, "previous"));
        File.WriteAllText(Path.Combine(_root, "previous", "build.txt"), "older");

        RunHelper();

        Assert.Equal("current", Build("previous"));
    }

    [Fact]
    public void A_staged_build_that_cannot_move_leaves_current_where_it_was()
    {
        // An open file without sharing keeps next\ from being renamed.
        using (File.Open(Path.Combine(_root, "next", "build.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            RunHelper();

        Assert.Equal("current", Build("current"));
        Assert.True(File.Exists(Path.Combine(_root, "next", "Deckle.exe")));
        Assert.Contains("apply failed", Log());
    }

    [Fact]
    public void A_way_back_held_for_a_moment_is_retried_and_current_returns()
    {
        // next\ cannot move; then previous\ is held too, until the way back has
        // failed once (the renames wait 5 s) and before its retries run out.
        using (File.Open(Path.Combine(_root, "next", "build.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            RunHelper(afterSwapStarted: () => HoldPrevious(TimeSpan.FromSeconds(7)));

        Assert.Equal("current", Build("current"));
        Assert.Contains("outcome=runs_current", Log());
        Assert.DoesNotContain("current_missing", Log());
    }

    [Fact]
    public void A_way_back_that_keeps_failing_says_current_is_missing_and_starts_nothing()
    {
        int exitCode;
        using (File.Open(Path.Combine(_root, "next", "build.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            exitCode = RunHelper(afterSwapStarted: () => HoldPrevious(TimeSpan.FromSeconds(30)));

        Assert.False(Directory.Exists(Path.Combine(_root, "current")));
        Assert.Contains("outcome=current_missing", Log());
        Assert.DoesNotContain("runs_current", Log());
        Assert.Contains("reason=exe_missing", Log());
        Assert.NotEqual(0, exitCode);
    }

    // Waits for current\ to be moved to previous\, then keeps a file of it open
    // without sharing, which keeps the folder from being renamed back.
    private void HoldPrevious(TimeSpan duration)
    {
        string file = Path.Combine(_root, "previous", "build.txt");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(file))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("current was never moved to previous");
            Thread.Sleep(20);
        }
        using var held = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
        _holdUntilExit.Wait(duration);
    }

    private readonly ManualResetEventSlim _holdUntilExit = new(false);

    private int RunHelper(Action? afterSwapStarted = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(_root, ProdUpdate.ApplyScriptName), "-Root", _root,
        })
            psi.ArgumentList.Add(arg);

        using Process helper = Process.Start(psi)!;
        Task? holder = afterSwapStarted is null ? null : Task.Run(afterSwapStarted);
        bool finished = helper.WaitForExit(TimeSpan.FromSeconds(90));
        _holdUntilExit.Set();
        holder?.GetAwaiter().GetResult();
        Assert.True(finished, "the helper did not finish within 90 seconds");
        return helper.ExitCode;
    }

    private string? Build(string folder)
    {
        string path = Path.Combine(_root, folder, "build.txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private string Log() => File.ReadAllText(Path.Combine(_root, "apply-update.log"));

    private static string HelperSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repository root not found");
        return Path.Combine(dir.FullName, "src", "Deckle.App", "Prod", ProdUpdate.ApplyScriptName);
    }
}
