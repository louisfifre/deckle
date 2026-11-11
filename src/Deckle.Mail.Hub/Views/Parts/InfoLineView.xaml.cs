using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Mail.Hub;

// A line of secondary information. It carries no other command and opens
// nothing: its detail is read in Activity (spec, Secondary information; Open
// 2 leans on this reading).
public sealed partial class InfoLineView : UserControl
{
    private readonly TriageSession _session;
    private bool _pointerOver;

    public InfoLineView(TriageSession session, InfoLine line)
    {
        InitializeComponent();
        _session = session;
        Line = line;
        SentenceRun.Text = line.Sentence;
        // Two spaces, not a column: the sender follows the sentence.
        SenderRun.Text = "  " + line.Sender;
        LineText.Language = line.Language;
        AutomationProperties.SetName(this, MailText.Format("Join_Comma_Format", line.Sentence, line.Sender));
        AutomationProperties.SetName(DismissButton, MailText.Format("Info_DismissName_Format", line.Sentence));
    }

    public InfoLine Line { get; }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = true;
        UpdateCross();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        UpdateCross();
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e) => UpdateCross();

    // The list item that holds this line took or lost the keyboard focus: the
    // page relays it, since focus on the container does not reach its content.
    public void SetContainerFocused(bool focused)
    {
        _containerFocused = focused;
        UpdateCross();
    }

    private bool _containerFocused;

    // Opacity, not Visibility: the cross keeps its place and stays a Tab stop,
    // so the line never reflows when it shows.
    private void UpdateCross()
    {
        bool focused = _containerFocused || DismissButton.FocusState != FocusState.Unfocused;
        DismissButton.Opacity = _pointerOver || focused ? 1 : 0;
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        bool applied = _session.DismissInfo(Line.Id);
        DeckleMailHubSource.Log.GestureApplied("dismiss_info", applied);
    }
}
