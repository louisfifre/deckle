using Deckle.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Hud;

// Theme tracking of the transcript panel: the system or the app switches
// light/dark while the panel is on screen, the panel logs it like the HUD does
// and repaints what XAML does not re-resolve by itself.
public sealed partial class TranscriptPanelWindow
{
    private ElementTheme _lastTheme;

    // Called once from the constructor, after InitializeComponent.
    private void WireThemeTracking()
    {
        if (Content is not FrameworkElement root) return;
        _lastTheme = root.ActualTheme;
        root.ActualThemeChanged += OnRootActualThemeChanged;
    }

    // App.ApplyTheme pushes "user"/"app-init" on the probe; a change that
    // arrives with no pending value is labelled "system", as for the HUD.
    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        ElementTheme to = sender.ActualTheme;
        if (to == _lastTheme) return;
        string source = ThemeRequestSourceProbe.Consume() ?? "system";
        DeckleThemeSource.Log.ThemeChanged("transcript", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;

        ApplyFadeBrush();
    }

    // The "more above" copy of the text wears a gradient brush whose stops were
    // resolved when the window loaded: a ThemeResource inside a brush that is a
    // property of a text block is not re-resolved by the theme change, so the
    // ramp kept the old theme's text colour. The solid copy follows the theme
    // through its style, so the ramp is rebuilt from the colour that copy now
    // shows, transparent at the top. High Contrast keeps the text solid, as the
    // XAML dictionary says.
    private void ApplyFadeBrush()
    {
        if (TranscriptText.Foreground is not SolidColorBrush text) return;
        Windows.UI.Color end = text.Color;
        Windows.UI.Color start = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? end
            : Windows.UI.Color.FromArgb(0, end.R, end.G, end.B);

        TranscriptFadeText.Foreground = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = start },
                new GradientStop { Offset = 1, Color = end },
            },
        };
    }
}
