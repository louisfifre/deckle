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
        IconSymbol.Instructions => "", // Edit, the pencil
        // SquareSparkle, the sign Windows 11 gives its AI features. It came
        // with a recent Segoe Fluent Icons (present in version 1.56); an older
        // font draws an empty tile.
        IconSymbol.Intelligence => "",
        IconSymbol.Terminal => "",     // CommandPrompt
        IconSymbol.Settings => "",     // Settings
        IconSymbol.Bug => "",          // Bug
        IconSymbol.Test => "",         // Beaker
        IconSymbol.Important => "",    // Important
        IconSymbol.Flag => "",         // Flag
        IconSymbol.Tag => "",          // Tag
        IconSymbol.Idea => "",         // Lightbulb
        IconSymbol.Private => "",      // Lock
        IconSymbol.Cloud => "",        // Cloud
        _ =>"",                     // Folder
    };
}
