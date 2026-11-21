using Deckle.Contributions;

namespace Deckle.Mail.App;

// The one place that says which modules this executable carries
// (Deckle.Contributions): Deckle Mail carries Mail, and nothing else.
internal static class Composition
{
    public static IReadOnlyList<IContribution> All { get; } =
    [
        new MailContribution(),
    ];

    // The walks of the composition, as the host lists them: read by the
    // self-check target, which parses before any window or host exists.
    public static IReadOnlyList<ContributedWalk> Walks { get; } =
        [.. All.SelectMany(c => c.SelfCheckWalks)];
}
