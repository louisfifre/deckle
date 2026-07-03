using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── SettingsModuleRegistry ────────────────────────────────────────────────────
//
// The runtime list of module-owned settings pages the shell's NavigationView
// materialises — the seam that replaces the frozen <NavigationView.MenuItems>
// tree in SettingsWindow.xaml. A module (via the App composition root today, the
// module installer tomorrow) Registers its descriptor; SettingsWindow reads
// Modules to build its nav and subscribes to Changed so an install/uninstall
// while the window is open re-materialises the band live.
//
// Same lib-exposes-a-static / host-populates-it pattern as SettingsHost above it:
// Deckle.Settings owns the registry, the composition root fills it, and the shell
// stays a pure aggregator with zero literal module references. The shell's own
// pages (General, Recording, Diagnostics) stay static XAML anchors and are NOT in
// here — the registry carries only the pages that live in other assemblies and
// could come and go.
public static class SettingsModuleRegistry
{
    // Guards the list against a Register/Unregister racing the shell's read. Every
    // access takes it; Modules snapshots under it so a caller iterates a stable
    // copy while a concurrent mutation is in flight.
    private static readonly object _gate = new();
    private static readonly List<SettingsModuleDescriptor> _modules = new();

    // The registered modules in nav order (by tier, then Order within the tier), as
    // a snapshot copy — safe to iterate even if a registration fires mid-enumeration.
    public static IReadOnlyList<SettingsModuleDescriptor> Modules
    {
        get { lock (_gate) return _modules.OrderBy(m => m.Tier).ThenBy(m => m.Order).ToList(); }
    }

    // Raised after any mutation, so an open SettingsWindow can rebuild its module
    // band. Fired outside the lock (the list is already committed) so a handler
    // that reads Modules does not re-enter the gate under it.
    public static event Action? Changed;

    // Add a module, or replace the existing one with the same Id (idempotent — a
    // second Register of the same id updates it rather than duplicating the nav
    // entry). Raises Changed.
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

    // Remove the module with this Id. No-op (and no Changed) when absent, so an
    // uninstall of an already-gone module is silent.
    public static void Unregister(string id)
    {
        bool removed;
        lock (_gate) removed = _modules.RemoveAll(m => m.Id == id) > 0;
        if (!removed) return;
        DeckleSettingsSource.Log.SettingsModuleUnregistered(id);
        Changed?.Invoke();
    }
}
