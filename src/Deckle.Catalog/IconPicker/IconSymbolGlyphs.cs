using Deckle.Core;

namespace Deckle.Catalog;

// The Segoe Fluent Icons glyph of each IconSymbol, written as escapes so the
// code reads without the font. Every symbol of the set has one: a symbol
// without a glyph would draw an empty tile.
internal static class IconSymbolGlyphs
{
    public static string Of(IconSymbol symbol) => symbol switch
    {
        IconSymbol.Photos => "",     // Pictures
        IconSymbol.Documents => "",  // Document
        IconSymbol.Code => "",       // Code
        IconSymbol.Music => "",      // Audio
        IconSymbol.Videos => "",     // Video
        IconSymbol.Downloads => "",  // Download
        IconSymbol.Archive => "",    // Package
        IconSymbol.Work => "",       // Work
        IconSymbol.Home => "",       // Home
        IconSymbol.People => "",     // People
        IconSymbol.Favorite => "",   // FavoriteStar
        _ => "",                     // Folder
    };
}
