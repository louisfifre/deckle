using CommunityToolkit.Mvvm.Input;

namespace Deckle.Orchestration;

// One folder of the "Folders to scan" list. Like AppEntryRow, removal calls
// back into the view-model, which owns the write.
public sealed partial class ScanFolderRow
{
    private readonly Action<ScanFolderRow> _onRemoved;

    public string Path { get; }

    public ScanFolderRow(string path, Action<ScanFolderRow> onRemoved)
    {
        Path = path;
        _onRemoved = onRemoved;
    }

    [RelayCommand]
    private void Remove() => _onRemoved(this);
}
