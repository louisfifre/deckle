namespace Deckle.Modules;

// ── ModuleGraph ───────────────────────────────────────────────────────────────
//
// The cascade rules of the module selector, as pure set arithmetic over the
// catalogue's DependsOn edges. A module whose needs are not all checked cannot
// be checked, and unchecking one expels everything that needs it. Checking
// still closes the selection over what the module needs, so a caller that
// skips the greying cannot record a module without its dependencies. The walks
// are transitive and cycle-safe (a visited set bounds them), and they leave the
// rest of the selection untouched — the UI hands in the current selection and
// renders the returned one, holding no cascade logic of its own.
public static class ModuleGraph
{
    // Dependency ids the catalogue does not know are skipped: a stale edge must
    // not block the check.
    public static IReadOnlySet<string> WithDependencies(
        IReadOnlyList<ModuleDescriptor> catalog, IEnumerable<string> selection, string id)
    {
        var result = new HashSet<string>(selection, StringComparer.Ordinal);
        var byId = catalog.ToDictionary(m => m.Id, StringComparer.Ordinal);

        var pending = new Stack<string>();
        pending.Push(id);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!byId.ContainsKey(current) || !result.Add(current)) continue;
            foreach (string dep in byId[current].DependsOn)
                pending.Push(dep);
        }

        return result;
    }

    public static IReadOnlySet<string> WithoutDependents(
        IReadOnlyList<ModuleDescriptor> catalog, IEnumerable<string> selection, string id)
    {
        var result = new HashSet<string>(selection, StringComparer.Ordinal);

        // Visited guards the walk, not the Remove: an intermediate that is
        // already unchecked must still relay the cascade to its own dependents.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(id);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!visited.Add(current)) continue;
            result.Remove(current);
            foreach (ModuleDescriptor m in catalog)
            {
                if (m.DependsOn.Contains(current, StringComparer.Ordinal))
                    pending.Push(m.Id);
            }
        }

        return result;
    }

    // True when every dependency the catalogue knows is in the selection; the
    // selector greys a module out until then. An unknown id is skipped, as in
    // WithDependencies.
    public static bool DependenciesMet(
        IReadOnlyList<ModuleDescriptor> catalog, IReadOnlySet<string> selection, string id)
    {
        ModuleDescriptor? module = catalog.FirstOrDefault(m => m.Id == id);
        return module is null
            || module.DependsOn.All(dep => selection.Contains(dep) || !catalog.Any(m => m.Id == dep));
    }

    // Drops every module whose dependencies are not all selected, transitively.
    // A recorded choice can predate an edge the catalogue gained since, and the
    // selector must never show a box checked on a module it greys out, since
    // that box could not be unchecked.
    public static IReadOnlySet<string> WithoutUnmetDependencies(
        IReadOnlyList<ModuleDescriptor> catalog, IEnumerable<string> selection)
    {
        IReadOnlySet<string> result = new HashSet<string>(selection, StringComparer.Ordinal);
        foreach (ModuleDescriptor m in catalog)
        {
            if (!result.Contains(m.Id)) result = WithoutDependents(catalog, result, m.Id);
        }

        return result;
    }
}
