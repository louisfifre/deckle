namespace Deckle.Contributions;

// A tray menu line a contribution asks for, free of the tray's own types so
// this project stays below it: the executable turns it into its menu command.
public sealed record ContributionTrayEntry(string Id, string LabelKey, Action Open);
