using static Deckle.Hosting.SelfCheck.SelfCheckCapture;
using Deckle.Hosting.SelfCheck;
using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.Vision;

namespace Deckle.Atelier.App;

// The self-check's walk of the search button, called from WalkAtelierAsync
// with folders added, so the button is enabled, and before the added folders'
// walk resizes the window: right after that resize, the panel's first opening
// was placed under the address bar instead of under the button. It
// photographs the panel under the button as a click opens it, first with a
// probe that answers Command Palette absent (12.13), then with one that
// answers present (spec, Open 33). The probe is the walk's own, so the images
// do not depend on what this machine has installed. Nothing presses the
// panel's buttons: the walk never opens the Store, a browser or Command
// Palette.
internal static partial class AtelierSelfCheck
{
    private sealed class FixedCommandPaletteProbe(bool installed) : ICommandPaletteProbe
    {
        public bool IsInstalled() => installed;
    }

    private static async Task WalkAtelierSearchAsync(
        SelfCheckReport report, InterfaceCaptureRun run, string outputFolder, Action<string> step,
        AtelierWindow window, SelfCheckWalkGate gate)
    {
        gate.ThrowIfDone();

        // The panel is a popup: the image keeps it open.
        async Task Capture(string state, Func<Task> reach)
        {
            gate.ThrowIfDone();
            bool photographed = gate.Photographs(state);
            step($"atelier/{state}");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await reach();
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                report.Surfaces.Add(new SelfCheckSurface("atelier", state, null, false, clock.ElapsedMilliseconds, null, 0, 0,
                    "window-capture", Error: $"{ex.GetType().Name}: {ex.Message}"));
                report.Save(outputFolder);
                return;
            }
            if (photographed)
            {
                report.Surfaces.Add(await CaptureWindowFrameAsync(
                    run, window, "atelier", state, null, true, clock.ElapsedMilliseconds, 0, 0));
                report.Save(outputFolder);
            }
            window.CloseSearchForReview();
            await Task.Delay(300);
        }

        async Task Open(bool installed)
        {
            if (!window.IsSearchEnabledForReview)
                throw new InvalidOperationException("The search button is disabled with a folder added.");
            if (!await window.OpenSearchForReviewAsync(new FixedCommandPaletteProbe(installed)))
                throw new InvalidOperationException("The search panel did not open.");
        }

        await Capture("search-palette-absent", () => Open(installed: false));
        await Capture("search-palette-present", () => Open(installed: true));
    }
}
