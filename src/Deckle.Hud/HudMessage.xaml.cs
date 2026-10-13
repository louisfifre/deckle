using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Hud;

// Message card for Pasted / Copied / Error / UserFeedback feedback, fixed
// 272x78, filling the whole HUD window.
public sealed partial class HudMessage : UserControl
{
    public HudMessage()
    {
        InitializeComponent();
    }

    internal void Show(MessagePayload payload)
    {
        var entry = HudPalette.Resolve(payload.Kind);

        MessageTitle.Text    = payload.Title    ?? string.Empty;
        MessageSubtitle.Text = payload.Subtitle ?? string.Empty;

        // Look the brush up in the local resources, never in
        // Application.Current.Resources, which does not walk ThemeDictionaries.
        // Theme changes on the UserControl re-evaluate the {ThemeResource}
        // binding, so no ActualThemeChanged handler is needed.
        if (Resources.TryGetValue(entry.BrushKey, out var obj) && obj is Brush brush)
            BadgeBackground.Background = brush;

        BadgeGlyph.Glyph = entry.Glyph;
    }
}
