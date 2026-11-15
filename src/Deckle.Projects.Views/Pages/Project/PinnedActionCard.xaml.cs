using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// What a pinned action's card draws: the menu's label, the script's file name
// with its variant, and the state the engine worked out.
internal sealed record PinnedCardModel(ActionRef Action, string Title, string Detail, PinnedCardState State);

// A pinned action's card. It holds no logic: the section gives it a model each
// time the board, the menu or the clock moves, and hears one of two requests
// when its button is pressed.
internal sealed partial class PinnedActionCard : UserControl
{
    private ActionButtonMode _mode;

    public PinnedActionCard(ActionRef action)
    {
        Action = action;
        InitializeComponent();
    }

    public ActionRef Action { get; }

    // "Run" was pressed.
    public event Action<ActionRef>? RunRequested;

    // "Show terminal" was pressed.
    public event Action<ActionRef>? ShowTerminalRequested;

    // The button, for the self-check to find and for a test to press.
    public Button Button => ActionButton;

    public string StateText => Line.Text;

    public RunLineKind? StateKind => Line.Kind;

    public string LabelText => TitleText.Text;

    public bool ButtonIsEnabled => ActionButton.IsEnabled;

    public string ButtonText => ActionLabel.Text;

    public void Apply(PinnedCardModel model)
    {
        TitleText.Text = model.Title;
        DetailText.Text = model.Detail;
        Line.Apply(model.State.Line);

        _mode = model.State.Button;
        bool show = _mode == ActionButtonMode.ShowTerminal;
        ActionIcon.Glyph = show ? ProjectsGlyphs.OpenInNew : ProjectsGlyphs.Play;
        ActionLabel.Text = ProjectsText.Get(show ? "Pinned_ShowTerminal" : "Pinned_Run");
        ActionButton.IsEnabled = model.State.ButtonEnabled;

        // Two cards say "Run": the name tells a screen reader which one.
        AutomationProperties.SetName(
            ActionButton,
            ProjectsText.Format(show ? "Pinned_ShowTerminalNamed_Format" : "Pinned_RunNamed_Format", model.Title));
        AutomationProperties.SetName(this, model.Title);
        AutomationProperties.SetHelpText(this, model.State.Line.Text);
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (_mode == ActionButtonMode.ShowTerminal) ShowTerminalRequested?.Invoke(Action);
        else RunRequested?.Invoke(Action);
    }
}
