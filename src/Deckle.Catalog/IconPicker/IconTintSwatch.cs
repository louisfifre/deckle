using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Catalog;

// One colour tile of the picker: a rounded square of the tint, as Windows
// Settings draws its accent swatches. Redrawn when the theme or Windows'
// accent colour changes, like IconChoiceView.
internal sealed partial class IconTintSwatch : UserControl
{
    private const double Side = 24;

    private readonly IconTint _tint;
    private readonly Border _square = new()
    {
        Width = Side,
        Height = Side,
        CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1),
    };

    public IconTintSwatch(IconTint tint)
    {
        _tint = tint;
        IsTabStop = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
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

    private void OnPaletteChanged() => DispatcherQueue?.TryEnqueue(Render);

    private void Render()
    {
        _square.Background = IconTintPalette.For(_tint, ActualTheme).Fill;
        // The thin edge every Windows swatch has, so a tint close to the
        // background still shows its square.
        _square.BorderBrush = Application.Current.Resources.TryGetValue("ControlStrokeColorDefaultBrush", out object? edge)
            ? edge as Brush
            : null;
    }
}
