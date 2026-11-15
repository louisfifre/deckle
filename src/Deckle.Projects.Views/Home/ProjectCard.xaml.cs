using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Deckle.Projects.Views;

// A project's card on Home (spec, Home, "A card"). Clicking the card, outside
// its two buttons, opens the project's page; the footer button runs the main
// action; "..." and a right click open the one menu; the card can be dragged
// onto a category.
//
// The card draws what the workspace knows when Refresh is called and keeps
// nothing subscribed: the page that holds the cards listens to the board, the
// readings and the store, and calls Refresh on the card concerned.
internal sealed partial class ProjectCard : UserControl
{
    private readonly ProjectsContext _context;
    private readonly IProjectCardCommands _commands;

    public ProjectCard(ProjectsContext context, IProjectCardCommands commands, ProjectEntry project)
    {
        _context = context;
        _commands = commands;
        ProjectId = project.Id;
        InitializeComponent();

        TrimmedTextToolTip.Attach(NameText);

        PointerEntered += (_, _) => VisualStateManager.GoToState(this, "PointerOver", true);
        PointerExited += (_, _) => VisualStateManager.GoToState(this, "Normal", true);
        PointerCanceled += (_, _) => VisualStateManager.GoToState(this, "Normal", true);
        PointerCaptureLost += (_, _) => VisualStateManager.GoToState(this, "Normal", true);
        Tapped += OnTapped;
        KeyDown += OnKeyDown;
        ContextRequested += OnContextRequested;

        CanDrag = true;
        DragStarting += (_, args) => ProjectDragData.Put(args.Data, ProjectId);

        MoreButton.Click += (_, _) => ShowMenu(MoreButton, null);
        MainAction.Invoked += () => _ = _context.RunMainActionAsync(ProjectId);

        Refresh();
    }

    public string ProjectId { get; }

    // The "..." button, for the self-check to find.
    public Button More => MoreButton;

    // Draws the project as the store, the readings and the board say now. A
    // project that left the store is not drawn: the page rebuilds without it.
    public void Refresh()
    {
        if (_context.Store.Find(ProjectId) is not { } project) return;

        string name = project.DisplayName;
        NameText.Text = name;
        MetaText.Text = CardMeta.Text(_context.Workspace.Readings.Get(ProjectId));

        string more = ProjectsText.Format("Card_More_Format", name);
        ToolTipService.SetToolTip(MoreButton, more);
        AutomationProperties.SetName(MoreButton, more);
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetHelpText(this, MetaText.Text);

        // No button when there is no main action, or when the folder is gone;
        // the footer keeps its height so the cards of a row line up.
        MainActionState? action = MainActionState.Of(_context.Workspace, project);
        MainAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        if (action is not null) MainAction.Apply(action);
    }

    // Opens the menu under "...", as pressing the button does; what the
    // self-check calls to photograph board H3. A menu a person opens may leave
    // the window's edge, in a window of its own that a capture of this window
    // does not see (and that the self-check's guard takes for a window on a
    // monitor). The review's menu is kept inside the window, as the Mail
    // window's capture steps do.
    public void ShowMenuForReview() => ShowMenu(MoreButton, null, constrainToWindow: true);

    private void ShowMenu(FrameworkElement anchor, Windows.Foundation.Point? at, bool constrainToWindow = false)
    {
        if (_context.Store.Find(ProjectId) is not { } project) return;
        MenuFlyout menu = ProjectCardMenu.Build(_context, project, _commands);
        if (constrainToWindow)
        {
            menu.ShouldConstrainToRootBounds = true;
            // Inside the window the menu's acrylic has nothing to blur, so the
            // page shows through it and the words overlap. The review's menu
            // takes the solid fill acrylic falls back to.
            menu.MenuFlyoutPresenterStyle = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"MenuFlyoutPresenter\" "
                + "BasedOn=\"{StaticResource DefaultMenuFlyoutPresenterStyle}\">"
                + "<Setter Property=\"Background\" Value=\"{ThemeResource SolidBackgroundFillColorQuarternaryBrush}\" /></Style>");
        }
        if (at is { } position) menu.ShowAt(anchor, new FlyoutShowOptions { Position = position });
        else menu.ShowAt(anchor);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        // A right click opens the menu where the pointer is; the menu key and
        // Shift+F10 give no position, and the menu opens under "...".
        if (args.TryGetPosition(this, out Windows.Foundation.Point position)) ShowMenu(this, position);
        else ShowMenu(MoreButton, null);
    }

    // The card's own click. A press on one of its two buttons is theirs: the
    // button handles its click, and the tap still reaches the card.
    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsWithin(e.OriginalSource, MoreButton) || IsWithin(e.OriginalSource, MainAction)) return;
        _context.Navigate(new ProjectsTarget.Project(ProjectId));
    }

    // Enter and Space on the card itself, as on a button.
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            _context.Navigate(new ProjectsTarget.Project(ProjectId));
        }
    }

    private static bool IsWithin(object? source, DependencyObject ancestor)
    {
        for (DependencyObject? e = source as DependencyObject; e is not null; e = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(e))
            if (ReferenceEquals(e, ancestor)) return true;
        return false;
    }
}
