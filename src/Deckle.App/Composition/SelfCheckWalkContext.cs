using Deckle.App.SelfCheck;
using Deckle.Hosting.SelfCheck;
using Deckle.Contributions;
using Deckle.Vision;

namespace Deckle.App;

// What the self-check hands a contributed walk: the contract's three members,
// plus the run's report and capture, which the walks of the host's own
// assembly use directly. A walk that moves to its own application takes these
// from the shared harness instead.
internal sealed class SelfCheckWalkContext(
    App app,
    SelfCheckReport report,
    InterfaceCaptureRun run,
    string outputFolder,
    string? targetView,
    Action<string> step) : IContributionWalkContext
{
    public App App { get; } = app;
    public SelfCheckReport Report { get; } = report;
    public InterfaceCaptureRun Run { get; } = run;
    public string OutputFolder { get; } = outputFolder;
    public string? TargetView { get; } = targetView;

    public void Step(string state) => step(state);

    // The walks of this process reach their context through the contract.
    public static SelfCheckWalkContext From(IContributionWalkContext context) =>
        (SelfCheckWalkContext)context;
}
