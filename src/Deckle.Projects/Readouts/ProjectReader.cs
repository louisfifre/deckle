namespace Deckle.Projects;

// What the readings of a project's folder stand behind: tests of the cache
// give it canned readings, the window gives it the real reader.
public interface IProjectReader
{
    Task<ProjectReading> ReadAsync(string projectPath, CancellationToken cancellation);
}

// Reads a project's folder: its version (the highest Git tag and the manifests'
// versions), the uncommitted files, the remote and whether the script menu is
// there. Three git commands at most, run once per project and refresh, and a
// fourth that lists the files when the folder is a repository.
public sealed class ProjectReader : IProjectReader
{
    private readonly IGit _git;
    private readonly TimeProvider _clock;

    // The reasons git was reported unavailable and not yet answered since: one
    // missing git is one incident, written once, not once per project and
    // refresh (deckle-logging).
    private readonly HashSet<string> _reported = [];

    public ProjectReader(IGit? git = null, TimeProvider? clock = null)
    {
        _git = git ?? new GitCommand();
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ProjectReading> ReadAsync(string projectPath, CancellationToken cancellation)
    {
        if (!Directory.Exists(projectPath)) return ProjectReading.Missing(_clock.GetUtcNow());

        bool hasMenu = File.Exists(Path.Combine(projectPath, "scripts", "menu.ps1"));

        GitResult status = await _git.RunAsync(
            projectPath, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--", "."], cancellation).ConfigureAwait(false);

        if (status.IsNotARepository)
        {
            Answered();
            return Compose(projectPath, RepositoryState.NotARepository, tag: null, uncommitted: null, remote: null, candidates: null, hasMenu);
        }
        if (!status.Succeeded)
        {
            Unavailable(status);
            return Compose(projectPath, RepositoryState.Unreadable, tag: null, uncommitted: null, remote: null, candidates: null, hasMenu,
                gitNotInstalled: status.Failure == GitFailure.NotFound);
        }
        Answered();

        int uncommitted = UncommittedFiles.Count(status.Output);

        // The three other questions do not depend on one another.
        Task<GitResult> tags = _git.RunAsync(projectPath, ["tag", "--list"], cancellation);
        Task<GitResult> remotes = _git.RunAsync(projectPath, ["config", "--get-regexp", @"^remote\..+\.url$"], cancellation);
        Task<GitResult> files = _git.RunAsync(
            projectPath, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], cancellation);
        await Task.WhenAll(tags, remotes, files).ConfigureAwait(false);

        string? tag = tags.Result.Succeeded
            ? GitTags.Highest(tags.Result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : null;
        string? remoteUrl = remotes.Result.Succeeded ? GitRemote.PickUrl(remotes.Result.Output) : null;
        IReadOnlyList<string>? candidates = files.Result.Succeeded
            ? ManifestVersions.Candidates(files.Result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            : null;

        return Compose(projectPath, RepositoryState.Repository, tag, uncommitted, GitRemote.Parse(remoteUrl), candidates, hasMenu);
    }

    private ProjectReading Compose(
        string projectPath, RepositoryState state, string? tag, int? uncommitted, GitRemote? remote,
        IReadOnlyList<string>? candidates, bool hasMenu, bool gitNotInstalled = false)
    {
        // The manifests are read from the files git listed; with no listing
        // (not a repository, git unreadable) the bounded walk stands in.
        ManifestScan scan = ManifestVersions.Read(projectPath, candidates ?? ManifestVersions.Walk(projectPath));

        var sources = new List<VersionSource>();
        if (tag is not null) sources.Add(new VersionSource(VersionSource.GitTagLabel, GitTags.WithoutV(tag)));
        sources.AddRange(scan.Sources);

        return new ProjectReading(
            FolderState.Present, state, new VersionReading(tag, sources, scan.More),
            uncommitted, remote, hasMenu, _clock.GetUtcNow(), gitNotInstalled);
    }

    private void Answered()
    {
        lock (_reported) _reported.Clear();
    }

    private void Unavailable(GitResult result)
    {
        string reason = result.Failure switch
        {
            GitFailure.NotFound => "not-found",
            GitFailure.TimedOut => "timeout",
            GitFailure.DubiousOwnership => "dubious-ownership",
            _ => "exit-code",
        };
        bool first;
        lock (_reported) first = _reported.Add(reason);
        if (first) DeckleProjectsSource.Log.GitUnavailable(reason);
    }
}
