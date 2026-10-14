using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Deckle.Catalog;

// ── Readout ───────────────────────────────────────────────────────────────────
//
// The diagnostic readout block of the settings design system: a value the user
// consults and never edits — a state, a measurement, a format. A label in
// caption text over the value in body text, with an optional status dot before
// the value in the system fill colour of its meaning. The value is text, or an
// element when text would not show it (a row of colour swatches).
//
// Built in code like the other blocks, on the Catalog floor. Value is the
// content property, so a module may write an element inside the readout.
[ContentProperty(Name = nameof(Value))]
public sealed partial class Readout : UserControl
{
    private readonly TextBlock _label;
    private readonly Ellipse _dot;
    private readonly ContentPresenter _value;

    public Readout()
    {
        _label = new TextBlock
        {
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
        };

        _dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _value = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };

        var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, MinHeight = 20 };
        valueRow.Children.Add(_dot);
        valueRow.Children.Add(_value);

        var root = new StackPanel { Spacing = 2 };
        root.Children.Add(_label);
        root.Children.Add(valueRow);
        Content = root;
    }

    // ── Properties (dependency properties so x:Uid can set them from .resw) ──

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Readout),
        new PropertyMetadata(string.Empty, (d, e) => ((Readout)d)._label.Text = (string?)e.NewValue ?? string.Empty));

    // What the value measures, in one or two words.
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(Readout),
        new PropertyMetadata(null, (d, e) => ((Readout)d).ApplyValue(e.NewValue)));

    // Text, or an element. Null shows nothing.
    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(ReadoutStatus), typeof(Readout),
        new PropertyMetadata(ReadoutStatus.None, (d, e) => ((Readout)d).ApplyStatus((ReadoutStatus)e.NewValue)));

    // The meaning of the value at a glance; None shows no dot.
    public ReadoutStatus Status
    {
        get => (ReadoutStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private void ApplyValue(object? value)
    {
        _value.Content = value is string text
            ? new TextBlock
            {
                Style = Application.Current.Resources["BodyTextBlockStyle"] as Style,
                Text = text,
            }
            : value;
    }

    private void ApplyStatus(ReadoutStatus status)
    {
        string? key = status switch
        {
            ReadoutStatus.Neutral  => "SystemFillColorNeutralBrush",
            ReadoutStatus.Success  => "SystemFillColorSuccessBrush",
            ReadoutStatus.Caution  => "SystemFillColorCautionBrush",
            ReadoutStatus.Critical => "SystemFillColorCriticalBrush",
            _ => null,
        };
        _dot.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
        if (key is not null) _dot.Fill = Application.Current.Resources[key] as Brush;
    }
}

// The four meanings Windows gives a status colour, plus none.
public enum ReadoutStatus
{
    None,
    Neutral,
    Success,
    Caution,
    Critical,
}
