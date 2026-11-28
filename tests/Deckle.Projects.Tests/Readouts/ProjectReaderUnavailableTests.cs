using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Projects.Tests;

// When git cannot answer, the project reads as "not readable" and the page
// shows nothing for git; the Warning is one incident, not one per project.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "observability")]
public sealed class ProjectReaderUnavailableTests
{
    private sealed class FixedGit(GitResult result) : IGit
    {
        public int Calls { get; private set; }

        public Task<GitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellation)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static GitResult Failed(GitFailure failure, int code = -1, string error = "") => new(code, "", error, failure);

    [Theory]
    [InlineData(GitFailure.NotFound, "not-found")]
    [InlineData(GitFailure.TimedOut, "timeout")]
    [InlineData(GitFailure.DubiousOwnership, "dubious-ownership")]
    public async Task GitThatCannotAnswerGivesUnreadableAndOneWarningWithItsReason(GitFailure failure, string reason)
    {
        using var scratch = new ScratchFolder();
        using var listener = new TestEventListener("Deckle-Projects");
        var reader = new ProjectReader(new FixedGit(Failed(failure)));

        ProjectReading reading = await reader.ReadAsync(scratch.Folder("p"), CancellationToken.None);

        Assert.Equal(RepositoryState.Unreadable, reading.Git);
        Assert.Null(reading.UncommittedFiles);
        Assert.Null(reading.Version.Tag);
        Assert.Equal(FolderState.Present, reading.Folder);
        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtGitUnavailable);
        Assert.Equal(reason, warning.Payload?[0]);
    }

    [Theory]
    [InlineData(GitFailure.NotFound, true)]
    [InlineData(GitFailure.TimedOut, false)]
    [InlineData(GitFailure.DubiousOwnership, false)]
    public async Task OnlyAGitThatIsNotInstalledIsReadAsNotInstalled(GitFailure failure, bool notInstalled)
    {
        using var scratch = new ScratchFolder();
        var reader = new ProjectReader(new FixedGit(Failed(failure)));

        ProjectReading reading = await reader.ReadAsync(scratch.Folder("p"), CancellationToken.None);

        Assert.Equal(notInstalled, reading.GitNotInstalled);
    }

    [Fact]
    public async Task ManyProjectsAndRefreshesWithTheSameFailureAreOneWarning()
    {
        using var scratch = new ScratchFolder();
        using var listener = new TestEventListener("Deckle-Projects");
        var reader = new ProjectReader(new FixedGit(Failed(GitFailure.NotFound)));

        for (int i = 0; i < 5; i++) await reader.ReadAsync(scratch.Folder($"p{i}"), CancellationToken.None);

        Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtGitUnavailable);
    }

    [Fact]
    public async Task AnAnswerClosesTheIncidentSoTheNextFailureIsWrittenAgain()
    {
        using var scratch = new ScratchFolder();
        using var listener = new TestEventListener("Deckle-Projects");
        var down = new FixedGit(Failed(GitFailure.NotFound));
        var up = new FixedGit(new GitResult(0, "", "", GitFailure.None));
        string folder = scratch.Folder("p");

        var swappable = new SwitchableGit(down);
        var reader = new ProjectReader(swappable);
        await reader.ReadAsync(folder, CancellationToken.None);
        swappable.Inner = up;
        await reader.ReadAsync(folder, CancellationToken.None);
        swappable.Inner = down;
        await reader.ReadAsync(folder, CancellationToken.None);

        Assert.Equal(2, listener.Events.Count(e => e.EventId == DeckleProjectsSource.EvtGitUnavailable));
    }

    private sealed class SwitchableGit(IGit inner) : IGit
    {
        public IGit Inner { get; set; } = inner;

        public Task<GitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellation) =>
            Inner.RunAsync(directory, arguments, cancellation);
    }

    [Fact]
    public async Task ANonZeroExitThatIsNotAMissingRepositoryIsUnreadableWithTheExitCodeReason()
    {
        using var scratch = new ScratchFolder();
        using var listener = new TestEventListener("Deckle-Projects");
        var reader = new ProjectReader(new FixedGit(new GitResult(1, "", "fatal: something else", GitFailure.None)));

        ProjectReading reading = await reader.ReadAsync(scratch.Folder("p"), CancellationToken.None);

        Assert.Equal(RepositoryState.Unreadable, reading.Git);
        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtGitUnavailable);
        Assert.Equal("exit-code", warning.Payload?[0]);
    }

    [Fact]
    public async Task AFolderGitCallsNotARepositoryReadsAsSuchWithoutAWarning()
    {
        using var scratch = new ScratchFolder();
        using var listener = new TestEventListener("Deckle-Projects");
        var reader = new ProjectReader(new FixedGit(new GitResult(128, "", "fatal: not a git repository (or any of the parent directories): .git", GitFailure.None)));

        ProjectReading reading = await reader.ReadAsync(scratch.Folder("p"), CancellationToken.None);

        Assert.Equal(RepositoryState.NotARepository, reading.Git);
        Assert.DoesNotContain(listener.Events, e => e.Level == EventLevel.Warning);
    }
}
