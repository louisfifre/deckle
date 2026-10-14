using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Catalog;

// ── PreviewBlock ──────────────────────────────────────────────────────────────
//
// The one preview component of the settings design system (decisions 17 and
// 18): a card holding a stage with its commands above and its readouts below.
// A module gives it the three parts; the block owns the frame, so every
// preview in the application has the same shape — the Ambient capture today,
// the HUD and other scenes later.
//
// A card, never a fold: the preview is there to be looked at while the values
// beside it change, and a fold hid it behind a click and a chevron (Louis,
// 2026-10-14: "je veux pas qu'on collapse"). The owner shows the block only
// while there is something live to show (the engine runs) and refreshes it
// while the block is loaded; nothing runs on a page whose instrument is
// hidden. The stage sits on a bordered layer at the control corner radius;
// the stage content sizes itself (a Viewbox keeps a scene's ratio). Commands
// are a row at the stage's leading edge; readouts are Readout blocks in a row
// under it. An empty slot takes no room.
//
// Built in code like the section and the page header, on the Catalog floor
// every settings-bearing module references. Stage is the content property, so
// a module writes its scene inside the element.
[ContentProperty(Name = nameof(Stage))]
public sealed partial class PreviewBlock : UserControl
{
    private readonly SettingsCard _card = new()
    {
        ContentAlignment = ContentAlignment.Vertical,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private readonly Border _commandsSlot;
    private readonly Border _stageSlot;
    private readonly Border _readoutsSlot;

    public PreviewBlock()
    {
        _commandsSlot = new Border { Visibility = Visibility.Collapsed };

        // The stage's layer: the same materials as a card's content area, so the
        // scene reads as a surface inside the card rather than a second card.
        _stageSlot = new Border
        {
            Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Padding = new Thickness(12),
        };

        _readoutsSlot = new Border { Visibility = Visibility.Collapsed };

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(_commandsSlot);
        body.Children.Add(_stageSlot);
        body.Children.Add(_readoutsSlot);
        _card.Content = body;

        Content = _card;
    }

    // ── Properties (dependency properties so x:Uid can set them from .resw) ──

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(PreviewBlock),
        new PropertyMetadata(string.Empty, (d, e) => ((PreviewBlock)d)._card.Header = e.NewValue));

    // What the preview shows, in a few words.
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PreviewBlock),
        new PropertyMetadata(string.Empty, (d, e) => ((PreviewBlock)d)._card.Description = e.NewValue));

    // One line under the header; empty hides it.
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty HeaderIconProperty = DependencyProperty.Register(
        nameof(HeaderIcon), typeof(string), typeof(PreviewBlock),
        new PropertyMetadata(string.Empty, (d, e) => ((PreviewBlock)d).ApplyHeaderIcon((string?)e.NewValue)));

    // A glyph from Icons.xaml, the same way a card names its icon.
    public string HeaderIcon
    {
        get => (string)GetValue(HeaderIconProperty);
        set => SetValue(HeaderIconProperty, value);
    }

    public static readonly DependencyProperty CommandsProperty = DependencyProperty.Register(
        nameof(Commands), typeof(UIElement), typeof(PreviewBlock),
        new PropertyMetadata(null, (d, e) => Fill(((PreviewBlock)d)._commandsSlot, (UIElement?)e.NewValue)));

    // The row above the stage: what the user can do to the preview itself
    // (pause it, show an overlay), never a setting.
    public UIElement? Commands
    {
        get => (UIElement?)GetValue(CommandsProperty);
        set => SetValue(CommandsProperty, value);
    }

    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage), typeof(UIElement), typeof(PreviewBlock),
        new PropertyMetadata(null, (d, e) => ((PreviewBlock)d)._stageSlot.Child = (UIElement?)e.NewValue));

    // The scene.
    public UIElement? Stage
    {
        get => (UIElement?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    public static readonly DependencyProperty ReadoutsProperty = DependencyProperty.Register(
        nameof(Readouts), typeof(UIElement), typeof(PreviewBlock),
        new PropertyMetadata(null, (d, e) => Fill(((PreviewBlock)d)._readoutsSlot, (UIElement?)e.NewValue)));

    // The row under the stage: what the scene is made of, read from the live
    // thing, never typed (a format, a measured rate, the colours sent).
    public UIElement? Readouts
    {
        get => (UIElement?)GetValue(ReadoutsProperty);
        set => SetValue(ReadoutsProperty, value);
    }

    private void ApplyHeaderIcon(string? glyph)
        => _card.HeaderIcon = string.IsNullOrEmpty(glyph) ? null! : new FontIcon { Glyph = glyph };

    private static void Fill(Border slot, UIElement? element)
    {
        slot.Child = element;
        slot.Visibility = element is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
