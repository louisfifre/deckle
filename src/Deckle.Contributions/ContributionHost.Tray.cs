using static Deckle.Contributions.DeckleContributionsSource;

namespace Deckle.Contributions;

public sealed partial class ContributionHost
{
    // The tray lines of the composition's windows, in composition order, for
    // the developer menu. A window with no label key has none; a developer-only
    // window has one only while Developer mode is on; a window of a module the
    // person did not install has none. Each line opens its window through Open,
    // so the gates hold when the line is clicked as well.
    //
    // forDeveloperMenu: the menu is itself the developer door and is composed
    // once, whatever Developer mode says at that moment, so a developer-only
    // window is listed; its click still refuses while the mode is off.
    public IReadOnlyList<ContributionTrayEntry> TrayEntries(bool forDeveloperMenu = false)
    {
        var entries = new List<ContributionTrayEntry>();
        foreach (IContribution contribution in _contributions)
        {
            foreach (ContributedWindow window in contribution.Windows)
            {
                if (window.TrayLabelKey is null) continue;
                string? refusal = Refusal(window.Name);
                if (refusal is not null
                    && !(forDeveloperMenu && refusal == WindowRefusals.DeveloperModeOff))
                    continue;

                string name = window.Name;
                entries.Add(new ContributionTrayEntry(name, window.TrayLabelKey, () => Open(name)));
            }
        }
        return entries;
    }
}
