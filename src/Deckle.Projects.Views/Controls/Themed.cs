using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Deckle.Projects.Views;

// Elements built in code whose colour must follow the theme of the window they
// are in (light, dark, contrast, accent).
//
// Why not `Foreground = (Brush)Application.Current.Resources[key]`: a brush read
// from the application's resources in code is resolved once, against the
// application's theme, and then frozen. In a window whose root asks for another
// theme, or after the person switches theme, the text keeps the first colour
// (seen: white counts on a light page). XAML's {ThemeResource} is resolved
// against the element's own theme and again when it changes, but code has no way
// to write one. So the element is parsed from a one-line XAML with the
// {ThemeResource} in it, which is the same thing a .xaml file does at load.
//
// Only the brush needs this. A Style read from the application's resources
// carries its own {ThemeResource} setters and follows the theme by itself.
internal static class Themed
{
    private const string Namespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    // A TextBlock in the given text style (a key such as "CaptionTextBlockStyle",
    // or none) with the given brush (a key such as "TextFillColorTertiaryBrush").
    public static TextBlock Text(string foregroundKey, string? styleKey = null)
    {
        string style = styleKey is null ? "" : $" Style=\"{{StaticResource {styleKey}}}\"";
        return (TextBlock)XamlReader.Load(
            $"<TextBlock xmlns=\"{Namespace}\"{style} Foreground=\"{{ThemeResource {foregroundKey}}}\" />");
    }

    public static FontIcon Icon(string glyph, double size, string foregroundKey) =>
        (FontIcon)XamlReader.Load(
            $"<FontIcon xmlns=\"{Namespace}\" Glyph=\"{glyph}\" FontSize=\"{size}\" Foreground=\"{{ThemeResource {foregroundKey}}}\" />");

    // A subtle button whose text takes the given brush at rest; its hover and
    // pressed colours stay the style's own.
    public static Button SubtleButton(string foregroundKey) =>
        (Button)XamlReader.Load(
            $"<Button xmlns=\"{Namespace}\" Style=\"{{StaticResource SubtleButtonStyle}}\" Foreground=\"{{ThemeResource {foregroundKey}}}\" />");
}
