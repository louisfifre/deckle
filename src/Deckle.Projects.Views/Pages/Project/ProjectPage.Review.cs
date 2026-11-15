using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// What the self-check drives and reads to photograph the project page in each of
// its states (boards P1 to P4). Each step takes the path a click takes.
internal sealed partial class ProjectPage
{
    // Completes when the first reading of the folder, the menu and the estimate
    // is on screen, so an image shows the page settled.
    public Task SettledForReview => _initialLoad;

    public bool IsRenamingForReview => NameEditor.IsEditing;

    // The box the person types in while the name is being edited, else null.
    public TextBox? RenameBoxForReview => NameEditor.Box;

    // Turns the name into a text box, as clicking it or its pencil does (board P4).
    public void BeginRenameForReview() => NameEditor.BeginEdit();

    // Opens the list of versions under the readout (board P2); false when the
    // page found nothing worth listing.
    public bool OpenVersionsForReview()
    {
        if (!Readouts.InfoIsShown) return false;
        Readouts.OpenVersions();
        return true;
    }

    public void CloseVersionsForReview() => Readouts.CloseVersions();

    public bool VersionsFlyoutIsOpenForReview => Readouts.FlyoutIsOpen;

    // Presses "Copy the install command" (board P2); false when it is not shown.
    public bool CopyInstallCommandForReview()
    {
        if (Pinned.NoMenu.Visibility != Visibility.Visible) return false;
        Pinned.NoMenu.Copy();
        return true;
    }

    // What the page says, line by line, so a check can compare it with the
    // board: the breadcrumb, the name, the path, the header's buttons and
    // their line, the readouts, the links and the cards. A line that is not
    // drawn is not listed.
    public IReadOnlyList<string> OutlineForReview
    {
        get
        {
            List<string> lines = [];
            void Add(string label, string? value)
            {
                if (!string.IsNullOrEmpty(value)) lines.Add($"{label}: {value}");
            }

            Add("name", NameEditor.Text);
            Add("path", PathText.Text);
            if (BreadcrumbHost.Content is Breadcrumb crumb)
                Add("breadcrumb", Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(crumb));
            if (ScriptMenuButton.Visibility == Visibility.Visible) Add("button", ScriptMenuLabel.Text);
            if (MainButton.Visibility == Visibility.Visible)
                Add("accent", MainLabel.Text + (MainButton.IsEnabled ? "" : " (off)"));
            if (ShowTerminalButton.Visibility == Visibility.Visible) Add("button", ShowTerminalLabel.Text);
            if (MainLine.Visibility == Visibility.Visible) Add("main line", $"{MainLine.Kind}: {MainLine.Text}");
            if (MissingText.Visibility == Visibility.Visible) Add("missing", MissingText.Text);

            if (Readouts.Visibility == Visibility.Visible)
            {
                Add("version", Readouts.VersionText + (Readouts.InfoIsShown ? " (i)" : ""));
                Add("uncommitted", Readouts.UncommittedText);
                Add("agent", Readouts.AgentText);
            }
            if (OpenIn.Visibility == Visibility.Visible)
                Add("open in", string.Join(", ", OpenIn.LabelsForReview));
            if (Pinned.Visibility == Visibility.Visible)
            {
                if (Pinned.PinButton.Visibility == Visibility.Visible) Add("pin", "Pin an action…");
                if (Pinned.NoMenu.Visibility == Visibility.Visible) Add("no menu", Pinned.NoMenu.LinkText);
                if (Pinned.ErrorNotice.Visibility == Visibility.Visible) Add("menu error", Pinned.ErrorNotice.Lines);
                foreach (PinnedActionCard card in Pinned.CardsForReview)
                    Add("card", $"{card.LabelText} | {card.StateKind}: {card.StateText} | {card.ButtonText}{(card.ButtonIsEnabled ? "" : " (off)")}");
                if (Pinned.NoteIsShown) Add("note", "shown");
            }
            return lines;
        }
    }
}
