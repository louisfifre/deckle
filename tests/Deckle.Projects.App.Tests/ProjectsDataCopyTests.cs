using Xunit;

namespace Deckle.Projects.App.Tests;

// The one-time copy of the Projects data the published Deckle kept. Each test
// builds a suite root in a temp folder and reads what the copy left.
[Trait("Category", "unit")]
public sealed class ProjectsDataCopyTests : IDisposable
{
    private readonly string _suite = Path.Combine(Path.GetTempPath(), "deckle-projects-copy-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_suite)) Directory.Delete(_suite, recursive: true);
    }

    private string Old(params string[] rest) => Path.Combine([_suite, "modules", "projects", .. rest]);

    private string New(params string[] rest) => Path.Combine([_suite, "apps", "projects", "modules", "projects", .. rest]);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void AnEmptySuiteRootHasNothingToCopy()
    {
        Directory.CreateDirectory(_suite);

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.NothingToCopy, result.Outcome);
        Assert.False(Directory.Exists(New()));
    }

    [Fact]
    public void TheProjectsAndTheTerminalChoiceAreCopiedAndTheOldFolderStays()
    {
        Write(Old("projects.json"), "projects");
        Write(Old("terminal.json"), "terminal");

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.Copied, result.Outcome);
        Assert.Equal(2, result.Files);
        Assert.Equal("projects", File.ReadAllText(New("projects.json")));
        Assert.Equal("terminal", File.ReadAllText(New("terminal.json")));
        // The running published Deckle still reads them.
        Assert.Equal("projects", File.ReadAllText(Old("projects.json")));
        Assert.False(Directory.Exists(New() + ".copying"));
    }

    [Fact]
    public void TheRunsOfThePublishedDeckleAreNotCopied()
    {
        Write(Old("projects.json"), "projects");
        Write(Old("runs", "a1", "exit.txt"), "0");

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.Copied, result.Outcome);
        Assert.False(Directory.Exists(New("runs")));
    }

    [Fact]
    public void AnAppThatAlreadyHasItsDataIsNotOverwritten()
    {
        Write(Old("projects.json"), "old");
        Write(New("projects.json"), "mine");

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.AlreadyHasData, result.Outcome);
        Assert.Equal("mine", File.ReadAllText(New("projects.json")));
    }

    [Fact]
    public void TheCopyHappensOnce()
    {
        Write(Old("projects.json"), "first");
        ProjectsDataCopy.Run(_suite);
        File.WriteAllText(Old("projects.json"), "changed later in the published Deckle");

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.AlreadyHasData, result.Outcome);
        Assert.Equal("first", File.ReadAllText(New("projects.json")));
    }

    [Fact]
    public void AnOldFolderWithNoFileHasNothingToCopy()
    {
        Directory.CreateDirectory(Old("runs"));

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.NothingToCopy, result.Outcome);
        Assert.False(Directory.Exists(New()));
    }

    [Fact]
    public void ACopyLeftHalfDoneByAnEarlierStartIsStartedAgain()
    {
        Write(Old("projects.json"), "projects");
        Write(Path.Combine(New() + ".copying", "projects.json"), "half");

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.Copied, result.Outcome);
        Assert.Equal("projects", File.ReadAllText(New("projects.json")));
    }

    [Fact]
    public void AFileHeldOpenFailsTheCopyAndLeavesNoModuleFolder()
    {
        Write(Old("projects.json"), "projects");
        using var held = new FileStream(Old("projects.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ProjectsDataCopyResult result = ProjectsDataCopy.Run(_suite);

        Assert.Equal(ProjectsDataCopyOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.False(Directory.Exists(New()));
        Assert.False(Directory.Exists(New() + ".copying"));
    }
}
