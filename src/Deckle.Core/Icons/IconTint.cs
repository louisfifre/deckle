namespace Deckle.Core;

// The colours an IconChoice can take: five tiles, Windows' accent colour first
// (specs/folders.md, Open 13, as drawn). The four others are taken from the
// accent swatches Windows Settings offers; their values, one per theme, sit
// in Deckle.Catalog's IconTintPalette.
//
// Persisted by name, like IconSymbol.
public enum IconTint
{
    Accent,
    Red,
    Orange,
    Green,
    Purple,
}
