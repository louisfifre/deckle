using System;
using System.Threading;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Diagnostics;
using Deckle.Llm;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage Models section ────────────────────────────────────────────────
//
// Lists local Ollama models (via LlmOllamaContext) + Refresh button. Per-model
// Remove with confirmation ContentDialog. The host listens to RefreshRequested
// to relaunch page-side RefreshOllamaStateAsync after manual refresh.
//
// Local errors are displayed in the section ErrorBar; the global LlmPage
// StatusBar is only for Ollama availability state.
//
// GGUF import was removed: Ollama natively handles `ollama create` and
// `ollama pull` from the shell, so an in-app UI wrapper adds nothing beyond
// list + refresh + delete.

public sealed partial class LlmModelsSection : UserControl
{
    private LlmOllamaContext? _context;

    // Section lifecycle CTS. Cancels in-flight Ollama operations
    // (DeleteModelAsync) when the user closes SettingsWindow or navigates away
    // during the wait. Without this, the HTTP request continues until the 30s
    // timeout, and post-await UI updates try to touch an unloaded section: not
    // a guaranteed crash, but wasted resources. Recreated on every Loaded
    // because the section can be reloaded (NavigationCacheMode.Required on
    // LlmPage).
    private CancellationTokenSource _sectionCts = new();

    public event EventHandler? RefreshRequested;

    public LlmModelsSection()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // Rearm a fresh CTS on each return to the page.
            if (_sectionCts.IsCancellationRequested)
            {
                _sectionCts.Dispose();
                _sectionCts = new CancellationTokenSource();
            }
        };

        Unloaded += (_, _) =>
        {
            // Cancel: lets in-flight operations observe and abandon. No Dispose
            // here: an active await could still read the token. The next Loaded
            // rotates cleanly.
            try { _sectionCts.Cancel(); }
            catch (ObjectDisposedException) { /* already disposed, ignore */ }
        };
    }

    internal void Initialize(LlmOllamaContext context)
    {
        if (_context != null)
            _context.StateChanged -= OnContextStateChanged;

        _context = context;
        _context.StateChanged += OnContextStateChanged;
    }

    private void OnContextStateChanged(object? sender, EventArgs e) => Reload();

    public void Reload()
    {
        ModelsContainer.Children.Clear();

        bool enabled = _context?.Available ?? false;
        RefreshModelsButton.IsEnabled = enabled;

        if (!enabled) return;

        var models = _context?.Models ?? Array.Empty<OllamaModel>();

        foreach (var model in models)
        {
            string sizeText = model.Size > 0
                ? $"{model.Size / (1024.0 * 1024 * 1024):F1} GB"
                : "";

            var card = new SettingsCard
            {
                Header = model.Name,
                Description = sizeText
            };

            var delBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = Glyphs.Delete, FontSize = 14 },
                        new TextBlock { Text = Loc.Get("Settings_RemoveLabel") }
                    }
                }
            };
            string modelName = model.Name;
            delBtn.Click += async (_, _) =>
            {
                bool confirmed = await ConfirmationService.RequestAsync(
                    this.XamlRoot,
                    new ConfirmationRequest(
                        Loc.Get("Settings_RemoveModelDialog_Title"),
                        Loc.Format("Settings_RemoveModelDialog_Content_Format", modelName),
                        Loc.Get("Common_Remove"),
                        IsDestructive: true));
                if (confirmed)
                {
                    try
                    {
                        if (_context?.Service != null)
                        {
                            // Bind the 30s timeout to the section CTS so Unload
                            // (SettingsWindow close, navigate away) cancels
                            // the in-flight deletion instead of waiting 30s for
                            // nothing.
                            using var localCts = CancellationTokenSource.CreateLinkedTokenSource(_sectionCts.Token);
                            localCts.CancelAfter(TimeSpan.FromSeconds(30));
                            await _context.Service.DeleteModelAsync(modelName, localCts.Token);
                        }
                        // If the section was unloaded during the await, avoid
                        // triggering RefreshRequested, which would force a
                        // page-side Reload on a detached visual tree.
                        if (_sectionCts.IsCancellationRequested) return;
                        RefreshRequested?.Invoke(this, EventArgs.Empty);
                    }
                    catch (OperationCanceledException) when (_sectionCts.IsCancellationRequested)
                    {
                        // Section unloaded during deletion: silent; the user
                        // closed Settings, no need to surface it. Cross-cutting
                        // Cancellation sub-provider: the user closed the
                        // surface, no dedicated Stopwatch here.
                        DeckleCancellationSource.Log.OperationCancelled(
                            "llm-models", "user", -1);
                    }
                    catch (Exception ex)
                    {
                        if (_sectionCts.IsCancellationRequested) return;
                        ErrorBar.Title = Loc.Get("Settings_ErrorRemovingModel_Title");
                        ErrorBar.Message = ex.Message;
                        ErrorBar.IsOpen = true;
                    }
                }
            };

            card.Content = delBtn;
            ModelsContainer.Children.Add(card);
        }

        if (models.Count == 0)
        {
            ModelsContainer.Children.Add(new TextBlock
            {
                Text = "No models found in Ollama. Pull a model with `ollama pull` from your shell.",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(1, 4, 0, 0)
            });
        }
    }

    private void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
