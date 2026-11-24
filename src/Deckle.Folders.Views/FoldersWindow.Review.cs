using Microsoft.UI.Xaml;

namespace Deckle.Folders.Views;

// What the self-check drives to photograph the window in each of its states.
// Each step takes the same path a click takes and awaits what it starts, so
// the image shows the state settled.
public sealed partial class FoldersWindow
{
    // Set when a tool threw while it showed a folder; the self-check fails
    // the image that should have shown it.
    public Exception? LastToolFailure { get; private set; }

    public bool HasAddedFolders => _folders.Folders.Count > 0;

    public IReadOnlyList<string> ToolIds => [.. _tools.Select(t => t.Id)];

    public async Task AddFolderForReviewAsync(string path)
    {
        LastToolFailure = null;
        await AddAndOpen(path);
        ThrowIfToolFailed("files");
    }

    public async Task ShowToolForReviewAsync(string toolId)
    {
        LastToolFailure = null;
        SwitchToWithoutShowing(_tools.First(t => t.Id == toolId));
        await ShowCurrentToolAsync();
        ThrowIfToolFailed(toolId);
    }

    // Selects an item of the current folder by name, as a click would, and
    // shows it with the active tool.
    public async Task SelectForReviewAsync(string name)
    {
        if (Place is null) return;
        _selection = System.IO.Path.Combine(Place.Path, name);
        await ShowCurrentToolAsync();
    }

    // Clears the selection and shows the folder again, so the panel holds
    // its line for an empty selection.
    public async Task ClearSelectionForReviewAsync()
    {
        if (Place is null) return;
        _selection = null;
        await ShowCurrentToolAsync();
    }

    // The details toggle as a click sets it.
    public void SetDetailsOnForReview(bool on)
    {
        DetailsToggle.IsChecked = on;
        OnDetailsToggleClick(DetailsToggle, new RoutedEventArgs());
    }

    public async Task OpenForReviewAsync(string name)
    {
        if (Place is null) return;
        await NavigateTo(Place.Enter(System.IO.Path.Combine(Place.Path, name)));
    }

    public void SetPaneOpenForReview(bool open) => _pane.SetOpen(open);

    // The address bar's text mode (12.12b), as Ctrl+L opens it.
    public void EditAddressForReview() => Address.EditAddress();

    private void SwitchToWithoutShowing(IFoldersTool tool)
    {
        _activeTool = tool;
        MarkActiveTool();
    }

    private void ThrowIfToolFailed(string toolId)
    {
        if (LastToolFailure is { } failure)
            throw new InvalidOperationException($"The {toolId} tool failed to show the folder.", failure);
    }
}
