using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Transcription.Views;

public partial class TranscriptionViewModel
{
    [ObservableProperty]
    public partial string ModelsDirectory { get; set; }

    partial void OnModelsDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    [ObservableProperty]
    public partial bool UseGpu { get; set; }

    partial void OnUseGpuChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }
}
