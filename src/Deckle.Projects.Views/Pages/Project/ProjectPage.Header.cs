using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Projects.Views;

// The header: on the left the breadcrumb, the name and the path; on the right,
// on the name's line, "Open script menu" and the main action's accent button, and
// on the path's line the main action's last result (spec, Project page, Header).
internal sealed partial class ProjectPage
{
    // The breadcrumb is made again only when what it says changes (the project
    // moved to another category, or its category was renamed).
    private string? _crumbKey;

    private void ConfigureHeader()
    {
        NameEditor.TextStyle = (Style)Application.Current.Resources["TitleTextBlockStyle"];
        NameEditor.HintText = ProjectsText.Get("Project_RenameHint");
        NameEditor.Committed += OnNameCommitted;

        TrimmedTextToolTip.Attach(PathText);

        ScriptMenuIcon.Glyph = ProjectsGlyphs.Apps;
        ScriptMenuLabel.Text = ProjectsText.Get("Project_OpenScriptMenu");
        MainIcon.Glyph = ProjectsGlyphs.Play;
        ShowTerminalIcon.Glyph = ProjectsGlyphs.OpenInNew;
        ShowTerminalLabel.Text = ProjectsText.Get("Pinned_ShowTerminal");
        MainLine.ShowTerminalRequested += () =>
        {
            if (Project?.MainAction is { } main) ShowTerminal(main.Action);
        };
    }

    private void ApplyHeader(ProjectEntry project, bool missing)
    {
        // The box holds what the person is typing: it is not overwritten while
        // it is open.
        if (!NameEditor.IsEditing) NameEditor.Text = project.DisplayName;
        PathText.Text = project.Path;
        ApplyBreadcrumb(project);
        ApplyHeaderAction(project, missing);
    }

    private void ApplyBreadcrumb(ProjectEntry project)
    {
        string? categoryName = project.CategoryId is { } id ? _context.Store.FindCategory(id)?.Name : null;
        string key = $"{project.CategoryId}|{categoryName}";
        if (key == _crumbKey) return;
        _crumbKey = key;
        BreadcrumbHost.Content = Breadcrumb.ForProjectPage(_context, project);
    }

    // Enter keeps the name. An empty name brings back the folder's name, and the
    // path never changes: the name is the person's label in Deckle only (spec).
    private void OnNameCommitted(string typed)
    {
        ProjectEntry? renamed = _context.Store.RenameProject(_projectId, string.IsNullOrWhiteSpace(typed) ? null : typed);
        if (renamed is not null) NameEditor.Text = renamed.DisplayName;
    }

    // "Open script menu" and the main action's button, the line under them.
    // A project without the script menu has neither button (board P2); one with
    // no main action has no accent button.
    private void ApplyHeaderAction(ProjectEntry project, bool missing)
    {
        bool hasMenu = !missing && HasScriptMenu(project);
        ScriptMenuButton.Visibility = hasMenu ? Visibility.Visible : Visibility.Collapsed;
        ScriptMenuButton.IsEnabled = _context.Workspace.Shell is not null;

        PinnedAction? main = project.MainAction;
        if (!hasMenu || main is null)
        {
            MainButton.Visibility = Visibility.Collapsed;
            ShowTerminalButton.Visibility = Visibility.Collapsed;
            MainLine.Visibility = Visibility.Collapsed;
            return;
        }

        HeaderActionState state = ActionStates.ForHeader(
            main,
            _context.Workspace.Board.ActiveRun(_projectId),
            _context.Workspace.Board.LastResult(_projectId, main.Action),
            CanStartAnAction(project, missing),
            _context.Workspace.Clock.GetUtcNow());

        bool showTerminal = state.Button == ActionButtonMode.ShowTerminal;

        // The two buttons take turns in the same place. A button that goes away
        // while the keyboard is on it would drop the focus to nowhere, so the
        // other one takes it.
        bool mainHadFocus = HasFocus(MainButton);
        bool terminalHadFocus = HasFocus(ShowTerminalButton);

        MainLabel.Text = state.Label;
        MainButton.IsEnabled = state.ButtonEnabled;
        MainButton.Visibility = showTerminal ? Visibility.Collapsed : Visibility.Visible;
        ShowTerminalButton.Visibility = showTerminal ? Visibility.Visible : Visibility.Collapsed;

        if (showTerminal && mainHadFocus) ShowTerminalButton.Focus(FocusState.Programmatic);
        else if (!showTerminal && terminalHadFocus) MainButton.Focus(FocusState.Programmatic);

        // The main button stays on screen but is switched off while another
        // action runs: with the keyboard on it, the focus would drop to nowhere,
        // so the button beside it takes it (as MainActionButton does).
        else if (!showTerminal && mainHadFocus && !state.ButtonEnabled && ScriptMenuButton is { IsEnabled: true, Visibility: Visibility.Visible })
            ScriptMenuButton.Focus(FocusState.Programmatic);

        MainLine.Apply(state.Line);
        MainLine.SetShowTerminal(
            !showTerminal && _context.Workspace.Board.ActiveRun(_projectId) is null
            && _context.Workspace.Runner.TerminalIsOpen(_projectId, main.Action));
        MainLine.Visibility = state.Line is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool HasFocus(Control control) =>
        XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), control);

    private void OnScriptMenuClick(object sender, RoutedEventArgs e)
    {
        if (Project is { } project) _context.Workspace.Opener.OpenScriptMenu(project.Path);
    }

    private void OnMainClick(object sender, RoutedEventArgs e) =>
        _ = StartAsync(() => _context.Workspace.StartMainActionAsync(_projectId));

    private void OnShowMainTerminalClick(object sender, RoutedEventArgs e)
    {
        if (Project?.MainAction is { } main) ShowTerminal(main.Action);
    }
}
