namespace Deckle.Orchestration;

// The fixed cards inside an entry's expander. The expander lists one
// collection (these two, then one MonitorHdrRow per HDR-capable monitor), and
// EntryItemTemplateSelector picks each card's template by type.
public sealed record EntryKindItem(AppEntryRow Row);

public sealed record EntryExecutableItem(AppEntryRow Row);
