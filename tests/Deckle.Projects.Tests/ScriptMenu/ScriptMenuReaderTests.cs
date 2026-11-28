using Xunit;

namespace Deckle.Projects.Tests;

// The reader against a stub menu.ps1 written in a scratch project and run by the
// real PowerShell, hidden. Each test skips with its reason without pwsh.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class ScriptMenuReaderTests
{
    private static string? Shell => ShellLocator.Find();

    private static string StubMenu(ScratchFolder scratch, string body) =>
        scratch.File(body, "project", "scripts", "menu.ps1");

    [Fact]
    public async Task AProjectWithoutAMenuFileIsNoMenu()
    {
        using var scratch = new ScratchFolder();
        string project = scratch.Folder("project");

        ScriptMenuListing listing = await new ScriptMenuReader(@"C:\any\pwsh.exe").ListAsync(project, TestContext.Current.CancellationToken);

        Assert.IsType<ScriptMenuListing.NoMenu>(listing);
    }

    [Fact]
    public async Task AMenuWithNoShellOnTheMachineIsNoShell()
    {
        using var scratch = new ScratchFolder();
        StubMenu(scratch, "# stub");

        ScriptMenuListing listing = await new ScriptMenuReader(null).ListAsync(scratch.Folder("project"), TestContext.Current.CancellationToken);

        Assert.IsType<ScriptMenuListing.NoShell>(listing);
    }

    [Fact(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    public async Task AStubThatPrintsRowsIsListedWithItsVariantsAndItsNonAsciiLabel()
    {
        using var scratch = new ScratchFolder();
        string body = """
            "WARNING: a banner"
            [Console]::WriteLine(("build-run.1", "Build & run", "build-run.ps1", "", "") -join "`t")
            [Console]::WriteLine(("self-check.0", "Contr$([char]0x00F4)le", "self-check.ps1", "", "Debug") -join "`t")
            [Console]::WriteLine(("self-check.0", "Contr$([char]0x00F4)le", "self-check.ps1", "", "Release") -join "`t")
            """;
        StubMenu(scratch, body);

        ScriptMenuListing listing = await new ScriptMenuReader(Shell).ListAsync(scratch.Folder("project"), TestContext.Current.CancellationToken);

        var listed = Assert.IsType<ScriptMenuListing.Listed>(listing);
        Assert.Equal(["build-run.1", "self-check.0"], listed.Entries.Select(e => e.Id));
        Assert.Equal(["Debug", "Release"], listed.Entries[1].Variants);
        Assert.Equal("Contr\u00F4le", listed.Entries[1].Label);
    }

    [Fact(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    public async Task AStubThatExitsOneWithAnErrorIsUnreadableWithItsLastLines()
    {
        using var scratch = new ScratchFolder();
        StubMenu(scratch, "[Console]::Error.WriteLine('first line'); [Console]::Error.WriteLine('the real problem'); exit 1");

        ScriptMenuListing listing = await new ScriptMenuReader(Shell).ListAsync(scratch.Folder("project"), TestContext.Current.CancellationToken);

        var unreadable = Assert.IsType<ScriptMenuListing.Unreadable>(listing);
        Assert.Contains("the real problem", unreadable.LastLines);
    }

    [Fact(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    public async Task AStubThatPrintsNoRowIsUnreadableNotEmpty()
    {
        using var scratch = new ScratchFolder();
        StubMenu(scratch, "'this menu does not know -List'");

        ScriptMenuListing listing = await new ScriptMenuReader(Shell).ListAsync(scratch.Folder("project"), TestContext.Current.CancellationToken);

        Assert.IsType<ScriptMenuListing.Unreadable>(listing);
    }

    [Fact(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    public async Task AStubThatSleepsPastTheLimitIsKilledAndUnreadable()
    {
        using var scratch = new ScratchFolder();
        StubMenu(scratch, "Start-Sleep -Seconds 60");
        var reader = new ScriptMenuReader(Shell, timeout: TimeSpan.FromSeconds(2));

        ScriptMenuListing listing = await reader.ListAsync(scratch.Folder("project"), TestContext.Current.CancellationToken);

        Assert.IsType<ScriptMenuListing.Unreadable>(listing);
    }

    [Fact]
    public void TheLastLinesKeepOnlyTheEndOfALongError()
    {
        string text = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"line {i}")) + "\n\n";

        string tail = ScriptMenuReader.Tail(text);

        Assert.DoesNotContain("line 1" + Environment.NewLine, tail + Environment.NewLine);
        Assert.EndsWith("line 30", tail);
        Assert.Equal(8, tail.Split(Environment.NewLine).Length);
    }
}
