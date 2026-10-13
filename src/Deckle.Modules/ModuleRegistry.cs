namespace Deckle.Modules;

// ── ModuleRegistry ────────────────────────────────────────────────────────────
//
// The runtime list of module descriptors, filled by the composition root
// (AppModules.RegisterAll); the module selector reads Modules to build its
// checklist and ModulePresence to know which boxes start checked. No Changed
// event, unlike SettingsModuleRegistry: the catalogue is fixed for the life of
// the process (a presence change takes a restart to act, since composition
// happens in OnLaunched).
public static class ModuleRegistry
{
    private static readonly object _gate = new();
    private static readonly List<ModuleDescriptor> _modules = [];

    // A snapshot copy, so a caller iterates it outside the lock.
    public static IReadOnlyList<ModuleDescriptor> Modules
    {
        get { lock (_gate) return _modules.OrderBy(m => m.Order).ToList(); }
    }

    // Idempotent: AppModules.RegisterAll runs on several startup paths, and a
    // second Register of the same id must not duplicate the selector entry.
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

    public static ModuleDescriptor? Find(string id)
    {
        lock (_gate) return _modules.Find(m => m.Id == id);
    }
}
