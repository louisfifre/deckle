using Deckle.Contributions;
using Deckle.Hosting.SelfCheck;
using Deckle.Vision;

namespace Deckle.Atelier.App;

// What the self-check hands the Atelier walk: the contract's three members,
// plus the run's report and capture and the host that builds the window.
internal sealed class AtelierWalkContext(
    ContributionHost host,
    SelfCheckReport report,
    InterfaceCaptureRun run,
    string outputFolder,
    string? targetView,
    Action<string> step) : IContributionWalkContext
{
    public ContributionHost Host { get; } = host;
    public SelfCheckReport Report { get; } = report;
    public InterfaceCaptureRun Run { get; } = run;
    public string OutputFolder { get; } = outputFolder;
    public string? TargetView { get; } = targetView;

    public void Step(string state) => step(state);

    // The walk of this process reaches its context through the contract.
    public static AtelierWalkContext From(IContributionWalkContext context) =>
        (AtelierWalkContext)context;
}
