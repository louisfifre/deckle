using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Deckle.Catalog;

// The colours of each IconTint, per theme: the symbol drawn alone, the square
// behind it when the background is on, and the symbol on that square.
//
// The accent is Windows' own, read from the system the way WinUI's theme
// resources read it: the symbol alone takes the shade of
// AccentTextFillColorPrimary (Dark2 in light theme, Light3 in dark), the
// square the shade of AccentFillColorDefault (Dark1, Light2), and the symbol
// on it the colour of TextOnAccentFillColorPrimary (white, black).
//
// The four other tints have no theme resource in Windows. Each keeps the hue
// of the Windows Settings accent swatch it was first taken from, but is built
// in OKLCH, where one lightness reads as equally light across hues, so the
// four sit together instead of shouting: the swatches themselves were too
// vivid beside Mica. Light theme: chroma 0.15 and the lightest lightness that
// keeps the white symbol on the square at 4.5:1 (about 0.58). Dark theme:
// lightness 0.74, chroma 0.14, a soft pastel that keeps the black symbol at
// 8.5:1 and 6.5:1 against the dark window. Each keeps at least 3:1 against
// the window's background, the contrast WCAG asks of a graphic. The table
// below is the one place the colours live.
//
// In high contrast every tint takes the system's colours: high contrast
// replaces an app's colours by the person's own.
internal static class IconTintPalette
{
    internal readonly record struct TintColors(Brush Foreground, Brush Fill, Brush OnFill);

    private static readonly UISettings Settings = CreateSettings();
    private static readonly AccessibilitySettings Accessibility = new();

    //                                         light theme       dark theme
    private static readonly Dictionary<IconTint, (Color Light, Color Dark)> Swatches = new()
    {
        [IconTint.Red] = (Rgb(0xC5, 0x51, 0x4D), Rgb(0xF7, 0x85, 0x7E)),
        [IconTint.Orange] = (Rgb(0xC1, 0x56, 0x26), Rgb(0xF3, 0x8B, 0x60)),
        [IconTint.Green] = (Rgb(0x31, 0x86, 0x2D), Rgb(0x73, 0xC1, 0x6D)),
        [IconTint.Purple] = (Rgb(0xA4, 0x59, 0xB0), Rgb(0xD4, 0x8B, 0xDF)),
    };

    // Raised, off the UI thread, when the person changes Windows' accent
    // colour or turns high contrast on or off.
    public static event Action? Changed;

    public static TintColors For(IconTint tint, ElementTheme theme)
    {
        bool dark = theme == ElementTheme.Dark;
        if (Accessibility.HighContrast)
        {
            Brush text = Resource("SystemColorWindowTextColorBrush");
            Brush highlight = Resource("SystemColorHighlightColorBrush");
            return new TintColors(text, highlight, Resource("SystemColorHighlightTextColorBrush"));
        }

        if (tint == IconTint.Accent)
        {
            Color foreground = Settings.GetColorValue(dark ? UIColorType.AccentLight3 : UIColorType.AccentDark2);
            Color fill = Settings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
            return new TintColors(new SolidColorBrush(foreground), new SolidColorBrush(fill),
                new SolidColorBrush(dark ? Rgb(0, 0, 0) : Rgb(0xFF, 0xFF, 0xFF)));
        }

        (Color light, Color darkColor) = Swatches[tint];
        Color color = dark ? darkColor : light;
        return new TintColors(new SolidColorBrush(color), new SolidColorBrush(color),
            new SolidColorBrush(dark ? Rgb(0, 0, 0) : Rgb(0xFF, 0xFF, 0xFF)));
    }

    private static UISettings CreateSettings()
    {
        var settings = new UISettings();
        settings.ColorValuesChanged += (_, _) => Changed?.Invoke();
        return settings;
    }

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Rgb(0, 0, 0));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);
}
