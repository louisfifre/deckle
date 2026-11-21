using Deckle.Modules;
using Microsoft.UI.Xaml;

namespace Deckle.Contributions;

// ── ContributionHost ──────────────────────────────────────────────────────────
//
// Turns an executable's composition into what the executable shows: the
// descriptors for its module catalogue, one lazy window per contributed
// window, the developer tray entries, the names the interface capture knows
// and the walks the self-check runs. It holds the rules and none of the
// plumbing: the window mechanics of the application come in through
// IContributionHostServices.
//
// The windows of the composition are handled the way the application handles
// its own: built on first use through the logging constructor, placed where the
// person left them, given the theme and the recording state, and dropped when
// closed so the next open builds a fresh one. UI thread only, like the
// windows themselves.
//
// A composition that breaks a rule (duplicate id, window name already taken,
// a loop in DependsOn) is refused at construction, before any window exists.
public sealed partial class ContributionHost
{
    private readonly IReadOnlyList<IContribution> _contributions;
    private readonly IContributionHostServices _services;
    private readonly Dictionary<string, WindowSlot> _slots = new(StringComparer.Ordinal);

    private bool _lastRecordingState;

    // reservedWindowNames: the names the executable's own windows use, so a
    // contribution cannot take one ("settings", "log").
    public ContributionHost(
        IReadOnlyList<IContribution> contributions,
        IContributionHostServices services,
        IEnumerable<string>? reservedWindowNames = null)
    {
        _contributions = contributions;
        _services = services;

        var reserved = new HashSet<string>(reservedWindowNames ?? [], StringComparer.Ordinal);
        ContributionValidation.Check(contributions, reserved);

        foreach (IContribution contribution in contributions)
        {
            foreach (ContributedWindow window in contribution.Windows)
                _slots.Add(window.Name, new WindowSlot(contribution, window));
            DeckleContributionsSource.Log.ContributionRegistered(
                contribution.Id, contribution.Windows.Count, contribution.SelfCheckWalks.Count);
        }
    }

    // The descriptors of the composition's modules, in selector order, for
    // the host's catalogue. A contribution without a descriptor is not a
    // module and adds none.
    public IReadOnlyList<ModuleDescriptor> Descriptors =>
        _contributions.Select(c => c.Descriptor).OfType<ModuleDescriptor>().OrderBy(d => d.Order).ToList();

    // Plants every contribution's start hooks, once, in composition order.
    // A contribution whose hooks throw runs without them: the host starts.
    public void Compose()
    {
        foreach (IContribution contribution in _contributions)
        {
            try
            {
                contribution.Compose(_services);
            }
            catch (Exception ex)
            {
                DeckleContributionsSource.Log.ComposeFailed(contribution.Id, ex.GetType().Name);
            }
        }
    }

    // The names of every contributed window, in composition order: what the
    // interface capture and the self-check target list add to the host's own.
    public IReadOnlyList<string> WindowNames =>
        _contributions.SelectMany(c => c.Windows).Select(w => w.Name).ToList();

    // The walks of the composition, in composition order (the contributions as
    // listed, each one's walks as it lists them): the order the self-check
    // adds them after the host's own windows.
    public IReadOnlyList<ContributedWalk> Walks =>
        _contributions.SelectMany(c => c.SelfCheckWalks).ToList();

    // The views a window's walk shows, or empty when the window has none or
    // is unknown: the `--only` target list.
    public IReadOnlyList<string> ViewsOf(string windowName) =>
        Walks.FirstOrDefault(w => w.WindowName == windowName)?.Views ?? [];

    private sealed class WindowSlot(IContribution owner, ContributedWindow definition)
    {
        public IContribution Owner { get; } = owner;
        public ContributedWindow Definition { get; } = definition;
        public Window? Open { get; set; }
    }
}
