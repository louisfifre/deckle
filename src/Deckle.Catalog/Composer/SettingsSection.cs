using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Catalog;

// ── SettingsSection ───────────────────────────────────────────────────────────
//
// The flat section block of a settings page: a title, at most one line of
// caption, then the cards — the PowerToys SettingsGroup form, with the title
// 32 px under what precedes it and the cards 8 px under the title. No body
// paragraph: what a section has to say fits its caption, or belongs to a
// card's description. The title row is [title | link | reset].
//
// A section that holds resettable values carries the section-level reset of the
// doctrine's three (the value, its group, the surface): the same wheel a card
// shows, revealed on hover at the title's trailing edge, enabled only while a
// value in the section differs from its default. The page wires it like a
// card's: it sets IsResetEnabled from its composer's dirtiness and answers
// ResetRequested. HasReset false (the default) hides the wheel for a section of
// commands, readouts or lists.
//
// A section may carry one text link at the title's trailing edge, before the
// reset wheel: the HyperlinkButton Windows Settings uses for a related
// surface, in the accent colour, which opens something beside the section
// (the Ambient page opens its live preview there). LinkText shows it; a page
// answers LinkClicked or binds LinkCommand, the two forms a HyperlinkButton
// offers. The same pair the page header carries (SettingsPageHeader.LinkText,
// LinkClicked), minus the URI: a section's link acts in the app.
//
// Built in code like the page header and the composer's cards, on the Catalog
// floor every settings-bearing module references. Body is the content property,
// so a page writes the cards (or a composer's host panel) directly inside the
// element.
[ContentProperty(Name = nameof(Body))]
public sealed partial class SettingsSection : UserControl
{
    private const double TopGap = 28;

    private readonly TextBlock _title;
    private readonly TextBlock _caption;
    private readonly HyperlinkButton _link;
    private readonly Button _reset;
    private readonly Border _bodySlot;
    private readonly StackPanel _root;

    public event RoutedEventHandler? ResetRequested;

    // Raised by the title link; see LinkText.
    public event RoutedEventHandler? LinkClicked;

    public SettingsSection()
    {
        _title = new TextBlock
        {
            Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(1, 0, 0, 0),
        };

        // The reset wheel in the form every card uses: subtle, 32×32, invisible
        // at rest. The title row stays the height of its text line (BodyStrong,
        // 20 px): the wheel overflows 6 px above and below through a negative
        // margin instead of stretching the row. A 32 px row with the text at
        // its bottom put 12 px of air over every section title, which Louis
        // read as a gap too many against Windows Settings (2026-10-14).
        _reset = new Button
        {
            Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(0, -6, 0, -6),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            IsEnabled = false,
            Content = new FontIcon { Glyph = Glyphs.Refresh, FontSize = 14 },
        };
        ToolTipService.SetToolTip(_reset, Loc.Get("SettingsSection_ResetTooltip"));
        _reset.Click += (_, e) => ResetRequested?.Invoke(this, e);

        // The link in the control's own style (accent text, the subtle fill on
        // hover), on the title's line: like the wheel it keeps the row at its
        // 20 px text line by overflowing 6 px above and below, and its text
        // sits centred in those 32 px, level with the title. The 8 px side
        // padding is the fill's own margin around the words.
        _link = new HyperlinkButton
        {
            Height = 32,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, -6, 0, -6),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _link.Click += (_, e) => LinkClicked?.Invoke(this, e);

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_title, 0);
        Grid.SetColumn(_link, 1);
        Grid.SetColumn(_reset, 2);
        titleRow.Children.Add(_title);
        titleRow.Children.Add(_link);
        titleRow.Children.Add(_reset);

        _caption = new TextBlock
        {
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        _bodySlot = new Border { Margin = new Thickness(0, 8, 0, 0) };

        // 28 px above plus the page's 4 px card spacing makes the 32 px gap.
        _root = new StackPanel { Margin = new Thickness(0, TopGap, 0, 0) };
        _root.Children.Add(titleRow);
        _root.Children.Add(_caption);
        _root.Children.Add(_bodySlot);
        Content = _root;

        Loaded += (_, _) => ApplyTopGap();

        // Reveal on the section's hover and the wheel's keyboard focus, like a
        // card's reset: either keeps it shown.
        bool pointerOver = false, focused = false;
        void Update() => _reset.Opacity = pointerOver || focused ? 1 : 0;
        PointerEntered += (_, _) => { pointerOver = true; Update(); };
        PointerExited += (_, _) => { pointerOver = false; Update(); };
        _reset.GotFocus += (_, _) => { focused = true; Update(); };
        _reset.LostFocus += (_, _) => { focused = false; Update(); };
    }

    // ── Properties (dependency properties so x:Uid can set them from .resw) ──

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingsSection),
        new PropertyMetadata(string.Empty, (d, e) => ((SettingsSection)d)._title.Text = (string?)e.NewValue ?? string.Empty));

    // The question the section answers, in a few words.
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(SettingsSection),
        new PropertyMetadata(string.Empty, (d, e) => ((SettingsSection)d).ApplyCaption((string?)e.NewValue)));

    // One line under the title, when the title alone does not say enough. Empty
    // hides the row.
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public static readonly DependencyProperty LinkTextProperty = DependencyProperty.Register(
        nameof(LinkText), typeof(string), typeof(SettingsSection),
        new PropertyMetadata(string.Empty, (d, e) => ((SettingsSection)d).ApplyLink((string?)e.NewValue)));

    // The words of the title link, which open something beside the section.
    // Empty hides the link. Settable from .resw through x:Uid
    // ("<Uid>.LinkText") like Title and Caption.
    public string LinkText
    {
        get => (string)GetValue(LinkTextProperty);
        set => SetValue(LinkTextProperty, value);
    }

    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.Register(
        nameof(LinkCommand), typeof(ICommand), typeof(SettingsSection),
        new PropertyMetadata(null, (d, e) => ((SettingsSection)d)._link.Command = (ICommand?)e.NewValue));

    // The command form of the link, for a page that binds rather than handles
    // LinkClicked. Both fire on the one click.
    public ICommand? LinkCommand
    {
        get => (ICommand?)GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(UIElement), typeof(SettingsSection),
        new PropertyMetadata(null, (d, e) => ((SettingsSection)d)._bodySlot.Child = (UIElement?)e.NewValue));

    // The cards, or the panel a composer fills with them.
    public UIElement? Body
    {
        get => (UIElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public static readonly DependencyProperty HasResetProperty = DependencyProperty.Register(
        nameof(HasReset), typeof(bool), typeof(SettingsSection),
        new PropertyMetadata(false, (d, e) => ((SettingsSection)d)._reset.Visibility =
            (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    // True on a section whose values can be reset together.
    public bool HasReset
    {
        get => (bool)GetValue(HasResetProperty);
        set => SetValue(HasResetProperty, value);
    }

    public static readonly DependencyProperty IsResetEnabledProperty = DependencyProperty.Register(
        nameof(IsResetEnabled), typeof(bool), typeof(SettingsSection),
        new PropertyMetadata(false, (d, e) => ((SettingsSection)d)._reset.IsEnabled = (bool)e.NewValue));

    // Active-when-dirty: the page sets it from its composer's IsDirty().
    public bool IsResetEnabled
    {
        get => (bool)GetValue(IsResetEnabledProperty);
        set => SetValue(IsResetEnabledProperty, value);
    }

    private void ApplyCaption(string? text)
    {
        _caption.Text = text ?? string.Empty;
        _caption.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyLink(string? text)
    {
        _link.Content = text ?? string.Empty;
        _link.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // The gap above a section is the 32 px between a card and the next title.
    // The first section of a page sits under the page header, whose 12 px
    // bottom margin plus the page's card spacing already place it where a
    // module's switch card goes: the same 16 px on every page, instead of the
    // 44 px that stood over the first section and over nothing (2026-10-14).
    // A sibling that takes no room (collapsed, or an InfoBar that is not open)
    // does not count as something above.
    private void ApplyTopGap()
    {
        bool underHeader = false;
        if (VisualTreeHelper.GetParent(this) is Panel panel)
        {
            int index = panel.Children.IndexOf(this);
            for (int i = index - 1; i >= 0; i--)
            {
                UIElement sibling = panel.Children[i];
                if (sibling.Visibility == Visibility.Collapsed) continue;
                if (sibling is InfoBar { IsOpen: false }) continue;
                underHeader = sibling is SettingsPageHeader;
                break;
            }
        }
        _root.Margin = new Thickness(0, underHeader ? 0 : TopGap, 0, 0);
    }
}
