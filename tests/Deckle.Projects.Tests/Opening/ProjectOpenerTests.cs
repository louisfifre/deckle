using Xunit;

namespace Deckle.Projects.Tests;

// The links with a process starter and a terminal that start nothing.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class ProjectOpenerTests
{
    private const string Shell = @"C:\fake\pwsh.exe";

    private static (ProjectOpener Opener, FakeProcessStarter Starter, FakeTerminalLauncher Terminal) New(string? shell = Shell)
    {
        var starter = new FakeProcessStarter();
        var terminal = new FakeTerminalLauncher();
        return (new ProjectOpener(terminal, shell, starter), starter, terminal);
    }

    [Fact]
    public void ExplorerOpensTheFolderThroughTheShell()
    {
        using var scratch = new ScratchFolder();
        var (opener, starter, _) = New();
        string folder = scratch.Folder("alpha");

        Assert.True(opener.OpenInExplorer(folder));

        var started = Assert.Single(starter.Started);
        Assert.Equal(folder, started.FileName);
        Assert.True(started.UseShellExecute);
    }

    [Fact]
    public void AFolderThatIsGoneOpensNothingAndSaysSo()
    {
        using var scratch = new ScratchFolder();
        var (opener, starter, terminal) = New();
        string gone = Path.Combine(scratch.Path, "gone");

        Assert.False(opener.OpenInExplorer(gone));
        Assert.False(opener.OpenInTerminal(gone));
        Assert.False(opener.OpenScriptMenu(gone));
        Assert.Empty(starter.Started);
        Assert.Empty(terminal.Commands);
    }

    [Fact]
    public void TheTerminalLinkOpensTheAppsTerminalInTheFolderWithNothingRunning()
    {
        using var scratch = new ScratchFolder();
        var (opener, _, terminal) = New();
        string folder = scratch.Folder("alpha");

        Assert.True(opener.OpenInTerminal(folder));

        TerminalCommand command = Assert.Single(terminal.Commands);
        Assert.Equal(folder, command.WorkingDirectory);
        Assert.Null(command.ScriptText);
    }

    [Fact]
    public void TheScriptMenuOpensInATerminalRunningTheMenuAndNotAnAction()
    {
        using var scratch = new ScratchFolder();
        var (opener, _, terminal) = New();
        scratch.File("# stub", "alpha", "scripts", "menu.ps1");

        Assert.True(opener.OpenScriptMenu(scratch.Folder("alpha")));

        TerminalCommand command = Assert.Single(terminal.Commands);
        Assert.Contains("scripts", command.ScriptText);
        Assert.DoesNotContain("-Run", command.ScriptText);
        Assert.DoesNotContain("started.txt", command.ScriptText);
    }

    [Fact]
    public void TheScriptMenuOfAProjectWithoutOneOrWithoutAShellIsNotOpened()
    {
        using var scratch = new ScratchFolder();
        scratch.File("# stub", "withmenu", "scripts", "menu.ps1");
        var (opener, _, terminal) = New();
        var (noShell, _, _) = New(shell: null);

        Assert.False(opener.OpenScriptMenu(scratch.Folder("nomenu")));
        Assert.False(noShell.OpenScriptMenu(scratch.Folder("withmenu")));
        Assert.Empty(terminal.Commands);
    }

    [Fact]
    public void AnHttpsPageOpensAndAnythingElseDoesNot()
    {
        var (opener, starter, _) = New();

        Assert.True(opener.OpenWebPage(new Uri("https://github.com/owner/repo")));
        Assert.False(opener.OpenWebPage(new Uri("http://github.com/owner/repo")));
        Assert.False(opener.OpenWebPage(new Uri("file:///c:/windows/system32/calc.exe")));

        Assert.Equal("https://github.com/owner/repo", Assert.Single(starter.Started).FileName);
    }

    [Fact]
    public void ATerminalThatDoesNotStartMakesTheLinkFail()
    {
        using var scratch = new ScratchFolder();
        var (opener, _, terminal) = New();
        terminal.Answer = TerminalStart.Failed(TerminalStart.Console, "start-failed");

        Assert.False(opener.OpenInTerminal(scratch.Folder("alpha")));
    }
}
