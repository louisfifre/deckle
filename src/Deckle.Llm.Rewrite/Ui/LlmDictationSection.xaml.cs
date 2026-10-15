using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage "While you dictate" section ───────────────────────────────────
//
// What the active profile does before the dictation stops: batch rewriting
// and its batch size, composed from the ViewModel's manifest. The composer
// subscribes to the ViewModel, so the cards reflect Load() and their own
// resets with no code-behind sync; the auto-save lives in the VM's partial
// setters.

public sealed partial class LlmDictationSection : UserControl
{
    public LlmDictationViewModel ViewModel { get; } = new();

    // Held in a field so its subscription to the ViewModel lives as long as
    // the cached page.
    private readonly SettingsComposer _composer;

    public LlmDictationSection()
    {
        InitializeComponent();

        _composer = new SettingsComposer(BatchHost, ViewModel);
        _composer.Compose(ViewModel.BatchRewriting);
    }

    // A reset elsewhere may have replaced the values while the page sat cached.
    public void Reload() => ViewModel.Load();
}
