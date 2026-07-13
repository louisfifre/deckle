namespace Deckle.Modules;

// ── ModuleRegistry ────────────────────────────────────────────────────────────
//
// The runtime list of module descriptors — the presence catalogue's spine. The
// composition root fills it once at boot (AppModules.RegisterAll); the module
// selector reads Modules to build its checklist and ModulePresence to know
// which boxes start checked. Same lib-exposes-a-static / host-populates-it
// pattern as SettingsModuleRegistry, minus the Changed event: the catalogue is
// fixed for the life of the process (a presence change takes a restart to act,
// since composition happens in OnLaunched).
public static class ModuleRegistry
{
    // Guards the list against a Register racing a read. Modules snapshots under
    // it so a caller iterates a stable copy.
    private static readonly object _gate = new();
    private static readonly List<ModuleDescriptor> _modules = [];

    // The registered modules in selector order, as a snapshot copy.
    public static IReadOnlyList<ModuleDescriptor> Modules
    {
        get { lock (_gate) return _modules.OrderBy(m => m.Order).ToList(); }
    }

    // Add a module, or replace the existing one with the same Id (idempotent —
    // a second Register of the same id updates it rather than duplicating the
    // selector entry).
    public static void Register(ModuleDescriptor descriptor)
    {
        lock (_gate)
        {
            int existing = _modules.FindIndex(m => m.Id == descriptor.Id);
            if (existing >= 0) _modules[existing] = descriptor;
            else _modules.Add(descriptor);
        }
        DeckleModulesSource.Log.ModuleRegistered(
            descriptor.Id, descriptor.Order, string.Join(",", descriptor.DependsOn));
    }

    // The descriptor with this Id, or null when the catalogue does not know it.
    public static ModuleDescriptor? Find(string id)
    {
        lock (_gate) return _modules.Find(m => m.Id == id);
    }
}
