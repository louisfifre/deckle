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
// Local errors are displayed in the section ErrorBar; the LlmPage status bar
// is only for Ollama availability.
//
// No GGUF import or pull here: `ollama create` and `ollama pull` already do it
// from the shell, so the section stays list, refresh and delete.

public sealed partial class LlmModelsSection : UserControl
{
    private LlmOllamaContext? _context;

    // Cancels in-flight Ollama operations when the user closes Settings or
    // navigates away, so post-await UI updates never touch an unloaded
    // section. Recreated on Loaded because LlmPage is cached
    // (NavigationCacheMode.Required).
    private CancellationTokenSource _sectionCts = new();

    public event EventHandler? RefreshRequested;

    public LlmModelsSection()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (_sectionCts.IsCancellationRequested)
            {
                _sectionCts.Dispose();
                _sectionCts = new CancellationTokenSource();
            }
        };

        Unloaded += (_, _) =>
        {
            // No Dispose here: an active await could still read the token. The
            // next Loaded rotates it.
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
                        // The user closed Settings: nothing to surface. -1 because
                        // no elapsed time is measured here.
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
                Text = Loc.Get("Settings_NoOllamaModels"),
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
