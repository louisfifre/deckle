using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// The button named after a project's main action, as a card's footer and a
// category row draw it (spec, Home): a standard button with the Play glyph and
// the action's name, and beside it the run's state, a progress ring while an
// action of the project runs (the button is then disabled) or the warning glyph
// in the critical colour when the main action's last run failed.
//
// It holds no logic: the owner gives it a MainActionState (MainActionState.Of)
// each time the board or the readings change, and hears Invoked when the button
// is pressed. The page does the subscribing, so a button that leaves the screen
// leaves nothing subscribed behind it.
internal sealed class MainActionButton : UserControl
{
    private const double RingSize = 16;

    private readonly StackPanel _layout;
    private readonly Button _button;
    private readonly TextBlock _label;
    private readonly ProgressRing _ring;
    private readonly FontIcon _warning;

    public MainActionButton()
    {
        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _button = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = ProjectsGlyphs.Play, FontSize = 14 }, _label },
            },
        };
        _button.Click += (_, _) => Invoked?.Invoke();

        _ring = new ProgressRing
        {
            Width = RingSize,
            Height = RingSize,
            IsActive = false,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(_ring, ProjectsText.Get("Card_Running"));
        AutomationProperties.SetName(_ring, ProjectsText.Get("Card_Running"));

        _warning = Themed.Icon(ProjectsGlyphs.Warning, 14, "SystemFillColorCriticalBrush");
        _warning.Visibility = Visibility.Collapsed;
        _warning.VerticalAlignment = VerticalAlignment.Center;

        _layout = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _button, _ring, _warning },
        };
        Content = _layout;
    }

    // Where the run's state sits. After the button by default (a card's footer
    // is left-aligned, so the button stays put when the ring appears); before it
    // for an owner that right-aligns the button, so the button stays put there.
    public bool StateBeforeButton
    {
        get => _layout.Children[0] != _button;
        set
        {
            if (value == StateBeforeButton) return;
            _layout.Children.Clear();
            if (value) { _layout.Children.Add(_ring); _layout.Children.Add(_warning); _layout.Children.Add(_button); }
            else { _layout.Children.Add(_button); _layout.Children.Add(_ring); _layout.Children.Add(_warning); }
        }
    }

    // The button was pressed. The owner runs the action.
    public event Action? Invoked;

    // The button itself, for the self-check to find and for a test to press.
    public Button Button => _button;

    public void Apply(MainActionState state)
    {
        _label.Text = state.Label;

        // A button that is switched off while the keyboard is on it drops the
        // focus to nowhere (the run starts when it is pressed). The card or the
        // row around it takes the focus, and the person keeps their place.
        bool hadFocus = _button.FocusState != FocusState.Unfocused;
        _button.IsEnabled = state.Enabled;
        if (hadFocus && !state.Enabled) FocusOwner();
        AutomationProperties.SetName(_button, state.Label);

        bool running = state.Status == MainActionStatus.Running;
        _ring.IsActive = running;
        _ring.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        bool failed = state.Status == MainActionStatus.Failed;
        _warning.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        string failure = ProjectsText.Format("Card_LastFailed_Format", state.Label);
        ToolTipService.SetToolTip(_warning, failed ? failure : null);
        AutomationProperties.SetName(_warning, failed ? failure : "");
    }

    private void FocusOwner() => KeyboardFocus.HandToOwner(this);
}
