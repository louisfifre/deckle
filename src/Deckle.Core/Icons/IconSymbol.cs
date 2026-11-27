namespace Deckle.Core;

// The symbols an IconChoice can show, in the order the picker lays them out,
// six to a row (as drawn for specs/folders/deckle-folders.md): twenty-four, four rows. The
// first two rows say what a folder holds; the last two, added for sorting
// work (Louis, 2026-11-21), say what the person does with it: development on
// the third row, marks on the fourth.
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

    Instructions,
    Intelligence,
    Terminal,
    Settings,
    Bug,
    Test,

    Important,
    Flag,
    Tag,
    Idea,
    Private,
    Cloud,
}
