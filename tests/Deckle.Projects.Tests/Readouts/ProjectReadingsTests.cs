using Xunit;

namespace Deckle.Projects.Tests;

// The cache keeps the previous reading while a refresh runs, reads at most
// three projects at a time and says when each one finished.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "unit")]
public sealed class ProjectReadingsTests
{
    private static ProjectEntry Project(string id) =>
        new(id, @"D:\sample\" + id, null, null, [], null, DateTimeOffset.UnixEpoch);

    private static ProjectReading Reading(string version) =>
        new(FolderState.Present, RepositoryState.Repository, new VersionReading(version, []), 0, null, false, DateTimeOffset.UnixEpoch);

    // A reader whose reads wait for the test to release them.
    private sealed class GatedReader : IProjectReader
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, TaskCompletionSource<ProjectReading>> _pending = [];
        public int Running { get; private set; }
        public int MostAtOnce { get; private set; }
        public int Started { get; private set; }

        public async Task<ProjectReading> ReadAsync(string projectPath, CancellationToken cancellation)
        {
            TaskCompletionSource<ProjectReading> source;
            lock (_gate)
            {
                Started++;
                Running++;
                MostAtOnce = Math.Max(MostAtOnce, Running);
                source = _pending[projectPath] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            try
            {
                using (cancellation.Register(() => source.TrySetCanceled(cancellation)))
                    return await source.Task;
            }
            finally
            {
                lock (_gate) Running--;
            }
        }

        public void Release(string projectPath, ProjectReading reading)
        {
            TaskCompletionSource<ProjectReading> source;
            lock (_gate) source = _pending[projectPath];
            source.SetResult(reading);
        }

        public bool IsWaiting(string projectPath)
        {
            lock (_gate) return _pending.ContainsKey(projectPath);
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.True(condition(), "the condition did not become true in time");
    }

    [Fact]
    public async Task NothingIsKnownBeforeTheFirstReadAndAnEventSaysWhenOneArrives()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        var changed = new List<string>();
        readings.ReadingChanged += changed.Add;
        ProjectEntry a = Project("a");

        Assert.Null(readings.Get("a"));
        Task refresh = readings.RefreshAsync([a], CancellationToken.None);
        await Eventually(() => reader.IsWaiting(a.Path));
        Assert.Null(readings.Get("a"));

        reader.Release(a.Path, Reading("v1.0.0"));
        await refresh;

        Assert.Equal("v1.0.0", readings.Get("a")!.Version.Tag);
        Assert.Equal(["a"], changed);
    }

    [Fact]
    public async Task ThePreviousReadingStaysWhileARefreshRuns()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        ProjectEntry a = Project("a");
        Task first = readings.RefreshAsync([a], CancellationToken.None);
        await Eventually(() => reader.IsWaiting(a.Path));
        reader.Release(a.Path, Reading("v1.0.0"));
        await first;

        // A second refresh: its read waits, and the old reading is still served.
        Task again = readings.RefreshAsync([a], CancellationToken.None);
        await Eventually(() => reader.Started == 2);

        Assert.Equal("v1.0.0", readings.Get("a")!.Version.Tag);

        reader.Release(a.Path, Reading("v2.0.0"));
        await again;
        Assert.Equal("v2.0.0", readings.Get("a")!.Version.Tag);
    }

    [Fact]
    public async Task AtMostThreeProjectsAreReadAtATime()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        ProjectEntry[] projects = [.. Enumerable.Range(0, 8).Select(i => Project($"p{i}"))];

        Task refresh = readings.RefreshAsync(projects, CancellationToken.None);
        await Eventually(() => reader.Running == 3);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(3, reader.Running);

        // Which three hold the slots first is the pool's choice: release
        // whichever are waiting until all eight are done.
        var released = new HashSet<string>();
        while (released.Count < projects.Length)
        {
            foreach (ProjectEntry project in projects.Where(p => !released.Contains(p.Id) && reader.IsWaiting(p.Path)))
            {
                reader.Release(project.Path, Reading("v1.0.0"));
                released.Add(project.Id);
            }
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        await refresh;

        Assert.Equal(3, reader.MostAtOnce);
        Assert.All(projects, p => Assert.NotNull(readings.Get(p.Id)));
    }

    [Fact]
    public async Task AnEventIsRaisedPerFinishedProject()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        var changed = new List<string>();
        readings.ReadingChanged += id => { lock (changed) changed.Add(id); };
        ProjectEntry[] projects = [Project("a"), Project("b")];
        Task refresh = readings.RefreshAsync(projects, CancellationToken.None);

        await Eventually(() => reader.IsWaiting(projects[0].Path) && reader.IsWaiting(projects[1].Path));
        reader.Release(projects[1].Path, Reading("v2.0.0"));
        await Eventually(() => { lock (changed) return changed.Count == 1; });
        Assert.Equal(["b"], changed);

        reader.Release(projects[0].Path, Reading("v1.0.0"));
        await refresh;
        Assert.Equal(["b", "a"], changed);
    }

    [Fact]
    public async Task TwoRefreshesOfOneProjectAtOnceAreOneRead()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        ProjectEntry a = Project("a");

        Task first = readings.RefreshAsync([a], CancellationToken.None);
        await Eventually(() => reader.IsWaiting(a.Path));
        Task second = readings.RefreshAsync([a], CancellationToken.None);
        reader.Release(a.Path, Reading("v1.0.0"));
        await Task.WhenAll(first, second);

        Assert.Equal(1, reader.Started);
    }

    [Fact]
    public async Task ACancelledRefreshKeepsThePreviousReadingAndRaisesNothing()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        var changed = new List<string>();
        readings.ReadingChanged += changed.Add;
        ProjectEntry a = Project("a");
        using var cancel = new CancellationTokenSource();

        Task refresh = readings.RefreshAsync([a], cancel.Token);
        await Eventually(() => reader.IsWaiting(a.Path));
        cancel.Cancel();
        await refresh;

        Assert.Null(readings.Get("a"));
        Assert.Empty(changed);
    }

    [Fact]
    public async Task ACallerLeavingDoesNotCancelTheReadAnotherCallerWaitsOn()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        ProjectEntry a = Project("a");
        using var leaving = new CancellationTokenSource();

        Task first = readings.RefreshAsync([a], leaving.Token);
        await Eventually(() => reader.IsWaiting(a.Path));
        Task second = readings.RefreshAsync([a], CancellationToken.None);
        leaving.Cancel();
        await first;
        reader.Release(a.Path, Reading("v1.0.0"));
        await second;

        Assert.Equal("v1.0.0", readings.Get("a")!.Version.Tag);
        Assert.Equal(1, reader.Started);
    }

    [Fact]
    public async Task AForgottenProjectHasNoReadingAnyMore()
    {
        var reader = new GatedReader();
        var readings = new ProjectReadings(reader);
        ProjectEntry a = Project("a");
        Task refresh = readings.RefreshAsync([a], CancellationToken.None);
        await Eventually(() => reader.IsWaiting(a.Path));
        reader.Release(a.Path, Reading("v1.0.0"));
        await refresh;

        readings.Forget("a");

        Assert.Null(readings.Get("a"));
    }
}
