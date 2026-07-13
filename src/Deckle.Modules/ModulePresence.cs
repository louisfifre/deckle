using Deckle.Core;

namespace Deckle.Modules;

// ── ModulePresence ────────────────────────────────────────────────────────────
//
// The app-side view of the presence choice: which modules the user chose to
// have installed. The composition root asks IsPresent before composing a
// module's engine or registering its settings pages; the module selector reads
// Choice for its initial checkboxes and calls Save when the user commits.
//
// Presence is the choice axis only. Whether a chosen module's heavy assets are
// actually on disk is the descriptor's IsProvisioned — a chosen-but-unprovisioned
// module is present (its pages register, its setup entry points show) and simply
// not runnable yet.
//
// No choice on disk means everything is present: installs that predate the
// presence model, and dev builds, keep behaving as before the model existed.
public static class ModulePresence
{
    private static readonly object _gate = new();
    private static IReadOnlySet<string>? _present;
    private static bool _loaded;

    // The choice file, beside the per-module folders it governs.
    public static string FilePath => Path.Combine(AppPaths.ModulesDirectory, "presence.json");

    // Whether the user chose to have this module installed. True for every id
    // while no choice is recorded.
    public static bool IsPresent(string id)
    {
        IReadOnlySet<string>? present = Load();
        return present is null || present.Contains(id);
    }

    // The recorded choice, or null when none is — the selector then defaults to
    // everything checked.
    public static IReadOnlySet<string>? Choice => Load();

    // Records the choice and adopts it in place, so a reader after Save sees
    // what was written without a reload.
    public static void Save(IReadOnlyCollection<string> present)
    {
        PresenceFile.SaveTo(FilePath, present);
        lock (_gate)
        {
            _present = new HashSet<string>(present, StringComparer.Ordinal);
            _loaded = true;
        }
        DeckleModulesSource.Log.PresenceSaved();
        DeckleModulesSource.Log.PresenceSavedDetail(string.Join(",", present), FilePath);
    }

    private static IReadOnlySet<string>? Load()
    {
        lock (_gate)
        {
            if (_loaded) return _present;
            _present = PresenceFile.LoadFrom(FilePath);
            _loaded = true;
            DeckleModulesSource.Log.PresenceLoaded();
            DeckleModulesSource.Log.PresenceLoadedDetail(
                _present is null ? "default-all" : "file",
                _present is null ? "" : string.Join(",", _present));
            return _present;
        }
    }
}
