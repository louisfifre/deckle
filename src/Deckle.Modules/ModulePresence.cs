using Deckle.Core;

namespace Deckle.Modules;

// ── ModulePresence ────────────────────────────────────────────────────────────
//
// The app-side view of the presence choice: which modules the user chose to
// have installed. The composition root asks IsPresent before composing a
// module's engine or registering its settings pages; the module selector reads
// Choice for its initial checkboxes and calls Save when the user commits.
//
// Presence is the choice axis only. Each domain module owns the probe for its
// heavy assets; the setup plan composes those probes without duplicating them in
// the presence catalogue.
//
// No choice on disk means everything is present, so older installs and dev
// builds keep every module. A module the choice never offered (added to the
// catalogue after it was made) counts as present too: see PresenceChoice.
public static class ModulePresence
{
    private static readonly object _gate = new();
    private static PresenceChoice? _choice;
    private static bool _loaded;

    public static string FilePath => FilePathUnder(AppPaths.UserDataRoot);

    // The same location under an arbitrary data root — for the installer
    // companion writing the choice into a root this process did not resolve
    // (its own AppPaths froze on the default at first touch).
    public static string FilePathUnder(string dataRoot) =>
        Path.Combine(dataRoot, "modules", "presence.json");

    // Asks the choice alone, never the registry: the answer must not depend on
    // whether the catalogue has been filled yet when the composition root asks.
    public static bool IsPresent(string id)
    {
        PresenceChoice? choice = Load();
        return choice is null || choice.Includes(id);
    }

    // Null when no choice is recorded: the selector then defaults to everything
    // checked. Otherwise the kept ids plus the modules the choice never offered,
    // the same set IsPresent answers from, so the checkboxes show what boots.
    public static IReadOnlySet<string>? Choice =>
        Load()?.Resolve(Catalogue());

    // Records the choice and adopts it in place, so a reader after Save sees
    // what was written without a reload.
    public static void Save(IReadOnlyCollection<string> present)
    {
        IReadOnlyCollection<string> known = Catalogue();
        PresenceFile.SaveTo(FilePath, present, known);
        lock (_gate)
        {
            _choice = new PresenceChoice(
                new HashSet<string>(present, StringComparer.Ordinal),
                new HashSet<string>(known, StringComparer.Ordinal));
            _loaded = true;
        }
        DeckleModulesSource.Log.PresenceSaved();
        DeckleModulesSource.Log.PresenceSavedDetail(
            string.Join(",", present), string.Join(",", known), FilePath);
    }

    // Save into a root this process did not resolve, for the installer
    // companion. It adopts nothing: this process never reads that root back.
    public static void SaveUnder(string dataRoot, IReadOnlyCollection<string> present) =>
        PresenceFile.SaveTo(FilePathUnder(dataRoot), present, Catalogue());

    // What the catalogue offers now, recorded with every choice as "known". The
    // registry is read here and not in PresenceFile, which stays path-only.
    private static IReadOnlyCollection<string> Catalogue() =>
        [.. ModuleRegistry.Modules.Select(m => m.Id)];

    private static PresenceChoice? Load()
    {
        lock (_gate)
        {
            if (_loaded) return _choice;
            _choice = PresenceFile.LoadFrom(FilePath);
            _loaded = true;
            DeckleModulesSource.Log.PresenceLoaded();
            DeckleModulesSource.Log.PresenceLoadedDetail(
                _choice is null ? "default-all" : "file",
                _choice is null ? "" : string.Join(",", _choice.Present),
                _choice is null ? "" : string.Join(",", _choice.Known));
            return _choice;
        }
    }
}
