using CommunityToolkit.WinUI.Controls;
using Deckle.App.SelfCheck;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.App;

// ── Self-check: the Diagnostics page with every fold open ────────────────────
//
// A fresh data root has every opt-in of the Diagnostics page off, and each
// fold then hides what it holds: Detailed logs and its sources, the
// retention of the logs on disk, the options of each corpus. A review pass
// would never see them. So, after the walk, the run turns them on in its own
// root, opens every fold and photographs the page again
// (settings-diagnostics-open). The values are written straight to the stores,
// past the consent dialogs, which belong to a person's gesture. Only this
// run's root is written, never the maintainer's.
public partial class App
{
    private static void SeedDiagnosticsSample()
    {
        LoggingSettings logging = LoggingSettingsService.Instance.Current;
        logging.DetailedLogs = true;
        logging.ApplicationLogToDisk = true;
        LoggingSettingsService.Instance.Save();

        TelemetrySettings telemetry = TelemetrySettingsService.Instance.Current;
        telemetry.CorpusEnabled = true;
        telemetry.RecordAudioCorpus = true;
        telemetry.AutocorrectText = true;
        TelemetrySettingsService.Instance.Save();
    }

    private static async Task CaptureDiagnosticsOpenAsync(
        SelfCheckReport report, Settings.SettingsWindow settings, string outputFolder, string captures)
    {
        SeedDiagnosticsSample();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool loaded = await settings.ShowPageAsync(typeof(Diagnostics.Logging.Views.DiagnosticsPage));
        const string slug = "diagnostics-open";
        SelfCheckSurface surface = new("settings", slug, "Diagnostics (every fold open)", loaded,
            clock.ElapsedMilliseconds, null, 0, 0, "page-render");
        if (loaded && settings.CurrentPage is { } page)
        {
            try
            {
                foreach (SettingsExpander expander in Descendants<SettingsExpander>(page))
                    expander.IsExpanded = true;
                // The folds open on the next frames.
                await Task.Delay(600);
                ElementImage.Result image = await ElementImage.CapturePageAsync(
                    page, Path.Combine(captures, $"settings-{slug}"));
                string[] files = [.. image.Files.Select(f => Path.GetRelativePath(outputFolder, f))];
                surface = surface with
                {
                    Png = files[0],
                    Parts = files.Length > 1 ? files : null,
                    Width = image.Width,
                    Height = image.Height,
                };
            }
            catch (Exception ex)
            {
                surface = surface with { Error = $"{ex.GetType().Name}: {ex.Message}" };
            }
        }
        report.Surfaces.Add(surface);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }
}
