using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Catalog;

// ── PreviewBlock ──────────────────────────────────────────────────────────────
//
// The one preview component of the settings design system (decisions 17 and
// 18): a fold the user opens, holding a stage with its commands above and its
// readouts below. A module gives it the three parts; the block owns the frame,
// so every preview in the application has the same shape — the Ambient capture
// today, the HUD and other scenes later.
//
// The fold is the gesture that starts a live instrument; nothing runs on page
// open. The owner reads IsExpanded (or IsExpandedChanged) and refreshes its
// stage only while the fold is open. The stage sits on a bordered layer at the
// control corner radius; the stage content sizes itself (a Viewbox keeps a
// scene's ratio). Commands are a row at the stage's leading edge; readouts are
// Readout blocks in a row under it. An empty slot takes no room.
//
// Built in code like the section and the page header, on the Catalog floor
// every settings-bearing module references. Stage is the content property, so
// a module writes its scene inside the element.
[ContentProperty(Name = nameof(Stage))]
public sealed partial class PreviewBlock : UserControl
{
    private readonly SettingsExpander _expander = new() { IsExpanded = false };
    private readonly Border _commandsSlot;
    private readonly Border _stageSlot;
    private readonly Border _readoutsSlot;

    public event EventHandler? IsExpandedChanged;

    public PreviewBlock()
    {
        _commandsSlot = new Border { Visibility = Visibility.Collapsed };

        // The stage's layer: the same materials as a card's content area, so the
        // scene reads as a surface inside the fold rather than a second card.
        _stageSlot = new Border
        {
            Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Padding = new Thickness(12),
        };

        _readoutsSlot = new Border { Visibility = Visibility.Collapsed };

        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(16, 12, 16, 16) };
        footer.Children.Add(_commandsSlot);
        footer.Children.Add(_stageSlot);
        footer.Children.Add(_readoutsSlot);
        _expander.ItemsFooter = footer;

        _expander.RegisterPropertyChangedCallback(SettingsExpander.IsExpandedProperty, (_, _) =>
        {
            if (IsExpanded != _expander.IsExpanded) IsExpanded = _expander.IsExpanded;
        });

        Content = _expander;
    }

    // ── Properties (dependency properties so x:Uid can set them from .resw) ──

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(PreviewBlock),
        new PropertyMetadata(string.Empty, (d, e) => ((PreviewBlock)d)._expander.Header = e.NewValue));

    // What the preview shows, in a few words.
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PreviewBlock),
        new PropertyMetadata(string.Empty, (d, e) => ((PreviewBlock)d)._expander.Description = e.NewValue));

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

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(PreviewBlock),
        new PropertyMetadata(false, (d, e) => ((PreviewBlock)d).ApplyExpanded((bool)e.NewValue)));

    // True while the fold is open: the owner's cue to refresh the stage.
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    private void ApplyHeaderIcon(string? glyph)
        => _expander.HeaderIcon = string.IsNullOrEmpty(glyph) ? null! : new FontIcon { Glyph = glyph };

    private void ApplyExpanded(bool expanded)
    {
        if (_expander.IsExpanded != expanded) _expander.IsExpanded = expanded;
        IsExpandedChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void Fill(Border slot, UIElement? element)
    {
        slot.Child = element;
        slot.Visibility = element is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
