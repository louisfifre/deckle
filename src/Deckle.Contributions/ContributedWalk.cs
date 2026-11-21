namespace Deckle.Contributions;

// The self-check's walk of one contributed window: the states it photographs
// and how it reaches them. The host runs the walks of a composition in order,
// after its own windows' (ContributionHost.Walks).
public sealed record ContributedWalk
{
    // The window the walk photographs: the Name of a ContributedWindow of the
    // same contribution, and the window part of a `--only` target.
    public required string WindowName { get; init; }

    // The views the walk shows, the view part of a `--only` target
    // ("mail/activity"); empty for a window of one view.
    public IReadOnlyList<string> Views { get; init; } = [];

    // True for a window whose views are only known once the walk runs (the
    // states it reaches depend on the data of the run): `window/<view>` is then
    // a valid target although Views lists none.
    public bool ShowsViewsAtRun { get; init; }

    public bool HasViews => ShowsViewsAtRun || Views.Count > 0;

    public required Func<IContributionWalkContext, Task> Run { get; init; }
}

// What a walk asks of the run that calls it, kept to what every walk needs.
// The self-check harness implements it; a walk reaches its own window through
// the contribution that owns it.
public interface IContributionWalkContext
{
    // Where the run writes its images and report.
    string OutputFolder { get; }

    // The view a targeted run asks for, or null for a full run.
    string? TargetView { get; }

    // Announces the state the walk is about to reach ("folders/files").
    void Step(string state);
}
