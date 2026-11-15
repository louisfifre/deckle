namespace Deckle.Projects;

// One entry of a project's script menu: the rows `menu.ps1 -List` prints for
// one id, grouped. Variants keeps the order of the listing, so its first value
// is the one `-Run <id>` takes without `-Variant` and the menu's own default.
// An entry with no variant has an empty list.
//
// Script is the file name under the project's scripts folder; Arguments are
// what the menu passes it (shown nowhere, kept because the listing carries
// them and a test reads them).
public sealed record ScriptMenuEntry(
    string Id,
    string Label,
    string Script,
    string Arguments,
    IReadOnlyList<string> Variants)
{
    // True when this entry still gives the pinned action: the same id, and for
    // a pin made with a variant, a variant that is still listed. A pin made
    // without one is given by the id alone: `-Run` takes the default.
    public bool Gives(ActionRef action) =>
        string.Equals(Id, action.Id, StringComparison.Ordinal)
        && (action.Variant is null || Variants.Contains(action.Variant, StringComparer.Ordinal));
}
