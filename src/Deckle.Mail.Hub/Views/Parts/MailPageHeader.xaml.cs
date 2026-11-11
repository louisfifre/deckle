using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Deckle.Mail.Hub;

public sealed partial class MailPageHeader : UserControl
{
    private IReadOnlyList<HeaderCrumb> _trail = [];
    private Action<string?>? _commitName;
    private string _currentName = "";
    private bool _pointerOver;

    public MailPageHeader()
    {
        InitializeComponent();
        // The breadcrumb and the name being edited read as the title, so both
        // take TitleTextBlockStyle's own size and weight, read from the theme
        // rather than written here; a TextBox and a BreadcrumbBar cannot take
        // a TextBlock style. Set locally, as SettingsPageHeader does, since a
        // FontWeight resource cannot be given in XAML; with room around the
        // chevron.
        (double size, Windows.UI.Text.FontWeight weight) = TitleType();
        Crumbs.Resources["BreadcrumbBarItemThemeFontSize"] = size;
        Crumbs.Resources["BreadcrumbBarItemFontWeight"] = weight;
        Crumbs.Resources["BreadcrumbBarChevronFontSize"] = 16d;
        Crumbs.Resources["BreadcrumbBarChevronPadding"] = new Thickness(8, 8, 8, 0);
        NameBox.FontSize = size;
        NameBox.FontWeight = weight;
    }

    private static (double Size, Windows.UI.Text.FontWeight Weight) TitleType()
    {
        double size = 28;
        Windows.UI.Text.FontWeight weight = FontWeights.SemiBold;
        // A style's setters, then those of the style it is based on.
        var seen = new HashSet<DependencyProperty>();
        for (Style? style = Application.Current.Resources["TitleTextBlockStyle"] as Style; style is not null; style = style.BasedOn)
        {
            foreach (SetterBase setterBase in style.Setters)
            {
                if (setterBase is not Setter { Property: { } property } setter || !seen.Add(property)) continue;
                if (property == TextBlock.FontSizeProperty && setter.Value is double s) size = s;
                else if (property == TextBlock.FontWeightProperty && setter.Value is Windows.UI.Text.FontWeight w) weight = w;
            }
        }
        return (size, weight);
    }

    // A crumb that leads to its target when clicked; the last one is the page.
    public event Action<MailDestination>? CrumbClicked;

    public void SetTitle(string title, string? language = null)
    {
        _trail = [];
        TitleText.Text = title;
        if (language is not null) TitleText.Language = language;
        TitleText.Visibility = Visibility.Visible;
        Crumbs.Visibility = Visibility.Collapsed;
        NameBox.Visibility = Visibility.Collapsed;
    }

    public void SetTrail(IReadOnlyList<HeaderCrumb> trail)
    {
        _trail = trail;
        Crumbs.ItemsSource = trail.Select(c => c.Label).ToList();
        Crumbs.Visibility = Visibility.Visible;
        TitleText.Visibility = Visibility.Collapsed;
        NameBox.Visibility = Visibility.Collapsed;
    }

    public void SetDescription(string? text)
    {
        DescriptionText.Text = text ?? "";
        DescriptionText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Offers the pencil: the current name, the placeholder of an empty name
    // (it must fit inside the field), and what to do with the new one.
    public void EnableRename(string currentName, string placeholder, Action<string?> commit)
    {
        _currentName = currentName;
        _commitName = commit;
        NameBox.PlaceholderText = placeholder;
        string name = MailText.Format("Header_RenameName_Format", currentName);
        ToolTipService.SetToolTip(RenameButton, name);
        AutomationProperties.SetName(RenameButton, name);
        RenameButton.Visibility = Visibility.Visible;
        UpdatePencil();
    }

    public void DisableRename()
    {
        _commitName = null;
        RenameButton.Visibility = Visibility.Collapsed;
    }

    private void OnCrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index >= _trail.Count - 1) return;
        if (_trail[args.Index].Target is { } target) CrumbClicked?.Invoke(target);
    }

    private void OnTitlePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = true;
        UpdatePencil();
    }

    private void OnTitlePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        UpdatePencil();
    }

    private void OnRenameFocusChanged(object sender, RoutedEventArgs e) => UpdatePencil();

    private void UpdatePencil()
        => RenameButton.Opacity = _pointerOver || RenameButton.FocusState != FocusState.Unfocused ? 1 : 0;

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        NameBox.Text = _currentName;
        NameBox.Visibility = Visibility.Visible;
        Crumbs.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Collapsed;
        RenameButton.Visibility = Visibility.Collapsed;
        NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
    }

    private void OnNameBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Commit(NameBox.Text);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Commit(null, cancelled: true);
        }
    }

    private void OnNameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (NameBox.Visibility == Visibility.Visible) Commit(NameBox.Text);
    }

    // The page rebuilds the header from the session once the name is taken;
    // a cancel only puts the title back.
    private void Commit(string? name, bool cancelled = false)
    {
        NameBox.Visibility = Visibility.Collapsed;
        RenameButton.Visibility = Visibility.Visible;
        if (_trail.Count > 0) Crumbs.Visibility = Visibility.Visible;
        else TitleText.Visibility = Visibility.Visible;
        if (!cancelled) _commitName?.Invoke(name);
    }
}

public sealed record HeaderCrumb(string Label, MailDestination? Target);
