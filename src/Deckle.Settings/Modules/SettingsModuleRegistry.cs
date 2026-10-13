using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── SettingsModuleRegistry ────────────────────────────────────────────────────
//
// The runtime list of module-owned settings pages the shell's NavigationView
// materialises. The composition root Registers each module's descriptor;
// SettingsWindow reads Modules to build its nav and subscribes to Changed so an
// install/uninstall while the window is open re-materialises the band live. The
// shell stays a pure aggregator with zero literal module references; its one
// static page, General, is not in here.
public static class SettingsModuleRegistry
{
    // Register/Unregister can race the shell's read; Modules snapshots under the
    // lock so a caller iterates a stable copy.
    private static readonly object _gate = new();
    private static readonly List<SettingsModuleDescriptor> _modules = new();

    public static IReadOnlyList<SettingsModuleDescriptor> Modules
    {
        get { lock (_gate) return _modules.OrderBy(m => m.Tier).ThenBy(m => m.Order).ToList(); }
    }

    // Fired outside the lock, so a handler never runs while holding the gate.
    public static event Action? Changed;

    public static void Register(SettingsModuleDescriptor descriptor)
    {
        lock (_gate)
        {
            int existing = _modules.FindIndex(m => m.Id == descriptor.Id);
            if (existing >= 0) _modules[existing] = descriptor;
            else _modules.Add(descriptor);
        }
        DeckleSettingsSource.Log.SettingsModuleRegistered(descriptor.Id, descriptor.PageTag);
        Changed?.Invoke();
    }

    public static void Unregister(string id)
    {
        bool removed;
        lock (_gate) removed = _modules.RemoveAll(m => m.Id == id) > 0;
        if (!removed) return;
        DeckleSettingsSource.Log.SettingsModuleUnregistered(id);
        Changed?.Invoke();
    }
}
