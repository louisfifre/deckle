using System.Diagnostics;
using Xunit;

namespace Deckle.App.Tests;

// The update helper of the local copy, run the way LocalCopyUpdate runs it:
// under Windows PowerShell 5.1, on a temporary local copy. Its last step starts
// current\Deckle.exe, so a copy of rundll32.exe, which exits at once and opens
// no window when given no arguments, stands in for the app.
[Trait("Category", "integration")]
public sealed class LocalCopyApplyUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Deckle-LocalCopy-" + Guid.NewGuid().ToString("N")[..8]);

    public LocalCopyApplyUpdateTests()
    {
        foreach (string folder in new[] { "current", "next" })
        {
            Directory.CreateDirectory(Path.Combine(_root, folder));
            File.Copy(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), Path.Combine(_root, folder, "Deckle.exe"));
            File.WriteAllText(Path.Combine(_root, folder, "build.txt"), folder);
        }
        File.Copy(HelperSource(), Path.Combine(_root, LocalCopyUpdate.ApplyScriptName));
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

    private void RunHelper()
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
            "-File", Path.Combine(_root, LocalCopyUpdate.ApplyScriptName), "-Root", _root,
        })
            psi.ArgumentList.Add(arg);

        using Process helper = Process.Start(psi)!;
        Assert.True(helper.WaitForExit(TimeSpan.FromSeconds(60)), "the helper did not finish within a minute");
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
        return Path.Combine(dir.FullName, "src", "Deckle.App", "LocalCopy", LocalCopyUpdate.ApplyScriptName);
    }
}
