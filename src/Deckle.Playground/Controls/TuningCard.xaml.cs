using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;

namespace Deckle.Playground.Controls;

// A titled tuning section with a hover/focus-revealed per-section reset. Mirrors
// TunableRow's shape (UserControl, DP-driven, theme resources only) one level up :
// where TunableRow is a single knob, TuningCard is the card that groups several.
// The body is whatever XAML sits between the tags — mapped to CardContent by the
// ContentProperty attribute — so a section reads as one declarative block.
[ContentProperty(Name = nameof(CardContent))]
public sealed partial class TuningCard : UserControl
{
    // Either pointer or keyboard focus keeps the reset shown ; track both so
    // leaving one while the other still holds doesn't hide it prematurely.
    private bool _pointerOver;
    private bool _resetFocused;

    public TuningCard()
    {
        InitializeComponent();
    }

    // ── Content slot ─────────────────────────────────────────────────────────

    public static readonly DependencyProperty CardContentProperty =
        DependencyProperty.Register(nameof(CardContent), typeof(object), typeof(TuningCard),
            new PropertyMetadata(null));
    public object CardContent { get => GetValue(CardContentProperty); set => SetValue(CardContentProperty, value); }

    // ── Header DPs ───────────────────────────────────────────────────────────

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TuningCard),
            new PropertyMetadata("", OnTitleChanged));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(TuningCard),
            new PropertyMetadata("", OnDescriptionChanged));
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    // ── Reset DPs ────────────────────────────────────────────────────────────

    public static readonly DependencyProperty ResetLabelProperty =
        DependencyProperty.Register(nameof(ResetLabel), typeof(string), typeof(TuningCard),
            new PropertyMetadata("Reset section", OnResetLabelChanged));
    public string ResetLabel { get => (string)GetValue(ResetLabelProperty); set => SetValue(ResetLabelProperty, value); }

    public static readonly DependencyProperty ResetTooltipProperty =
        DependencyProperty.Register(nameof(ResetTooltip), typeof(string), typeof(TuningCard),
            new PropertyMetadata("Reset this section to its defaults", OnResetTooltipChanged));
    public string ResetTooltip { get => (string)GetValue(ResetTooltipProperty); set => SetValue(ResetTooltipProperty, value); }

    // Fired when the per-section reset is clicked ; the page restores that
    // section's defaults.
    public event RoutedEventHandler? ResetClick;

    // ── DP plumbing ──────────────────────────────────────────────────────────

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TuningCard self) self.TitleText.Text = (string)e.NewValue;
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TuningCard self) return;
        string text = (string)e.NewValue;
        self.DescriptionText.Text = text;
        self.DescriptionText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnResetLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TuningCard self) self.ResetLabelText.Text = (string)e.NewValue;
    }

    private static void OnResetTooltipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TuningCard self) ToolTipService.SetToolTip(self.ResetButton, (string)e.NewValue);
    }

    // ── Reveal ───────────────────────────────────────────────────────────────

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) { _pointerOver = true; UpdateReveal(); }
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) { _pointerOver = false; UpdateReveal(); }
    private void OnResetGotFocus(object sender, RoutedEventArgs e) { _resetFocused = true; UpdateReveal(); }
    private void OnResetLostFocus(object sender, RoutedEventArgs e) { _resetFocused = false; UpdateReveal(); }

    private void UpdateReveal() =>
        VisualStateManager.GoToState(this, _pointerOver || _resetFocused ? "Revealed" : "Rest", true);

    private void OnResetClick(object sender, RoutedEventArgs e) => ResetClick?.Invoke(this, e);
}
