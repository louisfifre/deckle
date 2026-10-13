using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Catalog;

// ── SettingsPageHeader ────────────────────────────────────────────────────────
//
// The one header block every settings page opens with (settings design system,
// decisions 9 and 15): a breadcrumb in place of the title, one line of
// description, an optional link, and Reset all on the trailing edge. Built in
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
    private readonly Button _resetAll;
    private IReadOnlyList<SettingsBreadcrumbSegment>? _segments;

    public event RoutedEventHandler? ResetAllRequested;

    public SettingsPageHeader()
    {
        // The breadcrumb reads as the page title: TitleTextBlockStyle's size and
        // weight, applied through the control's lightweight-styling resources
        // rather than a retemplate. The chevron scales with it.
        _crumbs.Resources["BreadcrumbBarItemThemeFontSize"] = 28d;
        _crumbs.Resources["BreadcrumbBarItemFontWeight"] = FontWeights.SemiBold;
        _crumbs.Resources["BreadcrumbBarChevronFontSize"] = 16d;
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

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleSlot = new Grid();
        titleSlot.Children.Add(_crumbs);
        titleSlot.Children.Add(_fallbackTitle);
        Grid.SetColumn(titleSlot, 0);
        Grid.SetColumn(_resetAll, 1);
        titleRow.Children.Add(titleSlot);
        titleRow.Children.Add(_resetAll);

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
