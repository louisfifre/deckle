using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Catalog;

// ── SettingsPageHeader ────────────────────────────────────────────────────────
//
// The one header block every settings page opens with (settings design system,
// decisions 9 and 15): a breadcrumb in place of the title, one line of
// description, an optional link, and on the trailing edge the Info toggle
// that shows the cards' descriptions (SettingsDescriptions) then Reset all. Built in
// code like the composer's cards, so it lives on the Catalog floor that every
// settings-bearing module already references and no page draws its own.
//
// The breadcrumb is the page's path in the rail — the module, then the page —
// resolved from the shell through SettingsNavigation.BreadcrumbFor once the
// header is in a page's tree. Clicking the module segment goes to the module's
// General page through SettingsNavigation.GoToPage; it is the way up when the
// rail is closed. Outside the shell (no resolver) the header shows Title alone,
// so a page remains readable hosted anywhere.
//
// Reset all restores the whole page. It is active only once something on the
// page differs from its default (the page gates IsResetAllEnabled off its
// composers' DirtyChanged) and asks before acting, with the shell's generic
// wording; a page whose reset needs its own words, to say what is kept, sets
// ConfirmsReset false and asks itself before honouring ResetAllRequested.
public sealed partial class SettingsPageHeader : UserControl
{
    private readonly BreadcrumbBar _crumbs = new();
    private readonly TextBlock _fallbackTitle;
    private readonly TextBlock _description;
    private readonly HyperlinkButton _link;
    private readonly ToggleButton _info;
    private readonly Button _resetAll;
    private IReadOnlyList<SettingsBreadcrumbSegment>? _segments;

    public event RoutedEventHandler? ResetAllRequested;

    public SettingsPageHeader()
    {
        // The breadcrumb reads as the page title: TitleTextBlockStyle's size and
        // weight, applied through the control's lightweight-styling resources
        // rather than a retemplate. The chevron scales with it, and gets room on
        // both sides (the stock padding is 2 px) and a top padding: the glyph is
        // centred on the item's line box, which puts it above the visual middle
        // of 28 px lowercase text, and the asymmetric padding lowers it by half
        // its own height (2026-10-14).
        // The brushes come as a merged dictionary, not as the Resources
        // themselves: a dictionary with Source set refuses local values, and the
        // sizes below are set locally (2026-10-14, every page threw at
        // navigation when the compiled dictionary was the Resources).
        _crumbs.Resources.MergedDictionaries.Add(CrumbBrushes());
        _crumbs.Resources["BreadcrumbBarItemThemeFontSize"] = 28d;
        _crumbs.Resources["BreadcrumbBarItemFontWeight"] = FontWeights.SemiBold;
        _crumbs.Resources["BreadcrumbBarChevronFontSize"] = 16d;
        _crumbs.Resources["BreadcrumbBarChevronPadding"] = new Thickness(8, 8, 8, 0);
        _crumbs.VerticalAlignment = VerticalAlignment.Center;
        _crumbs.ItemClicked += OnCrumbClicked;

        _fallbackTitle = new TextBlock
        {
            Style = Application.Current.Resources["TitleTextBlockStyle"] as Style,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _resetAll = new Button
        {
            Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = false,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = Glyphs.Reset, FontSize = 14 },
                    new TextBlock { Text = Loc.Get("SettingsPageHeader_ResetAll") },
                },
            },
        };
        ToolTipService.SetToolTip(_resetAll, Loc.Get("SettingsPageHeader_ResetAllTooltip"));
        _resetAll.Click += OnResetAllClick;

        // The Info toggle, left of Reset all: shows or hides every description
        // on every page (SettingsDescriptions). A toggle, not a command, so its
        // pressed look says the long form is on; its state is process-wide, so
        // the button follows a click made on another page through Changed.
        _info = new ToggleButton
        {
            Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = SettingsDescriptions.IsShown,
            Content = new FontIcon { Glyph = Glyphs.Info, FontSize = 16 },
        };
        _info.Click += (_, _) => SettingsDescriptions.IsShown = _info.IsChecked == true;
        // Subscribed while in a tree only: a cached page's header outlives its
        // visits, and a static event would otherwise hold it for good.
        Loaded += (_, _) => { SettingsDescriptions.Changed += OnDescriptionsChanged; SyncInfo(); };
        Unloaded += (_, _) => SettingsDescriptions.Changed -= OnDescriptionsChanged;
        ActualThemeChanged += (_, _) => SyncInfo();
        SyncInfo();

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        commands.Children.Add(_info);
        commands.Children.Add(_resetAll);

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleSlot = new Grid();
        titleSlot.Children.Add(_crumbs);
        titleSlot.Children.Add(_fallbackTitle);
        Grid.SetColumn(titleSlot, 0);
        Grid.SetColumn(commands, 1);
        titleRow.Children.Add(titleSlot);
        titleRow.Children.Add(commands);

        _description = new TextBlock
        {
            Style = Application.Current.Resources["BodyTextBlockStyle"] as Style,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        _link = new HyperlinkButton
        {
            Margin = new Thickness(-11, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        // The content starts 12 px under the header (the PowerToys page control's
        // gap), and the StackPanel's own card spacing adds the rest.
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(titleRow);
        root.Children.Add(_description);
        root.Children.Add(_link);
        Content = root;

        Loaded += (_, _) => ResolveBreadcrumb();
    }

    // ── Properties (dependency properties so x:Uid can set them from .resw) ──

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingsPageHeader),
        new PropertyMetadata(string.Empty, (d, _) => ((SettingsPageHeader)d).ApplyTitle()));

    // The page's own name, shown when no breadcrumb can be resolved and used as
    // the last segment's fallback.
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsPageHeader),
        new PropertyMetadata(string.Empty, (d, e) => ((SettingsPageHeader)d).ApplyDescription((string?)e.NewValue)));

    // One line that says what the page sets. Empty hides the row.
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty LinkTextProperty = DependencyProperty.Register(
        nameof(LinkText), typeof(string), typeof(SettingsPageHeader),
        new PropertyMetadata(string.Empty, (d, _) => ((SettingsPageHeader)d).ApplyLink()));

    public static readonly DependencyProperty LinkUriProperty = DependencyProperty.Register(
        nameof(LinkUri), typeof(string), typeof(SettingsPageHeader),
        new PropertyMetadata(string.Empty, (d, _) => ((SettingsPageHeader)d).ApplyLink()));

    // An optional link under the description (a guide, a related page). Both
    // empty hides the row; a text without a URI raises LinkClicked instead.
    public string LinkText
    {
        get => (string)GetValue(LinkTextProperty);
        set => SetValue(LinkTextProperty, value);
    }

    public string LinkUri
    {
        get => (string)GetValue(LinkUriProperty);
        set => SetValue(LinkUriProperty, value);
    }

    public event RoutedEventHandler? LinkClicked;

    public static readonly DependencyProperty IsResetAllEnabledProperty = DependencyProperty.Register(
        nameof(IsResetAllEnabled), typeof(bool), typeof(SettingsPageHeader),
        new PropertyMetadata(false, (d, e) => ((SettingsPageHeader)d)._resetAll.IsEnabled = (bool)e.NewValue));

    // Active-when-dirty: the page sets it from its composers' IsDirty().
    public bool IsResetAllEnabled
    {
        get => (bool)GetValue(IsResetAllEnabledProperty);
        set => SetValue(IsResetAllEnabledProperty, value);
    }

    public static readonly DependencyProperty HasResetAllProperty = DependencyProperty.Register(
        nameof(HasResetAll), typeof(bool), typeof(SettingsPageHeader),
        new PropertyMetadata(true, (d, e) => ((SettingsPageHeader)d)._resetAll.Visibility =
            (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    // False on a page with nothing to reset (a list the user authors, a page of
    // commands): the button is absent rather than forever disabled.
    public bool HasResetAll
    {
        get => (bool)GetValue(HasResetAllProperty);
        set => SetValue(HasResetAllProperty, value);
    }

    // True asks with the shell's generic wording before raising ResetAllRequested.
    // A page that must say what its reset keeps sets this false and asks itself.
    public bool ConfirmsReset { get; set; } = true;

    // ── Behaviour ────────────────────────────────────────────────────────────

    // The stock control paints every segment in the primary text colour and
    // only greys one on hover, so a parent read like the current page and a
    // click on it looked like nothing. Windows Settings does the reverse: the
    // parents in the secondary colour, the current page in the primary, and a
    // parent lights up to primary under the pointer because it leads somewhere.
    //
    // The overrides are aliases in theme dictionaries (Default, Light,
    // HighContrast), the documented lightweight-styling form, not brushes copied
    // out of Application.Current.Resources: that lookup follows the application
    // theme, while Deckle applies its Light/Dark choice on each window's root
    // (FrameworkElement.RequestedTheme), so a copied brush took the wrong
    // theme's grey whenever Deckle's theme differed from Windows'. In theme
    // dictionaries, the template's ThemeResource lookups resolve against the
    // element's own theme and follow it when it changes. The dictionary is
    // compiled XAML (Themes/SettingsPageHeaderBrushes.xaml), not a XamlReader
    // string: XamlReader.Load parses without Application.Resources in sight, so
    // the StaticResource aliases would fail at parse time, in this constructor.
    // One instance per header, shared by the breadcrumb (merged into its
    // Resources) and the Info toggle (read by theme in ThemeBrush).
    private readonly ResourceDictionary _headerBrushes = new()
    {
        Source = new Uri("ms-appx:///Deckle.Catalog/Themes/SettingsPageHeaderBrushes.xaml"),
    };

    private ResourceDictionary CrumbBrushes() => _headerBrushes;

    private void ResolveBreadcrumb()
    {
        Type? pageType = FindPageType();
        _segments = pageType is null ? null : SettingsNavigation.BreadcrumbFor?.Invoke(pageType);
        ApplyTitle();
    }

    private Type? FindPageType()
    {
        DependencyObject? node = this;
        while (node is not null)
        {
            if (node is Page page) return page.GetType();
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void ApplyTitle()
    {
        if (_segments is { Count: > 0 })
        {
            _crumbs.ItemsSource = _segments.Select(s => s.Label).ToList();
            _crumbs.Visibility = Visibility.Visible;
            _fallbackTitle.Visibility = Visibility.Collapsed;
        }
        else
        {
            _fallbackTitle.Text = Title;
            _fallbackTitle.Visibility = Visibility.Visible;
            _crumbs.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (_segments is null || args.Index >= _segments.Count - 1) return;
        string? tag = _segments[args.Index].PageTag;
        if (tag is not null) SettingsNavigation.GoToPage?.Invoke(tag);
    }

    private void ApplyDescription(string? text)
    {
        _description.Text = text ?? string.Empty;
        _description.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyLink()
    {
        bool shown = !string.IsNullOrEmpty(LinkText);
        _link.Content = LinkText;
        _link.NavigateUri = Uri.TryCreate(LinkUri, UriKind.Absolute, out Uri? uri) ? uri : null;
        _link.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _link.Click -= OnLinkClick;
        if (shown && _link.NavigateUri is null) _link.Click += OnLinkClick;
    }

    private void OnLinkClick(object sender, RoutedEventArgs e) => LinkClicked?.Invoke(this, e);

    private void OnDescriptionsChanged(object? sender, EventArgs e) => SyncInfo();

    // The SubtleButtonStyle is a Button style; a ToggleButton accepts it
    // (ButtonBase setters) but draws no checked state from it, so the checked
    // look is given here: the accent colour on the glyph while the
    // descriptions are shown. The brush is read from the header's own theme
    // dictionary by the element's ActualTheme, not from Application.Resources,
    // which follows the application theme rather than the window's (see
    // CrumbBrushes).
    private void SyncInfo()
    {
        bool shown = SettingsDescriptions.IsShown;
        if (_info.IsChecked != shown) _info.IsChecked = shown;
        if (_info.Content is FontIcon icon)
        {
            if (shown)
                icon.Foreground = ThemeBrush("SettingsPageHeaderInfoCheckedForeground");
            else
                icon.ClearValue(IconElement.ForegroundProperty);
        }
        ToolTipService.SetToolTip(_info, Loc.Get(shown
            ? "SettingsPageHeader_HideDescriptionsTooltip"
            : "SettingsPageHeader_ShowDescriptionsTooltip"));
    }

    // A brush of the header's compiled dictionary for this element's theme. A
    // StaticResource alias inside a theme dictionary resolves in that theme's
    // scope, so each theme's copy carries the right colour; HighContrast is
    // taken as Default here, as no element API tells it apart.
    private Brush? ThemeBrush(string key)
    {
        string theme = ActualTheme == ElementTheme.Light ? "Light" : "Default";
        return _headerBrushes.ThemeDictionaries[theme] is ResourceDictionary dictionary
            ? dictionary[key] as Brush
            : null;
    }

    private async void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        if (ConfirmsReset)
        {
            bool confirmed = await ConfirmationService.RequestAsync(
                XamlRoot,
                new ConfirmationRequest(
                    Loc.Get("SettingsPageHeader_ResetAllDialogTitle"),
                    Loc.Get("SettingsPageHeader_ResetAllDialogBody"),
                    Loc.Get("SettingsPageHeader_ResetAll"),
                    IsDestructive: true));
            if (!confirmed) return;
        }
        ResetAllRequested?.Invoke(this, e);
    }
}
