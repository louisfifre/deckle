using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// Draws one IconChoice: the symbol in its tint, or, with the background on,
// the symbol on a square of its tint (specs/folders.md, Window: the
// background is always a square). Wherever a chosen icon shows, the pane of
// added folders, the address bar, the picker's own tiles, it is this view,
// so all of them draw it alike.
//
// BoxSize is the side of the square the view takes either way: the symbol
// alone fills most of it, the symbol on its square is smaller so the square
// keeps a margin around it, and both weigh the same in a row of icons.
//
// The tints are not theme resources (IconTintPalette), so the view redraws
// itself when its theme changes or Windows' accent colour does.
//
// It is decoration: whatever holds it says the name to a screen reader.
public sealed partial class IconChoiceView : UserControl
{
    private const double GlyphShare = 0.8;
    private const double GlyphOnSquareShare = 0.6;

    private readonly Border _square = new();
    private readonly FontIcon _glyph = new();
    private IconChoice _choice;
    private double _boxSize = 20;

    public IconChoiceView()
    {
        IsTabStop = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        _square.CornerRadius = new CornerRadius(4);
        _square.Child = _glyph;
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.VerticalAlignment = VerticalAlignment.Center;
        Content = _square;

        ActualThemeChanged += (_, _) => Render();
        Loaded += (_, _) =>
        {
            IconTintPalette.Changed += OnPaletteChanged;
            Render();
        };
        Unloaded += (_, _) => IconTintPalette.Changed -= OnPaletteChanged;
        Render();
    }

    public IconChoiceView(IconChoice choice, double boxSize) : this()
    {
        _choice = choice;
        _boxSize = boxSize;
        Render();
    }

    public IconChoice Choice
    {
        get => _choice;
        set
        {
            if (_choice == value) return;
            _choice = value;
            Render();
        }
    }

    public double BoxSize
    {
        get => _boxSize;
        set
        {
            _boxSize = value;
            Render();
        }
    }

    private void OnPaletteChanged() => DispatcherQueue?.TryEnqueue(Render);

    private void Render()
    {
        IconTintPalette.TintColors colors = IconTintPalette.For(_choice.Tint, ActualTheme);
        Width = _boxSize;
        Height = _boxSize;
        _glyph.Glyph = IconSymbolGlyphs.Of(_choice.Symbol);
        if (_choice.HasBackground)
        {
            _square.Background = colors.Fill;
            _glyph.Foreground = colors.OnFill;
            _glyph.FontSize = Math.Round(_boxSize * GlyphOnSquareShare);
        }
        else
        {
            _square.Background = null;
            _glyph.Foreground = colors.Foreground;
            _glyph.FontSize = Math.Round(_boxSize * GlyphShare);
        }
    }
}
