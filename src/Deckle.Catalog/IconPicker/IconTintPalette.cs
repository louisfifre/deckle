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
// The four other tints have no theme resource in Windows: their values are
// the accent swatches of Windows Settings (Personalisation > Colours), a
// deeper one for the light theme and a brighter one of the same hue for the
// dark theme. Each keeps at least 3:1 against the window's background, the
// contrast WCAG asks of a graphic, and its symbol on the square keeps 4.5:1.
// A proposal for the maintainer to change; the table below is the one place.
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
        [IconTint.Red] = (Rgb(0xD1, 0x34, 0x38), Rgb(0xFF, 0x43, 0x43)),
        [IconTint.Orange] = (Rgb(0xCA, 0x50, 0x10), Rgb(0xF7, 0x63, 0x0C)),
        [IconTint.Green] = (Rgb(0x10, 0x7C, 0x10), Rgb(0x00, 0xCC, 0x6A)),
        [IconTint.Purple] = (Rgb(0x88, 0x17, 0x98), Rgb(0xC2, 0x39, 0xB3)),
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
