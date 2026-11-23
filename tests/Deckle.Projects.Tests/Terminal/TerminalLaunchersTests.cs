using System.Diagnostics.Tracing;
using System.Text;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Projects.Tests;

// The launchers with a process starter that starts nothing: the command lines
// they build, and what they do when wt.exe is missing.
[Collection("projects provider singleton")]
[Trait("Category", "unit")]
public sealed class TerminalLaunchersTests
{
    private const string Shell = @"C:\Program Files\PowerShell\7\pwsh.exe";

    private static TerminalCommand Command(string script = "Write-Host 'hi'; exit 3") =>
        new(@"C:\sample\alpha", "Build & run \u00B7 alpha (1a2b)", Shell, script);

    [Fact]
    public void TheWindowsTerminalArgumentListIsTheMeasuredOne()
    {
        string script = "Write-Host 'a;b'; exit 3";

        IReadOnlyList<string> arguments = WindowsTerminalLauncher.Arguments(Command(script));

        Assert.Equal(
            ["-w", "new", "--title", "Build & run \u00B7 alpha (1a2b)", "--suppressApplicationTitle",
             "-d", @"C:\sample\alpha", Shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", RunWrapper.Encode(script)],
            arguments);
    }

    [Fact]
    public void NothingInTheCommandReachesWtAsASemicolon()
    {
        var command = new TerminalCommand(@"C:\a;b", "Title; with; semicolons", Shell, "a; b; c");

        IReadOnlyList<string> arguments = WindowsTerminalLauncher.Arguments(command);

        // The script crosses as base64; the title and the folder are escaped.
        Assert.DoesNotContain(';', arguments[^1]);
        Assert.Equal("Title, with, semicolons", arguments[arguments.ToList().IndexOf("--title") + 1]);
        Assert.Equal(@"C:\a\;b", arguments[arguments.ToList().IndexOf("-d") + 1]);
    }

    [Fact]
    public void ATerminalWithNoCommandIsOnlyTheFolder()
    {
        IReadOnlyList<string> arguments = WindowsTerminalLauncher.Arguments(new TerminalCommand(@"C:\a", "Terminal"));

        Assert.Equal(["-w", "new", "--title", "Terminal", "--suppressApplicationTitle", "-d", @"C:\a"], arguments);
    }

    [Fact]
    public void WindowsTerminalPresentIsStartedAndNotAFallback()
    {
        var starter = new FakeProcessStarter();

        TerminalStart start = new WindowsTerminalLauncher(starter).Launch(Command());

        Assert.True(start.Started);
        Assert.False(start.UsedFallback);
        Assert.Equal(TerminalStart.WindowsTerminal, start.Terminal);
        Assert.Equal("wt.exe", Assert.Single(starter.Started).FileName);
    }

    [Fact]
    public void WtMissingFallsBackToTheConsoleAndSaysSo()
    {
        var starter = new FakeProcessStarter();
        starter.Missing.Add("wt.exe");
        ITerminalLauncher launcher = TerminalLaunchers.Create(TerminalChoice.WindowsTerminal, starter);

        TerminalStart start = launcher.Launch(Command());

        Assert.True(start.Started);
        Assert.True(start.UsedFallback);
        Assert.Equal(TerminalStart.Console, start.Terminal);
        var started = Assert.Single(starter.Started);
        Assert.Equal(Shell, started.FileName);
        Assert.True(started.UseShellExecute);
        Assert.Equal(@"C:\sample\alpha", started.WorkingDirectory);
        Assert.StartsWith("-NoProfile -ExecutionPolicy Bypass -EncodedCommand ", started.Arguments);
    }

    [Fact]
    public void WtPresentDoesNotFallBack()
    {
        var starter = new FakeProcessStarter();
        ITerminalLauncher launcher = TerminalLaunchers.Create(TerminalChoice.WindowsTerminal, starter);

        TerminalStart start = launcher.Launch(Command());

        Assert.False(start.UsedFallback);
        Assert.Equal(TerminalStart.WindowsTerminal, start.Terminal);
    }

    [Fact]
    public void TheConsoleChosenByThePersonIsNotAFallback()
    {
        var starter = new FakeProcessStarter();
        ITerminalLauncher launcher = TerminalLaunchers.Create(TerminalChoice.WindowsDefault, starter);

        TerminalStart start = launcher.Launch(Command());

        Assert.True(start.Started);
        Assert.False(start.UsedFallback);
        Assert.DoesNotContain(starter.Started, s => s.FileName == "wt.exe");
    }

    [Fact]
    public void WhenNeitherTerminalStartsTheAnswerIsAFailure()
    {
        var starter = new FakeProcessStarter();
        starter.Missing.Add("wt.exe");
        starter.Missing.Add(Shell);
        ITerminalLauncher launcher = TerminalLaunchers.Create(TerminalChoice.WindowsTerminal, starter);

        TerminalStart start = launcher.Launch(Command());

        Assert.False(start.Started);
        Assert.NotNull(start.FailureReason);
    }

    [Fact]
    public void TheConsoleLauncherWithNoShellOpensWindowsPowerShell()
    {
        var starter = new FakeProcessStarter();

        new DefaultConsoleLauncher(starter).Launch(new TerminalCommand(@"C:\a", "Terminal"));

        var started = Assert.Single(starter.Started);
        Assert.Equal("powershell.exe", started.FileName);
        Assert.Equal("", started.Arguments);
    }

    [Fact]
    public void ConsoleArgumentsDecodeToTheScript()
    {
        var starter = new FakeProcessStarter();
        string script = "Write-Host 'x'";

        new DefaultConsoleLauncher(starter).Launch(Command(script));

        string encoded = starter.Started.Single().Arguments![
            "-NoProfile -ExecutionPolicy Bypass -EncodedCommand ".Length..];
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void AFallbackIsOneWarningNamingWhatWasAskedAndWhatWasObtained()
    {
        using var listener = new TestEventListener("Deckle-Projects");
        var starter = new FakeProcessStarter();
        starter.Missing.Add("wt.exe");

        TerminalLaunchers.Create(TerminalChoice.WindowsTerminal, starter).Launch(Command());

        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtTerminalFallbackUsed);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.Equal(TerminalStart.WindowsTerminal, warning.Payload?[0]);
        Assert.Equal(TerminalStart.Console, warning.Payload?[1]);
    }
}

[Trait("Category", "integration")]
public sealed class TerminalChoiceFileTests
{
    [Fact]
    public void AnAbsentFileMeansWindowsTerminal()
    {
        using var scratch = new ScratchFolder();

        Assert.Equal(TerminalChoice.WindowsTerminal, TerminalChoiceFile.Read(scratch.Folder("data")));
    }

    [Fact]
    public void AChoiceWrittenIsReadBack()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Path + @"\data";

        TerminalChoiceFile.Write(data, TerminalChoice.WindowsDefault);

        Assert.Equal(TerminalChoice.WindowsDefault, TerminalChoiceFile.Read(data));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"Terminal\": \"Alacritty\" }")]
    [InlineData("{ \"Terminal\": 7 }")]
    [InlineData("")]
    public void AFileThatCannotBeReadOrNamesAnUnknownTerminalMeansWindowsTerminal(string content)
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        File.WriteAllText(Path.Combine(data, TerminalChoiceFile.FileName), content);

        Assert.Equal(TerminalChoice.WindowsTerminal, TerminalChoiceFile.Read(data));
    }
}
