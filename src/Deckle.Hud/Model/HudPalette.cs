using Deckle.Catalog;

namespace Deckle.Hud;

// Semantic palette + Segoe Fluent glyphs for HUD message badges. Each kind
// maps to a brush key local to HudMessage.xaml, where the brush is declared
// with {ThemeResource SystemFillColor*}, plus its semantic glyph.
internal static class HudPalette
{
    internal readonly record struct Entry(string BrushKey, string Glyph);

    internal static Entry Resolve(MessageKind kind) => kind switch
    {
        MessageKind.Success       => new("BadgeSuccessBrush",  Glyphs.Badge.Success),
        MessageKind.Critical      => new("BadgeCriticalBrush", Glyphs.Badge.Critical),
        MessageKind.Warning       => new("BadgeWarningBrush",  Glyphs.Badge.Warning),
        MessageKind.Informational => new("BadgeInfoBrush",     Glyphs.Badge.Info),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
