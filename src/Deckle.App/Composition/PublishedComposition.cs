using Deckle.Contributions;

namespace Deckle.App;

// The one place that says which contributed modules this executable carries
// (Deckle.Contributions): a module leaves the executable by leaving this list.
// The host reads it for the module catalogue, the windows, the developer tray,
// the interface capture and the self-check, and names none of them.
//
// Mail and the Atelier are still carried while their own apps do not exist
// yet; the order is the order of their walks in the self-check.
internal static class PublishedComposition
{
    public static IReadOnlyList<IContribution> All { get; } =
    [
        new MailContribution(),
        new AtelierContribution(),
    ];

    // The walks of the composition in composition order, as the host lists
    // them: read here too by the self-check target, which parses before any
    // window or host exists.
    public static IReadOnlyList<ContributedWalk> Walks { get; } =
        [.. All.SelectMany(c => c.SelfCheckWalks)];
}
