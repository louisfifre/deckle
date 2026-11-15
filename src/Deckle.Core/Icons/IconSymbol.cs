namespace Deckle.Core;

// The symbols an IconChoice can show, in the order the picker lays them out.
// The set stays small, from two rows of four to three rows of six (specs/
// atelier.md, Window): twelve, two rows of six.
//
// Persisted by name: a member is never renamed, and a new one is added at the
// end of a row, never in place of another.
public enum IconSymbol
{
    Folder,
    Photos,
    Documents,
    Code,
    Music,
    Videos,
    Downloads,
    Archive,
    Work,
    Home,
    People,
    Favorite,
}
