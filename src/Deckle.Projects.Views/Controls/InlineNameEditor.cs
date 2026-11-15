using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Deckle.Projects.Views;

// A name that is a title until the person clicks it, and then a text box
// (spec, Renaming): the project's name on its page, a category's title on its
// page and on Home right after "New category".
//
//   at rest   the name, and beside it a small pencil that shows on hover or
//             when the keyboard is on the name (the spec keeps Louis's hover
//             mark; an always-visible pencil was proposed and not adopted)
//   editing   a 320 DIP text box holding the current name, selected, and the
//             hint beside it when the page gives one
//
// Enter keeps what was typed, Esc brings back the previous name, and leaving
// the box keeps it too, as File Explorer's rename does. The editor does not
// decide what an empty name means: it raises Committed with the trimmed text
// and the owner decides (a project falls back to its folder's name, a
// category keeps its title), then sets Text.
internal sealed class InlineNameEditor : UserControl
{
    // The box's width on the project page (board P4).
    public const double BoxWidth = 320;

    // The text box's left padding, which the box hangs out to the left.
    private const double BoxPadding = 10;

    private readonly TextBlock _text = new()
    {
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ContentControl _name;
    private readonly Button _pencil;
    private readonly StackPanel _atRest;
    private readonly TextBox _box;
    private readonly TextBlock _hint;
    private readonly StackPanel _editing;
    private readonly Grid _root;

    private bool _isEditing;
    private bool _pointerOver;
    private bool _keyboardFocus;

    public InlineNameEditor()
    {
        _name = new ContentControl
        {
            Content = _text,
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            // The name is the way in for the keyboard and for a screen reader.
            Padding = new Thickness(0),
        };
        _name.Tapped += (_, _) => BeginEdit();
        _name.KeyDown += OnNameKeyDown;
        _name.GotFocus += (_, _) => SetKeyboardFocus(_name.FocusState == FocusState.Keyboard);
        _name.LostFocus += (_, _) => SetKeyboardFocus(false);
        AutomationProperties.SetHelpText(_name, ProjectsText.Get("Project_Rename"));

        _pencil = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = ProjectsGlyphs.Edit, FontSize = 14 },
            // Out of the tab order: the name is the keyboard's way in, and a
            // second stop that does the same would only slow the keyboard.
            IsTabStop = false,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        string rename = ProjectsText.Get("Project_Rename");
        ToolTipService.SetToolTip(_pencil, rename);
        AutomationProperties.SetName(_pencil, rename);
        AutomationProperties.SetAccessibilityView(_pencil, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _pencil.Click += (_, _) => BeginEdit();

        _atRest = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { _name, _pencil },
        };
        _atRest.PointerEntered += (_, _) => SetPointerOver(true);
        _atRest.PointerExited += (_, _) => SetPointerOver(false);

        // The box wears the name's own size and weight (TextStyle) and is laid
        // out so the typed text starts where the title's did: its border and
        // left padding hang out to the left, its border and the missing
        // vertical padding keep the line's height (board P4).
        _box = new TextBox
        {
            Width = BoxWidth,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 0,
            Padding = new Thickness(BoxPadding, 0, 6, 0),
            Margin = new Thickness(-(BoxPadding + 1), -1, 0, -1),
        };
        _box.KeyDown += OnBoxKeyDown;
        _box.Loaded += (_, _) => HideClearButton();
        _box.LostFocus += (_, _) => Finish(commit: true, restoreFocus: false);

        _hint = Themed.Text("TextFillColorTertiaryBrush", "CaptionTextBlockStyle");
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.Visibility = Visibility.Collapsed;

        _editing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Visibility = Visibility.Collapsed,
            Children = { _box, _hint },
        };

        _root = new Grid { Children = { _atRest, _editing } };
        Content = _root;
        HorizontalContentAlignment = HorizontalAlignment.Left;
    }

    // Raised with the trimmed text when the person keeps what they typed. Not
    // raised on Esc.
    public event Action<string>? Committed;

    // Raised once the box has closed, whether the text was kept or not, so an
    // owner that hid its own title while the box was open can show it again.
    public event Action? Ended;

    public string Text
    {
        get => _text.Text;
        set
        {
            _text.Text = value;
            AutomationProperties.SetName(_name, value);
        }
    }

    // The style of the name at rest: the page's title, or a category's.
    public Style? TextStyle
    {
        get => _text.Style;
        set
        {
            _text.Style = value;
            _box.FontSize = _text.FontSize;
            _box.FontWeight = _text.FontWeight;
        }
    }

    // The built-in clear (X) button only empties the text. Beside "Esc to
    // cancel" it reads as a cancel, so it is taken out of the box. The TextBox
    // template shows and hides the part by visual state, which would undo a
    // Visibility, so the part is given no width and no focus instead.
    private void HideClearButton()
    {
        if (FindPart(_box, "DeleteButton") is not Button clear) return;
        clear.MinWidth = 0;
        clear.Width = 0;
        clear.IsTabStop = false;
        clear.IsHitTestVisible = false;
    }

    private static FrameworkElement? FindPart(DependencyObject node, string name)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
            if (child is FrameworkElement element && element.Name == name) return element;
            if (FindPart(child, name) is { } deeper) return deeper;
        }
        return null;
    }

    // The sentence beside the box while editing (board P4); none on Home.
    public string? HintText
    {
        get => _hint.Text;
        set
        {
            _hint.Text = value ?? "";
            _hint.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public bool IsEditing => _isEditing;

    // The box the person types in, for the self-check to read and a page to
    // measure; null while the name is at rest.
    public TextBox? Box => _isEditing ? _box : null;

    public void BeginEdit()
    {
        if (_isEditing) return;
        _isEditing = true;
        _box.Text = _text.Text;

        // The box is shorter than the title it replaces, so the editor keeps
        // the height the name had: nothing below it moves when editing starts
        // or ends (board P4). Measured when the name was never laid out, as on
        // a page that opens already renaming.
        double height = _atRest.ActualHeight;
        if (height <= 0)
        {
            _atRest.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            height = _atRest.DesiredSize.Height;
        }
        _root.MinHeight = height;

        _atRest.Visibility = Visibility.Collapsed;
        _editing.Visibility = Visibility.Visible;

        // The box takes the focus once it is in the tree: a page that opens
        // already renaming asks before it has been laid out.
        void Focus()
        {
            if (!_isEditing) return;
            _box.Focus(FocusState.Programmatic);
            _box.SelectAll();
        }

        if (IsLoaded) DispatcherQueue.TryEnqueue(Focus);
        else
        {
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                Loaded -= OnLoaded;
                DispatcherQueue.TryEnqueue(Focus);
            }
            Loaded += OnLoaded;
        }
    }

    // Closes the box as Esc does: the previous name stays.
    public void CancelEdit() => Finish(commit: false, restoreFocus: true);

    // restoreFocus is false when the focus left the box for something the
    // person clicked: it must keep the focus it was given.
    private void Finish(bool commit, bool restoreFocus)
    {
        // Enter hides the box, which loses its focus, which comes back here.
        if (!_isEditing) return;
        _isEditing = false;
        string typed = _box.Text.Trim();
        _editing.Visibility = Visibility.Collapsed;
        _atRest.Visibility = Visibility.Visible;
        _root.MinHeight = 0;
        if (restoreFocus) _name.Focus(FocusState.Programmatic);
        if (commit) Committed?.Invoke(typed);
        Ended?.Invoke();
    }

    private void OnBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Finish(commit: true, restoreFocus: true);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Finish(commit: false, restoreFocus: true);
        }
    }

    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter or VirtualKey.F2)
        {
            e.Handled = true;
            BeginEdit();
        }
    }

    private void SetPointerOver(bool over)
    {
        _pointerOver = over;
        ShowPencil();
    }

    private void SetKeyboardFocus(bool focus)
    {
        _keyboardFocus = focus;
        ShowPencil();
    }

    private void ShowPencil()
    {
        bool show = _pointerOver || _keyboardFocus;
        _pencil.Opacity = show ? 1 : 0;
        _pencil.IsHitTestVisible = show;
    }
}
