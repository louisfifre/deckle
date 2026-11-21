using Deckle.Modules;

namespace Deckle.Contributions;

// ── IContribution ─────────────────────────────────────────────────────────────
//
// What one module adds to an executable: the windows it opens, the walks the
// self-check makes of them, and the hooks it plants at start. An executable's
// composition (a Composition.cs listing its contributions) is the one place
// that says which modules it carries, so a module is taken out of an
// executable by leaving the list, and the host names none of them.
//
// This serves composition at build time, not plugins at run time: WinUI loads
// compiled XAML only from an assembly the host referenced when it was built
// (microsoft-ui-xaml#6299).
public interface IContribution
{
    // Lowercase ASCII, no spaces: a ModuleIds value for a module, and the
    // name of its data folder. Unique within a composition.
    string Id { get; }

    // The module's place in the presence catalogue (glyph, order, DependsOn),
    // moved here from the host's own catalogue. Null for a surface that is
    // not a module yet, such as a developer preview with no setting of its
    // own: its windows are then gated by developer mode alone.
    ModuleDescriptor? Descriptor { get; }

    IReadOnlyList<ContributedWindow> Windows { get; }

    IReadOnlyList<ContributedWalk> SelfCheckWalks { get; }

    // Start-time hooks, called once after registration: what the module needs
    // to plant in the rest of the application (a setting's store, a link the
    // Projects window follows). A hook that throws costs this contribution its
    // hooks, never the host its start.
    void Compose(IContributionContext context);
}
