using CommunityToolkit.Mvvm.Input;

namespace Deckle.Orchestration;

// One folder of the "Folders to scan" list. Like AppEntryRow, removal calls
// back into the view-model, which owns the write.
public sealed partial class ScanFolderRow
{
    private readonly Action<ScanFolderRow> _onRemoved;

    public string Path { get; }

    // The icon-only remove button's accessible name, told apart by its folder.
    public string RemoveName => OrchestrationText.Format(
        "OrchestrationPage_RemoveFolderButton_NameFormat", Path);

    public ScanFolderRow(string path, Action<ScanFolderRow> onRemoved)
    {
        Path = path;
        _onRemoved = onRemoved;
    }

    [RelayCommand]
    private void Remove() => _onRemoved(this);
}
