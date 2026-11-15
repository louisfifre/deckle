using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// A project's row on a category page (spec, Category page). Clicking it opens
// the project's page (the list's ItemClick does that, so Enter and Space work
// too); its button runs the main action; its handle is where a drag starts.
//
// The row draws what the workspace knows when Refresh is called and keeps
// nothing subscribed: the page listens to the store, the readings and the
// board, and calls Refresh on the row concerned.
internal sealed partial class CategoryRow : UserControl
{
    private readonly ProjectsContext _context;

    public CategoryRow(ProjectsContext context, ProjectEntry project)
    {
        _context = context;
        ProjectId = project.Id;
        InitializeComponent();

        TrimmedTextToolTip.Attach(NameText);
        TrimmedTextToolTip.Attach(PathText);
        ToolTipService.SetToolTip(Grip, ProjectsText.Get("Category_Grip"));

        PointerEntered += (_, _) => VisualStateManager.GoToState(this, "PointerOver", true);
        PointerExited += (_, _) => VisualStateManager.GoToState(this, "Normal", true);
        PointerCanceled += (_, _) => VisualStateManager.GoToState(this, "Normal", true);
        PointerCaptureLost += (_, _) => VisualStateManager.GoToState(this, "Normal", true);

        // handledEventsToo: the list marks the press as handled when it starts
        // to track a drag, and the page still needs to know where it landed.
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);

        MainAction.Invoked += () => _ = _context.RunMainActionAsync(ProjectId);

        Refresh();
    }

    public string ProjectId { get; }

    // Whether the last press landed on the handle. The page reads it when the
    // list asks to start a drag, and cancels the drag when it is false.
    public bool GripPressed { get; private set; }

    // The handle and the button, for the self-check to find.
    public FrameworkElement GripElement => Grip;

    public MainActionButton Action => MainAction;

    // Draws the project as the store, the readings and the board say now. A
    // project that left the store is not drawn: the page rebuilds without it.
    public void Refresh()
    {
        if (_context.Store.Find(ProjectId) is not { } project) return;

        string name = project.DisplayName;
        NameText.Text = name;
        PathText.Text = project.Path;
        MetaText.Text = CardMeta.Text(_context.Workspace.Readings.Get(ProjectId));

        AutomationProperties.SetName(this, name);
        AutomationProperties.SetHelpText(this, MetaText.Text);

        // No button when there is no main action, or when the folder is gone.
        MainActionState? action = MainActionState.Of(_context.Workspace, project);
        MainAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        if (action is not null) MainAction.Apply(action);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e) =>
        GripPressed = IsWithin(e.OriginalSource, Grip);

    private static bool IsWithin(object? source, DependencyObject ancestor)
    {
        for (DependencyObject? e = source as DependencyObject; e is not null; e = VisualTreeHelper.GetParent(e))
            if (ReferenceEquals(e, ancestor)) return true;
        return false;
    }
}
