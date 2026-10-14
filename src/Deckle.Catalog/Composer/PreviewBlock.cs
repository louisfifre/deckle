using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Catalog;

// ── PreviewBlock ──────────────────────────────────────────────────────────────
//
// The one preview component of the settings surfaces: a stage on its layer, a
// row of commands above it, readouts under it, and nothing around them. A
// module gives it the parts; the block owns the frame, so every preview in the
// application has the same shape — the Ambient capture today, the HUD and
// other scenes later.
//
// Floating, never a card: the preview is an instrument beside the page, the
// Playground's form, and a card around it framed the stage twice (Louis,
// 2026-10-14, on screen: not framed on every side). The stage layer is the
// one border, the same materials as a card's content area, and it takes the
// height it is given so the scene scales to the room (a Viewbox keeps a
// scene's ratio). Commands sit at the stage's leading edge and trailing
// commands at its trailing edge, on one row above it; readouts are Readout
// blocks in a row under it, shown while the preview is live. An empty slot
// takes no room.
//
// IsLive says whether the scene has something to show. Off, the layer shows
// the block's empty text in the scene's place, and the owner's words say what
// to turn on; the owner still drives the refresh, and nothing runs in a block
// that is not live.
//
// Built in code like the section and the page header, on the Catalog floor
// every settings-bearing module references. Stage is the content property, so
// a module writes its scene inside the element.
[ContentProperty(Name = nameof(Stage))]
public sealed partial class PreviewBlock : UserControl
{
    private readonly Grid _commandsRow;
    private readonly Border _commandsSlot;
    private readonly Border _trailingSlot;
    private readonly Border _stageSlot;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyHeader;
    private readonly TextBlock _emptyDescription;
    private readonly Border _readoutsSlot;

    public PreviewBlock()
    {
        _commandsSlot = new Border
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _trailingSlot = new Border
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _commandsRow = new Grid { Visibility = Visibility.Collapsed, ColumnSpacing = 8 };
        _commandsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _commandsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_trailingSlot, 1);
        _commandsRow.Children.Add(_commandsSlot);
        _commandsRow.Children.Add(_trailingSlot);

        // The scene, stretched to the layer; the owner's Viewbox keeps its ratio.
        _stageSlot = new Border { Visibility = Visibility.Collapsed };

        // The empty text: what the scene would show, in the tertiary colour,
        // centred on the layer, the Playground's form.
        _emptyHeader = new TextBlock
        {
            Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style,
            Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _emptyDescription = new TextBlock
        {
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 440,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _empty = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _empty.Children.Add(_emptyHeader);
        _empty.Children.Add(_emptyDescription);

        var layerContent = new Grid();
        layerContent.Children.Add(_stageSlot);
        layerContent.Children.Add(_empty);

        // The stage's layer: the same materials as a card's content area, so the
        // scene reads as a surface in the page rather than a card. It fills the
        // row the block gives it; the floor keeps an empty block visible.
        var stageLayer = new Border
        {
            Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Padding = new Thickness(12),
            MinHeight = 160,
            Child = layerContent,
        };

        _readoutsSlot = new Border { Visibility = Visibility.Collapsed };

        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_commandsRow, 0);
        Grid.SetRow(stageLayer, 1);
        Grid.SetRow(_readoutsSlot, 2);
        root.Children.Add(_commandsRow);
        root.Children.Add(stageLayer);
        root.Children.Add(_readoutsSlot);

        Content = root;
    }

    // ── Properties (dependency properties so x:Uid can set them from .resw) ──

    public static readonly DependencyProperty CommandsProperty = DependencyProperty.Register(
        nameof(Commands), typeof(UIElement), typeof(PreviewBlock),
        new PropertyMetadata(null, (d, e) => ((PreviewBlock)d).ApplyCommands(((PreviewBlock)d)._commandsSlot, (UIElement?)e.NewValue)));

    // The row above the stage, at its leading edge: what the user can do to
    // the preview itself (pause it, show an overlay), never a setting.
    public UIElement? Commands
    {
        get => (UIElement?)GetValue(CommandsProperty);
        set => SetValue(CommandsProperty, value);
    }

    public static readonly DependencyProperty TrailingCommandsProperty = DependencyProperty.Register(
        nameof(TrailingCommands), typeof(UIElement), typeof(PreviewBlock),
        new PropertyMetadata(null, (d, e) => ((PreviewBlock)d).ApplyCommands(((PreviewBlock)d)._trailingSlot, (UIElement?)e.NewValue)));

    // The same row, at its trailing edge: what acts on the block's place rather
    // than on the scene (close the pane that holds it).
    public UIElement? TrailingCommands
    {
        get => (UIElement?)GetValue(TrailingCommandsProperty);
        set => SetValue(TrailingCommandsProperty, value);
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
        new PropertyMetadata(null, (d, e) => ((PreviewBlock)d).ApplyReadouts((UIElement?)e.NewValue)));

    // The row under the stage: what the scene is made of, read from the live
    // thing, never typed (a format, a measured rate). Shown while live.
    public UIElement? Readouts
    {
        get => (UIElement?)GetValue(ReadoutsProperty);
        set => SetValue(ReadoutsProperty, value);
    }

    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.Register(
        nameof(IsLive), typeof(bool), typeof(PreviewBlock),
        new PropertyMetadata(false, (d, _) => ((PreviewBlock)d).ApplyLive()));

    // True while the scene has something to show; false shows the empty text.
    public bool IsLive
    {
        get => (bool)GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    public static readonly DependencyProperty EmptyHeaderProperty = DependencyProperty.Register(
        nameof(EmptyHeader), typeof(string), typeof(PreviewBlock),
        new PropertyMetadata(string.Empty, (d, e) => ApplyText(((PreviewBlock)d)._emptyHeader, (string?)e.NewValue)));

    // What the stage shows, in a few words, while nothing is live.
    public string EmptyHeader
    {
        get => (string)GetValue(EmptyHeaderProperty);
        set => SetValue(EmptyHeaderProperty, value);
    }

    public static readonly DependencyProperty EmptyDescriptionProperty = DependencyProperty.Register(
        nameof(EmptyDescription), typeof(string), typeof(PreviewBlock),
        new PropertyMetadata(string.Empty, (d, e) => ApplyText(((PreviewBlock)d)._emptyDescription, (string?)e.NewValue)));

    // One line under it: what to turn on to make the stage live.
    public string EmptyDescription
    {
        get => (string)GetValue(EmptyDescriptionProperty);
        set => SetValue(EmptyDescriptionProperty, value);
    }

    // ── Applying ─────────────────────────────────────────────────────────────

    private void ApplyCommands(Border slot, UIElement? element)
    {
        Fill(slot, element);
        bool any = _commandsSlot.Child is not null || _trailingSlot.Child is not null;
        _commandsRow.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyReadouts(UIElement? element)
    {
        _readoutsSlot.Child = element;
        ApplyLive();
    }

    private void ApplyLive()
    {
        bool live = IsLive;
        _stageSlot.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        _readoutsSlot.Visibility = live && _readoutsSlot.Child is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void ApplyText(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Fill(Border slot, UIElement? element)
    {
        slot.Child = element;
        slot.Visibility = element is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
