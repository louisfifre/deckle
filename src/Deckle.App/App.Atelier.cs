using System.Diagnostics;
using Deckle.Atelier.Views;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Modules;

namespace Deckle.App;

// The Atelier's window, opened from the tray like the other windows: created
// on first use by the same lazy path, placed where the person left it, and
// dropped on close so the next open builds a fresh one. The module composes
// nothing at start: with no window open it costs nothing. Its data lives in
// the module's own folder of the data root.
public partial class App
{
    private AtelierWindow? _atelierWindow;

    private static bool AtelierPresent => ModulePresence.IsPresent(ModuleIds.Atelier);

    private void ShowAtelierWindowLazy()
    {
        if (!AtelierPresent) return;
        EnsureAtelierWindow().ShowAndActivate();
    }

    private AtelierWindow EnsureAtelierWindow()
    {
        if (_atelierWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("atelier",
                () => new AtelierWindow(AppPaths.GetModuleDirectory(ModuleIds.Atelier)));
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Atelier);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Atelier);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_atelierWindow, window)) _atelierWindow = null;
            };

            _atelierWindow = window;
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("atelier", sw.ElapsedMilliseconds);
        }
        return _atelierWindow;
    }
}
