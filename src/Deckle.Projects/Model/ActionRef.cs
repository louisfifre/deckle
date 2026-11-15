namespace Deckle.Projects;

// One entry of a project's script menu, as `scripts/menu.ps1 -List` names it.
// Id is the first column (self-check.0, build-run.1); Variant is the last one
// (Debug, patch) and is null for an entry that has none.
//
// A record, so two references compare by value and the comparison is ordinal:
// an id is a script's name and "Self-check" and "self-check" are two entries
// to a menu that matches them literally.
public sealed record ActionRef(string Id, string? Variant = null);
