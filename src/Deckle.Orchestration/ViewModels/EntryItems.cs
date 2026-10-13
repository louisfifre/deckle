namespace Deckle.Orchestration;

// The fixed cards inside an entry's expander; EntryItemTemplateSelector picks
// each card's template by type.
public sealed record EntryKindItem(AppEntryRow Row);

public sealed record EntryExecutableItem(AppEntryRow Row);
