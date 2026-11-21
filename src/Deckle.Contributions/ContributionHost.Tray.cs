namespace Deckle.Contributions;

public sealed partial class ContributionHost
{
    // The tray lines of the composition's windows, in composition order, for
    // the developer menu. A window with no label key has none; a developer-only
    // window has one only while Developer mode is on; a window of a module the
    // person did not install has none. Each line opens its window through Open,
    // so the gates hold when the line is clicked as well.
    public IReadOnlyList<ContributionTrayEntry> TrayEntries()
    {
        var entries = new List<ContributionTrayEntry>();
        foreach (IContribution contribution in _contributions)
        {
            foreach (ContributedWindow window in contribution.Windows)
            {
                if (window.TrayLabelKey is null) continue;
                if (Refusal(window.Name) is not null) continue;

                string name = window.Name;
                entries.Add(new ContributionTrayEntry(name, window.TrayLabelKey, () => Open(name)));
            }
        }
        return entries;
    }
}
