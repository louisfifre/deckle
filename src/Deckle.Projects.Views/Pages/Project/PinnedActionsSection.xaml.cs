using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// What the section can show in place of, or beside, its cards.
internal enum PinnedMenuKind
{
    // The project has a script menu and it can be run: the cards, the note
    // under them, and "Pin an action...".
    Listed,

    // No scripts\menu.ps1 (board P2): the notice with the install link, and no
    // cards.
    NoMenu,

    // Neither pwsh nor powershell is on the machine: nothing can be run.
    NoShell,

    // The menu is there and could not be read: its error, and the cards the
    // person pinned (the engine still runs them).
    Unreadable,
}

internal sealed record PinnedSectionModel(
    PinnedMenuKind Menu,
    string? ErrorLines,
    IReadOnlyList<PinnedCardModel> Cards);

// The section. It keeps its cards while the set of pinned actions is the same,
// and only redraws their words: a card that was rebuilt at every tick of the
// running time would lose the focus of the button the person just pressed.
internal sealed partial class PinnedActionsSection : UserControl
{
    private readonly Dictionary<ActionRef, PinnedActionCard> _cards = [];
    private List<ActionRef> _order = [];

    public PinnedActionsSection()
    {
        InitializeComponent();
        TitleText.Text = ProjectsText.Get("Pinned_Title");
        PinIcon.Glyph = ProjectsGlyphs.Add;
        PinLabel.Text = ProjectsText.Get("Pinned_Pin");
        NoteText.Text = ProjectsText.Get("Pinned_Note");
        NoShellText.Text = ProjectsText.Get("Pinned_NoShell");
    }

    // "Run" on a card.
    public event Action<ActionRef>? RunRequested;

    // "Show terminal" on a card.
    public event Action<ActionRef>? ShowTerminalRequested;

    // "Pin an action..." was pressed; the page opens the list under the button.
    public event Action? PinRequested;

    public Button PinButton => PinActionButton;

    public NoScriptMenuNotice NoMenu => NoMenuNotice;

    public ScriptMenuErrorNotice ErrorNotice => MenuErrorNotice;

    public IReadOnlyList<PinnedActionCard> CardsForReview => [.. _order.Select(a => _cards[a])];

    public bool NoteIsShown => NoteText.Visibility == Visibility.Visible;

    public void Apply(PinnedSectionModel model)
    {
        PinnedMenuKind kind = model.Menu;

        NoMenuNotice.Visibility = kind == PinnedMenuKind.NoMenu ? Visibility.Visible : Visibility.Collapsed;
        NoShellText.Visibility = kind == PinnedMenuKind.NoShell ? Visibility.Visible : Visibility.Collapsed;
        MenuErrorNotice.Visibility = kind == PinnedMenuKind.Unreadable ? Visibility.Visible : Visibility.Collapsed;
        if (kind == PinnedMenuKind.Unreadable) MenuErrorNotice.Show(model.ErrorLines ?? "");

        // "Pin an action..." lists what the menu gives, so it needs a menu that
        // was read; P2 draws the title alone.
        PinActionButton.Visibility = kind == PinnedMenuKind.Listed ? Visibility.Visible : Visibility.Collapsed;

        bool showCards = kind is PinnedMenuKind.Listed or PinnedMenuKind.Unreadable;
        IReadOnlyList<PinnedCardModel> cards = showCards ? model.Cards : [];
        SetCards(cards);
        Cards.Visibility = cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // "Actions run in a terminal window, ..." belongs with a menu that can be
        // pinned from; with nothing pinned it still reads under the empty list
        // (spec).
        NoteText.Visibility = kind == PinnedMenuKind.Listed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetCards(IReadOnlyList<PinnedCardModel> models)
    {
        List<ActionRef> order = [.. models.Select(m => m.Action)];
        if (!order.SequenceEqual(_order))
        {
            // Cards are made again when the set changes, not kept: the repeater
            // would otherwise be handed elements that still have the repeater
            // as their parent (see ProjectGrid). The rebuilt cards are drawn
            // from the models just below.
            var elements = new List<UIElement>();
            _cards.Clear();
            foreach (ActionRef action in order)
            {
                var card = new PinnedActionCard(action);
                card.RunRequested += a => RunRequested?.Invoke(a);
                card.ShowTerminalRequested += a => ShowTerminalRequested?.Invoke(a);
                _cards[action] = card;
                elements.Add(card);
            }

            _order = order;
            Cards.ItemsSource = elements;
        }

        foreach (PinnedCardModel model in models) _cards[model.Action].Apply(model);
    }

    private void OnPinClick(object sender, RoutedEventArgs e) => PinRequested?.Invoke();
}
